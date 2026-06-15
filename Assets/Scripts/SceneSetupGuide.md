# Traffic System — Scene Setup Guide

## 스크립트 파일 목록

### 런타임 스크립트 (`Scripts/TrafficSystem/`)

| 파일 | 역할 |
|------|------|
| `TrafficLight.cs` | 신호등 단일 유닛 — 차량(R/Y/G) + 보행(R/G) 독립 제어. 보행 초록은 greenDuration/2 후 깜빡임과 함께 자동 만료 |
| `TrafficIntersection.cs` | 교차로 신호 사이클 매니저 — groupA/B 통합 관리, Timeline 연동 |
| `Waypoint.cs` | 차량 경로 노드 — Next Waypoints 연결로 그래프 구성. 2개 연결 시 확률적 우회전 분기 지원 |
| `CarController.cs` | 차량 이동 + 신호등 감지(SphereCast) + 앞차 거리 유지 + 가감속. Waypoint 노드를 순차 방문 |
| `TrafficSpawner.cs` | 차량 자동 스폰 — Spawn Points(시작 웨이포인트)에서 라운드로빈 생성 |
| `PedestrianController.cs` | 보행자 NavMesh 이동 + 횡단보도별 신호 대기 |
| `CrosswalkWaypoint.cs` | 횡단보도 진입점 마커 — 전용 신호등 지정 가능 |
| `PedestrianSpawner.cs` | 보행자 자동 스폰 — NavMesh 위치 검증 후 생성 |

### Editor 확장 (`Scripts/TrafficSystem/Editor/`)

| 파일 | 제공 기능 |
|------|-----------|
| `WaypointEditor.cs` | 연결 수에 따른 상태 표시 · 우회전 확률 바 · Scene View 방향 화살표 · 자동 방향 버튼 · `Tools/Traffic/모든 웨이포인트 자동 방향 설정` 메뉴 |
| `TrafficLightEditor.cs` | 차량/보행 상태 색상 바 · Play 모드 차량 Force(R/Y/G) 버튼 · 보행 Force(빨/녹) 버튼 |
| `TrafficIntersectionEditor.cs` | Phase 색상 표시 · 페이즈 강제 4버튼 · Next Phase · Resume Timer · SceneView A/B 그룹 시각화 · 유효성 검사 |
| `TrafficSpawnerEditor.cs` | Validate 버튼 · Spawn Point별 차량 배분 표 · Scene 스폰 위치 및 방향 미리보기 |
| `PedestrianSpawnerEditor.cs` | Validate 버튼(CrosswalkWaypoint별 신호등 검사 포함) · Route별 색상 경로선 · CrosswalkWaypoint 빨간 마름모 + 신호등 이름 표시 |

### Editor 도구 (`Editor/`)

| 파일 | 제공 기능 |
|------|-----------|
| `AssetReplacer.cs` | 선택한 씬 오브젝트를 프리팹으로 교체 (Transform 유지) · 선택 오브젝트 센터에 빈 오브젝트 생성 |

---

## STEP 1 — 레이어 설정

Project Settings → Tags and Layers 에서 아래 레이어 추가:

| 레이어 이름 | 용도 |
|------------|------|
| `TrafficLight` | 신호등 Collider (차량 SphereCast 감지용) |
| `Vehicle` | 차량 Collider (앞차 거리 Raycast 감지용) |

---

## STEP 2 — 신호등 프리팹 제작

### 프리팹 구조

```
TrafficLightPrefab (루트)
 ├─ VehicleRed      (Renderer — 차량 빨강)
 ├─ VehicleYellow   (Renderer — 차량 노랑)
 ├─ VehicleGreen    (Renderer — 차량 초록)
 ├─ PedestrianRed   (Renderer — 보행 빨강)
 └─ PedestrianGreen (Renderer — 보행 초록)
```

### 머티리얼 설정 (URP/Lit — Emission 맵 방식)

- 각 파트에 **URP/Lit** 머티리얼 할당
- Surface Inputs → **Emission Map** 에 텍스처 지정
- 발광 ON/OFF는 스크립트가 `_EMISSION` 키워드를 자동 토글

### TrafficLight 컴포넌트 연결

