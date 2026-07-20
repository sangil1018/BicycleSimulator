# 자전거 시뮬레이터 — Unity 시리얼 통신 가이드

**펌웨어**: bicycle_sim_x v6.2 (ESP32-S3, 센서 + 진동 담당) — **변경 없음, 재플래싱 불필요**  
**대상**: 빛고을국민안전체험관 / FLUXION

> ## ⚠️ 진동 경로 변경 — ESP32 단독 처리 (USB 릴레이 폐기)
>
> 진동 모터는 **ESP32 GPIO2(2번핀) + GND**에 연결된 **MOSFET 광커플러 절연 드라이버 모듈**로
> 구동합니다. Unity는 진동 패턴 번호를 ESP32에 `V0`~`V6` 명령으로 보내고(§3-1b),
> 길이 배율은 연결 시 `P`(§3-1)로 한 번 보냅니다.
>
> **펌웨어는 수정하지 않았습니다.** v5.8부터 있던 GPIO2 진동 경로(V/P/B 명령, 시퀀서)가
> 그대로 살아 있고, 릴레이 시절에 Unity가 V 명령을 안 보내던 것뿐이었습니다.
> 이번 변경은 **Unity 쪽만** 고쳤으므로 보드에 올라가 있는 v6.2를 그대로 쓰면 됩니다.
>
> **USB 릴레이 진동 경로는 사용하지 않습니다.** 아래 §1-2, §3-6과 `config.ini`의
> `RelayPortName`/`RelayBaudRate`/`Vibe*Duration` 키는 옛 구성 기록으로만 남아 있으며
> 실제 동작에 영향을 주지 않습니다. `VibrationRelay.cs`는 포트를 열지 않는 빈 껍데기입니다.
>
> 배선: `GPIO2 → 모듈 SIG(IN)`, `ESP32 GND → 모듈 GND`(신호 기준 공통, 필수),
> 모터 전원은 모듈 출력단에 별도 DC로 연결(광커플러 절연이므로 전원 분리 가능).

ESP32는 센서(케이던스·조향·브레이크·버튼) 입출력과 **모든 진동 피드백**을 담당합니다.
조향 센서(ICM-20948)는 부팅 시 인식 실패해도 `str=0` 고정으로 정상 송신합니다(§2-4).

**v5.9~v6.0 추가 사항**: `H` keep-alive 에코(§3-1c), 좀비 포트 자동 재연결(§2-5),
PAS 진단 필드 `pc`/`pl`(§2-2), PAS micros() 랩어라운드 가드.

---

## 1. 연결 설정

Unity는 **COM 포트 1개**(ESP32)만 사용합니다. 센서 입력과 진동이 모두 이 포트로 오갑니다.

### 1-1. ESP32 (센서)

| 항목 | 값 |
|------|----|
| 포트 | `config.ini` → `PortName` (예: COM12) |
| 보드레이트 | **115,200 bps** |
| 데이터 비트 | 8 |
| 패리티 | None |
| 정지 비트 | 1 |
| 줄 끝 문자 | `\n` (LF) |
| 전송 주기 | **50 Hz** (20 ms마다 1 패킷) |

### 1-2. USB 릴레이 (진동) — ❌ 폐기됨 (옛 구성 기록)

| 항목 | 값 |
|------|----|
| 포트 | `config.ini` → `RelayPortName` (예: COM3) |
| 보드레이트 | **9,600 bps** |
| 데이터 비트 | 8 |
| 패리티 | None |
| 정지 비트 | 1 |

자세한 명령 프로토콜과 Unity 구현은 §3-6 참고.

---

## 2. ESP32 → Unity (출력)

### 2-1. 데이터 형식

매 20 ms마다 ESP32가 아래 JSON 한 줄을 전송합니다.

```
{"id":1,"rpm":80.0,"spd":20.0,"str":-5.2,"brk":0,"o":0,"x":0,"pc":1234,"pl":1}
```

