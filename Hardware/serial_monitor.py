# -*- coding: utf-8 -*-
# ================================================================
#  자전거 시뮬레이터 ESP32-S3 시리얼 데이터 모니터  (펌웨어 v6.2 기준)
#  사용법: check_serial.bat 더블클릭  또는  python serial_monitor.py [ESP32포트] [릴레이포트]
#  - ESP32-S3(CH343) 115200bps, JSON 라인 수신 (50Hz)
#  - USB 릴레이(CH340) 9600bps 자동 연결 — 'v' 키로 수동 진동, 'r' 키(소문자)로 포트 재스캔
#  - 릴레이 자동 연동: 브레이크/O/X 모두 '누르는 순간' 짧게 진동 (Unity와 동일)
#    진동은 릴레이 단일 경로다 — ESP32의 IRF520 모터 경로(V/P/B 명령)는 사용하지 않는다.
#  - 명령 전송(대문자 + Enter): C(조향 보정), R(PAS 인터럽트 재초기화), H(하트비트 에코)
#    ※ 소문자 'r'은 포트 재스캔 단축키다. ESP32의 R 명령은 대문자로 입력해야 전달된다.
#  - 연결 유지: Unity InputManager와 동일하게 10초 주기 'H' keep-alive 송신,
#    5초 무수신 시 좀비 포트로 판단해 자동 재연결
#  - 펌웨어 v6.2 워치독: loop가 5초간 굳으면 보드가 자동 리셋된다. 부팅 시 오는
#    reset_reason이 반복해서 6(TASK_WDT)이면 loop가 멈추고 있다는 뜻 — I2C 락업 의심.
# ================================================================
import sys
import os
import json
import time
import re
import atexit
import shutil
import signal
import threading
import unicodedata

BAUD = 115200

# ── 연결 유지 (Unity InputManager와 동일 정책) ──
HEARTBEAT_INTERVAL = 10.0    # 'H' keep-alive 송신 주기 (초) — USB 절전 방지 + 왕복 확인
STALE_RECONNECT_SEC = 5.0    # 무수신이 이 시간 지속되면 좀비 포트로 판단, 재연결
RECONNECT_RETRY_SEC = 5.0    # 재연결 실패 시 재시도 주기 (초)

# ── USB 릴레이(진동 모터) 프로토콜 — VibrationRelay.cs와 동일 ──
# 9600bps, N/8/1. 릴레이1 ON/OFF 명령 (마지막 바이트는 앞 3바이트 합산 체크섬)
RELAY_BAUD = 9600
RELAY_ON = bytes([0xA0, 0x01, 0x01, 0xA2])
RELAY_OFF = bytes([0xA0, 0x01, 0x00, 0xA1])
RELAY_VIBE_DURATION = 0.5  # 'v' 키 수동 진동 지속시간 (초) — 귀로 확인하기 좋게 길게
# 브레이크/O/X 자동 연동 진동 — Unity의 VibeShortDuration 기본값과 맞춘다.
# (config.ini에서 값을 바꿨다면 여기도 맞춰야 실제 체감과 같아진다)
AUTO_VIBE_DURATION = 0.2

# ── ESP32 리셋 원인 (펌웨어 v6.2가 부팅 시 송신) ──
# 1(POWERON)이 정상. 6(TASK_WDT)이 반복되면 loop가 굳어 워치독이 물고 있다는 뜻이다.
RESET_REASONS = {
    0: "UNKNOWN",
    1: "POWERON (정상 전원 인가)",
    2: "EXT (외부 리셋 핀)",
    3: "SW (소프트웨어 리셋)",
    4: "PANIC (예외로 죽음)",
    5: "INT_WDT (인터럽트 워치독)",
    6: "TASK_WDT (loop 정지 — 워치독이 물었음)",
    7: "WDT (기타 워치독)",
    8: "DEEPSLEEP",
    9: "BROWNOUT (전원 전압 불안정)",
    10: "SDIO",
}

def _init_console():
    """Windows 콘솔의 ANSI 색상과 UTF-8 출력을 활성화한다.

    cmd의 기본 코드페이지(한국어 윈도우는 cp949)에서는 '—', '─' 같은 문자를 찍는 순간
    UnicodeEncodeError로 죽는다. check_serial.bat은 chcp 65001을 하지만,
    python serial_monitor.py를 직접 실행하는 경우를 위해 여기서도 맞춰준다.
    """
    os.system("")               # VT(ANSI 이스케이프) 처리 활성화
    if os.name == "nt":
        try:
            import ctypes
            ctypes.windll.kernel32.SetConsoleOutputCP(65001)
            ctypes.windll.kernel32.SetConsoleCP(65001)
        except Exception:
            pass
    for stream in (sys.stdout, sys.stderr):
        try:
            # errors='replace' — 코드페이지 변경에 실패해도 죽지 않고 '?'로 대체
            stream.reconfigure(encoding="utf-8", errors="replace")
        except Exception:
            pass                # 리다이렉트된 파이프 등 reconfigure 불가한 스트림


_init_console()

