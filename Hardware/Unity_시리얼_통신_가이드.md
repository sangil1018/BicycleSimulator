# 자전거 시뮬레이터 — Unity 시리얼 통신 가이드

**펌웨어**: bicycle_sim_v4.1  
**대상**: 빛고을국민안전체험관 / FLUXION  
**작성일**: 2026.05

---

## 1. 연결 설정

| 항목 | 값 |
|------|----|
| 포트 | Windows 장치 관리자에서 확인 (예: COM3) |
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
{"id":1,"rpm":80.0,"spd":20.0,"str":-5.2,"brkL":0,"brkR":0,"o":0,"x":0}
```

줄 끝은 `\n`(LF) 한 문자입니다. `\r\n`이 아닙니다.

### 2-2. 필드 정의

| 필드 | 타입 | 범위 | 설명 |
|------|------|------|------|
| `id` | int | 1 | 스테이션 ID (고정값 1) |
| `rpm` | float | 0.0 ~ 300.0 | 페달 케이던스 (분당 회전수) |
| `spd` | float | 0.0 ~ 75.0 | 환산 속도 (km/h) · rpm × 0.25 |
| `str` | float | -45.0 ~ 45.0 | 핸들 조향각 (도) · 왼쪽 음수 / 오른쪽 양수 |
| `brkL` | int | 0 / 1 | 브레이크 좌 (1 = 당김) |
| `brkR` | int | 0 / 1 | 브레이크 우 (1 = 당김) |
| `o` | int | 0 / 1 | O 버튼 (1 = 눌림) |
| `x` | int | 0 / 1 | X 버튼 (1 = 눌림) |

### 2-3. 특수 출력 (이벤트성)

정상 JSON 외에 아래 메시지가 단발로 출력될 수 있습니다.

| 메시지 | 발생 시점 |
|--------|-----------|
| `{"error":"ICM-20948 init failed"}` | 부팅 시 IMU 초기화 실패 |
| `{"i2c_scan":[68,69,...]}` | IMU 실패 후 I2C 스캔 결과 (16진수) |
| `{"calibrated":true,"center":0.0}` | 조향 캘리브레이션 완료 |
| `{"magcal":"start","dur":5}` | 지자기 캘리브레이션 시작 |
| `{"magcal":"done","ox":12.3,"oy":-4.1,"oz":8.7}` | 지자기 캘리브레이션 완료 |

---

## 3. Unity → ESP32 (입력)

명령은 **ASCII 문자 + 선택적 숫자 + `\n`** 형식입니다.  
최대 16자까지 수신합니다.

```
V1\n        ← 진동 패턴 1 재생
S2\n        ← RGB 상태 2 설정
C\n         ← 조향 캘리브레이션
M\n         ← 지자기 캘리브레이션
```

### 3-1. V — 진동 (Vibration)

```
V0  진동 정지
V1  위험 경고    ████████████░░░  700ms 강진동
V2  성공         ██░░░██░  짧은 2회 진동
V3  정답         █░  100ms 중진동
V4  오답         ████████░  450ms 강진동
V5  보행 주의    ██░██░██░  3회 반복
```

| 명령 | 패턴 | 총 시간 |
|------|------|---------|
| `V0\n` | 정지 | 즉시 |
| `V1\n` | 강진동 1회 | 700 ms |
| `V2\n` | 중진동 2회 | 380 ms |
| `V3\n` | 중진동 1회 | 100 ms |
| `V4\n` | 강진동 1회 | 450 ms |
| `V5\n` | 3회 반복 | 560 ms |

> **주의**: 진동 재생 중 새 V 명령을 보내면 즉시 덮어씌워집니다.  
> 브레이크 미입력 상태에서 진동이 없을 때만 브레이크 피드백(약진동)이 자동 재생됩니다.

### 3-2. S — RGB LED 상태 (Status)

| 명령 | 상태 | LED 색상 |
|------|------|----------|
| `S0\n` | 대기 (IDLE) | 회색 느리게 깜박 |
| `S1\n` | 주행 중 (RUNNING) | 속도 > 1 km/h면 초록, 정지면 회색 깜박 |
| `S2\n` | 이벤트 (EVENT) | 빨간색 |
| `S3\n` | 퀴즈 (QUIZ) | 보라색 깜박 |

> 브레이크를 잡으면 상태와 무관하게 주황색으로 오버라이드됩니다.

### 3-3. C — 조향 캘리브레이션

```
C\n
```

핸들을 **정면(직진) 위치**에 놓은 상태에서 전송합니다.  
현재 IMU yaw 값을 기준점(0°)으로 저장합니다.  
응답: `{"calibrated":true,"center":0.0}`

### 3-4. M — 지자기 캘리브레이션 (하드 아이언 보정)

```
M\n
```

**5초간** 핸들을 좌우로 천천히 완전히 회전시켜야 합니다.  
캘리브레이션 중 RGB는 파란색으로 표시되며 **진동이 멈춥니다**.

응답 흐름:
```
→ M\n 전송
← {"magcal":"start","dur":5}
   (5초 동안 핸들 좌우 회전)
