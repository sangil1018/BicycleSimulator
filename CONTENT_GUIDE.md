# 자전거 시뮬레이터 콘텐츠 구성 가이드

---

## 1. 시스템 개요

자전거 페달링 속도(SpeedKph)에 따라 Unity Timeline 재생 속도가 실시간으로 동기화됩니다.  
이벤트(퀴즈, 브레이크, 횡단보도, 방향 안내)는 Timeline Signal로 타이밍을 지정합니다.

```
[ESP32 하드웨어] → InputManager → TimelineGameController → PlayableDirector
                       │        ↘ SpeedUIController (속도 UI / 네비게이션)
                       ↓
                  VibrationRelay (진동, USB 릴레이 — ESP32와 별도 포트)
                  GameSignalReceiver ← Timeline Signal Track
                       ↓
                  GameManager (이벤트 루틴)
```

---

## 2. 설정 파일 (config.ini)

실행 파일과 같은 폴더의 `config.ini`를 게임 시작 시 1회 로드합니다. 값 변경 후 게임을 재시작해야 반영됩니다.

### 2.1 [Settings]

| 파라미터 | 기본값 | 설명 |
| :--- | :--- | :--- |
| `StationID` | `1` | 스테이션 ID — 펌웨어 `STATION_ID`와 일치해야 데이터 수신 |
| `PortName` | `COM8` | ESP32 시리얼 포트 (센서 전용) |
| `BaudRate` | `115200` | ESP32 시리얼 통신 속도 |
| `BaseSpeedKph` | `15.0` | 1.0× 재생 기준 속도(km/h) |
| `MetersPerRevolution` | `1.5` | 페달 1회전당 이동 거리(m) — RPM→km/h 환산 |
| `PlaybackMultiplier` | `1.0` | 영상 재생 배속 승수 |
| `VibeMultiplier` | `1.0` | 진동 패턴 시간 배율 (0.5~3.0) |
| `BrakeStopDuration` | `1.0` | 브레이크 작동 시 완전 정지까지 걸리는 시간(초, 0.05~10). 잡는 순간의 속도에서 선형 감속하며, 잡고 있는 동안 페달 입력 무시 |
| `debugMode` | `0` | 디버그 GUI 표시 (1=표시) — 빌드에서도 동작 (§2.3) |
| `fps` | `60` | 목표 프레임레이트 (15~240, 0=제한 없음). VSync는 자동 비활성화 |
| `logo` | `1` | UI 로고 표시 여부 |

### 2.2 [Vibration] / [Camera] / [UI]

| 파라미터 | 기본값 | 설명 |
| :--- | :--- | :--- |
| `isActive` | `1` | 진동 사용 여부 (0=비활성화) |
| `RelayPortName` | `COM3` | 진동 릴레이 연결 포트 (ESP32와 별도 USB 장치) |
| `RelayBaudRate` | `9600` | 진동 릴레이 통신 속도 |
| `VibeShortDuration` | `0.2` | 짧은 진동 지속시간(초) — Ready/Walk/Correct/Click |
| `VibeMediumDuration` | `0.5` | 중간 진동 지속시간(초) — Success |
| `VibeLongDuration` | `1.5` | 긴 진동 지속시간(초) — Danger/Wrong |
| `SteeringRange` | `45` | 핸들 최대 조향각 출력 범위 (도, 1~45) |
| `CameraSteerSmoothTime` | `0.12` | 조향 회전 스무딩 시간(초) |
| `YellowThreshold` | `20.0` | 노란색 속도 경고 기준(km/h) |
| `RedThreshold` | `30.0` | 빨간색 속도 경고 기준(km/h) |

> 위 값은 모두 `InputManager`가 읽어서 각 시스템에 전달합니다. 섹션 헤더(`[Settings]` 등)는 구분용일 뿐이며 키 이름만 파싱됩니다. 상세 프로토콜은 `Hardware/Unity_시리얼_통신_가이드.md` §3-6 참고.

### 2.3 디버그 GUI (debugMode)

- `debugMode=1`이면 InputManager 상태 박스(연결/조향센서/속도/버튼)와 타임라인 상태 라벨이 화면에 표시됩니다. 빌드에서도 동일하게 동작하므로 현장 점검 시 ini만 수정하면 됩니다.
- 디버그 GUI 코드는 `DEBUG_GUI` 심볼(`Assets/csc.rsp`의 `-define:DEBUG_GUI`)로 컴파일됩니다. **최종 마스터 빌드에서 코드까지 제거하려면 csc.rsp에서 해당 줄을 삭제** 후 빌드하세요. 심볼이 없으면 `debugMode=1`이어도 표시되지 않습니다.
- 조향 센서(ICM-20948) 미인식 시 펌웨어가 조향값 0 고정으로 계속 동작하며, 디버그 GUI의 `SteerSens` 항목에서 "미인식(0고정)"으로 확인할 수 있습니다.

---

## 3. Timeline 이벤트 설정

이벤트는 세 종류의 트랙으로 구성합니다. 모두 `GameSignalReceiver`(PlayableDirector와 같은 오브젝트)로 전달됩니다.

- **게임 이벤트** → Unity 내장 **Signal Track** + Signal Emitter. 같은 오브젝트의 내장 **Signal Receiver**가 각 Signal Asset을 UnityEvent로 받아 `GameSignalReceiver`의 아래 메서드를 호출합니다.
- **퀴즈** → 커스텀 **Quiz Track**에 **QuizMarker** 배치.
- **방향 안내** → 커스텀 **Direction Track**에 **DirectionMarker** 배치.

