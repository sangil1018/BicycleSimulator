# 교통 시스템 이지 가이드

> **이 문서 하나만 따라하면 씬에 차량 + 보행자 + 신호등이 작동합니다.**

---

## 전체 작업 순서 한눈에 보기

```
① 레이어 등록         ← Unity 설정 (1회만)
② 신호등 프리팹 제작   ← 차량+보행 신호 통합 1개 프리팹
③ 교차로 신호 연결     ← groupA/B 각 4개씩
④ 차량 웨이포인트 배치  ← 노드 방식, 연결로 경로 구성
⑤ 차량 프리팹 제작
⑥ 차량 스포너 설정
⑦ NavMesh 베이크     ← 보행자용 바닥 설정
⑧ 보행자 경로 배치   ← CrosswalkWaypoint에 신호등 지정
⑨ 보행자 프리팹 제작
⑩ 보행자 스포너 설정
⑪ Play 테스트
```

---

## ① 레이어 등록

1. 메뉴 → **Edit → Project Settings → Tags and Layers**
2. 비어있는 슬롯에 아래 두 개 입력

```
TrafficLight
Vehicle
```

✅ Layers 목록에 두 항목이 보이면 완료

---

## ② 신호등 프리팹 제작

> 프리팹 1개에 차량 신호(빨/노/녹)와 보행 신호(빨/녹)가 함께 들어갑니다.

### 구조

```
TrafficLightPrefab (루트)
 ├─ VehicleRed      ← 차량 빨강
 ├─ VehicleYellow   ← 차량 노랑
 ├─ VehicleGreen    ← 차량 초록
 ├─ PedestrianRed   ← 보행 빨강
 └─ PedestrianGreen ← 보행 초록
```

### 머티리얼 (URP/Lit Emission 맵)

```
각 파트 머티리얼 선택
→ Surface Inputs → Emission Map → 텍스처 지정
```

> 발광 ON/OFF는 스크립트가 자동 처리합니다.

### Collider + TrafficLight 컴포넌트

```
루트에 Box Collider 추가 → Is Trigger ✅ → Layer: TrafficLight

루트에 TrafficLight 컴포넌트 추가
  Vehicle Red Light    ← VehicleRed Renderer
  Vehicle Yellow Light ← VehicleYellow Renderer
  Vehicle Green Light  ← VehicleGreen Renderer
  Pedestrian Red Light    ← PedestrianRed Renderer
  Pedestrian Green Light  ← PedestrianGreen Renderer

  보행 신호 깜빡임 간격 (Blink Interval) : 0.2  ← 기본값, 필요 시 조정
```

### 배치

프리팹을 교차로에 **총 8개** 배치 (4방향, 배치 방식에 따라 조정)

✅ 씬에 신호등이 배치되면 완료

---

## ③ 교차로 신호 연결

```
Hierarchy → Create Empty → 이름: "Intersection_01" → 교차로 중앙 배치
Inspector → Add Component → TrafficIntersection
```

```
Group A — N-S 방향 신호등 4개 드래그
Group B — E-W 방향 신호등 4개 드래그

Green Duration  : 20
Yellow Duration : 4
```

**신호 페이즈:**

| 페이즈 | 차량 A | 차량 B | 보행 A | 보행 B |
|--------|--------|--------|--------|--------|
| AGreen | 초록 | 빨강 | 빨강 | 초록 → 10초 후 자동 빨강 |
| AYellow | 노랑 | 빨강 | 빨강 | 초록 유지 |
| BGreen | 빨강 | 초록 | 초록 → 10초 후 자동 빨강 | 빨강 |
| BYellow | 빨강 | 노랑 | 초록 유지 | 빨강 |

> **보행 초록 신호 자동 만료**: Green Duration의 절반(20 → 10초) 동안 유지된 후,
> 마지막 1초 동안 깜빡이다가 빨강으로 자동 전환됩니다.
> 차량이 초록인 방향의 보행 신호는 항상 빨강입니다.

✅ Play 시 신호등이 교대로 점등되면 완료

---

## ④ 차량 웨이포인트 배치

> 경로 배열 방식 대신 **노드 연결 방식**을 사용합니다.
> 빈 오브젝트에 `Waypoint` 컴포넌트를 추가하고, Next Waypoints로 다음 노드를 연결합니다.

### 기본 설정

```
Hierarchy → Create Empty → 이름: "WP_00"
Add Component → Waypoint

Inspector:
  Next Waypoints → 다음 Waypoint 오브젝트 드래그
```

**연결 규칙:**

| 연결 수 | 동작 |
|---------|------|
| 0개 | 차량이 해당 지점에서 정지 |
| 1개 | 항상 직진 |
| 2개 | [0] 직진 / [1] 우회전 — Right Turn Chance(%) 확률로 분기 |

### 자동 방향 설정

웨이포인트를 연결한 뒤 **↺ 자동 방향 설정** 버튼을 클릭하면
첫 번째 연결 방향으로 transform.forward가 자동 회전됩니다.

```
또는 메뉴: Tools → Traffic → 모든 웨이포인트 자동 방향 설정
           → 씬 전체 Waypoint를 한 번에 처리
```