← {"magcal":"done","ox":12.30,"oy":-4.10,"oz":8.70}
```

> 전원을 끄면 캘리브레이션 값이 초기화됩니다. 매 세션 시작 시 재캘리브레이션 권장.

---

## 4. 캘리브레이션 절차

### 4-1. 첫 설치 시 (최초 1회)

> 하드웨어 배선 완료 후, 시뮬레이션 프로그램을 처음 구동하기 전에 반드시 수행합니다.

---

#### STEP 1 — 시리얼 모니터 연결 확인

Arduino IDE 또는 별도 터미널(PuTTY 등)로 시리얼 모니터를 열고 ESP32에 전원을 넣습니다.

**정상 부팅 출력:**
```
{"calibrated":true,"center":0.0}
{"id":1,"rpm":0.0,"spd":0.0,"str":0.0,"brkL":0,"brkR":0,"o":0,"x":0}
{"id":1,"rpm":0.0, ...}
...
```

**이상 부팅 출력:**
```
{"error":"ICM-20948 init failed"}
{"i2c_scan":[36]}        ← ICM-20948 미감지, 배선 점검 필요
{"i2c_scan":[]}           ← SDA/SCL 단선
```

> 에러가 출력되면 **6. 오류 대응** 표를 참조하여 하드웨어를 점검한 뒤 재부팅합니다.

---

#### STEP 2 — 지자기 캘리브레이션 (하드 아이언 보정)

조향 드리프트를 없애기 위한 **1회성 환경 보정**입니다.  
설치 장소의 자기장 환경(금속 구조물, 전선 등)이 바뀌면 재수행합니다.

```
① 핸들을 자유롭게 움직일 수 있는 상태로 준비
② Unity 또는 시리얼 모니터에서 M\n 전송
③ {"magcal":"start","dur":5} 수신 확인 → LED 파란색으로 변경
④ 5초 동안 핸들을 왼쪽 끝 → 오른쪽 끝으로 천천히 완전 회전 (2~3회)
⑤ {"magcal":"done","ox":...,"oy":...,"oz":...} 수신 → LED 초록색 복귀
```

**완료 예시:**
```
← {"magcal":"start","dur":5}
   (5초 동안 핸들 좌우 완전 회전)
