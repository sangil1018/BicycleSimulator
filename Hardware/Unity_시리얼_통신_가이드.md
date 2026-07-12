# 자전거 시뮬레이터 — Unity 시리얼 통신 가이드

**펌웨어**: bicycle_sim_x v6.0 (ESP32-S3, 센서 담당)  
**대상**: 빛고을국민안전체험관 / FLUXION

ESP32는 센서(케이던스·조향·브레이크·버튼) 입출력과 브레이크 진동 피드백을 담당합니다.
이벤트/퀴즈 진동은 별도 USB 릴레이(§3-6)가 처리합니다.
조향 센서(ICM-20948)는 부팅 시 인식 실패해도 `str=0` 고정으로 정상 송신합니다(§2-4).

**v5.9~v6.0 추가 사항**: `H` keep-alive 에코(§3-1c), 좀비 포트 자동 재연결(§2-5),
PAS 진단 필드 `pc`/`pl`(§2-2), PAS micros() 랩어라운드 가드.

---

## 1. 연결 설정

Unity는 **COM 포트 2개**를 사용합니다 — ① ESP32(센서), ② USB 릴레이(진동, ESP32와 무관한 별도 장치).

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

### 1-2. USB 릴레이 (진동)

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

펌웨어는 `P` `B` `C` `H` `M` `S` `V` 명령을 처리하며, **Unity(InputManager)가 실제로 보내는 명령은 `P` · `B1/B0` · `C` · `H` · `M`** 입니다. `S`(LED 상태)와 `V`(진동 패턴)는 펌웨어 단독 테스트용으로만 남아 있습니다.

```
P100\n  ← 진동 세기 배율 (100 = 1.0x)
B1\n    ← 브레이크 ON
C\n     ← 조향 캘리브레이션
H\n     ← keep-alive (10초 주기 자동 송신)
```

### 3-1. P — 진동 세기 배율 ✅ Unity 사용 중

ESP32 쪽 브레이크 진동(및 V 패턴) 지속시간에 곱해지는 배율입니다. 연결 직후·DMP 안정화 완료·조향 센서 미인식 시 Unity가 `config.ini`의 `VibeMultiplier` 값을 `P{배율×100}` 형태로 전송합니다.

> 같은 `VibeMultiplier` 값이 USB 릴레이의 진동 프리셋 길이에도 곱해집니다(§3-6). 즉 이 키 하나로 ESP32 브레이크 진동과 릴레이 이벤트 진동의 길이가 함께 조절됩니다.

| 명령 | 의미 |
|------|------|
| `P100\n` | 1.0배 (기본) |
| `P150\n` | 1.5배 |
| `P{n}\n` | n/100 배 · 펌웨어에서 50~300으로 제한 |

### 3-1b. V — 진동 패턴 (IRF520 모듈 → 진동 모터) — Unity 미사용

> 펌웨어 단독 테스트용입니다. Unity는 이 명령을 보내지 않으며, 이벤트/퀴즈 진동은 §3-6의 USB 릴레이로 처리합니다.
> 아래 지속시간은 `P` 배율이 100(1.0x)일 때 기준이며, 수신 시 `{"debug":"V{n} recv"}` 에코를 반환합니다.

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

### 3-2. B — 브레이크 피드백

| 명령 | 동작 |
|------|------|
| `B1\n` | 브레이크 당김 — 약진동 연속 (PWM 180) |
| `B0\n` | 브레이크 해제 — 진동 정지 |

> V 명령이 B보다 우선합니다. 패턴 재생 중 B 상태는 패턴 종료 후 적용됩니다.

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

### 3-6. 진동 제어 (USB 릴레이, ESP32와 별도 포트) ✅ 현재 사용 중

Unity는 ESP32 포트가 아니라 **별도의 USB 릴레이 포트**(`config.ini` → `RelayPortName`)로 진동을 제어합니다.
릴레이 보드 자체 스펙(9600bps, N/8/1)은 §1-2 참고.

**HEX 명령 (릴레이 1번 채널)**

| 동작 | 프레임 | 비고 |
|------|--------|------|
| ON | `A0 01 01 A2` | 체크섬 = 앞 3바이트 합산 |
| OFF | `A0 01 00 A1` | 체크섬 = 앞 3바이트 합산 |
| 상태확인 | `FF` | 응답은 ASCII 값 (Unity는 응답 수신 여부만으로 생존 확인) |

**Unity 구현**: `Assets/Scripts/Core/VibrationRelay.cs` (Singleton, DontDestroyOnLoad)

- `InputManager.SendVibrate(VibeState)`가 유일한 진입점이며, 내부에서 `VibrationRelay`의 프리셋 메서드로 위임합니다.
- Unity가 ESP32로 보내는 명령은 `P` · `B1/B0` · `C` · `M`입니다. 진동은 릴레이가 담당합니다.

