# 자전거 시뮬레이터 — Unity 시리얼 통신 가이드

**펌웨어**: bicycle_sim_x v5.8 (ESP32-S3 단독, 센서 + 진동 통합)  
**대상**: 빛고을국민안전체험관 / FLUXION  
**작성일**: 2026.06

---

## 1. 연결 설정

Unity는 **COM 포트 1개**만 사용합니다. (센서·진동 통합)

| 항목 | 값 |
|------|----|
| 포트 | `config.ini` → `PortName` (예: COM12) |
| 보드레이트 | **115,200 bps** |
| 데이터 비트 | 8 |
| 패리티 | None |
| 정지 비트 | 1 |
| 줄 끝 문자 | `\n` (LF) |
| 전송 주기 | **50 Hz** (20 ms마다 1 패킷) |

---

## 2. ESP32 → Unity (출력)

### 2-1. 데이터 형식

매 20 ms마다 ESP32가 아래 JSON 한 줄을 전송합니다.

```
{"id":1,"rpm":80.0,"spd":20.0,"str":-5.2,"brk":0,"o":0,"x":0}
```

줄 끝은 `\n`(LF) 한 문자입니다. `\r\n`이 아닙니다.

### 2-2. 필드 정의

| 필드 | 타입 | 범위 | 설명 |
|------|------|------|------|
| `id` | int | 1 | 스테이션 ID (`STATION_ID` 상수값) |
| `rpm` | float | 0.0 ~ 300.0 | 페달 케이던스 (분당 회전수) |
| `spd` | float | 0.0 ~ 75.0 | 환산 속도 (km/h) · `rpm × 0.25` |
| `str` | float | -45.0 ~ 45.0 | 핸들 조향각 (도) · DMP Yaw 기반, 왼쪽 음수 / 오른쪽 양수 |
| `brk` | int | 0 / 1 | 브레이크 (1 = 당김) |
| `o` | int | 0 / 1 | O 버튼 (1 = 눌림) |
| `x` | int | 0 / 1 | X 버튼 (1 = 눌림) |

### 2-3. 특수 출력 (이벤트성)

| 메시지 | 발생 시점 |
|--------|-----------|
| `{"debug":"DMP v5.8 Ready. Stabilizing for 3s..."}` | 부팅 완료, DMP 안정화 시작 |
| `{"debug":"DMP Stabilized"}` | DMP 안정화 완료 (부팅 후 약 3초) |
| `{"debug":"Connect failed"}` | IMU 연결 실패 |
| `{"debug":"DMP Init failed. Check ICM_20948_USE_DMP in library."}` | DMP 초기화 실패 |
| `{"debug":"Sensor enable failed"}` | 센서 활성화 실패 |
| `{"debug":"ODR set failed"}` | ODR 설정 실패 |
| `{"calibrated":true,"center":0.0}` | 조향 기준점 캘리브레이션 완료 |
| `{"magcal":"not_required_in_dmp"}` | M 명령 수신 응답 |

---

## 3. Unity → ESP32 (입력)

모든 명령은 **같은 COM 포트**로 전송합니다. 형식: `ASCII + 숫자 + \n`

```
V1\n   ← 진동 패턴 1
B1\n   ← 브레이크 ON
S2\n   ← RGB LED 상태
C\n    ← 조향 캘리브레이션
```

### 3-1. V — 진동 패턴 (IRF520 모듈 → 진동 모터)

| 명령 | 패턴명 | 내용 | 총 시간 |
|------|--------|------|---------|
| `V0\n` | 정지 | 진동 즉시 중단 | 즉시 |
| `V1\n` | DANGER | 강진동 1회 | 700 ms |
| `V2\n` | SUCCESS | 중진동 2회 | 380 ms |
| `V3\n` | CORRECT | 단진동 1회 | 100 ms |
| `V4\n` | WRONG | 강진동 1회 | 450 ms |
| `V5\n` | WALK | 3회 반복 | 560 ms |
| `V6\n` | READY | DMP 안정화 완료 알림 | 260 ms |

### 3-2. B — 브레이크 피드백

| 명령 | 동작 |
|------|------|
| `B1\n` | 브레이크 당김 — 약진동 연속 (PWM 180) |
| `B0\n` | 브레이크 해제 — 진동 정지 |

> V 명령이 B보다 우선합니다. 패턴 재생 중 B 상태는 패턴 종료 후 적용됩니다.

### 3-3. S — RGB LED 상태

| 명령 | 상태 | LED 색상 |
|------|------|----------|
| `S0\n` | 대기 (IDLE) | 흰색 느리게 깜박 |
| `S1\n` | 주행 중 (RUNNING) | 속도 > 1 km/h면 초록, 정지면 흰색 깜박 |
| `S2\n` | 이벤트 (EVENT) | 빨간색 |
| `S3\n` | 퀴즈 (QUIZ) | 보라색 깜박 |