C_RESET  = "\033[0m"
C_DIM    = "\033[90m"
C_GREEN  = "\033[92m"
C_YELLOW = "\033[93m"
C_RED    = "\033[91m"
C_CYAN   = "\033[96m"
C_MAGENTA = "\033[95m"
C_ORANGE = "\033[38;5;208m"
C_BOLD   = "\033[1m"

# 두 장치의 COM 포트를 한눈에 구분하기 위한 역할별 색
C_ESP    = C_CYAN      # ESP32-S3 (센서 데이터)
C_RELAY  = C_ORANGE    # USB 릴레이 (진동)

_ANSI_RE = re.compile(r"\x1b\[[0-9;?]*[A-Za-z]")


def _char_width(c):
    """한글·한자 등은 콘솔에서 두 칸을 차지한다."""
    return 2 if unicodedata.east_asian_width(c) in ("W", "F") else 1


def disp_width(s):
    """ANSI 색상 코드를 뺀 실제 표시 폭(칸 수)."""
    return sum(_char_width(c) for c in _ANSI_RE.sub("", s))


def fit(s, width):
    """색상 코드는 유지한 채, 표시 폭이 width를 넘지 않도록 자른다.

    상태 줄이 터미널 폭을 넘으면 자동 줄바꿈이 일어나 '\\r' 덮어쓰기가 무너지고
    화면이 계속 밀려나므로 반드시 잘라내야 한다.
    """
    if disp_width(s) <= width:
        return s
    out, w, i = [], 0, 0
    while i < len(s):
        m = _ANSI_RE.match(s, i)
        if m:                       # 색상 코드는 폭을 차지하지 않으므로 그대로 통과
            out.append(m.group())
            i = m.end()
            continue
        cw = _char_width(s[i])
        if w + cw > width:
            break
        out.append(s[i])
        w += cw
        i += 1
    return "".join(out) + C_RESET


class Header:
    """터미널 상단에 포트 정보를 고정 표시.

    ANSI 스크롤 영역(DECSTBM, ESC[top;bottom r)으로 헤더 아래쪽만 스크롤되게 만든다.
    터미널이 아니거나 화면이 너무 좁으면 그냥 한 번 출력하고 넘어간다.
    """

    def __init__(self):
        self.lines = []
        self.rows = 0
        self.cols = 80
        self.active = False

    def set(self, lines):
        self.lines = list(lines)
        if not sys.stdout.isatty():
            for l in lines:
                print(l)
            return
        self.draw()

    def draw(self):
        size = shutil.get_terminal_size((80, 25))
        rows, n = size.lines, len(self.lines)
        if rows < n + 4:          # 헤더 + 로그 3줄도 안 나오면 고정 포기
            self.disable()
            for l in self.lines:
                print(l)
            return
        self.rows, self.cols = rows, size.columns
        out = ["\033[r", "\033[2J", "\033[H"]        # 영역 해제 → 전체 지우기 → 홈
        out += [fit(l, self.cols - 1) + "\033[K\n" for l in self.lines]
        out.append(f"\033[{n + 1};{rows}r")          # 스크롤 영역 = 헤더 아래 ~ 맨 밑줄
        out.append(f"\033[{n + 1};1H")               # 커서를 영역 첫 줄로
        sys.stdout.write("".join(out))
        sys.stdout.flush()
        self.active = True

    def set_line(self, idx, text):
        """헤더의 특정 줄만 제자리에서 갱신. 고정에 실패한 상태면 False."""
        if idx < len(self.lines):
            self.lines[idx] = text       # 리사이즈로 다시 그릴 때를 대비해 보관
        if not self.active:
            return False
        # 커서 저장(DECSC) → 해당 행으로 이동해 덮어쓰기 → 커서 복원(DECRC).
        # 스크롤 영역 밖의 행이라 로그 출력 위치에는 영향이 없다.
        sys.stdout.write(
            f"\0337\033[{idx + 1};1H{fit(text, self.cols - 1)}\033[K\0338"
        )
        sys.stdout.flush()
        return True

    def refresh_if_resized(self):
        """터미널 크기가 바뀌면 스크롤 영역과 잘라낸 폭이 어긋나므로 다시 그린다."""
        if not self.active:
            return False
        size = shutil.get_terminal_size((80, 25))
        if (size.lines, size.columns) != (self.rows, self.cols):
            self.draw()
            return True
        return False

    def disable(self):
        if self.active:
            sys.stdout.write("\033[r")               # 스크롤 영역 해제
            sys.stdout.flush()
            self.active = False


HEADER = Header()


def ensure_pyserial():
    try:
        import serial  # noqa: F401
    except ImportError:
        print("pyserial이 설치되어 있지 않습니다. 설치를 시도합니다...")
        import subprocess
        subprocess.check_call([sys.executable, "-m", "pip", "install", "pyserial"])
    import serial
    from serial.tools import list_ports
    return serial, list_ports


def is_relay(p):
    """USB 릴레이(CH340) 포트인지."""
    return "ch340" in (p.description or "").lower()


