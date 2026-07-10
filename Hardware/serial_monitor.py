# -*- coding: utf-8 -*-
# ================================================================
#  자전거 시뮬레이터 ESP32-S3 시리얼 데이터 모니터
#  사용법: check_serial.bat 더블클릭  또는  python serial_monitor.py [ESP32포트] [릴레이포트]
#  - ESP32-S3(CH343) 115200bps, JSON 라인 수신 (50Hz)
#  - USB 릴레이(CH340) 9600bps 자동 연결 — 키보드 'v' 키로 진동 트리거
#  - 명령 전송 가능: C(조향 보정), V0~V6(진동), B0/B1(브레이크), S0~S3, P50~P300
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

# ── USB 릴레이(진동 모터) 프로토콜 — VibrationRelay.cs와 동일 ──
# 9600bps, N/8/1. 릴레이1 ON/OFF 명령 (마지막 바이트는 앞 3바이트 합산 체크섬)
RELAY_BAUD = 9600
RELAY_ON = bytes([0xA0, 0x01, 0x01, 0xA2])
RELAY_OFF = bytes([0xA0, 0x01, 0x00, 0xA1])
RELAY_VIBE_DURATION = 0.5  # 'v' 키 진동 지속시간 (초)

# Windows 콘솔 ANSI 색상 활성화
os.system("")

C_RESET  = "\033[0m"
C_DIM    = "\033[90m"
C_GREEN  = "\033[92m"
C_YELLOW = "\033[93m"
C_RED    = "\033[91m"
C_CYAN   = "\033[96m"
C_BOLD   = "\033[1m"

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
        desc = (p.description or "").lower()
        if "ch340" in desc:
            mark = " <- 릴레이 추정"
        elif p in candidates:
            mark = " <- ESP32 추정"
        else:
            mark = ""
        print(f"  [{i}] {p.device} - {p.description}{C_CYAN}{mark}{C_RESET}")

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
        if "ch340" in (p.description or "").lower():
            return p.device
    return None


STATUS_ROW = 6   # build_header()가 만드는 줄 중 실시간 상태 줄의 인덱스


def build_header(ports, port, relay):
    """상단 고정 영역: 포트 체크 정보 + 실시간 상태 줄 (총 8줄)."""
    width = min(shutil.get_terminal_size((80, 25)).columns - 1, 78)
    bar = C_DIM + "─" * width + C_RESET

    desc = next((p.description for p in ports if p.device == port), "")
    esp_line = f"{C_GREEN}{port}{C_RESET} @{BAUD}bps  {C_DIM}{desc}{C_RESET}"

    plist = "  ".join(
        p.device + (C_CYAN + "(릴레이)" + C_RESET if "ch340" in (p.description or "").lower()
                    else C_CYAN + "(ESP32)" + C_RESET if p.device == port
                    else "")
        for p in ports
    )

    return [
        bar,
        f" {C_BOLD}ESP32 {C_RESET} {esp_line}",
        f" {C_BOLD}릴레이{C_RESET} {relay.status}",
        f" {C_BOLD}포트  {C_RESET} {plist}",
        f" {C_BOLD}명령  {C_RESET} C=조향보정  V0~V6=진동  B0/B1=브레이크진동  "
        f"{C_YELLOW}[v]=릴레이진동(즉시){C_RESET}  q=종료",
        bar,
        C_DIM + " 데이터 대기 중..." + C_RESET,   # STATUS_ROW — 매 프레임 제자리 갱신
        bar,
    ]


class Relay:
    """USB 릴레이(CH340) 진동 제어. 'v' 키 입력 시 ON→duration 후 OFF."""

    def __init__(self, serial_mod, port, baud=RELAY_BAUD, duration=RELAY_VIBE_DURATION):
        self._serial = serial_mod
        self.port = port
        self.baud = baud
        self.duration = duration
        self.ser = None
        self.status = ""      # 헤더에 표시할 연결 상태 한 줄
        self._timer = None    # 진행 중인 OFF 예약 타이머
        self._lock = threading.Lock()

    @property
    def connected(self):
        return self.ser is not None and self.ser.is_open

    def connect(self):
        if not self.port:
            self.status = C_YELLOW + "미탐지 (CH340 없음) — 'v' 진동 비활성" + C_RESET
            return False
        try:
            self.ser = self._serial.Serial(self.port, self.baud, timeout=0.3)
            # 릴레이는 마지막 상태를 유지하는 래치 방식이라, 이전 실행이 강제 종료됐다면
            # ON인 채로 남아있을 수 있다. 연결 직후 무조건 OFF를 보내 상태를 맞춘다.
            with self._lock:
                self.ser.write(RELAY_OFF)
            self.status = f"{C_GREEN}{self.port}{C_RESET} @{self.baud}bps  {C_GREEN}연결됨{C_RESET} {C_DIM}(시작 시 OFF){C_RESET}"
            return True
        except Exception as e:
            self.status = C_RED + f"{self.port} 열기 실패: {e}" + C_RESET
            self.ser = None
            return False

    def vibrate(self):
        if not self.connected:
            print(C_YELLOW + ">> 릴레이 미연결 — 진동 무시 (v)" + C_RESET)
            return
        try:
            with self._lock:
                # 연타 시 이전 타이머가 새 진동을 중간에 끊어버리지 않도록 취소 후 재예약
                if self._timer:
                    self._timer.cancel()
                self.ser.write(RELAY_ON)
                self._timer = threading.Timer(self.duration, self._off)
                self._timer.daemon = True
                self._timer.start()
            print(C_CYAN + f">> 릴레이 진동 ON ({self.duration:.1f}s)" + C_RESET)
        except Exception as e:
            print(C_RED + f"릴레이 전송 실패: {e}" + C_RESET)

    def _off(self):
        with self._lock:
            self._timer = None
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