줄 끝은 `\n`(LF) 한 문자입니다. `\r\n`이 아닙니다.

### 2-2. 필드 정의

| 필드 | 타입 | 범위 | 설명 |
|------|------|------|------|
| `id` | int | 1 | 스테이션 ID (`STATION_ID` 상수값) |
| `rpm` | float | 0.0 ~ 300.0 | 페달 케이던스 (분당 회전수) |
| `spd` | float | 0.0 ~ 75.0 | 환산 속도 (km/h) · `rpm × CADENCE_TO_KPH` (펌웨어 참고용 — **Unity는 이 필드를 사용하지 않음**. Unity는 `rpm`과 `config.ini`의 `MetersPerRevolution`으로 자체 계산, CONTENT_GUIDE.md §2.1 참고) |
| `str` | float | -45.0 ~ 45.0 | 핸들 조향각 (도) · DMP Yaw 기반, 왼쪽 음수 / 오른쪽 양수 |
| `brk` | int | 0 / 1 | 브레이크 (1 = 당김) |
| `o` | int | 0 / 1 | O 버튼 (1 = 눌림) |
| `x` | int | 0 / 1 | X 버튼 (1 = 눌림) |
| `pc` | uint | 0 ~ | **PAS 진단** (v6.0+) — 부팅 후 누적 펄스 수. 페달을 돌려도 늘지 않으면 센서 전원/배선 문제, 늘는데 `rpm`=0이면 펌웨어 문제 |
| `pl` | int | 0 / 1 | **PAS 진단** (v6.0+) — PAS 핀(GPIO1)의 현재 레벨. 단선 시 풀업으로 1 고정 |

> `pc`/`pl`은 진단 전용 필드로, Unity `JsonUtility`는 모르는 필드를 무시하므로 게임 동작에 영향이 없습니다.
> `serial_monitor.py`(헤더 ESP32 줄)와 `hardware_signal_tester.html`(상태 줄)이 이 값을 표시합니다.

### 2-3. 특수 출력 (이벤트성)

| 메시지 | 발생 시점 |
|--------|-----------|
| `{"debug":"DMP v6.0 Ready. Stabilizing for 3s..."}` | 부팅 완료, DMP 안정화 시작 |
| `{"debug":"hb"}` | `H`(keep-alive) 명령 수신 에코 (§3-1c) — Unity·모니터가 10초 주기로 송신 |
| `{"debug":"DMP Stabilized"}` | DMP 안정화 완료 (부팅 후 약 3초) |
| `{"debug":"Connect failed"}` | IMU 연결 실패 (재시도 중 반복 출력될 수 있음) |
| `{"debug":"DMP Init failed. Check ICM_20948_USE_DMP in library."}` | DMP 초기화 실패 |
| `{"debug":"Sensor enable failed"}` | 센서 활성화 실패 |
| `{"debug":"ODR set failed"}` | ODR 설정 실패 |
| `{"debug":"Steer sensor NOT found. str fixed to 0"}` | 조향 센서 인식 최종 실패 — str=0 고정 모드 진입 (§2-4) |
| `{"calibrated":true,"center":0.0}` | 조향 기준점 캘리브레이션 완료 |
| `{"magcal":"not_required_in_dmp"}` | M 명령 수신 응답 |

### 2-4. 조향 센서 페일세이프 (str=0 고정 모드)

부팅 시 ICM-20948 초기화를 **5회 재시도**하고, 모두 실패하면 무한 대기 대신 부팅을 계속합니다.

- `{"debug":"Steer sensor NOT found. str fixed to 0"}` 출력 후 RGB LED **자홍색** 점등
- 이후 50Hz 데이터 송신은 정상 진행되며 `str`만 항상 `0.0` (rpm/spd/brk/o/x는 정상)
- Unity(InputManager)는 이 메시지를 받으면 3초 안정화 대기 없이 즉시 수신을 시작하고, `SteerSensorOk=false`로 표시 (디버그 GUI에서 "미인식(0고정)" 확인 가능)
- `Hardware/serial_monitor.py`도 이 상태를 감지해 상태 줄에 빨간색 `조향:센서없음(0)`으로 표시