> 브레이크를 잡으면 상태와 무관하게 주황색으로 오버라이드됩니다.

### 3-4. C — 조향 기준점 캘리브레이션

핸들을 **정면(직진) 위치**에 놓은 상태에서 전송합니다.  
응답: `{"calibrated":true,"center":0.0}`

### 3-5. M — 지자기 캘리브레이션 (불필요)

v5.8은 DMP Quat6(Game Rotation Vector)를 사용하므로 지자기 보정이 필요 없습니다.  
하위 호환을 위해 유지되며 응답만 반환합니다: `{"magcal":"not_required_in_dmp"}`

---

## 4. 캘리브레이션 절차

### 4-1. 첫 설치 시 (최초 1회)

#### STEP 1 — 부팅 확인

**정상 부팅 흐름:**
```
← {"debug":"DMP v5.8 Ready. Stabilizing for 3s..."}
   (파란 LED 점등 — 약 3초 대기)
← {"debug":"DMP Stabilized"}
   (LED 소등 → 이후 주행 상태에 따라 색상 변경)
← {"id":1,"rpm":0.0,"spd":0.0,"str":0.0,"brk":0,"o":0,"x":0}
   ...
```

**이상 부팅 출력:**
```
{"debug":"Connect failed"}           ← ICM-20948 연결 실패, 배선 점검
{"debug":"DMP Init failed. ..."}     ← 라이브러리 ICM_20948_USE_DMP 설정 확인
{"debug":"Sensor enable failed"}     ← 센서 활성화 실패
{"debug":"ODR set failed"}           ← ODR 설정 실패
```

#### STEP 2 — 조향 기준점 캘리브레이션

```
① 핸들을 정면(직진) 방향에 정확히 맞춤
② C\n 전송
③ {"calibrated":true,"center":0.0} 수신 확인
```

#### STEP 3 — 전체 입력 동작 확인

| 테스트 항목 | 조작 | 확인 | 기대값 |
|------------|------|------|--------|
| 페달 센서 | 페달을 천천히 밟음 | `rpm`, `spd` | rpm > 0, spd > 0 |
| 핸들 조향 | 핸들을 좌우로 기울임 | `str` | -45 ~ +45 범위 내 변화 |
| 브레이크 | 레버 당김 | `brk` + 체감 진동 | 0 → 1, 약진동 연속 |
| O 버튼 | O 버튼 누름 | `o` | 0 → 1 |
| X 버튼 | X 버튼 누름 | `x` | 0 → 1 |
| 진동 패턴 | `V1\n` 전송 | 체감 진동 | 700 ms 강진동 |
| RGB LED | `S2\n` 전송 | LED 색상 | 빨간색 |

#### STEP 4 — 초기 상태 설정

```
S0\n   ← 대기 상태 (흰색 깜박)로 설정
```

#### 첫 설치 체크리스트

```
[ ] STEP 1  부팅 시 "DMP Stabilized" 수신 확인 (약 3초)
[ ] STEP 2  조향 기준점 설정 완료 (str = 0.0° at 직진)
[ ] STEP 3  페달 / 핸들 / 브레이크 / 버튼 전체 동작 확인
[ ] STEP 3  진동 모터 동작 확인 (V1 전송)
[ ] STEP 3  RGB LED 색상 변경 확인
[ ] STEP 4  초기 상태 S0 설정
```

---

### 4-2. 매 세션 시작 시

```
1. ESP32 전원 ON
   → 파란 LED 약 3초 → "DMP Stabilized" 수신 = 안정화 완료

2. 핸들 정면 위치 고정 후 C\n  →  {"calibrated":true,"center":0.0} 수신

3. S1\n  →  주행 대기 상태 설정

4. 시뮬레이션 시작
```

| 항목 | 전원 OFF 후 유지 여부 |
|------|----------------------|
| 조향 기준점 (g_yawOffset) | ❌ 초기화 (매 세션 재수행) |
| 펌웨어 설정 (핀, 주파수 등) | ✅ 플래시에 저장됨 |

---

## 5. Unity C# 구현 예시

### 5-1. 시리얼 수신/송신 (COM 포트 1개)

