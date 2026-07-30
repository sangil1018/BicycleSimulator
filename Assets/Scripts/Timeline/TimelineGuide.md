# Timeline 구성 가이드

> **이 문서 하나만 따라하면 자전거 속도 연동 Timeline이 완성됩니다.**
>
> Timeline은 Unity가 자동 재생하지 않습니다.
> 자전거 페달 속도에 비례해 `TimelineGameController`가 매 프레임 재생 속도를 조절합니다.
> 이벤트는 네 종류의 트랙으로 배치합니다.
> - **게임 이벤트** → Unity 내장 **Signal Track** + Signal Emitter (내장 Signal Receiver가 UnityEvent로 수신)
> - **방향 안내** → 커스텀 **Direction Track** + `DirectionMarker`
> - **퀴즈** → 커스텀 **Quiz Track** + `QuizMarker`
> - **진행도 체크포인트** → 커스텀 **Checkpoint Track** + `CheckpointMarker`
>
> 앞의 세 트랙은 최종적으로 `GameSignalReceiver`로 전달되고,
> **Checkpoint Track만 예외**로 `TimelineGameController`가 타임라인 에셋에서 직접 읽습니다(바인딩 불필요).

---

## 전체 작업 순서

```
① GameObject 구성        ← PlayableDirector + 두 컴포넌트 + 내장 Signal Receiver
② Timeline Asset 생성    ← .playable 파일 생성 및 연결
③ 트랙 구성              ← Animation / Signal / Direction / Quiz / Checkpoint Track
④ 이벤트 배치            ← Signal Emitter · DirectionMarker · QuizMarker · CheckpointMarker
⑤ Inspector 값 설정      ← 속도·배속 파라미터 입력
⑥ 바인딩 최종 확인       ← 연결 누락 없는지 체크
⑦ Play 테스트            ← 디버그 HUD로 확인
```

---

## ① GameObject 구성

### 1-1. 빈 오브젝트 만들기

```
Hierarchy → Create Empty
이름: "TimelineDirector"
```

### 1-2. 컴포넌트 추가

```
Add Component → Playable Director
Add Component → Timeline Game Controller
Add Component → Game Signal Receiver
Add Component → Signal Receiver        ← Unity 내장. 게임 이벤트(Signal Track) 수신용
```

> 위 컴포넌트가 **반드시 같은 GameObject**에 있어야 합니다.
> `INotificationReceiver`(GameSignalReceiver)와 내장 `Signal Receiver`는 PlayableDirector와 동일한 오브젝트에서만 신호를 받습니다.

### 1-3. PlayableDirector 기본값 설정

```
Inspector → Playable Director
  Update Mode   : Game Time   ← 필수. GameTime 모드로 신호 발화
  Play On Awake : ❌ Off      ← 필수. 스크립트가 직접 제어
```

> ⚠️ `Manual` 또는 `DSP Clock` 모드에서는 마커/시그널이 발화되지 않습니다.
> (`TimelineGameController.Awake()`가 자동으로 Game Time · Play On Awake Off로 설정하지만, 에디터에서도 맞춰두는 것을 권장합니다.)

### 1-4. Game Signal Receiver 설정

```
Inspector → Game Signal Receiver
  Speed UI Controller : 씬의 SpeedUIController 오브젝트 드래그
```

`GameSignalReceiver`의 역할:
- `QuizMarker`·`DirectionMarker`는 `OnNotify()`에서 직접 수신.
- 게임 이벤트는 내장 `Signal Receiver`의 UnityEvent가 아래 public 메서드를 호출:
  `TriggerBrakeEvent` · `TriggerWarningStop` · `TriggerBicycleStop` · `TriggerAutoPlayStart` · `TriggerAutoPlayEnd`

---

## ② Timeline Asset 생성

### 2-1. .playable 파일 만들기

```
Project 패널 → Assets/Timelines/ 폴더 선택 (없으면 생성)
우클릭 → Create → Timeline
파일명: Level1_Timeline  (레벨 번호에 맞게)
```

### 2-2. PlayableDirector에 연결

```
TimelineDirector 오브젝트 선택
Inspector → Playable Director → Playable 필드
→ Level1_Timeline 드래그
```