### 2-5. 연결 유지 및 자동 재연결 (Unity InputManager, v6.0 연동)

장시간 무인 운영(키오스크)에서 USB 절전·재열거·보드 리셋으로 링크가 죽는 것에 대비한 3중 방어입니다.
.NET `SerialPort.IsOpen`은 장치가 죽어도 `true`를 유지하므로(좀비 포트) **무수신 시간**을 기준으로 판단합니다.

| 정책 | 조건 | 동작 |
|------|------|------|
| 입력값 리셋 | 0.5초 무수신 | 모든 입력값 0으로 리셋 (이전 입력 잔류 방지). 수신 재개 시 자동 복구 |
| 좀비 포트 재연결 | 5초 무수신 지속 | 포트를 강제로 닫고 재연결 (재연결 시 DTR로 보드가 리셋되어 3초 재안정화) |
| 첫 데이터 감시 | 연결 후 10초간 무데이터 | 강제 재연결 (다른 장치가 같은 COM 번호로 재열거된 경우 등) |
| keep-alive | 10초 주기 | `H` 송신 → 펌웨어 `{"debug":"hb"}` 에코로 왕복 확인, USB OUT 트래픽 유지 |

미연결 상태에서는 5초 주기로 재연결을 시도합니다. 관련 로그는 빌드 `Player.log`에도 남습니다
(`[Input] ... 좀비 포트로 판단, 강제 재연결` 등).

> 배포 PC는 `Hardware/kiosk_power_setup.bat`(더블클릭, 관리자 자동 승격)으로 Windows의
> USB 선택적 절전·장치 전원 관리·시스템 대기를 반드시 해제하세요. 장시간 유휴 후
> 무반응의 예방책입니다.

---

## 3. Unity → ESP32 (입력)

모든 명령은 **같은 COM 포트**로 전송합니다. 형식: `ASCII + 숫자 + \n`

펌웨어는 `P` `B` `C` `H` `M` `R` `S` `V` 명령을 처리하며, **Unity(InputManager)가 실제로 보내는 명령은 `V0~V6` · `P` · `C` · `R` · `H` · `M`** 입니다. `S`(LED 상태)와 `B1/B0`(브레이크 연속 진동)는 펌웨어 단독 테스트용으로만 남아 있습니다 — 브레이크 진동도 이제 상승 에지에서 `V3` 단발로 보냅니다.

```
V3\n    ← 진동 패턴 (V0~V6)
P100\n  ← 진동 길이 배율 (100 = 1.0x, 연결 시 1회)
C\n     ← 조향 캘리브레이션
R\n     ← PAS 인터럽트 재초기화
H\n     ← keep-alive (10초 주기 자동 송신)
```

### 3-1. P — 진동 길이 배율 ✅ Unity 사용 중

V 패턴의 각 단계 지속시간에 곱해지는 배율입니다. Unity는 **연결이 수립될 때마다 1회**
`config.ini`의 `VibeMultiplier` 값을 `P{배율×100}` 형태로 전송합니다.

> 진동 길이를 현장에서 조절하는 유일한 키입니다. 펌웨어 재플래싱 없이 `config.ini`만 고치고
> Unity를 재시작하면 반영됩니다.

| 명령 | 의미 |
|------|------|
| `P100\n` | 1.0배 (기본) |
| `P150\n` | 1.5배 |
| `P{n}\n` | n/100 배 · 펌웨어에서 50~300으로 제한 |

### 3-1b. V — 진동 패턴 (GPIO2 → MOSFET 광커플러 절연 드라이버 → 모터) ✅ Unity 사용 중

