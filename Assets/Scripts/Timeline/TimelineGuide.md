# Timeline 구성 가이드

> **이 문서 하나만 따라하면 자전거 속도 연동 Timeline이 완성됩니다.**
>
> Timeline은 Unity가 자동 재생하지 않습니다.
> 자전거 페달 속도에 비례해 `TimelineGameController`가 매 프레임 재생 속도를 조절합니다.
> 이벤트는 **SignalAsset 없이** 글로벌 Markers 트랙에 `GameMarker`를 직접 배치합니다.

---

## 전체 작업 순서

```
① GameObject 구성        ← PlayableDirector + 두 컴포넌트 추가
② Timeline Asset 생성    ← .playable 파일 생성 및 연결
③ 트랙 구성              ← Animation Track 추가
④ GameMarker 배치        ← 글로벌 Markers 트랙에 마커 핀 꽂기
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

### 1-2. 컴포넌트 세 개 추가

```
Add Component → Playable Director
Add Component → Timeline Game Controller
Add Component → Game Signal Receiver
```

> 세 컴포넌트가 **반드시 같은 GameObject**에 있어야 합니다.
> INotificationReceiver는 PlayableDirector와 동일한 오브젝트에서만 수신됩니다.

### 1-3. PlayableDirector 기본값 설정

```
Inspector → Playable Director
  Update Mode   : Game Time   ← 필수. GameTime 모드로 신호 발화
  Play On Awake : ❌ Off      ← 필수. 스크립트가 직접 제어
```

> ⚠️ `Manual` 또는 `DSP Clock` 모드에서는 Marker 신호가 발화되지 않습니다.

### 1-4. Game Signal Receiver 설정

```
Inspector → Game Signal Receiver
  Speed UI Controller : 씬의 SpeedUIController 오브젝트 드래그
```

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

---

## ④ GameMarker 배치

SignalAsset과 Signal Track이 **불필요**합니다.
Timeline 상단의 글로벌 **Markers 트랙**에 `GameMarker`를 직접 배치합니다.

### 4-1. Markers 트랙 확인

```
Timeline 창 상단에 "Markers" 행이 보여야 합니다.
보이지 않으면 → Timeline 창 우상단 ⋮ → Show Markers
```

### 4-2. GameMarker 추가

```
Markers 행에서 원하는 시간 위치에 우클릭
→ Add Game Marker
→ 생성된 마커 클릭 → Inspector에서 설정
```

### 4-3. Inspector 필드 설명

| 필드 | 설명 |
|------|------|
| `Marker Type` | 이벤트 종류 선택 (아래 표 참고) |
| `Quiz Index` | OXQuiz 타입일 때만 사용 (0~3) |
| `Retroactive` | 타임라인 도중 진입 시 이미 지난 마커 재실행 여부 |
| `Emit Once` | 루프 재생 시 1회만 발화 |

### 4-4. MarkerType 종류

| MarkerType | 동작 | 추가 설정 |
|------------|------|-----------|
| `BrakeEvent` | 급정거 이벤트 — 타임라인 Freeze, 브레이크 대기 | — |
| `OXQuiz` | OX 퀴즈 팝업 — 타임라인 Freeze, 12초 후 Resume | `Quiz Index` 설정 |
| `CrosswalkStart` | 횡단보도 루틴 — AutoPlay 전환, 하차/걷기/탑승 | — |
| `AutoPlayStart` | 자동진행 시작 — 입력 무시, 고정 배속 진행 | — |
| `AutoPlayEnd` | 자동진행 종료 — 속도 입력 연동 복귀 | — |
| `DirectionNormal` | 방향 화살표 → 직진 | — |
| `DirectionLeft` | 방향 화살표 → 좌회전 | — |
| `DirectionRight` | 방향 화살표 → 우회전 | — |
| `DirectionRight45` | 방향 화살표 → 우측 45° | — |

### 4-5. 배치 위치 설계 기준

| 구간 | MarkerType | 동작 |
|------|------------|------|
| 급정거 직전 | `BrakeEvent` | Freeze → 브레이크 대기(최대 9초) → Resume |
| 퀴즈 표시 구간 | `OXQuiz` | Freeze → 퀴즈 팝업(12초) → Resume |
| 횡단보도 진입 직전 | `CrosswalkStart` | 하차/걷기/탑승 루틴 |
| 입력 없이 진행할 구간 시작 | `AutoPlayStart` | 고정 배속(1.0×) 자동 진행 |
| 자동진행 구간 끝 | `AutoPlayEnd` | 속도 입력 연동 복귀 |
| 방향 전환 직전 | `DirectionLeft` 등 | 화살표 UI 변경 |

### 4-6. 배치 예시 (60초 코스 기준)

```
T= 0:00  ───── 라이딩 시작 (Play() 호출로 진입)
T= 0:08  [DirectionRight]    방향 화살표 → 우회전
T= 0:10  [DirectionNormal]   방향 화살표 → 직진
T= 0:15  [BrakeEvent]        급정거 이벤트
T= 0:25  [OXQuiz]  idx=0     퀴즈 0번
T= 0:40  [CrosswalkStart]    횡단보도
T= 0:55  [OXQuiz]  idx=1     퀴즈 1번
T= 1:00  ───── Timeline 끝 → GameResult 자동 전환
```

---

## ⑤ Inspector 값 설정

### 5-1. TimelineGameController

```
Inspector → Timeline Game Controller

  [Timeline]
  Director : (자동 참조됨, 비어있으면 PlayableDirector 드래그)

  [Speed Mapping]
  Base Speed Kph      : 15    ← 이 속도에서 1.0× 재생
  Min Speed Kph       : 1     ← 이 속도 미만이면 타임라인 정지
  Max Rate            : 1.5   ← 재생 배속 최대값
  Fixed Auto Speed    : 1.0   ← AutoPlay 구간 고정 배속

  [Playback Control]
  Playback Multiplier : 1.0   ← config.ini 값이 런타임에 덮어씌움
