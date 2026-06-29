# 차량 교통 시스템 구성 가이드

> 보행자 시스템은 `PedestrianSetupGuide.md`를 참조하세요.

---

## 에디터 윈도우 열기

메뉴 **`교통시스템 > 에디터 열기`** (단축키: `Ctrl+Shift+T`)

창 상단 탭: **개요 | 노드 | 교차로 | 신호기 | 매니저 | 검사**

> 탭 설명과 사용 순서를 아래 Step에서 설명합니다.

---

## 아키텍처 한눈에 보기

```
TrafficNode ──── exits[] ────► TrafficNode ──── exits[] ────► ...
     │                              │
     │ stopSignal (직접 참조)        │ stopSignal
     ▼                              ▼
TrafficSignal             TrafficSignal
 ├─ 차량 신호 (R/Y/G)               ·
 └─ 보행자 신호 (R/G) ← 시각 전용    ·
     ▲                              ▲
     └──────── TrafficJunction ─────┘
               (페이즈별 신호 제어)

TrafficManager ──► TrafficVehicle (차량 AI)
                        │
                        ├─ currentNode.StopSignal.CanPass 직접 체크
                        │
                        └─ Node Queue (정적 레지스트리)
                              같은 정지선 노드를 향한 차량끼리
                              경로 거리로 순서·안전거리 유지
                              ▼
                         TrafficNode (정지선)

PedestrianSpawner ──► PedestrianController (보행자 AI)
                           웨이포인트만 따라 이동 / 신호 반응 없음
```

| 컴포넌트 | 역할 |
|---|---|
| **TrafficNode** | 경로 노드. exits[] 배열로 다음 노드 연결, 가중치 선택 |
| **TrafficSignal** | 통합 신호기. 차량 (Red/Yellow/Green) + 보행자 (Red/Green) 라이트 관리. TrafficJunction이 구동 |
| **TrafficJunction** | 교차로 신호 사이클. JunctionPhase 배열로 구성 |
| **TrafficVehicle** | 차량 AI. 노드 추종, 신호 확인, Node Queue 기반 안전거리 유지 및 순차 출발 |
| **TrafficManager** | 차량 스폰 및 리스폰 관리 (씬에 1개) |

---

## Step 1 — 경로 노드 배치 (`노드` 탭)

**`교통시스템 > 에디터 열기`** → **`노드`** 탭

### 1-1. 노드 생성 모드로 노드 배치

1. **`📍 노드 생성 모드`** 버튼 클릭 (활성화 시 노란색으로 표시됨)
2. 씬 뷰에서 **`Shift + 좌클릭`** → 클릭 위치에 노드 자동 생성
3. 차로 중앙을 따라 노드를 배치해 나갑니다
4. 완료 후 **`ESC`** 또는 버튼 재클릭으로 종료

**노드 간격 권장값**

| 구간 | 권장 간격 |
|---|---|
| 직선 | 8 ~ 15m |
| 곡선 | 3 ~ 6m |
| 교차로 진입 직전 | 정지선 노드 1개 배치 |

### 1-2. 노드 연결 모드로 경로 구성

1. **`🔗 노드 연결 모드`** 버튼 클릭
2. 씬 뷰에서 **출발 노드 클릭** → 노드가 파란색으로 변함
3. **도착 노드 클릭** → exits[] 자동 추가, 로그 확인
4. 분기가 필요하면 같은 출발 노드에서 여러 도착 노드를 차례로 클릭
5. **`ESC`** 로 선택 취소 / 모드 종료

> 연결된 노드는 Inspector **Exits** 항목에 표시되며, 가중치(weight) 기본값 10으로 추가됩니다.  
> 분기 확률을 바꾸려면 해당 노드를 선택 → Inspector에서 weight 값 수정

### 1-3. 노드 목록 확인

탭 아래쪽 **노드 목록**에서 색상 도트로 상태 파악:
- **노란 도트** = stopSignal 없음 (자유 통과)
- **빨간 도트** = stopSignal 연결됨 (정지선)
- **`선택`** / **`핑`** 버튼으로 씬에서 빠르게 찾기

### 1-4. 방향 자동 설정

```
노드 탭 → [모두 방향 자동 설정]  (또는 개요 탭 → 빠른 도구 → [모든 노드 방향 자동 설정])
```

개별 노드는 Inspector 하단 **`↺ 자동 방향 설정`** 버튼으로 처리합니다.