> 모든 게임 진동이 이 명령 하나로 나갑니다. 아래 지속시간은 `P` 배율이 100(1.0x)일 때 기준이며,
> 수신 시 `{"debug":"V{n} recv"}` 에코와 함께 RGB LED가 패턴 색으로 점등합니다(현장 확인용).
>
> Unity의 `VibeState` 매핑: `Danger→V1`, `Success→V2`, `Correct/Click/Brake→V3`,
> `Wrong→V4`, `Walk→V5`, `Ready→V6`, `Stop→V0`.

| 명령 | 패턴명 | 내용 | 총 시간 |
|------|--------|------|---------|
| `V0\n` | 정지 | 진동 즉시 중단 | 즉시 |
| `V1\n` | DANGER | 강진동 1회 | 900 ms |
| `V2\n` | SUCCESS | 강진동 2회 | 900 ms |
| `V3\n` | CORRECT | 강진동 1회 | 500 ms |
| `V4\n` | WRONG | 강진동 1회 | 650 ms |
| `V5\n` | WALK | 강진동 3회 | 1400 ms |
| `V6\n` | READY | DMP 안정화 완료 알림 | 930 ms |

### 3-1c. H — keep-alive 하트비트 ✅ Unity 사용 중 (v5.9+)

Unity와 `serial_monitor.py`가 **10초 주기**로 자동 송신합니다. 펌웨어는 `{"debug":"hb"}`를 에코합니다.

- 목적: USB 링크의 호스트→장치 방향 트래픽을 유지해 절전 진입을 막고, 에코 수신으로 링크 왕복 상태를 확인
- Unity 디버그 GUI(`debugMode=1`)의 `HB Ack` 항목에서 마지막 에코 수신 경과를 확인 가능
- 수동 테스트: 시리얼 모니터에서 `H` + Enter → `HB 응답` 표시 확인

### 3-2. B — 브레이크 연속 진동 — Unity 미사용

| 명령 | 동작 |
|------|------|
| `B1\n` | 브레이크 당김 — 약진동 연속 (PWM 180) |
| `B0\n` | 브레이크 해제 — 진동 정지 |

> Unity는 이 명령을 보내지 않습니다. 브레이크를 오래 잡으면 모터가 그동안 계속 돌기 때문에,
> 게임에서는 브레이크 **상승 에지에 `V3` 단발**만 보냅니다. B는 수동 테스트 경로로만 남아 있습니다.
> V 명령이 B보다 우선하며, 패턴 재생 중 B 상태는 패턴 종료 후 적용됩니다.

### 3-3. S — RGB LED 상태 — Unity 미사용

> 펌웨어가 지원하지만 Unity는 현재 `S` 명령을 보내지 않습니다. LED는 기본 IDLE(흰색 깜박)에서 주행(초록)·브레이크(주황)·진동(패턴 색상)에 따라서만 변합니다.

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

v5.8부터 DMP Quat6(Game Rotation Vector)를 사용하므로 지자기 보정이 필요 없습니다.  
하위 호환을 위해 유지되며 응답만 반환합니다: `{"magcal":"not_required_in_dmp"}`

### 3-6. 진동 제어 (ESP32 GPIO2) ✅ 현재 사용 중

Unity는 ESP32 포트로 `V0`~`V6`만 보내고, ON/OFF 타이밍은 펌웨어의 진동 시퀀서가 처리합니다.

**하드웨어**

| 항목 | 값 |
|------|----|
| 신호 핀 | ESP32 **GPIO2** → 드라이버 모듈 SIG(IN) |
| 기준 | ESP32 **GND** → 드라이버 모듈 GND (필수) |
| 드라이버 | MOSFET 광커플러 절연 드라이버 모듈 |
| 모터 전원 | 모듈 출력단에 외부 DC (절연이므로 ESP32 전원과 분리 가능) |
| PWM | `analogWrite` 기본 1 kHz, ON은 듀티 255 고정 |