### 우회전 설정

```
교차로 직전 Waypoint 선택
Next Waypoints → 크기: 2
  [0] : 직진 방향 다음 WP 드래그
  [1] : 우회전 방향 다음 WP 드래그
Right Turn Chance : 30  ← 30% 확률로 우회전
```

**Scene View 표시:**

```
노란선  ── 직진 경로
청록선  ── 우회전 경로
Inspector 우회전 확률 바: [██████노랑██ | ██청록██]
                           직진 70%       우회전 30%
```

### 왕복 4차선 구성 예시

```
WP_A1_00 → WP_A1_01 → WP_A1_02(분기) → WP_A1_03(직진)
                                      ↘ WP_RT_00(우회전) → WP_RT_01 → ...
WP_A2_00 → WP_A2_01 → ...   ← A방향 2차선 (직진 전용)
WP_B1_00 → WP_B1_01 → ...   ← B방향 1차선
WP_B2_00 → WP_B2_01 → ...   ← B방향 2차선
```

✅ Scene View에 노란 선/청록 선과 화살표가 보이면 완료

---

## ⑤ 차량 프리팹 제작

```
Hierarchy → Create Empty → 이름: "CarPrefab"
자식으로 차체 메시 추가
```

```
Add Component → Rigidbody
  Is Kinematic : ❌ Off  ← 반드시 Off

Add Component → Box Collider
  Layer → "Vehicle"

Add Component → CarController
  Start Waypoint      : 시작 Waypoint 드래그 (또는 Spawner가 자동 할당)
  Traffic Light Layer : TrafficLight
  Vehicle Layer       : Vehicle
  Speed               : 8
  Acceleration        : 6
  Detection Distance  : 8
  Detection Radius    : 0.4
  Brake Distance      : 5
  Stop Check Interval : 4
```

```
CarPrefab → Project 패널 Assets/Prefabs/ 에 드래그하여 저장
```

✅ Project 패널에 CarPrefab 아이콘이 생기면 완료

---

## ⑥ 차량 스포너 설정

```
Hierarchy → Create Empty → 이름: "TrafficSpawner"
Add Component → TrafficSpawner

Car Prefab      : CarPrefab 드래그
Car Count       : 10
Spawn Per Frame : 5
Spawn Points    : 시작 Waypoint 오브젝트 드래그 (여러 개 가능)
```

> 차량은 스폰 시 Waypoint의 transform.forward 방향으로 초기 회전됩니다.
> 자동 방향 설정을 먼저 실행했다면 스폰 방향도 자동으로 맞춰집니다.

**[✓ Validate Setup] 클릭 — 출력 예시:**

```
✓ Car Prefab OK
✓ Spawn [0] 'WP_A1_00'  → 직진
✓ Spawn [1] 'WP_A1_02'  → 직진 70% / 우회전 30%
✓ Spawn [2] 'WP_B1_00'  → 직진
```

✅ Play 시 차량이 웨이포인트 노드를 따라 이동하면 완료

---

## ⑦ NavMesh 베이크

```
인도 오브젝트 → Static 드롭다운 → Navigation Static ✅
```

> ⚠️ 도로 오브젝트는 체크 해제!

```
Window → AI → Navigation → Bake 탭 → [Bake]
```

✅ 인도 위에 파란 면이 덮이면 완료

---

## ⑧ 보행자 경로 배치

```
Hierarchy → Create Empty → 이름: "PedRoute_01"
자식 빈 오브젝트(PW_00, PW_01 ...) 인도 위에 배치
```

**횡단보도 진입점 설정:**

```
횡단보도 앞 waypoint 선택 (예: PW_03)
Add Component → CrosswalkWaypoint
  Light : 이 횡단보도 전용 신호등 드래그  ← 중요!
```

**신호등 우선순위:**

```
CrosswalkWaypoint.light  ← 지정 시 우선 사용
        ↓ 없으면
PedestrianRoute.crosswalkLight  ← 경로 공통 fallback
        ↓ 없으면
신호 무시하고 바로 통과
```

✅ Scene View에서 빨간 마름모와 신호등 이름이 보이면 완료

---

## ⑨ 보행자 프리팹 제작

```
Hierarchy → Create Empty → 이름: "PedestrianPrefab"
자식으로 캐릭터 메시 추가

Add Component → NavMeshAgent (기본값 유지)
Add Component → PedestrianController

PedestrianPrefab → Project 패널에 드래그하여 저장
```

---

## ⑩ 보행자 스포너 설정

```
Hierarchy → Create Empty → 이름: "PedestrianSpawner"
Add Component → PedestrianSpawner

Pedestrian Prefab : PedestrianPrefab 드래그
Pedestrian Count  : 20
Spawn Per Frame   : 5

Routes [0]
  └ Waypoints      : PedRoute_01 Transform 배열
  └ Crosswalk Light : 경로 공통 fallback 신호등
```

---

## ⑪ Play 테스트

**Play 전 최종 체크:**