| VibeState | 프리셋 | 기본 지속시간 | 발생 위치 |
|-----------|--------|---------------|-----------|
| `Click` | Short | 0.2 s | `oButton`/`xButton`/`ExitxButton` — 모든 O/X UI 버튼 실행 |
| `Ready` | Short | 0.2 s | `InputManager` — DMP 안정화 완료, 조향 센서 미인식 |
| `Walk` | Short | 0.2 s | `GameManager.TriggerAutoPlayStart()` — 횡단보도 자동주행 시작 |
| `Correct` | Short | 0.2 s | `BlackBoard.SubmitAnswer()` — 퀴즈 정답 |
| `Success` | Medium | 0.5 s | `GameManager` — 브레이크 이벤트 성공, 최종 결과 표시 |
| `Danger` | Long | 1.5 s | `GameManager` — 경고 정지, 브레이크 이벤트 시작 |
| `Wrong` | Long | 1.5 s | `BlackBoard.SubmitAnswer()` — 퀴즈 오답 |
| `Stop` | (무시) | - | 호출하는 곳 없음 |

실제 지속시간은 **프리셋 × `VibeMultiplier`**이며, 최종값은 0.05~5초로 제한됩니다(config 오타 방어).

**진동 겹침 처리**: 진동이 나가는 중에 새 요청이 오면 **남은 시간이 더 긴 쪽을 유지**합니다. 예를 들어 `Danger`(1.5초) 도중 X 버튼을 눌러 `Click`(0.2초)이 들어와도 위험 진동이 잘리지 않습니다. 반대로 짧은 진동 뒤에 긴 진동이 오면 종료 시각이 뒤로 밀립니다.

**연결 확인 / 자동 재연결**: `Awake()`에서 포트를 연 직후 **OFF 프레임을 먼저 전송**해(이전 실행이 크래시로 릴레이를 켜둔 채 끝났을 수 있으므로) 항상 꺼진 상태로 시작합니다. 이어서 상태확인 명령(`FF`)으로 실제 보드 응답을 검증하고, 이후 `Reconnect Interval`(기본 5초)마다 재확인하여 응답이 없으면 자동으로 재연결을 시도합니다. OFF 전송이 실패하면 다음 폴링(20 ms)에서 재시도하므로, 일시적인 쓰기 실패로 진동이 켜진 채 남지 않습니다.

**릴레이 단독 점검 (Unity 없이)**: `Hardware/serial_monitor.py`가 ESP32(CH343)와 USB 릴레이(CH340) 포트를 자동 구분해 함께 연결합니다. 모니터 실행 중 키보드 **`v` 키**를 누르면(엔터 불필요) 릴레이로 진동 ON→0.5초 후 OFF 프레임을 직접 전송하므로, Unity를 켜지 않고도 릴레이 배선·전원을 빠르게 확인할 수 있습니다. 포트가 자동 구분되지 않으면 `python serial_monitor.py [ESP32포트] [릴레이포트]`로 직접 지정합니다.

**브라우저 테스터**: `Hardware/hardware_signal_tester.html`을 Chrome/Edge로 열면 ESP32 실시간 데이터 시각화 + 릴레이 진동 체크(수동 펄스/ON/OFF, 브레이크·버튼 자동 연동) + ESP32 V 패턴 테스트를 GUI로 수행할 수 있습니다. 최초 1회만 포트를 수동 선택하면 이후 자동 연결됩니다.

> 주의: Unity는 릴레이 상태확인(`FF`) **응답이 있어야만** 연결로 인정합니다. 상태확인에 응답하지 않는 호환 보드는 "모니터/테스터에서는 진동되는데 Unity에서는 안 됨" 증상을 보입니다 — 이 조합이 관찰되면 보드 교체 또는 상태확인 로직 수정이 필요합니다.

**config.ini 키** (모두 `InputManager`가 읽어서 `VibrationRelay`에 전달 — 릴레이 스크립트는 파일을 직접 읽지 않음)

> `config.ini`에서 릴레이 관련 키는 `[Vibration]` 섹션에 있습니다.

| 키 | 기본값 | 설명 |
|----|--------|------|
| `isActive` | `1` | 진동 사용 여부 (1=활성화, 0=비활성화 시 릴레이 연결 안 함) |
| `RelayPortName` | `COM3` | 릴레이 연결 포트 |
| `RelayBaudRate` | `9600` | 릴레이 통신 속도 |
| `VibeShortDuration` | `0.2` | 짧은 진동 지속시간(초) |
| `VibeMediumDuration` | `0.5` | 중간 진동 지속시간(초) |
| `VibeLongDuration` | `1.5` | 긴 진동 지속시간(초) |
| `VibeMultiplier` | `1.0` | 위 세 프리셋에 곱해지는 시간 배율 (0.5~3.0). `[Settings]` 섹션에 있음 |