> 부팅하자마자 계속 진동하고 `V` 명령에 오히려 멈춘다면 모듈이 **반전 입력**(IN=LOW에서 도통)입니다.
> 이 경우에만 펌웨어 `vibeSet()`을 `analogWrite(PIN_MOTOR, 255 - val)`로 고쳐 재플래싱하세요
> (현재 쓰는 모듈이 액티브-HIGH라면 손댈 필요 없습니다).

**Unity 구현**: `InputManager.SendVibrate(VibeState)`가 유일한 진입점입니다.
`VibeState`를 V 번호로 매핑해 `SendRaw("V{n}")`으로 큐에 넣습니다(§3-1b 매핑표).
`VibrationRelay.cs`는 폐기된 껍데기이며 포트를 열지 않습니다.

| VibeState | 명령 | 발생 위치 |
|-----------|------|-----------|
| `Click` | `V3` | `oButton`/`xButton`/`ExitxButton` — 모든 O/X UI 버튼 실행 |
| `Brake` | `V3` | `InputManager` — 브레이크 상승 에지 |
| `Correct` | `V3` | `BlackBoard.SubmitAnswer()` — 퀴즈 정답 |
| `Ready` | `V6` | `InputManager` — DMP 안정화 완료, 조향 센서 미인식 |
| `Walk` | `V5` | `GameManager.TriggerAutoPlayStart()` — 횡단보도 자동주행 시작 |
| `Success` | `V2` | `GameManager` — 브레이크 이벤트 성공, 최종 결과 표시 |
| `Danger` | `V1` | `GameManager` — 경고 정지, 브레이크 이벤트 시작 |
| `Wrong` | `V4` | `BlackBoard.SubmitAnswer()` — 퀴즈 오답 |
| `Stop` | `V0` | 호출하는 곳 없음 |

**진동 겹침 처리**: 릴레이 시절의 "긴 쪽 유지"와 달리, 펌웨어 시퀀서는 **나중에 온 패턴이
앞 패턴을 덮어씁니다**. 위험 이벤트 중 제동처럼 두 요청이 겹치면 마지막 패턴만 재생됩니다.

**단독 점검 (Unity 없이)**: `Hardware/serial_monitor.py` 실행 중 **`v` 키**(엔터 불필요)를 누르면
ESP32로 `V3`를 보냅니다. 브레이크/O/X를 누르는 순간에도 자동으로 `V3`가 나가므로,
Unity를 켜지 않고 배선·모터 전원을 확인할 수 있습니다.

**브라우저 테스터**: `Hardware/hardware_signal_tester.html`을 Chrome/Edge로 열면 ESP32 실시간
데이터 시각화 + 진동 체크(수동 `V3`, 브레이크·버튼 자동 연동) + V 패턴 버튼을 GUI로 쓸 수 있습니다.

**config.ini 키** (`InputManager`가 읽음)

| 키 | 기본값 | 설명 |
|----|--------|------|
| `isActive` | `1` | 진동 사용 여부 (0이면 `V` 명령을 아예 보내지 않음) |
| `VibeMultiplier` | `1.0` | 진동 길이 배율 (0.5~3.0) — 연결 시 `P{×100}`으로 전달. `[Settings]` 섹션에 있음 |

> `RelayPortName` · `RelayBaudRate` · `Vibe*Duration` 키는 폐기됐습니다. 남아 있어도 무시됩니다.

**씬 설정**: 진동에 필요한 컴포넌트는 `InputManager` 하나뿐입니다(`DontDestroyOnLoad` Singleton).
Home/Level1/Level2에 남아 있는 `VibrationRelay` 오브젝트는 지워도 무방합니다.

---

## 4. 캘리브레이션 절차

### 4-1. 첫 설치 시 (최초 1회)

#### STEP 1 — 부팅 확인