---

## Step 2 — 신호기 생성 (`신호기` 탭)

**`교통시스템 > 에디터 열기`** → **`신호기`** 탭

### 2-1. 신호기(TrafficSignal) 생성

1. **`새 TrafficSignal 생성`** 클릭 → 씬에 신호기 오브젝트 생성
2. Inspector에서 렌더러 슬롯 5개 연결:

   **차량 신호**
   - **Red Renderer** → 차량 빨간 불 Mesh Renderer
   - **Yellow Renderer** → 차량 노란 불 Mesh Renderer
   - **Green Renderer** → 차량 초록 불 Mesh Renderer

   **보행자 신호**
   - **Pedestrian Red Light** → 보행자 빨간 불 Mesh Renderer
   - **Pedestrian Green Light** → 보행자 초록 불 Mesh Renderer

3. 각 렌더러의 Material에 `_EMISSION` 활성화 (Standard Shader → Emission 체크)
4. 신호기 오브젝트를 실제 신호등 3D 오브젝트 위치로 이동

> 신호기 목록에서 렌더러 칩 `R` `Y` `G` `PR` `PG`가 **컬러로 켜져야** 정상입니다.  
> 회색으로 표시되면 해당 Renderer가 미연결 상태입니다.

### 2-2. 씬 뷰 배치

- 신호기 오브젝트를 교차로 각 방향의 정지선 위치에 배치합니다
- 콜라이더 불필요 — 차량은 SphereCast가 아닌 직접 참조로 신호를 확인합니다

---

## Step 3 — 교차로 자동 생성 (`개요` 탭 — 교차로 마법사)

**`교통시스템 > 에디터 열기`** → **`개요`** 탭 → **교차로 마법사** 섹션

### 방법 A: 4방향 교차로 자동 생성 (권장)

1. **교차로 이름** 입력 (예: `Junction_Center`)
2. **페이즈 수** 설정 (일반 4거리: 2)
3. **초록 지속(초)** 설정 (기본 25초)
4. **`4방향 교차로 자동 생성`** 클릭

→ 다음 오브젝트가 자동으로 생성됩니다:

```
Junction_Center/
├── Signal_N   (TrafficSignal)
├── Signal_S   (TrafficSignal)
├── Signal_E   (TrafficSignal)
├── Signal_W   (TrafficSignal)
└── Junction_Center_Junction   (TrafficJunction)
    ├── Phase 0 "남북 직진"  →  Signal_N, Signal_S
    └── Phase 1 "동서 직진"  →  Signal_E, Signal_W
```

5. 생성된 Signal_N/S/E/W 오브젝트를 실제 신호등 위치로 이동
6. Renderer 슬롯 연결 (Step 2-1 참조)

### 방법 B: 기존 신호기로 교차로 구성

이미 TrafficSignal 오브젝트를 배치했다면:

1. Hierarchy에서 교차로에 사용할 **TrafficSignal 오브젝트들을 다중 선택** (Ctrl+클릭)
2. **교차로 이름**, **페이즈 수** 설정
3. **`선택 신호기로 교차로 구성`** 클릭 → TrafficJunction 자동 생성 및 페이즈 배정

---

## Step 4 — 정지선 연결 (`신호기` 탭)

교차로 진입 직전 노드의 `Stop Signal`을 해당 방향 TrafficSignal에 연결합니다.

### GUI로 연결하기 (권장)

1. **Hierarchy에서 정지선 노드 선택** (빨간불에 멈춰야 할 방향의 최종 노드)
2. **`신호기`** 탭으로 이동
3. 상단에 `선택 중인 노드: [노드이름]` 안내가 표시됨
4. 목록에서 연결할 TrafficSignal 찾아 **`노드 연결`** 버튼 클릭
5. 버튼이 **`연결됨`** (초록색)으로 바뀌면 완료

> 반복: 교차로의 각 방향 정지선 노드마다 위 과정을 수행합니다 (4방향이면 4회)

### 드래그로 연결하기 (대안)

정지선 노드 선택 → Inspector → **Stop Signal** 슬롯에 TrafficSignal 드래그

### 우회전 빨간불 통과

우회전 전용 분기 노드에는 `Stop Signal`을 **연결하지 않습니다** (`null` = 신호 무관 통과).