### 2-3. Timeline 편집창 열기

```
방법 A : TimelineDirector 선택 → Window → Sequencing → Timeline
방법 B : Project 패널에서 Level1_Timeline 더블클릭
```

---

## ③ 트랙 구성

### 3-1. Animation Track 추가 (카메라 움직임)

```
Timeline 창 → 좌측 상단 + 버튼 → Animation Track
트랙 좌측 바인딩 영역 → 씬의 카메라 오브젝트 드래그
```

클립 추가 및 편집:

```
Animation Track 위 우클릭 → Add Animation Clip
생성된 클립 더블클릭 → Animation 창 열림
→ 키프레임 추가하여 카메라 이동 경로 설정
```

> **클립 길이 기준**: 기준 속도(기본 15 km/h)에서 1.0× 재생됩니다.
> 실제 구간 거리와 체감 속도에 맞춰 클립 길이를 조정하세요.

### 3-2. 이벤트 트랙 추가

```
Timeline 창 → + 버튼 →
  Signal Track       ← 게임 이벤트 (브레이크/경고/자동진행 등)
  Direction Track    ← 방향 안내 화살표
  Quiz Track         ← OX 퀴즈
  Checkpoint Track   ← 진행도 슬라이더 기준점 + 구간 이벤트
```

각 트랙의 좌측 바인딩 영역에 대상 컴포넌트를 드래그합니다.

| 트랙 | 바인딩 대상 |
|------|-------------|
| Signal Track | 내장 `Signal Receiver` (TimelineDirector) |
| Direction Track | `GameSignalReceiver` (TimelineDirector) |
| Quiz Track | `GameSignalReceiver` (TimelineDirector) |
| Checkpoint Track | **바인딩 없음** — `TimelineGameController`가 에셋에서 직접 읽음 |

---

## ④ 이벤트 배치

### 4-1. 게임 이벤트 (Signal Track)

```
Signal Track 위 원하는 시간에 우클릭 → Add Signal Emitter
생성된 Emitter 클릭 → Inspector → Signal Asset 지정
  (Assets/Signals/ 의 .signal 에셋 선택)
```

내장 `Signal Receiver`(TimelineDirector) Inspector에서 각 Signal Asset을
`GameSignalReceiver`의 메서드에 연결합니다.

| GameSignalReceiver 메서드 | 동작 | GameState |
|---------------------------|------|-----------|
| `TriggerBrakeEvent` | 돌발 브레이크 — Freeze → 브레이크 판정(기본 5초) → 성공 +10점 → Resume | `EventBrake` |
| `TriggerWarningStop` | 경고 정지 — Freeze → 대기(기본 3초) → Resume | `EventWarning` |
| `TriggerBicycleStop` | 정지 유지 — Freeze (다른 이벤트가 재개할 때까지) | `BicycleStop` |
| `TriggerAutoPlayStart` | 자동진행 시작 — 입력 무시, 고정 배속(횡단보도 걷기) | `CrosswalkWalk` |
| `TriggerAutoPlayEnd` | 자동진행 종료 — 속도 입력 연동 복귀 | `NormalRiding` |

> 내장 `Signal Receiver`의 UnityEvent에는 `GameSignalReceiver` 외의 씬 오브젝트도 함께 연결할 수 있습니다.
> Level1은 이 방식으로 자전거 모델 연출을 붙여 두었습니다.
>
> | 컴포넌트 | 메서드 | 용도 |
> |---|---|---|
> | `BikeRotation` | `RotateBike(각도)` | 자전거를 지정 Y각도로 즉시 고정 (횡단보도 진입 시 끌바 방향 등) |
> | `BikeRotation` | `FollowBikeRotation()` | 호출 시점부터 매 프레임 `bikeRotationTransform`의 회전을 추종 (주행 복귀) |
> | `BikeRotation` | `StopFollowBikeRotation()` | 추종 중단 (마지막 회전값 유지) |
>
> 예: `AutoPlayEnd` 시그널 하나에 `TriggerAutoPlayEnd` + `FollowBikeRotation`을 함께 연결해
> 자동진행이 끝나는 순간 자전거가 다시 조향을 따라가게 합니다.