**정상 부팅 흐름:**
```
← {"debug":"DMP v6.0 Ready. Stabilizing for 3s..."}
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

위 오류는 **5회까지 재시도**되며, 최종 실패 시 아래 출력과 함께 str=0 고정 모드로 계속 부팅합니다 (§2-4):
```
{"debug":"Steer sensor NOT found. str fixed to 0"}   ← LED 자홍색, 조향 없이 운영 가능
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
| 브레이크 | 레버 당김 | `brk` + 체감 진동 | 0 → 1, 누르는 순간 짧은 진동 (`V3`) |
| O 버튼 | O 버튼 누름 | `o` | 0 → 1 |
| X 버튼 | X 버튼 누름 | `x` | 0 → 1 |
| 진동 | `V1\n` 전송 (또는 `serial_monitor.py`에서 `v` 키) | 체감 진동 + RGB LED 색상 | 패턴 길이만큼 진동, LED 빨강 |
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
[ ] STEP 3  RGB LED 색상 변경 확인
[ ] STEP 3  진동 배선 확인 (GPIO2 → 드라이버 SIG, ESP32 GND → 드라이버 GND)
[ ] STEP 3  진동 동작 확인 (DebugInputPanel 진동 버튼 또는 `V1` 전송)
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

    // 진동 (ESP32 GPIO2 → MOSFET 광커플러 절연 드라이버 → 모터), §3-6 참고
    public void SetVibration(int pattern) => Send($"V{pattern}");
    public void SetBrake(bool on)         => Send(on ? "B1" : "B0");

    // 센서 제어
    public void SetRGBState(int state)    => Send($"S{state}");
    public void CalibrateSteer()          => Send("C");
}
```

> 이 예시는 ESP32 프로토콜 자체를 보여주기 위한 단독 참고 코드입니다. 실제 프로젝트에서는
> `InputManager.SendVibrate(VibeState)`가 같은 `V` 명령을 보냅니다 (§3-6).

### 5-2. 사용 예시

```csharp
float steer   = bike.Data.str;      // -45 ~ 45도
float speed   = bike.Data.spd;      // km/h
bool  braking = bike.Data.brk == 1;

