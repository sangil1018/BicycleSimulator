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
import atexit
import shutil
import threading

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


class Header:
    """터미널 상단에 포트 정보를 고정 표시.

    ANSI 스크롤 영역(DECSTBM, ESC[top;bottom r)으로 헤더 아래쪽만 스크롤되게 만든다.
    터미널이 아니거나 화면이 너무 좁으면 그냥 한 번 출력하고 넘어간다.
    """

    def __init__(self):
        self.lines = []
        self.rows = 0
        self.active = False

    def set(self, lines):
        self.lines = lines
        if not sys.stdout.isatty():
            for l in lines:
                print(l)
            return
        self.draw()

    def draw(self):
        rows = shutil.get_terminal_size((80, 25)).lines
        n = len(self.lines)
        if rows < n + 4:          # 헤더 + 로그 3줄도 안 나오면 고정 포기
            self.disable()
            for l in self.lines:
                print(l)
            return
        self.rows = rows
        out = ["\033[r", "\033[2J", "\033[H"]        # 영역 해제 → 전체 지우기 → 홈
        out += [l + "\033[K\n" for l in self.lines]
        out.append(f"\033[{n + 1};{rows}r")          # 스크롤 영역 = 헤더 아래 ~ 맨 밑줄
        out.append(f"\033[{n + 1};1H")               # 커서를 영역 첫 줄로
        sys.stdout.write("".join(out))
        sys.stdout.flush()
        self.active = True

    def refresh_if_resized(self):
        """터미널 높이가 바뀌면 스크롤 영역이 깨지므로 다시 그린다. 다시 그렸으면 True."""
        if not self.active:
            return False
        if shutil.get_terminal_size((80, 25)).lines != self.rows:
            self.draw()
            return True
        return False

    def disable(self):
        if self.active:
            sys.stdout.write("\033[r")               # 스크롤 영역 해제
            sys.stdout.flush()
            self.active = False


HEADER = Header()
atexit.register(HEADER.disable)


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


def build_header(ports, port, relay):
    """상단 고정 표시할 포트 체크 정보 (6줄)."""
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
            self.status = f"{C_GREEN}{self.port}{C_RESET} @{self.baud}bps  {C_GREEN}연결됨{C_RESET}"
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
                self.ser.write(RELAY_ON)
            print(C_CYAN + f">> 릴레이 진동 ON ({self.duration:.1f}s)" + C_RESET)
            threading.Timer(self.duration, self._off).start()
        except Exception as e:
            print(C_RED + f"릴레이 전송 실패: {e}" + C_RESET)

    def _off(self):
        try:
            with self._lock:
                if self.connected:
                    self.ser.write(RELAY_OFF)
        except Exception:
            pass

    def close(self):
        try:
            if self.connected:
                with self._lock:
                    self.ser.write(RELAY_OFF)
                    self.ser.close()
        except Exception:
            pass


def _send_esp(ser, cmd):
    cmd = cmd.strip()
    if not cmd:
        return
    if cmd.lower() in ("q", "quit", "exit"):
        HEADER.disable()      # os._exit는 atexit를 건너뛰므로 직접 해제
        os._exit(0)
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
        if ch == "\x03":             # Ctrl+C
            if relay:
                relay.close()
            HEADER.disable()
            os._exit(0)
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
    relay = Relay(serial, relay_port)
    relay.connect()

    # 포트 체크 정보를 화면 상단에 고정 (아래쪽만 스크롤)
    HEADER.set(build_header(ports, port, relay))

    threading.Thread(target=input_thread, args=(ser, relay), daemon=True).start()

    last_stat = time.time()
    count = 0
    hz = 0
    steer_ok = True     # 조향 센서 인식 여부 (펌웨어 v5.8: 실패 시 str=0 고정 송신)
    line_open = False   # 데이터 상태 줄이 \r 덮어쓰기 중인지

    def print_msg(msg):
        """상태 줄 덮어쓰기 중이면 줄바꿈 후 메시지 출력 (출력 섞임 방지)"""
        nonlocal line_open
        if line_open:
            print()
            line_open = False
        print(msg)

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
                str_out = f"조향:{strv:6.1f}°"
            else:
                str_out = C_RED + "조향:센서없음(0)" + C_RESET
            line_out = (
                f"[{ts}] "
                f"RPM:{rpm:6.1f}  "
                f"{spd_c}속도:{spd:5.1f}km/h{C_RESET}  "
                f"{str_out}  "
                f"{fmt_flag('브레이크', d.get('brk'))}  "
                f"{fmt_flag('O버튼', d.get('o'), C_GREEN)}  "
                f"{fmt_flag('X버튼', d.get('x'), C_YELLOW)}  "
                f"{C_DIM}{hz:4.0f}Hz{C_RESET}"
            )
            # 같은 줄에 덮어쓰기 (스크롤 방지), 상태 변화 시에만 새 줄
            print("\r" + line_out + " " * 4, end="", flush=True)
            line_open = True
        else:
            print_msg(f"[{ts}] {line}")


if __name__ == "__main__":
    try:
        main()
    except KeyboardInterrupt:
        HEADER.disable()
        print("\n종료합니다.")