### 4-2. 방향 안내 (Direction Track)

```
Direction Track 위 원하는 시간에 우클릭 → Add DirectionMarker
생성된 마커 클릭 → Inspector에서 설정
```

| 필드 | 설명 |
|------|------|
| `Direction` | `Normal`(직진) / `Left` / `Right` / `Right45`(우사선) |
| `Retroactive` | 타임라인 도중 진입 시 이미 지난 마커 재실행 여부 |
| `Emit Once` | 루프 재생 시 1회만 발화 |

> 속도 tier에 따라 UI 트리거에 자동 postfix가 붙습니다: `left` → `left_y`(yellow) → `left_r`(red)

### 4-3. 퀴즈 (Quiz Track)

```
Quiz Track 위 원하는 시간에 우클릭 → Add QuizMarker
생성된 마커 클릭 → Inspector에서 Quiz Index 설정
```

| 필드 | 설명 |
|------|------|
| `Quiz Index` | 퀴즈 번호 (0~3) |
| `Retroactive` / `Emit Once` | DirectionMarker와 동일 |

> 퀴즈 도달 시 타임라인이 Freeze되고 퀴즈 UI가 표시됩니다.
> **마지막 퀴즈**(`GameManager.FinalQuizIndex`, 기본 3)는 `BlackBoard`의 엔딩 시퀀스가
> `OnTimelineComplete()`를 호출하여 결과 화면으로 전환합니다.

### 4-4. 진행도 체크포인트 (Checkpoint Track)

상단 진행도 슬라이더의 **눈금 위치**를 정의하고, 통과 시 임의의 이벤트를 1회 발동합니다.

```
Checkpoint Track 위 원하는 시간에 우클릭 → Add CheckpointMarker
생성된 마커 클릭 → Inspector에서 Index 설정 (0~7)
```

| 필드 | 설명 |
|------|------|
| `Index` | 체크포인트 번호 (0~7). `TimelineGameController`의 `On Checkpoint[Index]` 슬롯과 대응 |

**동작 방식**

- `TimelineGameController.Start()`가 타임라인 에셋 전체(그룹 트랙 하위 포함)를 순회해 `CheckpointMarker`를 모으고 **시간 오름차순**으로 정렬합니다.
- 슬라이더 값은 실제 재생 시간이 아니라 **체크포인트 기준 등분값**으로 리매핑됩니다.
  전체를 10으로 두고 **양 끝 구간에 0.5씩**, 남은 9를 체크포인트 사이 (n−1)개 구간에 균등 배분합니다.
  → 구간 길이가 달라도 슬라이더는 일정한 간격으로 움직입니다.
- 마커 통과 시 `On Checkpoint[Index]` UnityEvent가 **1회만** 발동합니다. (`Play()` 호출 시 발동 이력 초기화)
- 퀴즈 Freeze 중에도 슬라이더/체크포인트 판정은 계속 처리되며, 같은 시각의 QuizMarker보다 **먼저** 발동하도록 한 프레임분 선행 보정됩니다.

> ⚠️ **마커는 8개 기준**입니다. 개수가 다르면 경고 로그와 함께 `(n-1)등분`으로 동작합니다.
> 시간순 i번째 마커의 `Index`가 i가 아니면 경고가 뜹니다 —
> **슬라이더는 시간순, 이벤트는 Index 슬롯**으로 동작하므로 둘을 일치시키세요.

### 4-5. 배치 예시 (60초 코스 기준)

```
T= 0:00  ───── 라이딩 시작 (Play() 호출로 진입)
T= 0:05  [Checkpoint] idx=0    슬라이더 첫 눈금
T= 0:08  [Direction] Right     방향 화살표 → 우회전
T= 0:10  [Direction] Normal    방향 화살표 → 직진
T= 0:15  [Signal] BrakeEvent   돌발 브레이크
T= 0:25  [Quiz] idx=0          퀴즈 0번
T= 0:30  [Checkpoint] idx=1    슬라이더 두 번째 눈금
T= 0:40  [Signal] AutoPlayStart 횡단보도 자동진행
T= 0:45  [Signal] AutoPlayEnd  자동진행 종료 (+ BikeRotation.FollowBikeRotation)
T= 0:55  [Quiz] idx=1          퀴즈 1번
T= 1:00  ───── Timeline 끝
```