### 3.1 게임 이벤트 (Signal Receiver → GameSignalReceiver)

| Signal Receiver가 호출할 메서드 | 동작 |
| :--- | :--- |
| `TriggerBrakeEvent()` | 돌발 브레이크 이벤트 — Freeze 후 `Brake Event Sec`(기본 5초) 내 브레이크 판정, 성공 시 +10점 |
| `TriggerWarningStop()` | 경고 정지 — Freeze 후 `Warning Stop Sec`(기본 3초) 대기 후 자동 재개 |
| `TriggerBicycleStop()` | 정지 유지 — Freeze 상태로 대기(별도 이벤트가 재개할 때까지) |
| `TriggerAutoPlayStart()` | 자동진행 구간 시작 (입력 무시, `Fixed Auto Speed`로 재생 · 횡단보도 걷기 구간) |
| `TriggerAutoPlayEnd()` | 자동진행 구간 종료 (페달 입력 재개) |

### 3.2 QuizMarker (Quiz Track)

OX 퀴즈를 띄웁니다. 마커의 `Quiz Index`(0~3)로 문제를 지정합니다.

### 3.3 DirectionMarker (Direction Track)

네비게이션 방향을 변경합니다. 마커의 `Direction` 값을 아래 중 하나로 설정하세요.

| `Direction` | UI 트리거 | 의미 |
| :--- | :--- | :--- |
| `Normal` | `normal` | 직진 |
| `Left` | `left` | 좌회전 |
| `Right` | `right` | 우회전 |
| `Right45` | `right_45` | 우사선 |

속도 tier에 따라 트리거에 자동으로 postfix가 붙습니다:  
기본(`left`) → yellow 이상(`left_y`) → red 이상(`left_r`)

---

## 4. 속도 UI (SpeedUIController)

Inspector에서 아래 값을 조정합니다.

| 항목 | 설명 |
| :--- | :--- |
| `Yellow Threshold` | 이 속도(km/h) 이상이면 UI 텍스트가 yellow로 변경 |
| `Red Threshold` | 이 속도(km/h) 이상이면 red + overSpeedUI 활성화 |
| `Fade Duration` | Show/Hide 알파 전환 시간 (기본 0.3초) |

GameState에 따른 자동 동작:
- `NormalRiding` → 속도가 `Visible Speed Threshold`(기본 1km/h) 이상일 때 Show
- `OXQuiz`, `EventBrake`, `EventWarning`, `BicycleStop`, `CrosswalkWalk` → Hide (alpha 0)

---

## 5. 씬 구조

### 5.1 홈 씬 (Home)
- `InputManager`, `GameManager`, `VibrationRelay` 오브젝트 필수 (버튼 선택 시 진동 피드백)
- Additive 로드 후 1.6초 커버 포인트에서 레벨 씬 활성화

### 5.2 레벨 씬 (Level)
**필수 컴포넌트:**
- `PlayableDirector` + `TimelineGameController` — 카메라 애니메이션 및 속도 제어
- `GameSignalReceiver` — Timeline Signal 수신 (PlayableDirector와 같은 오브젝트)
- `SpeedUIController` — 속도 UI 및 네비게이션 애니메이터
- `IntroManager`, `QuizManager` — 인트로 및 퀴즈
- `VibrationRelay` — 진동 피드백 (USB 릴레이, ESP32와 별도 포트)

### 5.3 매니저 수명
- **영속(`DontDestroyOnLoad`)**: `InputManager`, `VibrationRelay` — 씬 전환 후에도 유지됩니다.
- **씬 스코프(`SceneSingleton`)**: `GameManager`, `QuizManager` — 씬마다 새 인스턴스가 존재하므로 각 씬(Home/Level)에 오브젝트가 배치되어 있어야 합니다.

씬 로드 시 `GameManager`가 `TimelineGameController`를 자동으로 탐색합니다.  
`VibrationRelay`는 `InputManager`(`[DefaultExecutionOrder(-100)]`)보다 나중에 `Awake`되어 `config.ini` 값을 넘겨받습니다.

---

## 6. Inspector 주요 설정값

| 컴포넌트 | 변수명 | 설명 |
| :--- | :--- | :--- |
| `TimelineGameController` | `Base Speed Kph` | 1.0× 재생 기준 속도 |
| `TimelineGameController` | `Max Rate` | 최대 재생 배속 (기본 1.5×) |
| `TimelineGameController` | `Fixed Auto Speed` | 자동진행 구간 재생 배속 |
| `GameManager` | `Brake Event Sec` | 브레이크 판정 시간 |
| `GameManager` | `Quiz Duration Sec` | 퀴즈 UI 대기 시간 |
| `SpeedUIController` | `Yellow/Red Threshold` | 속도 색상 전환 기준값 |

---

## 7. 신규 코스 추가 절차

1. 기존 레벨 씬을 복제합니다.
2. `PlayableDirector`에 새 Timeline Asset을 연결하고 카메라 애니메이션을 제작합니다.
3. Signal Track(게임 이벤트) · Direction Track(`DirectionMarker`) · Quiz Track(`QuizMarker`)에 이벤트를 배치합니다. (§3 참고)
4. `HomeGameManager`에 새 씬 로드를 연결합니다.
5. **Build Settings**에 새 씬을 등록합니다.