← {"magcal":"done","ox":12.30,"oy":-4.10,"oz":8.70}
```

> ox·oy·oz 값을 메모해 두면 이후 값과 비교해 환경 변화 여부를 판단할 수 있습니다.

---

#### STEP 3 — 조향 기준점 캘리브레이션

```
① 핸들을 정면(직진) 방향에 정확히 맞춤
② C\n 전송
③ {"calibrated":true,"center":0.0} 수신 확인
```

이후 `str` 값이 직진 시 0.0°, 왼쪽 최대 -45°, 오른쪽 최대 +45° 범위로 출력됩니다.

---

#### STEP 4 — 전체 입력 동작 확인

시리얼 모니터(또는 Unity 디버그 화면)에서 JSON 출력을 보면서 각 입력을 테스트합니다.

| 테스트 항목 | 조작 | 확인할 필드 | 기대값 |
|------------|------|------------|--------|
| 페달 센서 | 페달을 천천히 밟음 | `rpm`, `spd` | rpm > 0, spd > 0 |
| 핸들 조향 | 핸들을 좌우로 돌림 | `str` | -45 ~ +45 범위 내 변화 |
| 브레이크 좌 | 좌 브레이크 레버 당김 | `brkL` | 0 → 1 |
| 브레이크 우 | 우 브레이크 레버 당김 | `brkR` | 0 → 1 |
| O 버튼 | O 버튼 누름 | `o` | 0 → 1 |
| X 버튼 | X 버튼 누름 | `x` | 0 → 1 |
| 진동 모터 L+R | `V1\n` 전송 | 체감 진동 | 700ms 강진동 |
| RGB LED | `S2\n` 전송 | LED 색상 | 빨간색 |

---

#### STEP 5 — 초기 상태 설정

```
S0\n   ← 대기 상태 (회색 깜박)로 설정
```

이제 Unity 프로젝트와 연결할 준비가 완료되었습니다.

---

#### 첫 설치 체크리스트

```
[ ] STEP 1  부팅 시 에러 없음 확인
[ ] STEP 2  지자기 캘리브레이션 완료 (ox·oy·oz 값 기록)
[ ] STEP 3  조향 기준점 설정 완료 (str = 0.0° at 직진)
[ ] STEP 4  페달 / 핸들 / 브레이크 / 버튼 전체 동작 확인
[ ] STEP 4  진동 모터 좌·우 모두 진동 확인
[ ] STEP 4  RGB LED 색상 변경 확인
[ ] STEP 5  초기 상태 S0 설정
```

---

### 4-2. 매 세션 시작 시

전원을 끄면 지자기 캘리브레이션 값이 초기화됩니다. 매 운영 시작 시 아래를 수행합니다.

```
1. ESP32 전원 ON
   → 진동 패턴 + 초록 LED = 부팅 완료

2. M\n  →  핸들 좌우 5초 회전  →  magcal done 수신
   (지자기 보정 — 전원 OFF 시 초기화되므로 매번 필요)

3. 핸들 정면 위치 고정 후 C\n  →  calibrated 수신
   (조향 기준점 0° 재설정)

4. S1\n  →  주행 대기 상태 설정

5. 시뮬레이션 시작
```

| 항목 | 전원 OFF 후 유지 여부 |
|------|----------------------|
| 지자기 캘리브레이션 (ox·oy·oz) | ❌ 초기화 (매 세션 재수행) |
| 조향 기준점 (g_steerOffset) | ❌ 초기화 (매 세션 재수행) |
| 펌웨어 설정 (핀, 주파수 등) | ✅ 플래시에 저장됨 |

---

## 5. Unity C# 구현 예시

### 5-1. 시리얼 수신 (ESP32 → Unity)

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
    public int   brkL;
    public int   brkR;
    public int   o;
    public int   x;
}

public class BikeSerial : MonoBehaviour
{
    [Header("Serial")]
    public string portName = "COM3";
    public int    baudRate  = 115200;

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
            // 버퍼에 쌓인 바이트를 모두 읽어 줄 단위로 처리
            while (_port.BytesToRead > 0)
            {
                char c = (char)_port.ReadChar();
                if (c == '\n')
                {
                    ProcessLine(_buffer.Trim());
                    _buffer = "";
                }
                else
                {
                    _buffer += c;
                }
            }
        }
        catch (TimeoutException) { }
    }

    void ProcessLine(string line)
    {
        if (string.IsNullOrEmpty(line)) return;

        // 이벤트 메시지 처리
        if (line.Contains("\"error\""))
        {
            Debug.LogError($"[Bike] {line}");
            return;
        }
        if (line.Contains("\"magcal\"") || line.Contains("\"calibrated\""))
        {
            Debug.Log($"[Bike] {line}");
            return;
        }

        // 센서 데이터 파싱
        try
        {
            Data = JsonUtility.FromJson<BikeData>(line);
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[Bike] parse error: {e.Message} / {line}");
        }
    }

    void OnDestroy()
    {
        _port?.Close();
    }

    // ── Unity → ESP32 송신 ─────────────────────────────────────────

    public void Send(string cmd)
    {
        if (_port == null || !_port.IsOpen) return;
        _port.WriteLine(cmd);   // WriteLine이 \n을 자동 추가
    }

    public void SetVibration(int pattern)   => Send($"V{pattern}");
    public void SetRGBState(int state)      => Send($"S{state}");
    public void CalibrateSteer()            => Send("C");
    public void CalibrateMag()              => Send("M");
}
```

