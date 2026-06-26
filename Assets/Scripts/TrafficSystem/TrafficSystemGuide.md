# 차량 교통 시스템 구성 가이드

> 보행자 시스템은 별도 문서를 참조하세요.

---

## 1. 아키텍처

```
Waypoint ──────────────────────────────────┐
  (경로 그래프)                              │
                                            ▼
TrafficSpawner ──► CarController (차량 AI)
                        │
                        │ SphereCast
                        ▼
                   TrafficLight ◄── TrafficIntersection
                   (신호등 상태)     (4-페이즈 사이클)
```

| 컴포넌트 | 역할 |
|---|---|
| **Waypoint** | 경로 노드. 직진/우회전/좌회전 연결 및 가중치 |
| **TrafficSpawner** | 게임 시작 시 차량 인스턴스화, 경로 분산 배치 |
| **CarController** | 웨이포인트 추종, 신호등/차량 감지, 감속/정차 |
| **TrafficLight** | 신호등 상태 관리 (Red/Yellow/Green + 보행 신호) |
| **TrafficIntersection** | 교차로 4-페이즈 사이클 자동 진행 |

---

## 2. Waypoint 설정

### 경로 연결 규칙
```
nextWaypoints[0] = 직진 (Straight)
nextWaypoints[1] = 우회전 (Right)
nextWaypoints[2] = 좌회전 (Left)
```
- 연결이 1개뿐이면 가중치 무시, 100% 그 방향
- 연결이 2개 이상이면 **가중치(%)** 로 확률 결정 (합계 기준 정규화)
- 연결이 없는 슬롯은 자동 무시

### 교차로 노드 (Junction Node)
`nextWaypoints` 중 2개 이상이 연결된 웨이포인트 = 교차로 분기점.  
TrafficSpawner가 이 노드를 인식해 **교차로 내부에는 스폰하지 않음**.

### Inspector 도구
- **Auto Orient** 버튼: 첫 번째 연결 방향으로 트랜스폼 자동 회전
- **Tools > Traffic > 모든 웨이포인트 자동 방향 설정**: 씬 전체 일괄 처리
- **씬 뷰**: 비선택 상태에서도 연결선 항상 표시 (노랑=직진, 청록=우회전, 연두=좌회전)

---

## 3. TrafficSpawner 설정

| 필드 | 설명 | 권장값 |
|---|---|---|
| Car Prefabs | 랜덤 선택할 차량 프리팹 목록 | 1개 이상 |
| Car Count | 총 스폰 차량 수 | 경로 수 × 2~4 |
| Spawn Points | 각 경로의 **시작 웨이포인트** | 경로당 1개 |
| Height Offset | 스폰 높이 오프셋 | 차량 반높이 |
| Spawn Per Frame | 프레임당 최대 스폰 수 | 3~5 |
| Max Path Steps | 경로 탐색 최대 노드 수 | 60 |

### 배치 방식
- 각 스폰 포인트에서 `nextWaypoints[0]`(직진)을 따라 교차로 직전까지 경로 수집
- 경로 **전반부 50%** 구간에 층화 샘플링으로 균등 분산
- 교차로 내부(분기 노드 이후)에는 배치 안 함

### 유효성 검사
Inspector 하단 **✓ Validate Setup** 버튼으로 프리팹·스폰 포인트 문제 사전 확인.

---

## 4. TrafficLight 설정

### Inspector 필드
| 필드 | 설명 |
|---|---|
| Vehicle Red/Yellow/Green Light | 차량 신호 렌더러 (머티리얼 _EMISSION 제어) |
| Pedestrian Red/Green Light | 보행 신호 렌더러 |
| Green/Yellow/Red Duration | 독립 모드용 각 상태 지속 시간 (초) |

### 콜라이더 필수 조건 ⚠️
CarController의 SphereCast로 감지되려면:

1. **Is Trigger = ON** — 차량이 물리적으로 통과할 수 있어야 함
2. **레이어 = trafficLightLayer** — CarController Inspector의 `trafficLightLayer`와 동일 레이어
3. **`TrafficLight` 컴포넌트가 같은 오브젝트 또는 부모에 존재** — `GetComponentInParent<TrafficLight>()` 로 조회

> 신호등 기둥(폴)에 solid 콜라이더가 있어도 무방. 감지용 트리거 콜라이더만 조건을 충족하면 됨.

### 동작 모드
- **Standalone**: `TrafficIntersection` 없을 때 단독 사이클 (Red→Green→Yellow→Red)
- **Managed**: `TrafficIntersection.ForceVehicleState()` 호출 시 외부 제어로 전환, 자체 사이클 정지

---

## 5. TrafficIntersection 설정

### 그룹 할당
```
Group A = N-S 방향 신호등 4개
Group B = E-W 방향 신호등 4개
```

### 4-페이즈 사이클

| 페이즈 | Group A 차량 | Group B 차량 | Group A 보행 | Group B 보행 |
|---|---|---|---|---|
| **AGreen** | 초록 | 빨강 | 빨강 | 초록 (카운트다운) |
| **AYellow** | 노랑 | 빨강 | 빨강 | 초록 (유지) |
| **BGreen** | 빨강 | 초록 | 초록 (카운트다운) | 빨강 |
| **BYellow** | 빨강 | 노랑 | 초록 (유지) | 빨강 |

| 필드 | 설명 |
|---|---|
| Green Duration | 초록 지속 시간 (초). 보행 초록 카운트다운 = 이 값의 절반 |
| Yellow Duration | 노랑 지속 시간 (초) |

### Play mode 에디터 컨트롤
- **A/B Green/Yellow 버튼**: 페이즈 즉시 강제 전환 + 타이머 고정
- **▶ Next Phase**: 다음 페이즈로 수동 전환
- **▶ Resume Timer**: 강제 고정 해제, 자동 사이클 재개

---

## 6. CarController 설정

### 신호등 감지 파라미터
| 필드 | 설명 | 기본값 |
|---|---|---|
| Light Detection Distance | SphereCast 거리. `brakeDistance`보다 크게 | 12m |
| Light Detection Radius | SphereCast 반경. 차선 폭 절반 이상 | 2m |
| Traffic Light Layer | 신호등 콜라이더 레이어 | — |
| Intersection Cooldown | 교차로 통과 후 신호등 재감지 억제 시간 | 4s |

### 차량 감지 파라미터
| 필드 | 설명 | 기본값 |
|---|---|---|
| Detection Radius | 차량 SphereCast 반경 | 0.4m |
| Vehicle Layer | 차량 콜라이더 레이어 | — |
| Brake Distance | 앞차 감지 시 감속 시작 거리 | 8m |
| Min Follow Dist | 완전 정차 유지 최소 차간 거리 | 2.5m |
| Acceleration | 가/감속도 (m/s²) | 6 |
| Stop Check Interval | 속도 계산 주기 (물리 프레임) | 4 |

### 우회전 신호 동작
`nextWaypoints[1]`(우회전) 방향을 선택한 차량은 **빨간불에도 통과**.  
직진(`[0]`)·좌회전(`[2]`)은 초록불일 때만 통과.

### 교차로 쿨다운 동작
신호등 감지 후 통과 허가 시 `intersectionCooldown` 타이머 시작.  
타이머 동안 신호등 감지를 건너뜀 → 교차로 내부에서 멈추지 않음.

---

## 7. Content Control API

### 차량 신호 강제 (런타임)
```csharp
// 특정 신호등 직접 제어
trafficLight.ForceVehicleState(TrafficLightState.Red);
trafficLight.ForceVehicleState(TrafficLightState.Green);

// 교차로 페이즈 강제 전환
intersection.ForcePhaseAGreen();
intersection.ForcePhaseBGreen();
intersection.AdvancePhase();    // 다음 페이즈
intersection.ResumeTimer();     // 자동 사이클 재개
```