**씬 설정**: `VibrationRelay`는 Singleton이라 씬에 컴포넌트가 붙은 GameObject가 최소 1개 있어야 동작합니다. 현재 Home/Level1/Level2 씬 모두에 배치되어 있습니다.

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
| 브레이크 | 레버 당김 | `brk` + 체감 진동 | 0 → 1, 약진동 연속 (ESP32 `B1` — 릴레이와 무관) |
| O 버튼 | O 버튼 누름 | `o` | 0 → 1 |
| X 버튼 | X 버튼 누름 | `x` | 0 → 1 |
| 진동 패턴 (테스트용) | `V1\n` 전송 | 체감 진동 | 펌웨어 단독 테스트용, Unity는 미사용 (§3-1b 참고) |
| RGB LED | `S2\n` 전송 | LED 색상 | 빨간색 |
| 진동 릴레이 | Unity 실행 후 `DebugInputPanel`에서 진동 버튼 클릭 (또는 `serial_monitor.py`에서 `v` 키) | 릴레이 클릭음 + 체감 진동 | 프리셋 길이만큼 ON |

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
[ ] STEP 3  진동 릴레이 연결 확인 (DebugInputPanel에서 "● 릴레이 연결됨" 표시)
[ ] STEP 3  진동 릴레이 동작 확인 (DebugInputPanel 진동 버튼)
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

    // 진동 (레거시, ESP32 GPIO2 → IRF520 → 모터) — 이 프로젝트에서는 미사용, §3-6 릴레이 참고
    public void SetVibration(int pattern) => Send($"V{pattern}");
    public void SetBrake(bool on)         => Send(on ? "B1" : "B0");

    // 센서 제어
    public void SetRGBState(int state)    => Send($"S{state}");
    public void CalibrateSteer()          => Send("C");
}
```

> 이 예시는 ESP32 프로토콜 자체를 보여주기 위한 단독 참고 코드입니다. 실제 프로젝트의 진동 제어는 `SetVibration()`이 아니라 별도 포트로 붙는 `Assets/Scripts/Core/VibrationRelay.cs`를 사용합니다 (§3-6).

### 5-2. 사용 예시

```csharp
float steer   = bike.Data.str;      // -45 ~ 45도
float speed   = bike.Data.spd;      // km/h
bool  braking = bike.Data.brk == 1;

bike.SetVibration(1);   // V1: 위험 경고 — 레거시, 이 프로젝트에서는 VibrationRelay 사용 (§3-6)
bike.SetVibration(2);   // V2: 성공 — 레거시
bike.SetBrake(true);    // B1: 브레이크 피드백 (ESP32 GPIO2 — 릴레이와 별개, 현재도 사용 중)
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
| 브레이크 진동이 작동 안 함 (ESP32 쪽) | IRF520 모듈 배선 오류 | SIG(GPIO2)·VCC(5V)·GND·V+·OUT 배선 점검 |
| 브레이크 진동이 약함 | IRF520 게이트 전압 부족 | 3.3V 신호로 동작 확인, 모터 전원 5V 확인 |
| 이벤트/퀴즈 진동이 작동 안 함 (릴레이 쪽) | 릴레이 포트 미연결 또는 상태확인 실패 | `DebugInputPanel`에서 "● 릴레이 연결됨" 확인, `config.ini`의 `RelayPortName` 점검, USB 케이블/전원(5V) 점검 |
| `VibrationRelay` 응답 없음 로그 반복 | 릴레이 보드가 상태확인(`FF`) 명령에 응답 안 함 | 릴레이 보드레이트(9600) 및 배선 확인, 다른 프로그램이 같은 포트 점유 중인지 확인 |
| 게임 종료 후 진동이 계속 켜져 있음 | 크래시·강제 종료·에디터 도메인 리로드로 OFF 프레임을 못 보냄 (릴레이는 마지막 상태를 유지하는 래치 방식) | Unity를 다시 실행하면 연결 직후 OFF를 보내 자동으로 꺼집니다. 즉시 끄려면 릴레이 USB를 뽑거나 `serial_monitor.py`에서 `v` 키로 ON→OFF를 한 번 보내세요 |
| 게임 실행 중 진동이 안 꺼짐 | OFF 전송 실패 후 포트가 죽은 상태 | `[VibrationRelay] 전송 실패` 로그 확인. 재연결이 성공하면 밀린 OFF가 자동 전송됩니다. `스레드가 1500ms 내에 종료되지 않음` 경고가 보이면 종료 시 정리가 끝나지 않은 것이므로 릴레이 전원을 재인가하세요 |

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
       ├─ 시리얼 수신 처리 (P/B/C/H/M 수신, S/V는 펌웨어 테스트용)
       ├─ 브레이크 진동 업데이트 (GPIO2 PWM, B1/B0 수신 시)
       └─ RGB LED 업데이트

Unity 측
  │
  ├─ 수신 스레드에서 ESP32 라인 읽기 → 메인 스레드에서 JSON 파싱
  ├─ ESP32 포트로 P/B/C/M 명령 + 10초 주기 H(keep-alive) 전송
  ├─ 0.5초 무수신 → 입력 리셋, 5초 무수신 → 포트 강제 재연결 (§2-5)
  └─ 이벤트 발생 시 진동은 InputManager.SendVibrate() → VibrationRelay가 별도 릴레이 포트로 전송 (§3-6)
```

---

*bicycle_sim_x v6.0 · 빛고을국민안전체험관 / FLUXION*