```

**속도-배속 대응표 (Base 15 km/h 기준)**

| 자전거 속도 | 재생 배속 |
|-------------|-----------|
| 1 km/h 미만 | 0× (정지) |
| 7.5 km/h | 0.5× |
| 15 km/h | 1.0× |
| 22 km/h 이상 | 1.5× (최대) |

> `config.ini`의 `PlaybackMultiplier` 값을 변경하면 전체 배속이 비례 조정됩니다.

### 5-2. GameManager (레벨 씬 루트)

```
Inspector → Game Manager

  [Timing]
  Brake Event Sec       : 9    ← 브레이크 대기 제한 시간
  Result Display Sec    : 3    ← 브레이크 결과 표시 시간
  Crosswalk Sec         : 4    ← 횡단 걷기 시간
  Quiz Duration Sec     : 12   ← 퀴즈 표시 시간
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
□ Markers 트랙의 각 GameMarker에 MarkerType이 설정됨
□ OXQuiz 마커에 QuizIndex가 올바르게 설정됨
□ GameManager가 씬에 1개 존재 (싱글턴)
```

> ✅ Signal Track, Signal Asset, Signal Emitter는 **사용하지 않습니다.**

---

## ⑦ Play 테스트

### 7-1. 화면 좌상단 디버그 HUD (에디터 전용)

Play를 누르면 좌상단에 아래 정보가 표시됩니다.

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
BrakeEvent 마커 도달:
  → canMove: False (Freeze)
  → 브레이크 입력 → canMove: True (Resume)

OXQuiz 마커 도달:
  → 퀴즈 팝업 표시
  → 12초 후 자동 Resume

CrosswalkStart 마커 도달:
  → 자동 진행 모드 전환 (rate = fixedAutoSpeed)
  → O 버튼 입력 후 속도 연동 복귀
```

**3단계 — 종료 확인**

```
Timeline 끝까지 도달 시:
  → GameResult 상태 전환
  → 결과 UI 표시 → 홈 씬 전환
```

---

## 문제 해결

| 증상 | 원인 | 해결책 |
|------|------|--------|
| Timeline이 전혀 안 움직임 | `canMove=false` 고착 | O버튼 → `StartRiding()` → `Play()` 호출 경로 확인 |
| Marker가 발화되어도 이벤트 없음 | UpdateMode 오류 | PlayableDirector.UpdateMode = **Game Time** 확인 |
| 속도 무관하게 일정 속도 진행 | `_autoPlay=true` 고착 | `AutoPlayEnd` 마커 배치 또는 `SetAutoPlay(false)` 확인 |
| `graphValid:False` | PlayableAsset 미연결 | PlayableDirector.Playable 필드에 .playable 에셋 재연결 |
| Timeline 끝에서 씬 전환 안 됨 | `OnTimelineComplete()` 미호출 | `director.duration` 값이 실제 클립 끝과 일치하는지 확인 |
| BrakeEvent 후 타임라인 안 재개 | 코루틴 중단 | `StopAllCoroutines()` 중복 호출 여부 확인 |
| 방향 화살표가 변경 안 됨 | SpeedUIController 미연결 | GameSignalReceiver.SpeedUIController 드래그 재확인 |
| 에디터 HUD가 안 보임 | 에디터 전용 빌드 | 빌드에서는 미표시 — 정상 동작 |