### 5-2. 사용 예시

```csharp
public class GameManager : MonoBehaviour
{
    public BikeSerial bike;

    void Update()
    {
        // 조향각 → 캐릭터 회전
        float steer = bike.Data.str;          // -45 ~ 45도
        transform.Rotate(0, steer * Time.deltaTime * 2f, 0);

        // 속도 → 캐릭터 이동
        float speed = bike.Data.spd;          // km/h
        transform.Translate(0, 0, speed * Time.deltaTime * 0.1f);

        // 브레이크
        bool braking = bike.Data.brkL == 1 || bike.Data.brkR == 1;

        // O 버튼 — 퀴즈 정답
        if (bike.Data.o == 1)
        {
            bike.SetVibration(3);   // V3: 정답 진동
            bike.SetRGBState(1);
        }

        // X 버튼 — 퀴즈 오답
        if (bike.Data.x == 1)
        {
            bike.SetVibration(4);   // V4: 오답 진동
        }
    }

    public void OnDangerZoneEnter()
    {
        bike.SetVibration(1);       // V1: 위험 경고
        bike.SetRGBState(2);        // S2: 빨간 LED
    }

    public void OnSessionStart()
    {
        bike.SetRGBState(1);        // S1: 주행 상태
    }

    public void OnQuizStart()
    {
        bike.SetRGBState(3);        // S3: 퀴즈 상태
    }
}
```

---

## 6. 오류 대응

| 증상 | 원인 | 조치 |
|------|------|------|
| 시리얼 포트 열리지 않음 | 드라이버 미설치 / 포트 번호 오류 | 장치 관리자 확인, CH340/CP2102 드라이버 설치 |
| `{"error":"ICM-20948 init failed"}` | IMU 배선 불량 또는 주소 불일치 | i2c_scan 결과 확인, SDA·SCL·VCC·GND 점검 |
| `str` 값이 항상 0 | 캘리브레이션 미실시 | `C\n` 전송 |
| `str` 값이 드리프트 | 지자기 캘리브레이션 미실시 | `M\n` 전송 후 5초 회전 |
| `rpm` 값이 간헐적으로 급등 | 정상 (클램프 적용됨, 최대 300) | `spd` 사용 시 `Mathf.Clamp` 추가 권장 |
| JSON 파싱 오류 | 부팅 직후 깨진 첫 줄 | `id` 필드 확인 후 사용 (`if (data.id != 1) return`) |
| 진동이 작동 안 함 | 브레이크 상태 아닐 때만 자동 진동 | `V1~V5` 명령은 항상 동작, 브레이크 피드백은 자동 |

---

## 7. 타이밍 다이어그램

```
ESP32 부팅
  │
  ├─ ICM-20948 초기화 (실패 시 빨간 LED + 에러 출력 후 정지)
  │
  ├─ Mahony 필터 warm-up 2초 (50Hz × 100회)
  │
  ├─ 조향 기준점 자동 캘리브레이션
  │
  ├─ 진동 (PAT_READY) + 초록 LED   ← 부팅 완료 신호
  │
  └─ loop() 시작 ──────────────────────────────────────────
       │ 20ms마다
       ├─ JSON 전송 (rpm, spd, str, brkL, brkR, o, x)
       ├─ 시리얼 수신 처리 (V/C/M/S 명령)
       ├─ 진동 시퀀서 업데이트
       └─ RGB LED 업데이트

Unity 측
  │
  ├─ Update()에서 매 프레임 버퍼 읽기
  ├─ \n 단위로 JSON 파싱
  └─ 이벤트 발생 시 V/S 명령 전송
```

---

*bicycle_sim_v4.1 · 빛고을국민안전체험관 · FLUXION · 2026.05*
