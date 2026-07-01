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
#define CADENCE_TO_KPH  0.25f // 속도 계수 (현장 조정)
```

### 속도 매핑

```
CadenceRPM × 0.25 = SpeedKph
60 RPM = 15 km/h → Timeline 1.0× 재생 (BaseSpeedKph 기본값)
```

### 참고 문서

- 콘텐츠 구성 및 Timeline 이벤트 설정 → `CONTENT_GUIDE.md`
- config.ini 설정 → `CONTENT_GUIDE.md` §2
- 시리얼 통신 프로토콜 (ESP32 센서 + 진동 릴레이) → `Hardware/Unity_시리얼_통신_가이드.md`
