# 차량 교통 시스템 구성 가이드

> 보행자 시스템은 `PedestrianSetupGuide.md`를 참조하세요.

---

## 아키텍처 한눈에 보기

```
TrafficNode ──── exits[] ────► TrafficNode ──── exits[] ────► ...
     │                              │
     │ stopSignal (직접 참조)        │ stopSignal
     ▼                              ▼
TrafficSignal             TrafficSignal
     ▲                              ▲
     └──────── TrafficJunction ─────┘
               (페이즈별 신호 제어)

TrafficManager ──► TrafficVehicle (차량 AI)
                        │
                        │ currentNode.StopSignal.CanPass 직접 체크
                        ▼
                   TrafficNode (정지선)
```

| 컴포넌트 | 역할 |
|---|---|
| **TrafficNode** | 경로 노드. exits[] 배열로 다음 노드 연결, 가중치 선택 |
| **TrafficSignal** | 차량 전용 신호 (Red/Yellow/Green). TrafficJunction이 구동 |
| **TrafficJunction** | 교차로 신호 사이클. JunctionPhase 배열로 구성 |
| **TrafficVehicle** | 차량 AI. 노드 추종, 직접 신호 확인, 차간 거리 유지 |
| **TrafficManager** | 차량 스폰 및 리스폰 관리 (씬에 1개) |
| **TrafficLight** | 보행자 전용 신호 (PedestrianController/CrosswalkWaypoint 전용) |

---

## Step 1 — 경로 노드 배치 (TrafficNode)

### 배치 원칙

차로 중앙선을 따라 노드를 배치합니다.

```
[노드] ──────── [노드] ──────── [노드] ──────── [노드]
   3~10m 간격         교차로 직전 노드에          교차로 직후 노드
                      stopSignal 연결
```

**노드 간격 권장값**
- 직선 구간: 8~15m
- 곡선 구간: 3~6m (차량이 Slerp로 조향하므로 촘촘할수록 자연스러움)
- 교차로 진입 직전: 1개의 노드에 `stopSignal` 연결

### Inspector 설정

```
TrafficNode 컴포넌트
├── Exits            ← 다음 노드 목록 (개수 제한 없음)
│   ├── [0] node: 직진 노드   weight: 70
│   ├── [1] node: 우회전 노드  weight: 20
│   └── [2] node: 좌회전 노드  weight: 10
└── Stop Signal      ← null이면 자유 통과 / TrafficSignal 연결 시 신호 대기
```

- `weight`는 상대 비율입니다. 합계가 100이 아니어도 됩니다.
- 출구가 1개뿐이면 weight 무관하게 100% 그 방향으로 이동합니다.
- **Exits 비어있음** = 경로 끝 → 차량이 TrafficManager에게 리스폰 요청

### 씬 뷰 시각화

- **노란 구** = stopSignal 없는 노드 (자유 통과)
- **빨간 구** = stopSignal 있는 노드 (정지선)
- **노란 선** = exits 연결
- 노드 선택 시 화살표 + 확률(%) 표시

### 도구

| 버튼 | 동작 |
|---|---|
| `↺ 자동 방향 설정` | exits[0] 방향으로 Transform 회전 |
| `Tools > Traffic > 모든 TrafficNode 자동 방향 설정` | 씬 전체 일괄 처리 |

---

## Step 2 — 신호등 배치 (TrafficSignal)

교차로 신호등 프리팹에 `TrafficSignal` 컴포넌트를 추가합니다.

```
신호등 GameObject
├── TrafficSignal 컴포넌트
│   ├── Red Renderer    ← 빨간 불 Renderer
│   ├── Yellow Renderer ← 노란 불 Renderer
│   └── Green Renderer  ← 초록 불 Renderer
└── (Collider 불필요 — SphereCast 사용 안 함)
```

> **콜라이더 레이어 설정 불필요.** 이전 시스템과 달리 SphereCast로 감지하지 않습니다.  
> TrafficNode.stopSignal 에 직접 연결하는 방식입니다.

### 신호 렌더러 연결

각 렌더러의 Material에 `_EMISSION` 키워드가 있어야 켜집니다.  
(Standard Shader: `Emission` 항목 활성화 → 색상 지정)

---

## Step 3 — 교차로 구성 (TrafficJunction)

교차로 GameObject에 `TrafficJunction` 컴포넌트를 추가합니다.

### 2방향 교차로 예시 (가장 흔한 경우)