```
[정지선 노드]
   ├── exits[0] 직진  → [다음 노드]    ← stopSignal 있음 (신호 대기)
   └── exits[1] 우회전 → [우회전 노드]  ← stopSignal 없음 (자유 통과)
```

---

## Step 5 — 교차로 세부 설정 (`교차로` 탭)

**`교통시스템 > 에디터 열기`** → **`교차로`** 탭

생성된 교차로 목록이 표시됩니다. 각 항목 클릭 시 펼쳐지며:

- **페이즈 수** / **황색 지속 시간** 확인
- 각 페이즈의 **차량 신호기 목록** / **보행 신호기 목록** 표시
- **`선택`** 버튼으로 Inspector에서 직접 수정 가능

### 직접 수정이 필요한 경우

교차로 선택 → Inspector:

```
TrafficJunction 컴포넌트
├── Yellow Duration: 4          ← 황색 신호 지속 시간 (초)
└── Phases
    ├── [0] label: "남북 직진"
    │   ├── Vehicle Green       ← 이 페이즈에서 초록이 될 차량신호기 목록
    │   ├── Pedestrian Green    ← 이 페이즈에서 초록이 될 보행신호기 목록 (선택)
    │   ├── Green Duration: 25  ← 초록 지속 시간 (초)
    │   └── Pedestrian Countdown: 10  ← 보행 신호 카운트다운 시작 시점
    └── [1] label: "동서 직진"
        └── ...
```

### 사이클 타이밍

```
Phase 0 Green (25s) → Phase 0 Yellow (4s)
  → Phase 1 Green (25s) → Phase 1 Yellow (4s) → 반복
```

---

## Step 6 — 차량 스폰 설정 (`매니저` 탭)

**`교통시스템 > 에디터 열기`** → **`매니저`** 탭

### 6-1. TrafficManager 생성

씬에 없으면 경고 메시지와 함께 **`TrafficManager 생성`** 버튼이 표시됩니다.

### 6-2. 차량 풀 설정

| 항목 | 설명 |
|---|---|
| **차량 프리팹** | TrafficVehicle 컴포넌트가 있는 프리팹 목록 |
| **차량 수** | 씬에 동시 활성화할 최대 차량 수 |
| **스폰 높이 오프셋** | 차량 반높이 (보통 0.3~0.5m) |

### 6-3. 스폰 노드 지정

| 버튼 | 동작 |
|---|---|
| **씬의 모든 노드로 설정** | 씬 전체 TrafficNode를 스폰 노드로 등록 |
| **선택한 노드만 설정** | Hierarchy에서 선택한 노드만 등록 |

> **스폰 노드 선택 기준**: 교차로에서 충분히 떨어진 직선 구간  
> 차량 수 ÷ 스폰 노드 수 = 노드당 2~4대 정도가 적당

### 6-4. 성능 설정

| 항목 | 권장값 | 설명 |
|---|---|---|
| **프레임당 스폰 수** | 3~5 | 한 프레임에 최대 스폰 수 |
| **차량 레이어** | Vehicle 레이어 | 차량 간 충돌 감지용 |

---

## Step 7 — 차량 프리팹 설정 (Project 탭)

차량 프리팹에 `TrafficVehicle` 컴포넌트를 추가합니다.

```
TrafficVehicle 컴포넌트
├── 이동
│   ├── Cruise Speed: 8          ← 최대 속도 (m/s)
│   ├── Turn Speed: 5            ← 조향 속도 (Slerp 강도)
│   └── Reach Distance: 1.5      ← 노드 도달 판정 거리 (m)
├── 신호 정지
│   └── Signal Check Dist: 6     ← 정지선 노드에서 이 거리부터 신호·큐 검사 시작 (m)
├── 차량 감지
│   ├── Brake Distance: 8        ← 원거리 사전 감속 범위, N프레임마다 SphereCast (m)
│   ├── Emergency Dist: 5        ← 근접 긴급 감속 범위, 매 프레임 SphereCast (m)
│   ├── Min Follow Dist: 2.5     ← 최소 안전거리 — 이 이내이면 무조건 정지 (m)
│   ├── Acceleration: 6          ← 가속도 (m/s²)
│   ├── Deceleration: 20         ← 제동력 (m/s²) — Acceleration보다 크게 설정
│   ├── Detection Radius: 0.4    ← 차량 SphereCast 반경 (m)
│   └── Vehicle Layer            ← 차량 콜라이더 레이어
└── 성능
    └── Speed Calc Interval: 4   ← N 물리프레임마다 원거리 SphereCast 실행 (4 권장)
```