def is_esp_candidate(p):
    """ESP32-S3 추정 포트 판별 (USB CDC / CP210x / CH343 등).
    CH340은 릴레이, 블루투스 가상 포트는 제외."""
    desc = (p.description or "").lower()
    esp_keywords = ("cp210", "ch343", "ch910", "usb", "uart", "jtag")
    return ("bluetooth" not in desc
            and "ch340" not in desc
            and any(k in desc for k in esp_keywords))


def pick_port(ports):
    if not ports:
        print(C_RED + "연결된 COM 포트가 없습니다. USB 케이블을 확인하세요." + C_RESET)
        sys.exit(1)

    candidates = [p for p in ports if is_esp_candidate(p)]

    print(C_BOLD + "사용 가능한 포트:" + C_RESET)
    for i, p in enumerate(ports):
        if is_relay(p):
            color, mark = C_RELAY, " <- 릴레이 추정"
        elif p in candidates:
            color, mark = C_ESP, " <- ESP32 추정"
        else:
            color, mark = C_DIM, ""
        print(f"  [{i}] {color}{p.device}{C_RESET} - {p.description}{color}{mark}{C_RESET}")

    if len(candidates) == 1:
        print(f"\nESP32로 추정되는 {candidates[0].device} 자동 선택")
        return candidates[0].device
    if len(ports) == 1:
        print(f"\n포트가 1개뿐이므로 {ports[0].device} 자동 선택")
        return ports[0].device

    sel = input("\n포트 번호 입력 (엔터 = 0): ").strip()
    idx = int(sel) if sel.isdigit() and int(sel) < len(ports) else 0
    return ports[idx].device


def pick_relay_port(ports, exclude):
    """USB 릴레이(CH340) 포트 자동 탐지. ESP32 포트(exclude)는 제외."""
    for p in ports:
        if p.device == exclude:
            continue
        if is_relay(p):
            return p.device
    return None


STATUS_ROW = 6   # build_header()가 만드는 줄 중 실시간 상태 줄의 인덱스


def build_header(ports, port, relay):
    """상단 고정 영역: 포트 체크 정보 + 실시간 상태 줄 (총 8줄)."""
    width = min(shutil.get_terminal_size((80, 25)).columns - 1, 78)
    bar = C_DIM + "─" * width + C_RESET

    desc = next((p.description for p in ports if p.device == port), "")
    esp_line = f"{C_ESP}{port}{C_RESET} @{BAUD}bps  {C_DIM}{desc}{C_RESET}"

    def colored(p):
        if is_relay(p):
            return f"{C_RELAY}{p.device}(릴레이){C_RESET}"
        if p.device == port:
            return f"{C_ESP}{p.device}(ESP32){C_RESET}"
        return f"{C_DIM}{p.device}{C_RESET}"

    plist = "  ".join(colored(p) for p in ports)

    return [
        bar,
        f" {C_ESP}{C_BOLD}ESP32 {C_RESET} {esp_line}",
        f" {C_RELAY}{C_BOLD}릴레이{C_RESET} {relay.status}",
        f" {C_BOLD}포트  {C_RESET} {plist}",
        f" {C_BOLD}명령  {C_RESET} C=조향보정  {C_MAGENTA}R=PAS재초기화{C_RESET}  I=펌웨어정보  H=하트비트  q=종료  "
        f"{C_YELLOW}[v]=수동진동{C_RESET}  {C_GREEN}[r]=포트재스캔{C_RESET}  "
        f"{C_DIM}brk/O/X=자동진동{C_RESET}",
        bar,
        C_DIM + " 데이터 대기 중..." + C_RESET,   # STATUS_ROW — 매 프레임 제자리 갱신
        bar,
    ]