```
TrafficJunction 컴포넌트
├── Phases
│   ├── Phase [0]  label: "NS_Green"
│   │   ├── Vehicle Green: [NS_Signal_1, NS_Signal_2]   ← N-S 방향 신호등
│   │   ├── Pedestrian Green: [EW_Light_1, EW_Light_2] ← E-W 보행자 (선택)
│   │   ├── Green Duration: 20
│   │   └── Pedestrian Countdown: 10
│   └── Phase [1]  label: "EW_Green"
│       ├── Vehicle Green: [EW_Signal_1, EW_Signal_2]   ← E-W 방향 신호등
│       ├── Pedestrian Green: [NS_Light_1, NS_Light_2] ← N-S 보행자 (선택)
│       ├── Green Duration: 20
│       └── Pedestrian Countdown: 10
└── Yellow Duration: 4
```

### 사이클 타이밍

```
Phase 0 Green (20s)  →  Phase 0 Yellow (4s)
       →  Phase 1 Green (20s)  →  Phase 1 Yellow (4s)  →  반복
```

- 황색 전환 시 현재 페이즈 신호만 Yellow로 전환 (다른 신호는 Red 유지)
- 보행자 신호는 황색 구간에 건드리지 않음 (카운트다운 자연 진행)

### 3방향/복잡 교차로

Phases 배열에 원하는 만큼 페이즈를 추가하면 됩니다.

---

## Step 4 — 정지선 연결

교차로 진입 직전 TrafficNode의 `Stop Signal` 필드에 해당 방향의 `TrafficSignal`을 연결합니다.

```
예시: N-S 방향 도로 진입 직전 노드
  TrafficNode (정지선 노드)
    └── Stop Signal → NS_Signal_1  (TrafficJunction Phase 0에서 제어)
```

### 우회전 빨간불 통과 (Right-Turn-on-Red)

우회전 전용 exit 노드에는 `Stop Signal`을 연결하지 마세요.  
`stopSignal = null`이면 신호 무관하게 자유 통과됩니다.

```
[정지선 노드] ── exits[0] 직진 → [다음 노드]     ← stopSignal 있음 (신호 대기)
              ── exits[1] 우회전 → [우회전 노드]  ← stopSignal 없음 (자유 통과)
```

---

## Step 5 — 차량 스폰 (TrafficManager)

씬에 빈 GameObject를 만들고 `TrafficManager` 컴포넌트를 추가합니다.  
**(씬에 1개만 존재해야 합니다.)**

```
TrafficManager 컴포넌트
├── Vehicle Prefabs   ← 차량 프리팹 목록 (TrafficVehicle 포함 확인)
├── Vehicle Count     ← 총 활성 차량 수
├── Spawn Nodes       ← 스폰/리스폰 기준 TrafficNode 목록
├── Height Offset     ← 스폰 높이 (차량 반높이, 보통 0.3~0.5)
├── Spawn Per Frame   ← 프레임당 최대 스폰 수 (3~5 권장)
└── Vehicle Layer     ← 차량 콜라이더 레이어 (리스폰 충돌 체크용)
```

### 스폰 노드 선택 기준
- 교차로에서 충분히 떨어진 직선 구간 노드 사용
- 차량 수 ÷ 스폰 노드 수 = 노드당 차량 수가 2~4대 정도가 적당
- `✔ Validate` 버튼으로 설정 이상 유무 사전 확인

---

## Step 6 — 차량 프리팹 설정 (TrafficVehicle)

차량 프리팹에 `TrafficVehicle` 컴포넌트를 추가합니다.

```
TrafficVehicle 컴포넌트
├── Movement
│   ├── Cruise Speed: 8          ← 최대 속도 (m/s)
│   ├── Turn Speed: 5            ← 조향 속도 (Slerp 강도)
│   └── Reach Distance: 1.5      ← 노드 도달 판정 거리 (m)
├── 신호 정지
│   └── Signal Check Dist: 6     ← 정지선 노드에서 이 거리부터 신호 확인 (m)
├── 차량 감지
│   ├── Brake Distance: 8        ← 앞차 감지 시 감속 시작 거리 (m)
│   ├── Min Follow Dist: 2.5     ← 완전 정차 최소 차간 거리 (m)
│   ├── Acceleration: 6          ← 가/감속도 (m/s²)
│   ├── Detection Radius: 0.4    ← 차량 SphereCast 반경 (m)
│   └── Vehicle Layer            ← 차량 콜라이더 레이어
└── Performance
    └── Speed Calc Interval: 4   ← N 물리프레임마다 속도 계산 (4 권장)
```

**차량 콜라이더**: Is Trigger = **OFF** (solid)

> `Signal Check Dist`는 정지선 노드에 차량이 도달하기 전 감속을 시작하는 거리입니다.  
> Reach Distance(1.5m)보다 충분히 크게 설정하세요 (기본 6m 권장).