**차량 콜라이더**: Is Trigger = **OFF** (solid)

> `Signal Check Dist`는 Reach Distance(1.5m)보다 충분히 크게 설정하세요 (기본 6m).

**차량 감지 3단계 구조**

| 단계 | 범위 | 실행 주기 | 역할 |
|---|---|---|---|
| 원거리 추종 (`Brake Distance`) | 8m | N프레임마다 | 앞차를 미리 감지해 부드럽게 감속 |
| 긴급 감속 (`Emergency Dist`) | 5m | 매 프레임 | 겹침 직전 강제 감속 — N프레임 지연 보완 |
| 완전 정지 (`Min Follow Dist`) | 2.5m | 매 프레임 | 이 이내이면 즉시 정지 |

**Node Queue — 신호 구역 안전거리·순차 출발**

신호 구역(`Signal Check Dist` 이내)에 진입한 차량은 같은 정지선 노드를 향한 차량 목록에 자동 등록됩니다.

- **빨간불**: 선두 차량만 신호로 정지 / 후속 차량은 앞차와의 **경로 거리**로 순서대로 정지
- **초록불**: 선두 차량 즉시 출발 → 간격이 벌어지면 2번째 차 출발 → 3번째 차 출발 (파도형 순차 출발)
- SphereCast 방향에 의존하지 않으므로 커브·합류 구간에서도 정확하게 동작

---

## Step 8 — 검사 및 최종 확인 (`검사` 탭)

**`교통시스템 > 에디터 열기`** → **`검사`** 탭 → **`검사 실행`** 버튼

오류/경고 메시지가 없으면 `모든 검사를 통과했습니다! ✓` 표시.

**검사 항목**

| 항목 | 경고/오류 조건 |
|---|---|
| TrafficManager | 없으면 Error |
| 차량 프리팹 | 미등록 시 Error |
| 스폰 노드 | 미등록 시 Error |
| 노드 exits | 모두 null이면 Warning |
| 교차로 페이즈 | 없으면 Error, 차량신호기 없으면 Warning |
| TrafficSignal 렌더러 | R/Y/G/PR/PG 중 하나라도 미할당 시 Warning |
| 미등록 신호기 | 어떤 교차로에도 없으면 Warning |

> **개요** 탭에서도 **`검사 실행`** 버튼 클릭 시 자동으로 검사 탭으로 이동합니다.

---

## 씬 구성 체크리스트

### ① 경로 (노드 탭)
- [ ] `📍 노드 생성 모드` 로 차로마다 TrafficNode 배치
- [ ] `🔗 노드 연결 모드` 로 exits[] 연결 완료
- [ ] 교차로 진입 직전 정지선 노드 배치
- [ ] `모두 방향 자동 설정` 클릭

### ② 신호기 (신호기 탭)
- [ ] 교차로 방향마다 `새 TrafficSignal 생성`
- [ ] Inspector에서 차량 R/Y/G + 보행자 PR/PG 렌더러 5개 연결
- [ ] 렌더러 칩 `R` `Y` `G` `PR` `PG`가 모두 컬러로 표시됨 확인

### ③ 교차로 (개요 탭 — 마법사)
- [ ] `4방향 교차로 자동 생성` 또는 `선택 신호기로 교차로 구성` 실행
- [ ] 교차로 탭에서 페이즈 / 신호기 확인

### ④ 정지선 연결 (신호기 탭)
- [ ] 각 방향 정지선 노드를 선택 후 `노드 연결` 클릭
- [ ] 노드 탭에서 해당 노드 도트가 빨간색으로 변함 확인

### ⑤ 스폰 (매니저 탭)
- [ ] `TrafficManager 생성` (없을 시)
- [ ] 차량 프리팹 등록
- [ ] 스폰 노드 지정

### ⑥ 최종 검사 (검사 탭)
- [ ] `검사 실행` → 이상 없음 확인
- [ ] Play 후 차량 이동 및 신호 정지 확인

---

## Play 모드 실시간 제어

### TrafficSignal Inspector (신호기 선택 후)

