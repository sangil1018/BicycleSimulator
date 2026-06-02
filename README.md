# 자전거 안전체험 시뮬레이터 v4
## 빛고을국민안전체험관 · FLUXION · 2026.05

### v4 주요 변경
| 항목 | 내용 |
|------|------|
| 보드 | ESP32-S3 22핀 커스텀 (GPIO48 WS2812 내장) |
| 속도 센서 | PAS 더블홀 12자석 (3선: 빨간·노랑·파랑) |
| 속도 계산 | 케이던스 RPM × 0.25 = 가상 km/h |
| 진동 모듈 | TZT PWM (3.0~5.3V, MOS 드라이버) — GPIO2/IO42 |
| 상태 표시 | GPIO48 RGB: 주행=초록 이벤트=빨강 퀴즈=보라 |

### 핵심 배선
```
PAS  빨간→3V3  노랑→GND  파랑→GPIO1
진동L  VCC→3V3  GND→GND  IN→GPIO2
진동R  VCC→3V3  GND→GND  IN→GPIO42
AS5600  VCC→3V3  SDA→GPIO11  SCL→GPIO12
브레이크L→GPIO4  브레이크R→GPIO5
O버튼→GPIO6  X버튼→GPIO7
O LED→GPIO9  X LED→GPIO10
USB-C→PC (전원+시리얼)
```

### Arduino IDE 라이브러리
```
Adafruit NeoPixel  (RGB LED 사용 시 필수)
```

### 펌웨어 수정 항목
```cpp
#define STATION_ID       1    // 스테이션 번호 1~6
#define CADENCE_TO_KPH  0.25f // 속도 계수 (현장 조정)
```

### Unity 에디터 테스트
```
케이던스 슬라이더: 0~120 RPM
  60 RPM = 15 km/h (1.0× 재생)
  80 RPM = 20 km/h
 120 RPM = 30 km/h
```