bike.SetVibration(1);   // V1: 위험 경고
bike.SetVibration(2);   // V2: 성공
bike.SetBrake(true);    // B1: 브레이크 연속 진동 — 게임에서는 미사용 (§3-2)
bike.SetBrake(false);   // B0: 브레이크 해제
bike.SetRGBState(2);    // S2: 빨간 LED
bike.SetRGBState(1);    // S1: 주행 복귀
```

---

## 6. 오류 대응

| 증상 | 원인 | 조치 |
|------|------|------|
| 시리얼 포트 열리지 않음 | 드라이버 미설치 / 포트 번호 오류 | 장치 관리자 확인, CH340/CP2102 드라이버 설치 |
| `"Connect failed"` 반복 출력 | IMU 배선 불량 | SDA(GPIO17)·SCL(GPIO18)·VCC·GND 배선 점검 (5회 실패 시 str=0 고정 모드로 전환됨) |
| `"DMP Init failed..."` | 라이브러리 설정 오류 | `ICM_20948_C.h`에서 `#define ICM_20948_USE_DMP` 주석 해제 확인 |
| `str` 값이 항상 0 + LED 자홍색 | 조향 센서 미인식 (str=0 고정 모드) | IMU 배선/전원 점검 후 ESP32 재부팅. 게임은 조향 없이 계속 운영 가능 (§2-4) |
| `str` 값이 항상 0 (LED 정상) | 캘리브레이션 미실시 또는 DMP 미안정화 | "DMP Stabilized" 수신 후 `C\n` 전송 |
| `str` 값이 드리프트 | DMP 안정화 전 캘리브레이션 | 3초 대기 후 재캘리브레이션 |
| `rpm` 값이 간헐적으로 급등 | 노이즈 | `spd` 사용 시 `Mathf.Clamp` 추가 권장 |
| **PAS만 무반응** (버튼/브레이크는 정상) | PAS 센서 전원/배선 문제 (버튼과 달리 PAS는 전원이 필요한 능동 홀센서) | 모니터에서 `pc`(누적 펄스) 확인 — 페달을 돌려도 안 늘면 센서 커넥터/배선/전원 점검 (`pl`=1 고정이면 단선, 0 고정이면 출력 래치). `pc`는 느는데 `rpm`=0이면 펌웨어 문제이므로 개발팀 문의 |
| 장시간 유휴 후 전체 입력 무반응 | Windows USB 절전으로 링크 사망 + 좀비 포트 | v5.9+ Unity는 5초 무수신 시 자동 재연결 (`Player.log`에서 `좀비 포트로 판단` 로그 확인). 예방: `kiosk_power_setup.bat` 실행 (§2-5) |
| JSON 파싱 오류 | 부팅 직후 깨진 첫 줄 | `id` 필드 확인 후 사용 (`if (data.id != 1) return`) |
| 진동이 전혀 안 됨 | 배선 또는 모터 전원 | `V1` 전송 시 RGB LED가 빨강으로 켜지는지 확인 — **LED는 켜지는데 진동이 없으면** 드라이버 이후 문제(SIG/GND/모터 전원/모터), **LED도 안 켜지면** 명령이 도달하지 않은 것(포트·연결 확인) |
| 진동이 안 멈추고 계속 돎 | 드라이버 모듈이 반전 입력 | 펌웨어 `vibeSet()`을 `analogWrite(PIN_MOTOR, 255 - val)`로 고쳐 재플래싱 (§3-6) |
| 진동이 약함 | 모터 전원 부족 | 드라이버 출력단 DC 전압/전류 용량 확인 (ESP32 5V에서 끌어쓰지 말 것) |
| GND만 빼먹어 동작 안 함 | 광커플러 입력 기준 누락 | ESP32 GND ↔ 드라이버 모듈 GND는 **반드시** 연결 (절연은 출력단 기준이며 입력 기준은 공통) |
| 진동 길이가 이상함 | `VibeMultiplier` 미반영 | Unity 재시작 시 `P{n}` 전송 로그 확인 — 보드는 포트를 열어도 리셋되지 않으므로 마지막 `P` 값이 유지됩니다 |

---

## 7. 타이밍 다이어그램

```
ESP32 부팅
  │
  ├─ ICM-20948 I2C 연결 확인 ─ 실패 시 5회 재시도
  │   └─ 최종 실패 → "Steer sensor NOT found. str fixed to 0" + 자홍 LED
  │                  (str=0 고정, 아래 loop()는 동일하게 진행)
  ├─ DMP 초기화 (Quat6 활성화, ODR 설정)
  ├─ 파란 LED 점등 + "DMP v6.0 Ready. Stabilizing for 3s..." 출력
  ├─ DMP 안정화 대기 (3초)
  │   └─ "DMP Stabilized" 출력 → 파란 LED 소등
  │
  └─ loop() 시작 ─────────────────────────────────
       │ 20ms마다
       ├─ JSON 전송 (rpm, spd, str, brk, o, x, pc, pl)
       ├─ 시리얼 수신 처리 (V/P/C/R/H/M 수신, S/B는 펌웨어 테스트용)
       ├─ 진동 시퀀서 업데이트 (GPIO2 PWM → MOSFET 광커플러 절연 드라이버)
       └─ RGB LED 업데이트

Unity 측
  │
  ├─ 수신 스레드에서 ESP32 라인 읽기 → 메인 스레드에서 JSON 파싱
  ├─ ESP32 포트로 V/P/C/R/M 명령 + 10초 주기 H(keep-alive) 전송
  ├─ 0.5초 무수신 → 입력 리셋, 5초 무수신 → 포트 강제 재연결 (§2-5)
  └─ 이벤트 발생 시 InputManager.SendVibrate() → 같은 ESP32 포트로 V{n} 전송 (§3-6)
```

---

*bicycle_sim_x v6.2 · 빛고을국민안전체험관 / FLUXION*