def _send_esp(ser, cmd):
    cmd = cmd.strip()
    if not cmd:
        return
    if cmd.lower() in ("q", "quit", "exit"):
        exit_now(0)
    try:
        ser.write((cmd + "\n").encode())
        print(C_CYAN + f">> 전송: {cmd}" + C_RESET)
    except Exception as e:
        print(C_RED + f"전송 실패: {e}" + C_RESET)


def input_thread(ser, relay):
    """실시간 키 입력. 'v'=릴레이 진동(즉시), 그 외 문자는 버퍼에 모아 Enter 시 ESP32로 전송.
    Windows에서는 msvcrt로 단일 키를 감지하고, 그 외 OS는 줄 단위 입력으로 대체한다."""
    try:
        import msvcrt
    except ImportError:
        _line_input_loop(ser, relay)
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
        if ch in ("\r", "\n"):       # Enter → 버퍼를 ESP32로 전송
            print()
            _send_esp(ser, buf)
            buf = ""
            continue
        if ch in ("\x08", "\x7f"):   # Backspace
            if buf:
                buf = buf[:-1]
                print("\b \b", end="", flush=True)
            continue
        buf += ch                    # 일반 문자 → 버퍼 누적 + 에코
        print(ch, end="", flush=True)


def _line_input_loop(ser, relay):
    """비 Windows 대체: 줄 단위 입력. 'v' 단독 입력 시 릴레이 진동."""
    while True:
        try:
            cmd = input()
        except (EOFError, KeyboardInterrupt):
            return
        cmd = cmd.strip()
        if not cmd:
            continue
        if cmd.lower() == "v":
            relay.vibrate()
            continue
        _send_esp(ser, cmd)


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
    try:
        ser = serial.Serial(port, BAUD, timeout=1)
    except Exception as e:
        print(C_RED + f"포트 열기 실패: {e}" + C_RESET)
        print("Unity나 아두이노 시리얼 모니터가 포트를 점유 중인지 확인하세요.")
        sys.exit(1)

    # USB 릴레이(CH340) 연결 — 인자로 지정하거나 자동 탐지 (ESP32 포트는 제외)
    relay_port = sys.argv[2] if len(sys.argv) > 2 else pick_relay_port(ports, port)
    global RELAY
    relay = RELAY = Relay(serial, relay_port)
    relay.connect()

    # 포트 체크 정보를 화면 상단에 고정 (아래쪽만 스크롤)
    HEADER.set(build_header(ports, port, relay))

    threading.Thread(target=input_thread, args=(ser, relay), daemon=True).start()

    last_stat = time.time()
    last_draw = 0.0
    count = 0
    hz = 0
    steer_ok = True     # 조향 센서 인식 여부 (펌웨어 v5.8: 실패 시 str=0 고정 송신)
    line_open = False   # (헤더 고정 실패 시) 상태 줄이 \r 덮어쓰기 중인지

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
        try:
            raw = ser.readline()
        except Exception as e:
            print(C_RED + f"\n수신 오류: {e} (케이블 분리?)" + C_RESET)
            sys.exit(1)
        if not raw:
            continue
        line = raw.decode(errors="replace").strip()
        if not line:
            continue

        count += 1
        now = time.time()
        if now - last_stat >= 1.0:
            hz = count / (now - last_stat)
            count = 0
            last_stat = now
            if HEADER.refresh_if_resized():
                line_open = False   # 화면을 지웠으므로 상태 줄도 초기화

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
            else:
                print_msg(C_CYAN + f"[{ts}] DEBUG: {msg}" + C_RESET)
        elif "calibrated" in d:
            print_msg(C_GREEN + f"[{ts}] 조향 보정 완료 (center={d.get('center')})" + C_RESET)
        elif "rpm" in d:
            rpm = d.get("rpm", 0)
            spd = d.get("spd", 0)
            strv = d.get("str", 0)
            spd_c = C_GREEN if spd > 1 else C_DIM
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
                f"{fmt_flag('brk', d.get('brk'))} "
                f"{fmt_flag('O', d.get('o'), C_GREEN)} "
                f"{fmt_flag('X', d.get('x'), C_YELLOW)} "
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