class Relay:
    """USB 릴레이(CH340) 진동 제어.

    pulse(duration): 누르는 '순간' 진동 → duration 후 자동 OFF.
    브레이크·O·X 모두 같은 방식이다 — Unity가 브레이크 상승 엣지에서
    SendVibrate(VibeState.Brake)로 짧은 펄스를 한 번 보내는 것과 동일하게 맞췄다.
    (예전에는 브레이크를 '잡고 있는 동안' 계속 켰지만, 그건 ESP32의 IRF520 모터를
     쓰던 시절의 동작이고 지금은 릴레이 단일 경로다.)
    미연결 상태에서는 조용히 무시한다(50Hz 데이터에 맞춰 호출되므로).
    """

    def __init__(self, serial_mod, port, baud=RELAY_BAUD, duration=RELAY_VIBE_DURATION):
        self._serial = serial_mod
        self.port = port
        self.baud = baud
        self.duration = duration   # 'v' 키 수동 진동 기본 길이
        self.ser = None
        self.status = ""      # 헤더에 표시할 연결 상태 한 줄
        self._timer = None    # 진행 중인 OFF 예약 타이머
        self._off_at = 0.0    # 현재 예약된 OFF 시각 — 겹친 진동 중 긴 쪽을 남기기 위해
        self._lock = threading.Lock()

    @property
    def connected(self):
        return self.ser is not None and self.ser.is_open

    def connect(self):
        if not self.port:
            self.status = C_RED + "미탐지 (CH340 없음) — 진동 비활성" + C_RESET
            return False
        try:
            self.ser = self._serial.Serial(self.port, self.baud, timeout=0.3)
            # 릴레이는 마지막 상태를 유지하는 래치 방식이라, 이전 실행이 강제 종료됐다면
            # ON인 채로 남아있을 수 있다. 연결 직후 무조건 OFF를 보내 상태를 맞춘다.
            with self._lock:
                self.ser.write(RELAY_OFF)
            self.status = (f"{C_RELAY}{self.port}{C_RESET} @{self.baud}bps  "
                           f"{C_GREEN}연결됨{C_RESET} {C_DIM}(시작 시 OFF){C_RESET}")
            return True
        except Exception as e:
            self.status = f"{C_RELAY}{self.port}{C_RESET} {C_RED}열기 실패: {e}{C_RESET}"
            self.ser = None
            return False

    def pulse(self, duration=None):
        """짧은 진동 1회. duration 생략 시 자동 연동용 기본값(AUTO_VIBE_DURATION).

        진동 중에 새 요청이 겹치면 Unity의 VibrationRelay와 마찬가지로 '긴 쪽'을 남긴다.
        (위험 이벤트 진동이 뒤이은 짧은 클릭 진동에 잘려나가지 않도록)
        """
        if not self.connected:
            return False
        dur = AUTO_VIBE_DURATION if duration is None else duration
        try:
            with self._lock:
                end_at = time.time() + dur
                if self._timer:
                    if end_at <= self._off_at:
                        return True     # 이미 더 길게 켜져 있다 — 그대로 둔다
                    self._timer.cancel()
                self.ser.write(RELAY_ON)
                self._off_at = end_at
                self._timer = threading.Timer(dur, self._off)
                self._timer.daemon = True
                self._timer.start()
            return True
        except Exception as e:
            print(C_RED + f"릴레이 전송 실패: {e}" + C_RESET)
            return False

    def vibrate(self):
        """'v' 키 수동 진동. 여기서만 미연결을 사용자에게 알린다."""
        if not self.connected:
            print(C_YELLOW + ">> 릴레이 미연결 — 진동 무시 (v)" + C_RESET)
            return
        if self.pulse(self.duration):
            print(C_CYAN + f">> 릴레이 진동 ON ({self.duration:.1f}s)" + C_RESET)

    def _off(self):
        with self._lock:
            self._timer = None
            self._off_at = 0.0
            try:
                if self.connected:
                    self.ser.write(RELAY_OFF)
            except Exception:
                pass

    def close(self):
        """릴레이 OFF 후 포트 닫기. 여러 번, 여러 스레드에서 호출해도 안전."""
        with self._lock:
            if self._timer:
                self._timer.cancel()   # cancel()은 join하지 않으므로 락 안에서 안전
                self._timer = None
            self._off_at = 0.0
            if self.ser is None:
                return
            try:
                if self.ser.is_open:
                    self.ser.write(RELAY_OFF)
                    self.ser.flush()   # 프로세스가 죽기 전에 OFF가 실제로 나가도록
                    self.ser.close()
            except Exception:
                pass
            self.ser = None


class EspLink:
    """ESP32 시리얼 포트 래퍼.

    .NET SerialPort와 마찬가지로 pyserial도 장치가 죽어도(USB 절전/재열거/보드 리셋)
    포트가 열린 것처럼 보이는 좀비 상태가 될 수 있다. Unity InputManager와 동일하게
    무수신 지속 시 포트를 닫고 다시 열어 복구한다. 재연결 시 DTR 재개방으로
    보드가 리셋되어 부팅 메시지부터 다시 나오는 것이 정상이다.
    """

    def __init__(self, serial_mod, port, baud=BAUD):
        self._serial = serial_mod
        self.port = port
        self.baud = baud
        self.ser = None

    @property
    def connected(self):
        return self.ser is not None and self.ser.is_open

    def open(self):
        """실패 시 예외 전파 — 최초 연결은 호출부에서 안내 후 종료 처리."""
        self.ser = self._serial.Serial(self.port, self.baud, timeout=1)

    def readline(self):
        if not self.connected:
            raise OSError("포트 닫힘")
        return self.ser.readline()

    def write_line(self, cmd):
        if not self.connected:
            raise OSError("포트 닫힘")
        self.ser.write((cmd + "\n").encode())

    def reconnect(self):
        """포트를 닫고 다시 연다. 성공 여부 반환."""
        try:
            if self.ser is not None:
                self.ser.close()
        except Exception:
            pass
        self.ser = None
        try:
            self.open()
            return True
        except Exception:
            return False


RELAY = None            # 종료 핸들러에서 릴레이를 끄기 위한 전역 참조
_CONSOLE_HANDLER = None  # 콘솔 컨트롤 콜백 — GC되면 안 되므로 전역 보관


def cleanup():
    """릴레이 OFF + 스크롤 영역 해제. 모든 종료 경로에서 호출되며 중복 호출해도 안전."""
    if RELAY is not None:
        RELAY.close()
    HEADER.disable()