### 보행 신호 오버라이드
```csharp
// 교차로 사이클과 무관하게 보행 신호 고정 (ClearOverride 전까지 유지)
intersection.OverridePedestrianAll(PedestrianState.Red);
intersection.OverridePedestrianAll(PedestrianState.Green);
intersection.OverridePedestrianGroupA(PedestrianState.Red);
intersection.OverridePedestrianGroupB(PedestrianState.Green);

// 오버라이드 해제 → 다음 페이즈 전환 시 자동 복원
intersection.ClearPedestrianOverrideAll();
```

---

## 8. 씬 구성 체크리스트

### ① 경로 구성
- [ ] 차로마다 웨이포인트 오브젝트 배치
- [ ] `Waypoint` 컴포넌트 → `nextWaypoints` 연결
- [ ] 교차로 분기점: 해당 웨이포인트에 2개 이상 연결
- [ ] 직진 가중치 기본 설정 후 필요 시 우/좌 가중치 조정
- [ ] 모든 웨이포인트 **Auto Orient** 적용

### ② 신호등 배치
- [ ] 신호등 프리팹 설치 → 렌더러 슬롯 연결
- [ ] 감지용 Box Collider 추가 → **Is Trigger = ON**
- [ ] 감지용 콜라이더 레이어 → `trafficLightLayer` 레이어 지정
- [ ] `TrafficLight` 컴포넌트가 같은/부모 오브젝트에 있는지 확인

### ③ 교차로 설정
- [ ] `TrafficIntersection` 컴포넌트 생성
- [ ] Group A → N-S 방향 신호등 4개 할당
- [ ] Group B → E-W 방향 신호등 4개 할당
- [ ] `greenDuration`, `yellowDuration` 조정

### ④ 차량 스폰
- [ ] `TrafficSpawner` 컴포넌트 생성
- [ ] Car Prefabs 등록 (CarController 포함 확인)
- [ ] Spawn Points → 각 차로 시작 웨이포인트 연결
- [ ] Car Count, Height Offset 설정
- [ ] **✓ Validate Setup** 클릭 → 이상 없음 확인

### ⑤ CarController 프리팹 확인
- [ ] `trafficLightLayer` 마스크 → 신호등 콜라이더와 동일 레이어
- [ ] `vehicleLayer` 마스크 → 차량 콜라이더 레이어
- [ ] 차량 콜라이더 **Is Trigger = OFF** (solid)
- [ ] `intersectionCooldown` >= 교차로 통과 시간 (보통 3~6초)

---

## 9. 트러블슈팅

### 차가 빨간불에 멈추지 않아요
1. CarController의 `trafficLightLayer` 필드 설정 여부 확인
2. 신호등 콜라이더가 해당 레이어에 있는지 확인
3. 신호등 콜라이더 **Is Trigger = ON** 확인
4. `lightDetectionRadius`를 키워서 차선 옆 콜라이더도 포착되는지 확인
5. `lightDetectionDistance`가 `brakeDistance`보다 큰지 확인

### 교차로 안에서 멈춰요
- `intersectionCooldown`을 늘려주세요
- 필요 시간 계산: 교차로 폭(m) / 차량 speed(m/s) + 여유 1~2초
- 예: 폭 20m, speed 8 → 2.5초 + 여유 = 5초

### 차량끼리 겹쳐요
1. 차량 콜라이더 **Is Trigger = OFF** 확인
2. `vehicleLayer` 마스크가 차량에 올바르게 적용됐는지 확인
3. `brakeDistance` 증가 또는 `minFollowDist` 증가
4. `stopCheckInterval`을 줄여 감지 빈도 높이기 (1~2)

### 우회전이 빨간불에 멈춰요
- `nextWaypoints[1]`에 우회전 웨이포인트가 연결되어 있는지 확인
- 인덱스가 맞는지 확인: [0]=직진, [1]=우회전, [2]=좌회전

### 차량이 스폰 직후 바로 텔레포트해요
- Spawn Point 이후 경로가 너무 짧음
- `maxPathSteps` 증가 또는 경로에 웨이포인트 추가