| 필드 | 연결 대상 |
|------|-----------|
| Vehicle Red Light | VehicleRed Renderer |
| Vehicle Yellow Light | VehicleYellow Renderer |
| Vehicle Green Light | VehicleGreen Renderer |
| Pedestrian Red Light | PedestrianRed Renderer |
| Pedestrian Green Light | PedestrianGreen Renderer |
| Blink Interval | 0.2 (보행 초록 만료 전 깜빡임 간격, seconds) |

### 보행 신호 자동 만료 동작

```
보행 초록 점등
  └─ (greenDuration/2 - 1)초 대기
  └─ 1초간 blinkInterval 주기로 깜빡임
  └─ 보행 빨강으로 자동 전환
```

Yellow 페이즈에서는 카운트다운 없이 이전 상태를 유지합니다.

### Collider 설정

루트에 **BoxCollider** 추가 (Is Trigger 체크) → Layer: **TrafficLight**

---

## STEP 3 — TrafficIntersection 설정

1. 교차로에 빈 GameObject `Intersection_A` 생성
2. `TrafficIntersection` 컴포넌트 추가
3. **Group A** (N-S 방향) → 신호등 4개 드래그
4. **Group B** (E-W 방향) → 신호등 4개 드래그
5. Green Duration / Yellow Duration 설정 (기본 20s / 4s)

### 신호 페이즈 동작

| 페이즈 | 차량 A | 차량 B | 보행 A | 보행 B | 지속 |
|--------|--------|--------|--------|--------|------|
| AGreen | 초록 | 빨강 | 빨강 | 초록 (greenDuration/2 후 자동 빨강) | greenDuration |
| AYellow | 노랑 | 빨강 | 빨강 | 초록 유지 | yellowDuration |
| BGreen | 빨강 | 초록 | 초록 (greenDuration/2 후 자동 빨강) | 빨강 | greenDuration |
| BYellow | 빨강 | 노랑 | 초록 유지 | 빨강 | yellowDuration |

> 차량이 초록인 그룹의 보행 신호는 항상 빨강입니다.
> Yellow 페이즈 진입 시 보행 신호는 카운트다운 없이 현재 상태를 유지합니다.

### Timeline 연동 API

| 메서드 | 역할 |
|--------|------|
| `ForcePhase(Phase)` | 페이즈 강제 전환 + 타이머 자동 고정 |
| `ForcePhaseAGreen()` | Timeline Signal용 파라미터 없는 편의 메서드 |
| `ForcePhaseAYellow()` | 동일 |
| `ForcePhaseBGreen()` | 동일 |
| `ForcePhaseBYellow()` | 동일 |
| `ResumeTimer()` | 타이머 재개 — 자동 순환 재개 |

**Timeline 배치 예시:**

```
[Signal Track]
  T=0:00  ForcePhaseAGreen()   ← 차량A 초록 / 보행B 빨강 (플레이어 대기)
  T=0:05  ForcePhaseBGreen()   ← 차량A 빨강 / 보행B 초록 (횡단 허용)
  T=0:25  ResumeTimer()        ← 이벤트 종료, 자동 순환 재개

[Animation Track]
  T=0:05 ~ T=0:25  캐릭터 하차 → 대기 → 횡단 애니메이션
```

---

## STEP 4 — 차량 웨이포인트 배치

차량 경로는 **노드 그래프** 방식으로 구성합니다.
각 빈 오브젝트에 `Waypoint` 컴포넌트를 추가하고 Next Waypoints로 연결합니다.

### Waypoint 컴포넌트

| 필드 | 설명 |
|------|------|
| `Next Waypoints` | 연결할 다음 Waypoint 배열 |
| `Right Turn Chance` | 두 번째 연결(우회전) 선택 확률 (%, 연결 2개일 때만 유효) |

### 연결 규칙

| Next Waypoints 수 | 동작 |
|-------------------|------|
| 0개 | 차량 정지 (경로 끝) |
| 1개 | 항상 직진 |
| 2개 | [0]=직진 / [1]=우회전, `Right Turn Chance`% 확률로 분기 |

### 씬 구성 절차

1. 빈 오브젝트 생성 → `Waypoint` 컴포넌트 추가
2. **Next Waypoints** 에 다음 노드 드래그 연결
3. Inspector **↺ 자동 방향 설정** 버튼 클릭 — 첫 번째 연결 방향으로 회전
4. 교차로에서 우회전이 필요한 노드만 연결 2개 + `Right Turn Chance` 설정