```
차량: Green  (CanPass: True)      ← 차량 신호 상태 바
보행: RED                          ← 보행자 신호 상태 바

── 차량 신호 강제 설정 ──
[Red]  [Yellow]  [Green]

── 보행자 신호 오버라이드 ──
[고정: 빨강]  [고정: 초록]  [오버라이드 해제]
```

### TrafficJunction Inspector (교차로 선택 후)

```
Phase 0: 남북 직진          ← 현재 페이즈 상태 바
[Phase 0]  [Phase 1]       ← 페이즈 강제 전환
[⏸ Pause]  [▶ Resume]      ← 타이머 제어
[전체 빨강]  [전체 초록]  [오버라이드 해제]   ← 보행자 신호등 라이트 (시각 전용)
```

> 보행자 신호 오버라이드는 신호등 라이트 색상만 변경합니다. 보행자 AI는 반응하지 않습니다.

### 런타임 스크립팅 API

```csharp
// 신호 직접 제어
trafficSignal.SetState(SignalState.Red);
trafficSignal.SetState(SignalState.Green);

// 교차로 제어
junction.ForcePhase(0);
junction.Pause();
junction.Resume();

// 차량 신호 제어
trafficSignal.SetState(SignalState.Red);
trafficSignal.SetState(SignalState.Green);

// 보행자 신호등 라이트 제어 (시각 연출 전용 — 보행자 AI는 반응하지 않음)
trafficSignal.OverridePedestrianSignal(PedestrianState.Red);
trafficSignal.OverridePedestrianSignal(PedestrianState.Green);
trafficSignal.ClearPedestrianOverride();

// 교차로 단위 보행자 신호등 일괄 제어
junction.OverridePedestrianAll(PedestrianState.Red);
junction.OverridePedestrianAll(PedestrianState.Green);
junction.ClearPedestrianOverrideAll();
```

---

## 트러블슈팅

### 차가 빨간불에 멈추지 않아요

1. **신호기 탭**에서 정지선 노드 선택 → 신호기의 **`노드 연결`** 상태 확인
2. **교차로 탭**에서 해당 TrafficSignal이 `vehicleGreen`에 등록됐는지 확인
3. `Signal Check Dist`가 너무 작지 않은지 확인 (기본 6m, Reach Distance 1.5m보다 크게)
4. Play 중 TrafficSignal Inspector 상태 바에서 현재 `Red` 인지 직접 확인

### 교차로 안에서 멈춰요

- 교차로 **내부** 노드에 Stop Signal이 연결되지 않았는지 확인
- 정지선 노드 위치가 교차로 진입 **직전**인지 확인
- `Reach Distance`를 줄여보세요 (교차로 내부에서 노드 도달 판정이 나는 경우)

### 차량끼리 겹쳐요 (일반 주행)

1. 차량 콜라이더 **Is Trigger = OFF** 확인
2. `Vehicle Layer` 마스크가 차량에 적용됐는지 확인
3. `Min Follow Dist` 증가 (기본 2.5m) — 완전 정지 최소 거리
4. `Emergency Dist` 증가 (기본 5m) — 근접 긴급 감속 구간 확대
5. `Deceleration` 증가 (기본 20) — 제동력 강화

### 신호 구역에서 차량끼리 겹쳐요

1. `Signal Check Dist`를 늘려 정지선 노드 접근을 더 일찍 감지하도록 설정
2. `Vehicle Layer`가 올바른 레이어인지 확인 — Node Queue는 이 레이어로 차량을 식별
3. `Min Follow Dist` 증가 — 신호 큐에서도 동일한 최소 거리 적용
4. Play 모드에서 차량 선택 → Scene 뷰에서 **하늘색 선**이 리더 차량을 가리키는지 확인  
   (하늘색 선이 없으면 큐 등록이 안 된 것 → Vehicle Layer 재확인)

### 코너에서 차가 벽에 부딪혀요

- 커브 구간 노드 간격을 줄이세요 (3~5m)
- `Turn Speed`를 높이면 더 빠르게 방향 전환

### 차량이 리스폰 후 즉시 다시 리스폰해요

- **매니저 탭**에서 스폰 노드가 실제 경로와 연결된 노드인지 확인
- `Height Offset`이 차량이 지면에 겹치지 않는 값인지 확인

### 우회전이 빨간불에 멈춰요

- 우회전 분기 노드의 `Stop Signal`이 `null`인지 확인 (신호기 탭 → 해당 노드 선택 시 표시)