---

## ⑤ Inspector 값 설정

### 5-1. TimelineGameController

```
Inspector → Timeline Game Controller

  [Timeline]
  Director   : (자동 참조됨, 비어있으면 PlayableDirector 드래그)
  Nav Slider : 상단 진행도 슬라이더 (Slider 컴포넌트) — 비워두면 슬라이더 갱신만 생략

  [Speed Mapping]
  Base Speed Kph      : 15    ← 이 속도에서 1.0× 재생
  Max Rate            : 1.5   ← 재생 배속 최대값
  Fixed Auto Speed    : 1.0   ← AutoPlay 구간 고정 배속

  [Playback Control]
  Playback Multiplier : 1.0   ← config.ini의 PlaybackMultiplier 값이 런타임에 덮어씌움

  [Checkpoint]
  On Checkpoint       : 8칸 UnityEvent 배열 — CheckpointMarker의 Index와 대응
```

> `Base Speed Kph` · `Playback Multiplier` · `Max Rate`는 `Start()`에서 `InputManager`(config.ini)
> 값으로 덮어씌워집니다. **인스펙터 값은 config.ini가 없을 때의 폴백**이므로,
> 현장 조정은 `config.ini`(`BaseSpeedKph` / `PlaybackMultiplier` / `MaxRate`)에서 하세요.
> 속도가 1 km/h 미만이면 재생 배속이 사실상 0에 수렴해 타임라인이 멈춥니다.

**속도-배속 대응표 (Base 15 km/h · MaxRate 1.5 기준)**

| 자전거 속도 | 재생 배속 |
|-------------|-----------|
| 0 km/h | 0× (정지) |
| 7.5 km/h | 0.5× |
| 15 km/h | 1.0× |
| 22.5 km/h 이상 | 1.5× (최대) |

> 현재 배포용 `config.ini`는 `MaxRate=3.0`이라 상한이 3.0×(45 km/h)까지 열려 있습니다.

### 5-2. GameManager (레벨 씬 루트)

```
Inspector → Game Manager

  [Timing]
  Delay Start           : 2.5  ← GameReady 후 시작 메뉴 표시까지 대기
  Brake Event Sec       : 5    ← 브레이크 판정 제한 시간
  Warning Stop Sec      : 3    ← 경고 정지 유지 시간
  Result Display Sec    : 3    ← 브레이크 결과 표시 시간
  Quiz Duration Sec     : 10   ← 퀴즈 표시 시간
  Final Quiz Index      : 3    ← 마지막 퀴즈 인덱스 (0-based)
  Final Result Wait Sec : 13   ← 결과 화면 유지 시간
  Total End Wait Sec    : 15   ← 결과 후 홈 씬 전환까지 대기
```

---

## ⑥ 바인딩 최종 확인

Play 전 아래 항목을 하나씩 확인합니다.

```
□ PlayableDirector.UpdateMode = Game Time
□ PlayableDirector.PlayOnAwake = Off
□ PlayableDirector.Playable = Level1_Timeline (에셋 연결됨)
□ TimelineGameController.Director 참조가 비어있지 않음
□ GameSignalReceiver.SpeedUIController 연결됨
□ Signal Track 바인딩 = 내장 Signal Receiver, 각 Emitter에 Signal Asset 지정
□ 내장 Signal Receiver의 UnityEvent가 GameSignalReceiver.Trigger* 메서드에 연결됨
□ Direction Track / Quiz Track 바인딩 = GameSignalReceiver
□ QuizMarker에 Quiz Index가 올바르게 설정됨
□ CheckpointMarker 8개 배치, Index가 시간순(0→7)과 일치
□ TimelineGameController.Nav Slider 연결됨 (진행도 슬라이더 사용 시)
□ On Checkpoint 배열에 필요한 이벤트 연결됨
□ GameManager가 씬에 1개 존재
```