---

## 씬 구성 체크리스트

### ① 경로
- [ ] 차로마다 `TrafficNode` 배치 및 `exits[]` 연결 완료
- [ ] 교차로 진입 직전 노드에 `Stop Signal` 연결
- [ ] 경로 끝 노드(리스폰 지점)는 `exits` 비워두거나 `TrafficManager.spawnNodes`에 등록
- [ ] 모든 노드 **Auto Orient** 적용

### ② 신호등
- [ ] 신호등 프리팹에 `TrafficSignal` 추가, 렌더러 슬롯 연결
- [ ] 렌더러 Material에 `_EMISSION` 활성화 확인
- [ ] *(콜라이더/레이어 설정 불필요)*

### ③ 교차로
- [ ] `TrafficJunction` 생성
- [ ] `Phases` 배열 구성 (방향별 vehicleGreen 연결)
- [ ] 보행자 연동 시 `pedestrianGreen` 연결

### ④ 스폰
- [ ] `TrafficManager` 생성 (씬에 1개)
- [ ] `vehiclePrefabs` 등록 (`TrafficVehicle` 포함 확인)
- [ ] `spawnNodes` 지정
- [ ] **✔ Validate** 클릭 → 이상 없음 확인

### ⑤ 차량 프리팹
- [ ] `TrafficVehicle` 컴포넌트 있음
- [ ] `vehicleLayer` 마스크 설정
- [ ] 차량 콜라이더 **Is Trigger = OFF**

---

## Content Control API (런타임 스크립팅)

### 신호 강제 전환

```csharp
// 개별 신호 직접 제어
trafficSignal.SetState(SignalState.Red);
trafficSignal.SetState(SignalState.Green);

// 교차로 페이즈 강제 전환 (타이머 자동 고정)
junction.ForcePhase(0);   // Phase 0으로 강제
junction.ForcePhase(1);   // Phase 1으로 강제
junction.Pause();         // 타이머 정지
junction.Resume();        // 타이머 재개
```

### 보행자 신호 오버라이드

```csharp
// 교차로 사이클과 무관하게 고정 (ClearOverride 전까지 유지)
junction.OverridePedestrianAll(PedestrianState.Red);
junction.OverridePedestrianAll(PedestrianState.Green);
junction.ClearPedestrianOverrideAll();
```

### Inspector Play mode 버튼
- **TrafficSignalEditor**: Red / Yellow / Green 강제 버튼
- **TrafficJunctionEditor**: Phase 0, 1, 2… 강제 버튼 + Pause/Resume

---

## 트러블슈팅

### 차가 빨간불에 멈추지 않아요

1. 정지선 TrafficNode의 `Stop Signal` 필드가 연결되었는지 확인
2. 해당 `TrafficSignal`이 `TrafficJunction.Phases`의 `vehicleGreen`에 포함되어 있는지 확인
3. `Signal Check Dist`가 너무 작지 않은지 확인 (Reach Distance보다 크게, 기본 6m)
4. Play 중 `TrafficSignalEditor`에서 현재 상태가 Red인지 직접 확인

### 교차로 안에서 멈춰요

- 교차로 내부 노드에 `Stop Signal`이 연결되어 있지 않은지 확인
- 정지선 노드 위치가 교차로 진입 **직전**인지 확인 (교차로 중앙 X)
- `Reach Distance`를 줄여보세요 (너무 크면 교차로 내부에서 노드 도달 판정)

### 차량끼리 겹쳐요

1. 차량 콜라이더 **Is Trigger = OFF** 확인
2. `Vehicle Layer` 마스크가 차량에 올바르게 적용됐는지 확인
3. `Brake Distance` 증가 또는 `Min Follow Dist` 증가
4. `Speed Calc Interval`을 줄여 감지 빈도 높이기 (1~2)

### 코너에서 차가 벽에 부딪혀요

- 커브 구간 노드 간격을 줄이세요 (3~5m)
- 노드가 촘촘할수록 `Slerp` 조향이 자연스러운 곡선을 만듭니다
- `Turn Speed`를 높이면 더 빠르게 방향을 전환합니다

### 차량이 리스폰 후 즉시 다시 리스폰해요

- `Spawn Nodes`가 실제 경로와 연결되어 있는지 확인
- 스폰 노드 위치가 다른 차량 근처가 아닌지 확인
- `Height Offset`이 지면에 차량이 겹치지 않는 값인지 확인

### 우회전이 빨간불에 멈춰요

- 우회전 경로의 분기 exit 노드에 `Stop Signal`이 연결되어 있으면 제거하세요
- `Stop Signal = null`이면 신호 무관하게 통과합니다