```
□ 신호등 Collider Layer = TrafficLight
□ 차량 Collider Layer = Vehicle
□ NavMesh 베이크 완료 (인도에 파란 면 있음)
□ CrosswalkWaypoint마다 전용 신호등 지정 또는 경로 fallback 설정
□ TrafficIntersection groupA/B 각 4개 연결
□ 모든 웨이포인트 자동 방향 설정 실행 완료
□ TrafficSpawner → Validate Setup 오류 없음
```

**Play 후 확인:**

| 확인 항목 | 정상 동작 |
|-----------|-----------|
| 차량 이동 | Waypoint 방향을 바라보며 생성, 부드럽게 가속 |
| 차량 정지 | 빨간 신호 앞에서 감속 후 멈춤 |
| 차량 출발 | 초록으로 바뀌면 서서히 가속 |
| 앞차 감지 | 앞 차량과 일정 거리 유지 |
| 우회전 차량 | 분기 노드에서 일부 차량이 우회전 경로로 전환 |
| 보행자 이동 | 인도 위를 걸어다님 |
| 보행자 대기 | CrosswalkWaypoint에서 빨강 신호 시 정지 |
| 보행자 횡단 | 초록 신호 시 건넘 |
| 보행 신호 깜빡임 | 초록 만료 1초 전 깜빡이다 빨강 전환 |

---

## Play 중 실시간 테스트 도구

### 신호등 상태 확인 및 강제 전환

```
신호등 오브젝트 선택 (Play 중)
Inspector:
  차량 상태 바  [빨강/노랑/초록 표시]
  보행 상태 바  [빨강/초록 표시]

  차량 강제: [■ Red] [■ Yellow] [■ Green]
  보행 강제: [■ 빨강] [■ 초록]
```

### 교차로 페이즈 제어

```
Intersection_01 선택 (Play 중)
  [A Green] [A Yellow] [B Green] [B Yellow]  ← 즉시 전환 + 타이머 고정
  [▶ Next Phase]   ← 다음 페이즈로 한 단계
  [▶ Resume Timer] ← 타이머 재개, 자동 순환
```

---

## Timeline 이벤트 연동 (자전거 안전 교육 예시)

```
[Signal Track] → TrafficIntersection 오브젝트
  T=0:00  ForcePhaseAGreen()   ← 차량A 초록 / 보행B 빨강 (플레이어 대기)
  T=0:05  ForcePhaseBGreen()   ← 차량A 빨강 / 보행B 초록 (횡단 허용)
  T=0:25  ResumeTimer()        ← 이벤트 종료, 자동 순환 재개

[Animation Track]
  T=0:05 ~ T=0:25  하차 → 신호 대기 → 횡단 애니메이션
```

---

## 에셋 리플레이서 도구

```
Tools → 에셋 리플레이서
```

| 기능 | 사용법 |
|------|--------|
| 오브젝트 교체 | 씬에서 교체할 오브젝트 선택 → 프리팹 드래그 → [적용] |
| 센터 빈 오브젝트 생성 | 오브젝트 선택 → [선택 오브젝트 센터에 빈 오브젝트 생성] |

---

## 문제 해결

| 증상 | 원인 | 해결책 |
|------|------|--------|
| 차량이 신호 무시 | TrafficLight Layer 미설정 | STEP ①, ⑤ 레이어 재확인 |
| 차량이 이상한 방향으로 스폰 | Waypoint 방향 미설정 | 자동 방향 설정 버튼 또는 메뉴 실행 |
| 차량이 특정 지점에서 멈춤 | Next Waypoints 미연결 | 해당 Waypoint Inspector 확인, 다음 노드 연결 |
| 차량이 급정지/급출발 | Acceleration 값 낮음 | CarController Acceleration 값 증가 |
| 커브에서 신호등 미감지 | Detection Radius 너무 작음 | CarController Detection Radius 값 증가 |
| 신호등 발광 안 됨 | Emission Map 미설정 | 머티리얼 Emission Map 텍스처 지정 |
| 보행 신호가 깜빡이지 않음 | greenDuration이 1초 이하 | TrafficIntersection Green Duration 값 확인 |
| 차량이 우회전 안 함 | Right Turn Chance = 0 또는 [1] 미연결 | Waypoint Inspector 재확인 |
| 차량이 항상 우회전만 함 | Right Turn Chance = 100 | 30~50 범위로 조정 |
| 보행자 스폰 안 됨 | NavMesh 밖 웨이포인트 | Console 경고 확인 → 재베이크, 위치 조정 |
| 보행자가 신호 안 기다림 | CrosswalkWaypoint 또는 light 미설정 | 마커 추가 또는 light 필드에 신호등 드래그 |
| 여러 횡단보도 신호 혼선 | 모두 같은 신호등 참조 | CrosswalkWaypoint마다 전용 light 지정 |
| 보행자가 도로 위를 걸음 | 도로 Navigation Static 체크됨 | 도로 체크 해제 후 재베이크 |
| Timeline 후 신호 계속 고정 | ResumeTimer() 미호출 | Signal Track에 ResumeTimer() 추가 |
| Validate에서 빨간 경고 | null 필드 또는 미지정 | 경고 항목 Inspector에서 재연결 |