```csharp
using System;
using System.IO.Ports;
using UnityEngine;

[Serializable]
public class BikeData
{
    public int   id;
    public float rpm;
    public float spd;
    public float str;   // -45 ~ 45
    public int   brk;
    public int   o;
    public int   x;
}

public class BikeSerial : MonoBehaviour
{
    public string portName = "COM12";
    public int    baudRate = 115200;

    SerialPort _port;
    string     _buffer = "";

    public BikeData Data { get; private set; } = new BikeData();

    void Start()
    {
        _port = new SerialPort(portName, baudRate)
        {
            ReadTimeout  = 20,
            WriteTimeout = 100,
            NewLine      = "\n"
        };
        _port.Open();
    }

    void Update()
    {
        if (_port == null || !_port.IsOpen) return;
        try
        {
            while (_port.BytesToRead > 0)
            {
                char c = (char)_port.ReadChar();
                if (c == '\n') { ProcessLine(_buffer.Trim()); _buffer = ""; }
                else            _buffer += c;
            }
        }
        catch (TimeoutException) { }
    }

    void ProcessLine(string line)
    {
        if (string.IsNullOrEmpty(line)) return;
        if (line.Contains("\"debug\"") || line.Contains("\"calibrated\"") || line.Contains("\"magcal\""))
        { Debug.Log($"[Bike] {line}"); return; }
        try { Data = JsonUtility.FromJson<BikeData>(line); }
        catch (Exception e) { Debug.LogWarning($"[Bike] parse error: {e.Message}"); }
    }

    void OnDestroy() => _port?.Close();

    void Send(string cmd)
    {
        if (_port?.IsOpen != true) return;
        try { _port.WriteLine(cmd); }
        catch (Exception e) { Debug.LogWarning($"[Bike] 송신 실패: {e.Message}"); }
    }

    // 진동 (ESP32 GPIO2 → IRF520 → 모터)
    public void SetVibration(int pattern) => Send($"V{pattern}");
    public void SetBrake(bool on)         => Send(on ? "B1" : "B0");

    // 센서 제어
    public void SetRGBState(int state)    => Send($"S{state}");
    public void CalibrateSteer()          => Send("C");
}
```

### 5-2. 사용 예시

```csharp
float steer   = bike.Data.str;      // -45 ~ 45도
float speed   = bike.Data.spd;      // km/h
bool  braking = bike.Data.brk == 1;

bike.SetVibration(1);   // V1: 위험 경고
bike.SetVibration(2);   // V2: 성공
bike.SetBrake(true);    // B1: 브레이크 피드백
bike.SetBrake(false);   // B0: 브레이크 해제
bike.SetRGBState(2);    // S2: 빨간 LED
bike.SetRGBState(1);    // S1: 주행 복귀
```

---

## 6. 오류 대응

| 증상 | 원인 | 조치 |
|------|------|------|
| 시리얼 포트 열리지 않음 | 드라이버 미설치 / 포트 번호 오류 | 장치 관리자 확인, CH340/CP2102 드라이버 설치 |
| `"Connect failed"` 반복 출력 | IMU 배선 불량 | SDA(GPIO17)·SCL(GPIO18)·VCC·GND 배선 점검 |
| `"DMP Init failed..."` | 라이브러리 설정 오류 | `ICM_20948_C.h`에서 `#define ICM_20948_USE_DMP` 주석 해제 확인 |
| `str` 값이 항상 0 | 캘리브레이션 미실시 또는 DMP 미안정화 | "DMP Stabilized" 수신 후 `C\n` 전송 |
| `str` 값이 드리프트 | DMP 안정화 전 캘리브레이션 | 3초 대기 후 재캘리브레이션 |
| `rpm` 값이 간헐적으로 급등 | 노이즈 | `spd` 사용 시 `Mathf.Clamp` 추가 권장 |
| JSON 파싱 오류 | 부팅 직후 깨진 첫 줄 | `id` 필드 확인 후 사용 (`if (data.id != 1) return`) |
| 진동이 작동 안 함 | IRF520 모듈 배선 오류 | SIG(GPIO2)·VCC(5V)·GND·V+·OUT 배선 점검 |
| 진동이 약함 | IRF520 게이트 전압 부족 | 3.3V 신호로 동작 확인, 모터 전원 5V 확인 |

---

## 7. 타이밍 다이어그램

```
ESP32 부팅
  │
  ├─ ICM-20948 I2C 연결 확인
  ├─ DMP 초기화 (Quat6 활성화, ODR 설정)
  ├─ 파란 LED 점등 + "DMP v5.8 Ready. Stabilizing for 3s..." 출력
  ├─ DMP 안정화 대기 (3초)
  │   └─ "DMP Stabilized" 출력 → V6(READY) 진동 → 파란 LED 소등
  │
  └─ loop() 시작 ─────────────────────────────────
       │ 20ms마다
       ├─ JSON 전송 (rpm, spd, str, brk, o, x)
       ├─ 시리얼 수신 처리 (V/B/C/M/S 명령)
       ├─ 진동 시퀀서 업데이트 (GPIO2 PWM)
       └─ RGB LED 업데이트

Unity 측
  │
  ├─ Update()에서 매 프레임 버퍼 읽기
  ├─ \n 단위로 JSON 파싱
  └─ 이벤트 발생 시 V/B/S/C 명령 전송 (동일 포트)
```

---

*bicycle_sim_x v5.8 · 빛고을국민안전체험관 · FLUXION · 2026.06*