```
Tools → Traffic → 모든 웨이포인트 자동 방향 설정
  → 씬 전체 Waypoint를 한 번에 처리 (Undo 지원)
```

### Scene View 표시

```
노란 구체 + 노란선  ── 직진 경로
청록선              ── 우회전 경로
방향 화살표         ── 선택된 Waypoint의 연결 방향
우회전 확률 바      ── Inspector에서 직진/우회전 비율 시각화
```

### 왕복 4차선 구성 예시

```
A방향 우측 (분기 있음)
  WP_A1_00 → WP_A1_01 → WP_A1_02(분기)
                               ├─[0]→ WP_A1_03 → WP_A1_04 → ...  (직진)
                               └─[1]→ WP_RT_00 → WP_RT_01 → ...  (우회전 30%)

A방향 좌측 (직진만)
  WP_A2_00 → WP_A2_01 → WP_A2_02 → ...

B방향 우측
  WP_B1_00 → WP_B1_01 → ...

B방향 좌측
  WP_B2_00 → WP_B2_01 → ...
```

---

## STEP 5 — 차량 프리팹 + CarController 설정

루트에 `Rigidbody` (Is Kinematic: **Off**) + `BoxCollider` (Layer: **Vehicle**) + `CarController` 추가.

| 파라미터 | 기본값 | 설명 |
|----------|--------|------|
| Start Waypoint | — | 씬에 직접 배치 시 시작 노드 지정. TrafficSpawner 사용 시 자동 할당 |
| Speed | 8 | 최고 이동 속도 (m/s) |
| Acceleration | 6 | 가감속도 (m/s²) |
| Detection Distance | 8 | 신호등 SphereCast 거리 (m) |
| Detection Radius | 0.4 | SphereCast 반경 — 커브 구간 신호등 감지 범위 |
| Brake Distance | 5 | 앞차 Raycast 거리 (m) |
| Stop Check Interval | 4 | Raycast 실행 주기 (물리 프레임 수) |

> Scene View 선택 시 SphereCast(빨강)와 Brake Raycast(노랑) Gizmos 표시.

---

## STEP 6 — TrafficSpawner 설정

1. 빈 GameObject `TrafficSpawner` 생성 → `TrafficSpawner` 컴포넌트 추가
2. **Car Prefab** / **Car Count** / **Spawn Points** 배열 설정
3. **[✓ Validate Setup]** 으로 연결 및 분기 설정 확인

| 필드 | 설명 |
|------|------|
| Car Prefab | CarController가 있는 차량 프리팹 |
| Car Count | 총 생성 차량 수 |
| Spawn Points | 시작 Waypoint 배열 — 라운드로빈으로 차량 배분 |
| Height Offset | 스폰 높이 오프셋 (기본 0.5) |
| Spawn Per Frame | 프레임당 최대 스폰 수 (0 = 한 번에 전부) |

> 차량은 Waypoint의 `transform.rotation`을 초기 방향으로 사용합니다.
> **자동 방향 설정**을 먼저 실행해야 스폰 방향이 올바릅니다.

---

## STEP 7 — NavMesh 베이크 (보행자용)

1. 인도 오브젝트 → Navigation Static 체크
2. 횡단보도 메시도 동일하게 체크
3. Window → AI → Navigation → Bake

> 차량 도로는 Navigation Static 체크 해제 필수.

---

## STEP 8 — 보행자 경로 배치

1. 인도 위에 빈 오브젝트 + 자식 Transform 배치
2. 횡단보도 진입 직전 Transform에 `CrosswalkWaypoint` 컴포넌트 추가
3. `CrosswalkWaypoint.light` — 이 횡단보도 전용 신호등 지정

### 신호등 우선순위

```
CrosswalkWaypoint.light  (지정 시 우선 사용)
        ↓ 없으면
PedestrianRoute.crosswalkLight  (경로 공통 fallback)
        ↓ 없으면
신호 무시하고 바로 통과
```

---

## STEP 9 — 보행자 프리팹 제작

루트에 `NavMeshAgent` + `PedestrianController` 추가. Spawner가 런타임에 경로를 자동 할당.

---

## STEP 10 — PedestrianSpawner 설정

