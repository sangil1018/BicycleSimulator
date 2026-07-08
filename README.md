# 자전거 안전체험 시뮬레이터
빛고을국민안전체험관 · FLUXION · 2026.05

---

### 하드웨어 (ESP32-S3)

```
PAS     빨간→3V3  노랑→GND  파랑→GPIO1
진동L   IN→GPIO2   (브레이크 피드백 전용, GPIO2 = B1/B0 명령)
진동R   IN→GPIO42  (레거시, Unity 미사용)
AS5600  SDA→GPIO11  SCL→GPIO12
브레이크L→GPIO4  브레이크R→GPIO5
O버튼→GPIO6  X버튼→GPIO7  O LED→GPIO9  X LED→GPIO10
RGB LED  GPIO48 (WS2812 내장)
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

### 참고 문서

- 콘텐츠 구성 및 Timeline 이벤트 설정 → `CONTENT_GUIDE.md`
- config.ini 설정 → `CONTENT_GUIDE.md` §2
- 시리얼 통신 프로토콜 (ESP32 센서 + 진동 릴레이) → `Hardware/Unity_시리얼_통신_가이드.md`
