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

## 2. 하드웨어 설정 (config.ini)

| 파라미터 | 기본값 | 설명 |
| :--- | :--- | :--- |
| `PortName` | `COM8` | ESP32 시리얼 포트 (센서 전용) |
| `BaudRate` | `115200` | ESP32 시리얼 통신 속도 |
| `BaseSpeedKph` | `15.0` | 1.0× 재생 기준 속도(km/h) |
| `logo` | `1` | UI 로고 표시 여부 |
| `RelayPortName` | `COM3` | 진동 릴레이 연결 포트 (ESP32와 별도 USB 장치) |
| `RelayBaudRate` | `9600` | 진동 릴레이 통신 속도 |
| `VibeShortDuration` | `0.15` | 짧은 진동 지속시간(초) — Ready/Walk/Correct/Click |
| `VibeMediumDuration` | `0.5` | 중간 진동 지속시간(초) — Success |
| `VibeLongDuration` | `1.5` | 긴 진동 지속시간(초) — Danger/Wrong |

> 위 값은 모두 `InputManager`가 읽어서 `VibrationRelay`에 전달합니다. 상세 프로토콜은 `Hardware/Unity_시리얼_통신_가이드.md` §3-6 참고.

---

## 3. Timeline 이벤트 설정

Timeline Signal Track에 아래 Signal Asset을 배치하여 이벤트를 구성합니다.

### 3.1 GameEventSignal

| `eventType` | 설명 |
| :--- | :--- |
| `BrakeEvent` | 돌발 브레이크 이벤트 (9초 판정) |
| `OXQuiz` | OX 퀴즈 (`quizIndex` 0~3 설정) |
| `CrosswalkStart` | 횡단보도 시퀀스 (자동진행 → 내리기 → 걷기 → 타기) |
| `AutoPlayStart` | 자동진행 구간 시작 (입력 무시, fixedAutoSpeed로 재생) |
| `AutoPlayEnd` | 자동진행 구간 종료 (페달 입력 재개) |

### 3.2 DirectionSignal

네비게이션 방향을 변경합니다. `direction` 값을 아래 중 하나로 설정하세요.

| `direction` 값 | 의미 |
| :--- | :--- |
| `normal` | 직진 |
| `left` | 좌회전 |
| `right` | 우회전 |
| `right_45` | 우사선 |

속도 tier에 따라 트리거에 자동으로 postfix가 붙습니다:  
기본(`left`) → yellow 이상(`left_y`) → red 이상(`left_r`)

---

## 4. 속도 UI (SpeedUIController)

Inspector에서 아래 값을 조정합니다.

| 항목 | 설명 |
| :--- | :--- |
| `Yellow Threshold` | 이 속도(km/h) 이상이면 UI 텍스트가 yellow로 변경 |
| `Red Threshold` | 이 속도(km/h) 이상이면 red + overSpeedUI 활성화 |
| `Fade Duration` | Show/Hide 알파 전환 시간 (기본 0.5초) |

GameState에 따른 자동 동작:
- `NormalRiding` → Show (alpha 1)
- `OXQuiz`, `EventBrake`, `CrosswalkWalk` → Hide (alpha 0)

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

### 5.3 영속 매니저
`GameManager`, `InputManager`, `QuizManager`, `VibrationRelay`는 ``DontDestroyOnLoad``로 씬 전환 후에도 유지됩니다.  
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
3. Timeline Signal Track에 `GameEventSignal` / `DirectionSignal`을 배치합니다.
4. `HomeGameManager`에 새 씬 로드를 연결합니다.
5. **Build Settings**에 새 씬을 등록합니다.
