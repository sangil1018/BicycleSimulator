# 자전거 안전체험 시뮬레이터
빛고을국민안전체험관 · FLUXION · 2026.05

---

### 하드웨어 (ESP32-S3, 펌웨어 `bicycle_sim_x` v6.0)

```
PAS(케이던스)   빨간→3V3  노랑→GND  파랑→GPIO1
진동 모터       IRF520 SIG→GPIO2   (브레이크 피드백 B1/B0)
조향 ICM-20948  SDA→GPIO17  SCL→GPIO18  (AD0=HIGH, I2C 0x69)
브레이크        GPIO4
O버튼→GPIO6  X버튼→GPIO7
RGB LED         GPIO48 (WS2812 내장)
```

> 이벤트/퀴즈 진동(위험·성공·정오답 등)은 위 ESP32 GPIO가 아니라 **별도 USB 릴레이 모듈**(COM 포트 별도, `config.ini` → `RelayPortName`)로 처리합니다. 자세한 내용은 `Hardware/Unity_시리얼_통신_가이드.md` §3-6 참고.

### 펌웨어 수정 항목

```cpp
#define STATION_ID      1     // 스테이션 번호 1~6
#define CADENCE_TO_KPH  0.25f // 참고용 spd 필드 계수 — Unity는 이 값을 사용하지 않음 (아래 참고)
```

### 속도 매핑

Unity는 ESP32가 보내는 `spd` 필드를 더 이상 사용하지 않고, `rpm`(케이던스)만으로 자체 계산합니다.

```
SpeedKph = CadenceRPM × MetersPerRevolution × 0.06   ← config.ini에서 조정 (CONTENT_GUIDE.md §2.1)
BaseSpeedKph = 15.0 (기본값) → 이 속도에서 Timeline 1.0× 재생
```

펌웨어의 `CADENCE_TO_KPH`/`spd` 필드는 `serial_monitor.py` 등 하드웨어 단독 점검용으로만 남아있습니다.

### 씬 로딩 (프리로드)

레벨 전환 속도를 위해 레벨 씬의 에셋 의존성 전체를 홈 화면 뒤에서 미리 로드해 상주시킵니다 (`PreloadManager`, HOME_GUIDE.md §3-1).

> ⚠️ **레벨 씬의 에셋 구성을 바꾸면 에디터 메뉴 `Tools → Build Preload Manifests`를 재실행한 후 빌드**하세요. 누락하면 해당 에셋만 전환이 다시 느려집니다.

### 배포 PC 설정 (키오스크)

- `Hardware/kiosk_power_setup.bat` 더블클릭 (관리자 자동 승격) — USB 선택적 절전/장치 전원 관리/시스템 대기 해제. 장시간 유휴 후 센서 무반응 예방을 위해 **배포 PC마다 1회 필수**, 실행 후 재부팅 권장.
- 빌드 진단: `%USERPROFILE%\AppData\LocalLow\fluxion\BicycleSimulator\Player.log`에 로딩 시간·시리얼 연결 수명주기 로그가 남습니다. (일반 Debug.Log는 `Assets/Plugins/Debug.cs` 래퍼에 의해 빌드에서 제거되므로, 빌드에 남겨야 하는 로그는 `UnityEngine.Debug`를 직접 호출해야 합니다.)

### 하드웨어 점검 도구

- `Hardware/check_serial.bat` (serial_monitor.py) — 콘솔 시리얼 모니터: 실시간 센서 표시, 릴레이 진동 테스트(`v`), keep-alive/자동 재연결, PAS 진단(pc/pl)
- `Hardware/hardware_signal_tester.html` — Chrome/Edge용 GUI 테스터: 신호 시각화 + 릴레이/ESP32 진동 체크 (최초 1회 포트 허용 후 자동 연결)

### 참고 문서

- 콘텐츠 구성 및 Timeline 이벤트 설정 → `CONTENT_GUIDE.md`
- config.ini 설정 → `CONTENT_GUIDE.md` §2
- 홈 씬 구성 / 씬 전환·프리로드 → `HOME_GUIDE.md`
- 시리얼 통신 프로토콜 (ESP32 센서 + 진동 릴레이, keep-alive/재연결/PAS 진단) → `Hardware/Unity_시리얼_통신_가이드.md`