def exit_now(code=0):
    """os._exit는 atexit를 건너뛰므로 반드시 cleanup을 먼저 부른다."""
    cleanup()
    sys.stdout.write("\n")   # 상태 줄에 프롬프트가 붙지 않도록
    sys.stdout.flush()
    os._exit(code)


def install_exit_handlers():
    """Ctrl+C, Ctrl+Break, taskkill(정상), 콘솔 창 [X], 로그오프/종료 시 릴레이를 끈다.

    taskkill /F 나 정전처럼 프로세스가 정리 코드를 돌 기회조차 없는 경우는 막을 수 없다.
    그런 경우는 connect()가 시작할 때 보내는 OFF가 안전망 역할을 한다.
    """
    atexit.register(cleanup)

    def on_signal(signum, frame):
        exit_now(0)

    for name in ("SIGINT", "SIGTERM", "SIGBREAK"):
        sig = getattr(signal, name, None)
        if sig is None:
            continue
        try:
            signal.signal(sig, on_signal)
        except (ValueError, OSError):
            pass   # 메인 스레드가 아니거나 지원하지 않는 시그널

    if os.name != "nt":
        return

    # 콘솔 창 닫기(X)/로그오프/시스템 종료는 시그널로 오지 않는다 — Win32 핸들러가 필요.
    global _CONSOLE_HANDLER
    try:
        import ctypes
        from ctypes import wintypes

        routine = ctypes.WINFUNCTYPE(wintypes.BOOL, wintypes.DWORD)

        def handler(ctrl_type):
            cleanup()
            return False   # 기본 핸들러가 프로세스를 종료하도록 넘긴다

        _CONSOLE_HANDLER = routine(handler)
        ctypes.windll.kernel32.SetConsoleCtrlHandler(_CONSOLE_HANDLER, True)
    except Exception:
        pass   # 콘솔이 없는 환경(pythonw 등)에서는 무시


def _send_esp(esp, cmd):
    cmd = cmd.strip()
    if not cmd:
        return
    if cmd.lower() in ("q", "quit", "exit"):
        exit_now(0)
    try:
        esp.write_line(cmd)
        print(C_CYAN + f">> 전송: {cmd}" + C_RESET)
    except Exception as e:
        print(C_RED + f"전송 실패: {e}" + C_RESET)


def input_thread(esp, relay, rescan):
    """실시간 키 입력. 'v'=릴레이 진동, 'r'=포트 재스캔(둘 다 즉시).
    그 외 문자는 버퍼에 모아 Enter 시 ESP32로 전송.
    Windows에서는 msvcrt로 단일 키를 감지하고, 그 외 OS는 줄 단위 입력으로 대체한다."""
    try:
        import msvcrt
    except ImportError:
        _line_input_loop(esp, relay, rescan)
        return

    buf = ""
    while True:
        ch = msvcrt.getwch()
        if ch in ("\x00", "\xe0"):   # 특수키(화살표/기능키) 프리픽스 — 다음 바이트 버림
            msvcrt.getwch()
            continue
        if ch == "\x03":             # Ctrl+C (getwch는 raw 입력이라 SIGINT가 안 뜬다)
            exit_now(0)
        if ch == "v":                # 릴레이 진동 트리거 (실시간, Enter 불필요)
            relay.vibrate()
            continue
        if ch == "r":                # 포트 재스캔 — 소문자만!
            # 펌웨어 v6.1부터 대문자 'R'은 ESP32의 PAS 재초기화 명령이다.
            # 여기서 대문자까지 가로채면 그 명령을 영영 보낼 수 없게 되므로,
            # 소문자만 단축키로 쓰고 'R'은 버퍼에 쌓아 Enter 시 ESP32로 보낸다.
            # (펌웨어의 handleCommand는 대소문자를 구분하므로 소문자 r은 무시된다)
            rescan()
            continue
        if ch in ("\r", "\n"):       # Enter → 버퍼를 ESP32로 전송
            print()
            _send_esp(esp, buf)
            buf = ""
            continue
        if ch in ("\x08", "\x7f"):   # Backspace
            if buf:
                buf = buf[:-1]
                print("\b \b", end="", flush=True)
            continue
        buf += ch                    # 일반 문자 → 버퍼 누적 + 에코
        print(ch, end="", flush=True)


def _line_input_loop(esp, relay, rescan):
    """비 Windows 대체: 줄 단위 입력. 'v'=릴레이 진동, 'r'(소문자)=포트 재스캔.

    대문자 'R'은 ESP32의 PAS 재초기화 명령이므로 가로채지 않고 그대로 전송한다.
    """
    while True:
        try:
            cmd = input()
        except (EOFError, KeyboardInterrupt):
            return
        cmd = cmd.strip()
        if not cmd:
            continue
        if cmd == "v":
            relay.vibrate()
            continue
        if cmd == "r":
            rescan()
            continue
        _send_esp(esp, cmd)


def fmt_flag(name, val, on_color=C_RED):
    if val:
        return on_color + C_BOLD + f"{name}:ON " + C_RESET
    return C_DIM + f"{name}:off" + C_RESET


