@echo off
:: 한글 깨짐 방지를 위해 UTF-8 코드 페이지로 설정합니다.
chcp 65001 >nul

:: Python 설치 여부 및 실행 가능 여부를 확인합니다.
python --version >nul 2>&1
if %errorlevel% neq 0 (
    echo [오류] Python이 설치되어 있지 않거나 환경 변수 PATH에 등록되지 않았습니다.
    echo Python 설치 시 Add Python to PATH 옵션을 선택했는지 확인해 주세요.
    pause
    exit /b 1
)

:: 빌드에 필요한 의존성 패키지 PyInstaller, PySerial, Pillow가 설치되어 있는지 확인하고 설치합니다.
echo [정보] 필요한 패키지 PyInstaller, PySerial, Pillow의 설치를 진행합니다...
python -m pip install --upgrade pip
python -m pip install pyinstaller pyserial pillow

:: serial_monitor_icon.png가 존재할 경우 .ico 파일로 자동 변환합니다.
if exist serial_monitor_icon.png (
    echo [정보] serial_monitor_icon.png 파일을 감지했습니다. .ico 파일로 변환합니다...
    python -c "from PIL import Image; img = Image.open('serial_monitor_icon.png'); img.save('serial_monitor.ico', format='ICO')"
)

:: PyInstaller를 사용하여 serial_monitor.py를 단일 실행 파일로 빌드합니다.
:: clean 옵션을 사용하여 이전 빌드 캐시를 지우고 깨끗하게 다시 빌드합니다.
:: 아이콘 파일(serial_monitor.ico)이 존재하는 경우 빌드 옵션에 추가합니다.
set "ICON_OPT="
if exist serial_monitor.ico (
    set "ICON_OPT=--icon=serial_monitor.ico"
)

echo [정보] serial_monitor.py 빌드를 시작합니다...
python -m PyInstaller --onefile --clean %ICON_OPT% serial_monitor.py

:: 빌드 완료 후 안내 메시지를 출력합니다.
echo [완료] 빌드가 완료되었습니다. dist 폴더 내의 실행 파일을 확인해 주세요.
pause
