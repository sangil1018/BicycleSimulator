# -*- coding: utf-8 -*-
# ================================================================
#  자전거 시뮬레이터 ESP32-S3 시리얼 데이터 모니터
#  사용법: check_serial.bat 더블클릭  또는  python serial_monitor.py [COM포트]
#  - 115200bps, JSON 라인 수신 (50Hz)
#  - 명령 전송 가능: C(조향 보정), V0~V6(진동), B0/B1(브레이크), S0~S3, P50~P300
# ================================================================
import sys
import os
import json
import time
import threading

BAUD = 115200

# Windows 콘솔 ANSI 색상 활성화
os.system("")

C_RESET  = "\033[0m"
C_DIM    = "\033[90m"
C_GREEN  = "\033[92m"
C_YELLOW = "\033[93m"
C_RED    = "\033[91m"
C_CYAN   = "\033[96m"
C_BOLD   = "\033[1m"


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


def pick_port(list_ports):
    ports = list(list_ports.comports())
    if not ports:
        print(C_RED + "연결된 COM 포트가 없습니다. USB 케이블을 확인하세요." + C_RESET)
        sys.exit(1)

    # ESP32-S3 우선 자동 선택 (USB CDC / CP210x / CH34x 등), 블루투스 가상 포트는 제외
    esp_keywords = ("cp210", "ch34", "ch910", "usb", "uart", "jtag")
    candidates = [
        p for p in ports
        if "bluetooth" not in (p.description or "").lower()
        and any(k in (p.description or "").lower() for k in esp_keywords)
    ]

    print(C_BOLD + "사용 가능한 포트:" + C_RESET)
    for i, p in enumerate(ports):
        mark = " <- ESP32 추정" if p in candidates else ""
        print(f"  [{i}] {p.device} - {p.description}{C_CYAN}{mark}{C_RESET}")

    if len(ports) == 1:
        print(f"\n포트가 1개뿐이므로 {ports[0].device} 자동 선택")
        return ports[0].device
    if len(candidates) == 1:
        print(f"\nESP32로 추정되는 {candidates[0].device} 자동 선택")
        return candidates[0].device

    sel = input("\n포트 번호 입력 (엔터 = 0): ").strip()
    idx = int(sel) if sel.isdigit() and int(sel) < len(ports) else 0
    return ports[idx].device


def input_thread(ser):
    """콘솔에서 명령을 입력받아 ESP32로 전송 (예: C, V1, B1)"""
    while True:
        try:
            cmd = input()
        except (EOFError, KeyboardInterrupt):
            return
        cmd = cmd.strip()
        if not cmd:
            continue
        if cmd.lower() in ("q", "quit", "exit"):
            os._exit(0)
        try:
            ser.write((cmd + "\n").encode())
            print(C_CYAN + f">> 전송: {cmd}" + C_RESET)
        except Exception as e:
            print(C_RED + f"전송 실패: {e}" + C_RESET)


def fmt_flag(name, val, on_color=C_RED):
    if val:
        return on_color + C_BOLD + f"{name}:ON " + C_RESET
    return C_DIM + f"{name}:off" + C_RESET


def main():
    serial, list_ports = ensure_pyserial()

    port = sys.argv[1] if len(sys.argv) > 1 else pick_port(list_ports)

    print(f"\n{port} @ {BAUD}bps 연결 중...")
    try:
        ser = serial.Serial(port, BAUD, timeout=1)
    except Exception as e:
        print(C_RED + f"포트 열기 실패: {e}" + C_RESET)
        print("Unity나 아두이노 시리얼 모니터가 포트를 점유 중인지 확인하세요.")
        sys.exit(1)

    print(C_GREEN + "연결됨!" + C_RESET)
    print(C_DIM + "-" * 70 + C_RESET)
    print("명령 입력 후 엔터로 전송 가능: C=조향보정  V1~V6=진동  V0=진동정지")
    print("                              B1/B0=브레이크진동  q=종료")
    print(C_DIM + "-" * 70 + C_RESET)

    threading.Thread(target=input_thread, args=(ser,), daemon=True).start()

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
        print("\n종료합니다.")