def main():
    install_exit_handlers()
    serial, list_ports = ensure_pyserial()

    ports = list(list_ports.comports())
    port = sys.argv[1] if len(sys.argv) > 1 else pick_port(ports)

    print(f"\n{port} @ {BAUD}bps 연결 중...")
    esp = EspLink(serial, port)
    try:
        esp.open()
    except Exception as e:
        print(C_RED + f"포트 열기 실패: {e}" + C_RESET)
        print("Unity나 아두이노 시리얼 모니터가 포트를 점유 중인지 확인하세요.")
        sys.exit(1)

    # 접속 직후 펌웨어 정보를 물어본다. 부팅 메시지(boot/wdt_armed)는 setup()에서 한 번만
    # 나가는데 이 보드는 포트를 열어도 리셋되지 않으므로, 나중에 붙는 쪽은 그걸 못 본다.
    # 'I' 응답으로 재플래싱 여부·워치독·마지막 리셋 원인을 지금 확인할 수 있다.
    try:
        esp.write_line("I")
    except Exception:
        pass

    # USB 릴레이(CH340) 연결 — 인자로 지정하거나 자동 탐지 (ESP32 포트는 제외)
    relay_port = sys.argv[2] if len(sys.argv) > 2 else pick_relay_port(ports, port)
    global RELAY
    relay = RELAY = Relay(serial, relay_port)
    relay.connect()

    # 포트 체크 정보를 화면 상단에 고정 (아래쪽만 스크롤)
    HEADER.set(build_header(ports, port, relay))
    # ESP32 헤더 줄 원본 — PAS 진단(pc/pl)을 1초 주기로 덧붙일 때 기준이 된다
    esp_hdr_base = HEADER.lines[1] if len(HEADER.lines) > 1 else ""

    def rescan():
        """'r' 키 — COM 포트를 다시 훑고 헤더를 갱신. 릴레이가 새로 꽂혔으면 연결한다.

        입력 스레드에서 호출된다. ESP32 포트는 이미 열려 있으므로 건드리지 않고,
        미연결 상태인 릴레이만 다시 찾는다.
        """
        nonlocal esp_hdr_base
        found = list(list_ports.comports())
        msg = f">> 포트 재스캔: {len(found)}개 발견"

        if not relay.connected:
            relay.port = pick_relay_port(found, port)
            if relay.connect():
                msg += f" — 릴레이 {relay.port} 연결됨"
            else:
                msg += " — 릴레이 없음"

        # 화면 전체를 다시 그리면 쌓인 로그가 지워지므로 헤더 줄만 제자리 갱신.
        # 상태 줄(STATUS_ROW)은 데이터 루프가 계속 쓰고 있으니 덮지 않는다.
        lines = build_header(found, port, relay)
        esp_hdr_base = lines[1]
        if HEADER.active:
            for i, l in enumerate(lines):
                if i != STATUS_ROW:
                    HEADER.set_line(i, l)
        else:
            for l in lines[1:5]:     # 헤더 고정 실패 시엔 정보 줄만 그대로 출력
                print(l)
        print(C_CYAN + msg + C_RESET)

    threading.Thread(target=input_thread, args=(esp, relay, rescan), daemon=True).start()

    last_stat = time.time()
    last_draw = 0.0
    count = 0
    hz = 0
    steer_ok = True     # 조향 센서 인식 여부 (펌웨어 v5.8: 실패 시 str=0 고정 송신)
    line_open = False   # (헤더 고정 실패 시) 상태 줄이 \r 덮어쓰기 중인지
    prev_brk = prev_o = prev_x = False   # 릴레이 연동용 이전 프레임 버튼 상태
    last_rx = time.time()      # 마지막 유효 수신 시각 — 무수신/좀비 포트 감지 기준
    last_hb = 0.0              # 마지막 keep-alive 송신 시각
    next_retry = 0.0           # 다음 재연결 시도 가능 시각
    read_err_notified = False  # 수신 예외 메시지 중복 출력 방지
    pas_pc = pas_pl = None     # PAS 진단 (펌웨어 v6.0: 누적 펄스 수 / 핀 레벨)
    boot_wdt = 0               # 워치독(TASK_WDT)에 의한 리셋 누적 횟수 — 반복되면 loop 정지

    def print_msg(msg):
        """상태 줄 덮어쓰기 중이면 줄바꿈 후 메시지 출력 (출력 섞임 방지)"""
        nonlocal line_open
        if line_open:
            print()
            line_open = False
        print(msg)

    def show_status(text):
        """상태 줄은 항상 같은 자리에서 갱신한다 (스크롤 없음)."""
        nonlocal line_open
        if HEADER.set_line(STATUS_ROW, text):
            return
        # 폴백: 헤더 고정 불가(비 TTY/좁은 화면) → 예전처럼 \r 덮어쓰기.
        # 잘라내지 않으면 줄바꿈이 일어나 화면이 밀린다.
        cols = shutil.get_terminal_size((80, 25)).columns
        print("\r" + fit(text, cols - 1) + "\033[K", end="", flush=True)
        line_open = True

    while True:
        now = time.time()

        # keep-alive — Unity와 동일하게 10초 주기 'H' 송신 (USB 절전 방지 + 왕복 확인)
        if esp.connected and now - last_hb >= HEARTBEAT_INTERVAL:
            last_hb = now
            try:
                esp.write_line("H")
            except Exception:
                pass   # 송신 실패는 아래 무수신 감지가 재연결로 처리한다

        try:
            raw = esp.readline()
        except Exception as e:
            # 포트가 죽으면 readline이 timeout 없이 즉시 예외를 던져 루프가 폭주할 수
            # 있다 — 메시지는 한 번만 찍고, 무수신 상태로 전환해 재연결 경로를 태운다.
            if not read_err_notified:
                read_err_notified = True
                print_msg(C_RED + f"수신 오류: {e} — 자동 재연결로 전환" + C_RESET)
            last_rx = min(last_rx, time.time() - STALE_RECONNECT_SEC)
            raw = b""
            time.sleep(0.2)

        if not raw:
            # 무수신 — 좀비 포트(USB 절전/재열거/보드 리셋) 감지 및 자동 재연결.
            # readline timeout(1초) 주기로 돌므로 상태 줄이 1Hz로 갱신된다.
            now = time.time()
            stale = now - last_rx
            if stale >= STALE_RECONNECT_SEC:
                show_status(C_RED + C_BOLD +
                            f" 무수신 {stale:3.0f}s — {port} 재연결 시도 중..." + C_RESET)
                if now >= next_retry:
                    next_retry = now + RECONNECT_RETRY_SEC
                    if esp.reconnect():
                        print_msg(C_GREEN + f">> {port} 재연결 성공" + C_RESET)
                        last_rx = time.time()
                        try:
                            esp.write_line("I")   # 재연결 후에도 펌웨어 상태를 다시 확인
                        except Exception:
                            pass
                    else:
                        print_msg(C_RED + f">> {port} 재연결 실패 — {RECONNECT_RETRY_SEC:.0f}초 후 재시도" + C_RESET)
            continue

        line = raw.decode(errors="replace").strip()
        if not line:
            continue

        now = time.time()
        last_rx = now
        read_err_notified = False
        count += 1
        if now - last_stat >= 1.0:
            hz = count / (now - last_stat)
            count = 0
            last_stat = now
            if HEADER.refresh_if_resized():
                line_open = False   # 화면을 지웠으므로 상태 줄도 초기화
            # PAS 진단 — 헤더의 ESP32 줄에 1초 주기로 덧붙인다 (v6.0 미만 펌웨어는 필드 없음).
            # 페달을 돌리면서 볼 것:
            #   펄스가 늘어난다        → 정상
            #   lv는 0/1로 바뀌는데 펄스가 안 는다 → PAS 인터럽트 사망. R 명령으로 복구된다.
            #   lv가 고정이다          → 신호선이 죽음. 센서 전원/배선 문제이며 R로는 못 고친다.
            if pas_pc is not None:
                HEADER.set_line(1, f"{esp_hdr_base}  {C_DIM}PAS펄스:{pas_pc} lv:{pas_pl}{C_RESET}")

        ts = time.strftime("%H:%M:%S")
        try:
            d = json.loads(line)
        except json.JSONDecodeError:
            print_msg(C_YELLOW + f"[{ts}] RAW: {line}" + C_RESET)
            continue

        if "debug" in d:
            msg = d["debug"]
            if "Steer sensor NOT found" in msg:
                # 펌웨어 v5.8: 센서 인식 실패 → str=0 고정 모드로 계속 송신
                steer_ok = False
                print_msg(C_RED + C_BOLD +
                          f"[{ts}] 경고: 조향 센서 미인식 — 조향값 0 고정 모드로 동작 중"
                          + C_RESET)
            elif "DMP Stabilized" in msg:
                steer_ok = True
                print_msg(C_GREEN + f"[{ts}] DEBUG: {msg}" + C_RESET)
            elif msg == "hb":
                # keep-alive 에코 (펌웨어 v5.9) — 10초마다 오므로 어둡게 한 줄만
                print_msg(C_DIM + f"[{ts}] HB 응답 — 링크 왕복 정상" + C_RESET)
            elif msg == "boot":
                # 펌웨어 v6.2 — 리셋 원인. 보드가 왜 다시 켜졌는지 알려주는 유일한 단서다.
                rr = d.get("reset_reason")
                label = RESET_REASONS.get(rr, f"코드 {rr}")
                if rr == 6:      # TASK_WDT — 워치독이 물었다 = loop가 굳었다
                    boot_wdt += 1
                    print_msg(C_RED + C_BOLD +
                              f"[{ts}] 부팅: {label}  ← loop가 멈춰 자동 리셋됨 "
                              f"(누적 {boot_wdt}회, I2C 락업 의심)" + C_RESET)
                elif rr == 9:    # BROWNOUT — 전원 전압 불안정
                    print_msg(C_RED + C_BOLD +
                              f"[{ts}] 부팅: {label}  ← USB 전원/허브 확인 필요" + C_RESET)
                elif rr == 1:
                    print_msg(C_GREEN + f"[{ts}] 부팅: {label}" + C_RESET)
                else:
                    print_msg(C_YELLOW + f"[{ts}] 부팅: {label}" + C_RESET)
            elif msg == "info":
                # 'I' 명령 응답 — 부팅 메시지를 놓쳐도 지금 올라간 펌웨어를 확인할 수 있다.
                rr = d.get("reset_reason")
                wdt = d.get("wdt")
                print_msg(C_BOLD +
                          f"[{ts}] 펌웨어 v{d.get('fw')}  I2C {int(d.get('i2c', 0)) // 1000}kHz  "
                          f"워치독 {'활성' if wdt == 1 else '미등록'}  "
                          f"조향센서 {'정상' if d.get('steer') == 1 else '미인식'}  "
                          f"마지막 리셋: {RESET_REASONS.get(rr, f'코드 {rr}')}" + C_RESET)
                if wdt != 1:
                    print_msg(C_RED + "  ← 워치독 미등록: loop가 굳어도 자동 복구되지 않는다 "
                                      "(펌웨어 v6.2 이상 확인)" + C_RESET)
                if rr == 6:
                    print_msg(C_RED + "  ← 직전 리셋이 TASK_WDT: loop가 멈췄다는 뜻. "
                                      "반복되면 I2C 락업 의심 (I2C_CLOCK_HZ를 100000으로)" + C_RESET)
            elif msg == "wdt_armed":
                print_msg(C_GREEN +
                          f"[{ts}] 워치독 활성 ({d.get('timeout_ms')}ms) — loop 정지 시 자동 리셋"
                          + C_RESET)
            elif msg == "wdt_setup_failed":
                print_msg(C_RED + C_BOLD +
                          f"[{ts}] 경고: 워치독 등록 실패 (err={d.get('err')}) — "
                          f"loop가 굳어도 자동 복구되지 않는다" + C_RESET)
            elif msg == "pas_reinit":
                # 펌웨어 v6.1 — R 명령 응답. prev(재초기화 직전까지의 누적 펄스)가 핵심이다.
                prev = d.get("prev", 0)
                if prev == 0:
                    print_msg(C_YELLOW + C_BOLD +
                              f"[{ts}] PAS 재초기화 완료 — 직전 누적 펄스 0 "
                              f"(부팅 후 펄스가 한 번도 안 들어옴 → 인터럽트가 아니라 "
                              f"센서 전원/배선을 확인할 것)" + C_RESET)
                else:
                    print_msg(C_GREEN +
                              f"[{ts}] PAS 재초기화 완료 — 직전 누적 펄스 {prev}, "
                              f"핀 레벨 {d.get('pl')}" + C_RESET)
                pas_pc = 0   # 펌웨어가 카운터를 0으로 되돌렸으므로 표시값도 맞춘다
            else:
                print_msg(C_CYAN + f"[{ts}] DEBUG: {msg}" + C_RESET)
        elif "calibrated" in d:
            print_msg(C_GREEN + f"[{ts}] 조향 보정 완료 (center={d.get('center')})" + C_RESET)
        elif "rpm" in d:
            rpm = d.get("rpm", 0)
            spd = d.get("spd", 0)
            strv = d.get("str", 0)
            pas_pc = d.get("pc", pas_pc)   # PAS 진단 (v6.0+)
            pas_pl = d.get("pl", pas_pl)
            spd_c = C_GREEN if spd > 1 else C_DIM
            brk, o, x = bool(d.get("brk")), bool(d.get("o")), bool(d.get("x"))

            # 릴레이 연동 — 미연결이면 pulse가 조용히 무시하므로 별도 분기 불필요.
            # 브레이크·O·X 모두 눌리는 '순간'(상승 에지)에만 짧게. Unity와 동일한 동작이다.
            if (brk and not prev_brk) or (o and not prev_o) or (x and not prev_x):
                relay.pulse()
            prev_brk, prev_o, prev_x = brk, o, x

            if steer_ok:
                str_out = f"조향:{strv:6.1f}°"        # 12칸
            else:
                str_out = C_RED + "조향:없음(0)" + C_RESET   # 위와 같은 12칸
            # 80칸 콘솔에서 줄바꿈되지 않도록 간격과 라벨을 압축했다 (전체 74칸)
            line_out = (
                f"[{ts}] "
                f"RPM:{rpm:5.1f} "
                f"{spd_c}속도:{spd:5.1f}km/h{C_RESET} "
                f"{str_out} "
                f"{fmt_flag('brk', brk)} "
                f"{fmt_flag('O', o, C_GREEN)} "
                f"{fmt_flag('X', x, C_YELLOW)} "
                f"{C_DIM}{hz:3.0f}Hz{C_RESET}"
            )
            # 50Hz 수신 전부를 그리면 깜빡이므로 20Hz로 제한 (최대 지연 50ms)
            if now - last_draw >= 0.05:
                last_draw = now
                show_status(line_out)
        else:
            print_msg(f"[{ts}] {line}")


if __name__ == "__main__":
    try:
        main()
    except KeyboardInterrupt:
        cleanup()
        print("\n종료합니다.")
