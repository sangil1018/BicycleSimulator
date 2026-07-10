# ================================================================
#  키오스크 절전 해제 설정 스크립트 (관리자 권한 필요)
#  자전거 안전체험 시뮬레이터 — ESP32 시리얼 연결 유지용
#
#  장시간 유휴 시 Windows의 USB 선택적 절전 / 시스템 대기 / 장치
#  전원 관리가 USB-시리얼 링크를 끊어 PAS 센서가 무반응이 되는 것을
#  방지한다. 배포 PC마다 1회 실행. (실행 후 재부팅 권장)
#
#  실행: 관리자 PowerShell에서
#    powershell -ExecutionPolicy Bypass -File .\kiosk_power_setup.ps1
# ================================================================

# ── 관리자 권한 확인 ─────────────────────────────────────────────
$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin)
{
    Write-Host "[오류] 관리자 권한으로 실행하세요." -ForegroundColor Red
    exit 1
}

Write-Host "=== 1. 시스템 절전/최대 절전 비활성화 ===" -ForegroundColor Cyan
powercfg /hibernate off                     # 최대 절전 끄기
powercfg /change standby-timeout-ac 0       # 대기 모드 진입 없음
powercfg /change hibernate-timeout-ac 0
powercfg /change disk-timeout-ac 0          # 디스크 절전 없음
powercfg /change monitor-timeout-ac 0       # 모니터 꺼짐 없음 (키오스크)

Write-Host "=== 2. USB 선택적 절전(selective suspend) 비활성화 ===" -ForegroundColor Cyan
# 서브그룹: USB 설정 / 항목: USB 선택적 절전 설정
$SUB_USB  = "2a737441-1930-4402-8d77-b2bebba308a3"
$USB_SUSP = "48e6b7a6-50f5-4782-a5d4-53bb8f07e226"
powercfg /setacvalueindex SCHEME_CURRENT $SUB_USB $USB_SUSP 0
powercfg /setdcvalueindex SCHEME_CURRENT $SUB_USB $USB_SUSP 0
powercfg /setactive SCHEME_CURRENT

Write-Host "=== 3. USB 장치별 전원 관리 해제 ===" -ForegroundColor Cyan
# 장치 관리자의 "전원 절약을 위해 컴퓨터가 이 장치를 끌 수 있음" 해제와 동일.
# USB 허브·컨트롤러와 USB-시리얼(CP210x/CH340/CDC) 장치 전체에 적용한다.
$count = 0
Get-CimInstance -Namespace root/wmi -ClassName MSPower_DeviceEnable -ErrorAction SilentlyContinue |
    Where-Object { $_.InstanceName -match 'USB' -and $_.Enable } |
    ForEach-Object {
        try
        {
            $_.Enable = $false
            Set-CimInstance -CimInstance $_ -ErrorAction Stop
            $count++
        }
        catch { }
    }
Write-Host "  전원 관리 해제된 USB 장치: $count 개"

Write-Host "=== 4. 빠른 시작(Fast Startup) 비활성화 ===" -ForegroundColor Cyan
# 빠른 시작은 종료 후 재부팅 시 USB 장치 상태를 비정상으로 남길 수 있음
Set-ItemProperty -Path 'HKLM:\SYSTEM\CurrentControlSet\Control\Session Manager\Power' -Name HiberbootEnabled -Value 0 -Type DWord

Write-Host ""
Write-Host "완료. 재부팅 후 적용을 확인하세요." -ForegroundColor Green
Write-Host "확인 명령: powercfg /query SCHEME_CURRENT $SUB_USB $USB_SUSP"