> Play 직후 Console의 `[TL] Checkpoint N개 수집 …` 로그로 마커 수집 결과를 확인할 수 있습니다.
> 개수·순서 경고가 함께 뜨면 마커 배치를 먼저 고치세요.

---

## ⑦ Play 테스트

### 7-1. 화면 좌상단 디버그 HUD (DEBUG_GUI 빌드 + debugMode=1)

`debugMode=1`(config.ini)일 때 좌상단에 아래 정보가 표시됩니다.

```
[TL] canMove:False  graphValid:True  SpeedKph:0.0  rate:0.00  time:0.00/60.00
```

| 항목 | 정상값 | 이상 신호 |
|------|--------|-----------|
| `graphValid` | True | False → PlayableAsset 미연결 |
| `canMove` | Play() 후 True | False → StartRiding() 미호출 |
| `rate` | 페달 밟으면 0 초과 | 계속 0 → 속도 입력 미수신 |
| `time` | 증가 | 멈춤 → Freeze 상태 확인 |

### 7-2. 단계별 확인

**1단계 — 라이딩 시작**

```
O 버튼 (또는 스페이스 키) 누름
→ canMove: True 로 변경 확인
→ 페달 밟기 → rate 값 0 초과 → time 증가 확인
```

**2단계 — 이벤트 동작**

```
BrakeEvent 신호 도달:
  → canMove: False (Freeze)
  → 브레이크 입력 → 판정 후 Resume

Quiz 마커 도달:
  → 퀴즈 팝업 표시 → 응답/시간 경과 후 진행

AutoPlayStart 신호 도달:
  → 자동 진행 모드 전환 (rate = fixedAutoSpeed)
  → AutoPlayEnd 신호에서 속도 연동 복귀
```

**3단계 — 종료 확인**

```
마지막 퀴즈(Final Quiz Index) 응답 →
  → BlackBoard 엔딩 시퀀스 → OnTimelineComplete()
  → GameResult 상태 전환 → 결과 UI 표시 → 홈 씬 전환
```

---

## 문제 해결

| 증상 | 원인 | 해결책 |
|------|------|--------|
| Timeline이 전혀 안 움직임 | `canMove=false` 고착 | O버튼 → `StartRiding()` → `Play()` 호출 경로 확인 |
| 시그널/마커가 발화 안 됨 | UpdateMode 오류 | PlayableDirector.UpdateMode = **Game Time** 확인 |
| 게임 이벤트만 발화 안 됨 | Signal Receiver 미연결 | Signal Track 바인딩 + UnityEvent → GameSignalReceiver 연결 확인 |
| 방향/퀴즈만 발화 안 됨 | 트랙 바인딩 누락 | Direction/Quiz Track 바인딩 = GameSignalReceiver 확인 |
| 속도 무관하게 일정 속도 진행 | `_autoPlay=true` 고착 | `AutoPlayEnd` 신호 배치 또는 `SetAutoPlay(false)` 확인 |
| `graphValid:False` | PlayableAsset 미연결 | PlayableDirector.Playable 필드에 .playable 에셋 재연결 |
| 방향 화살표가 변경 안 됨 | SpeedUIController 미연결 | GameSignalReceiver.SpeedUIController 드래그 재확인 |
| 진행도 슬라이더가 안 움직임 | Nav Slider 미연결 | TimelineGameController.Nav Slider에 Slider 드래그 |
| 슬라이더 간격이 들쭉날쭉 | CheckpointMarker 누락 | 마커 8개 배치 확인 (없으면 시간 비례로 폴백) |
| 체크포인트 이벤트가 안 나옴 | Index ↔ 슬롯 불일치 | Console의 `Checkpoint Index N — 대응하는 이벤트 슬롯이 없습니다` 경고 확인 후 Index를 0~7로 수정 |
| 슬라이더가 끝까지 안 참 | duration ≤ 마지막 마커 시각 | 마지막 CheckpointMarker를 타임라인 끝보다 앞으로 이동 |
| 디버그 HUD가 안 보임 | DEBUG_GUI 심볼 없음 / debugMode=0 | `Assets/csc.rsp`의 `-define:DEBUG_GUI` 및 config.ini `debugMode=1` 확인 |