1. `PedestrianSpawner` 컴포넌트 추가
2. Routes 배열:
   - `Waypoints`: 경로 Transform 배열
   - `Crosswalk Light`: fallback 신호등
3. **[✓ Validate Setup]** — CrosswalkWaypoint별 신호등 상태 확인

---

## 에셋 리플레이서 도구

메뉴 **Tools → 에셋 리플레이서** 로 창 열기.

| 기능 | 설명 |
|------|------|
| 오브젝트 교체 | 씬에서 선택한 오브젝트를 지정 프리팹으로 교체. Position/Rotation/Scale 유지. Undo 지원. |
| 센터 빈 오브젝트 생성 | 선택 오브젝트들의 중심 위치에 빈 GameObject 생성. |

---

## 검증 체크리스트

- [ ] 모든 Waypoint 연결 완료, 자동 방향 설정 실행
- [ ] Play 모드 → 차량이 Waypoint 노드를 따라 이동, 노드 방향으로 바라보며 생성됨
- [ ] 신호등 Red 전환 시 차량 감속 후 정지 / Green 시 가속 후 재출발
- [ ] 앞 차량이 있을 때 후속 차량 감속/정지
- [ ] 분기 노드에서 일부 차량이 우회전 경로로 전환됨
- [ ] 보행 초록 신호가 greenDuration/2 경과 후 깜빡이다 자동 빨강 전환됨
- [ ] 차량 초록 방향 보행 신호 = 빨강 확인
- [ ] 보행자가 `CrosswalkWaypoint`에서 신호 대기 후 횡단
- [ ] TrafficLight Inspector — 차량/보행 상태 바 2개 표시
- [ ] TrafficIntersection Inspector — 페이즈 4버튼 · Next Phase · Resume Timer 동작
- [ ] TrafficSpawner Validate — Spawn Points 분기 설정 상태 확인
- [ ] NavMesh 범위 밖 웨이포인트 → Console 경고 + 보행자 스킵 확인
- [ ] Timeline ForcePhase → 신호 고정 / ResumeTimer → 자동 순환 재개 확인

---

## 자주 발생하는 문제

| 문제 | 원인 | 해결 |
|------|------|------|
| 차량이 신호등에 반응 안 함 | Layer 미설정 | 신호등 Collider Layer = `TrafficLight`, CarController Layer Mask 동일 설정 |
| 차량이 이상한 방향으로 스폰됨 | Waypoint 방향 미설정 | 자동 방향 설정 버튼 또는 `Tools → Traffic → 모든 웨이포인트 자동 방향 설정` 실행 |
| 차량이 특정 지점에서 정지함 | Next Waypoints 미연결 | 해당 Waypoint의 Next Waypoints 필드 확인 및 연결 |
| 차량이 급정지/급출발함 | Acceleration 값 낮음 | CarController Acceleration 파라미터 증가 |
| 커브에서 신호등 미감지 | Detection Radius 너무 작음 | CarController Detection Radius 값 증가 |
| 신호등 발광 안 됨 | Emission Map 미설정 | 머티리얼 Surface Inputs → Emission Map 텍스처 지정 |
| 보행 신호가 깜빡이지 않음 | greenDuration이 너무 짧음 | TrafficIntersection Green Duration 값 증가 |
| 차량이 우회전하지 않음 | Right Turn Chance = 0 또는 [1] 미연결 | 분기 Waypoint Inspector 재확인 |
| 차량이 항상 우회전만 함 | Right Turn Chance = 100 | 30~50 범위로 조정 |
| 보행 신호가 차량과 동시 점등 | groupA/B 방향 혼선 | TrafficIntersection Group A/B 신호등 배치 재확인 |
| 보행자 스폰 안 됨 | NavMesh 범위 밖 위치 | Console 경고 확인 → 재베이크, 위치 조정 |
| 보행자가 신호 무시 | CrosswalkWaypoint.light 미지정 | 해당 마커에 전용 신호등 지정 또는 fallback 설정 |
| 보행자가 도로 위를 걸음 | 도로 Navigation Static 체크됨 | 도로 오브젝트 체크 해제 후 재베이크 |
| Timeline ForcePhase 후 신호 계속 변함 | ResumeTimer가 먼저 호출됨 | Timeline Signal 순서 확인 |
