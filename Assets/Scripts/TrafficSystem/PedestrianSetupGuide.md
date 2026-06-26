# 보행자 시스템 구성 가이드

---

## 구조 요약

```
PedestrianSpawner (씬 오브젝트)
 ├─ adultPrefabs[]      — 어른 캐릭터 프리팹 목록
 ├─ childPrefabs[]      — 아이 캐릭터 프리팹 목록
 ├─ adultWalkSpeed      — 어른 이동 속도
 ├─ childWalkSpeed      — 아이 이동 속도
 └─ groups[]            — 보행로 그룹 목록
      └─ PedestrianGroup (보행로 하나)
          ├─ waypoints[]       — 중심선 웨이포인트
          ├─ adultsForward     — 우측(정방향) 어른 수
          ├─ adultsReverse     — 좌측(역방향) 어른 수
          ├─ childrenForward   — 우측(정방향) 아이 수
          ├─ childrenReverse   — 좌측(역방향) 아이 수
          └─ lateralOffset     — 중심선 기준 좌우 간격(m)

캐릭터 프리팹
 └─ PedestrianController — 이동·방향전환 담당 (NavMesh 불필요)
```

---

## STEP 1 — 캐릭터 프리팹 준비

1. **Project** 창에서 사용할 캐릭터 FBX(또는 Prefab)를 선택합니다.
2. Prefab에 **PedestrianController** 컴포넌트를 추가합니다.
3. Animator가 있고 `Speed` float 파라미터가 존재하면 자동으로 연동됩니다.
   - 없어도 동작에는 문제 없습니다.
5. **어른용**, **아이용** 프리팹을 각각 별도로 만들어 두세요.
   - 같은 타입에 여러 종류의 프리팹을 준비하면 스폰 시 랜덤 선택됩니다.

> **PedestrianController Inspector 옵션**
> | 항목 | 설명 | 기본값 |
> |---|---|---|
> | Walk Mode | PingPong(왕복) / Loop(순환) / OneShot(1회) | PingPong |
> | Waypoint Reach Dist | 웨이포인트 도달 판정 거리 (m) | 0.35 |

---

## STEP 2 — 웨이포인트 배치

1. **Hierarchy**에서 빈 GameObject를 생성하고 이름을 `WP_보행로이름` 형식으로 지정합니다.
   - 예: `WP_SideWalk_North`, `WP_CrossWalk_A`
2. 해당 오브젝트의 **자식**으로 빈 GameObject를 순서대로 생성합니다.
   - 이름 예: `WP_00`, `WP_01`, `WP_02` …
3. **보행로 중심선** 위에 웨이포인트를 배치합니다.
   - 첫 번째(WP_00) → 마지막 순서가 **정방향(우측 차선)** 이동 방향입니다.
4. 웨이포인트 간격은 **1~3 m** 정도가 자연스럽습니다.

```
[WP_00] ──→ [WP_01] ──→ [WP_02] ──→ [WP_03]
 우측 차선: 0→3 방향으로 이동
 좌측 차선: 3→0 방향으로 이동
```

> **좌우 방향 기준**
> 웨이포인트 라인이 +Z 방향이라면
> - 우측(+X 방향 오프셋): 정방향으로 이동
> - 좌측(-X 방향 오프셋): 역방향으로 이동

---

## STEP 3 — PedestrianSpawner 오브젝트 생성

1. **Hierarchy**에서 빈 GameObject를 생성하고 이름을 `PedestrianSpawner`로 지정합니다.
2. **PedestrianSpawner** 컴포넌트를 추가합니다.
3. Inspector에서 다음 항목을 설정합니다.

| 항목 | 설명 |
|---|---|
| **Adult Prefabs** | 어른 캐릭터 프리팹 배열 (1개 이상) |
| **Child Prefabs** | 아이 캐릭터 프리팹 배열 (1개 이상) |
| **Adult Walk Speed** | 어른 이동 속도 (권장: 1.2 ~ 1.6) |
| **Child Walk Speed** | 아이 이동 속도 (권장: 0.7 ~ 1.0) |
| **Spawn Per Frame** | 프레임당 스폰 수 (0 = 일괄, 부하 분산 시 5 권장) |

---

## STEP 4 — 보행로 그룹 설정

`PedestrianSpawner` Inspector의 **Groups** 배열에 보행로 수만큼 요소를 추가합니다.

**각 그룹(PedestrianGroup) 설정 항목:**

| 항목 | 설명 | 예시 |
|---|---|---|
| **Label** | 그룹 식별 이름 (에디터 정리용) | "북쪽 인도", "횡단보도 A" |
| **Waypoints** | STEP 2에서 만든 웨이포인트 Transform 배열 | WP_00 ~ WP_03 |
| **Adults Forward** | 우측(정방향) 어른 스폰 수 | 2 |
| **Adults Reverse** | 좌측(역방향) 어른 스폰 수 | 2 |
| **Children Forward** | 우측(정방향) 아이 스폰 수 | 1 |
| **Children Reverse** | 좌측(역방향) 아이 스폰 수 | 1 |
| **Lateral Offset** | 중심선에서 좌우로 떨어지는 거리 (m) | 0.5 |

> **Lateral Offset 가이드**
> - `0.0` : 모든 보행자가 웨이포인트 중심선 위를 걸음
> - `0.5` : 우측 차선은 +0.5m, 좌측 차선은 -0.5m 간격
> - 인도 폭이 좁으면 0.3, 넓으면 0.7 ~ 1.0 추천

---

## STEP 5 — 플레이 테스트

1. **Play 모드** 실행
2. Hierarchy에서 `PedestrianSpawner` 하위에 `Ped_Adult_R_00` 같은 이름으로 생성되는지 확인
   - `R` = 우측(정방향), `L` = 좌측(역방향)
3. 보행자가 웨이포인트를 따라 걷다가 **끝에서 방향 전환** 후 되돌아오는지 확인
4. 어른/아이의 속도 차이가 보이는지 확인

---

## 자주 쓰는 설정 예시

### 일반 인도 (양방향 통행)
```
Adults Forward  : 3   Adults Reverse  : 3
Children Forward: 1   Children Reverse: 1
Lateral Offset  : 0.5
Walk Mode       : PingPong
```

### 넓은 광장 (단방향 많음)
```
Adults Forward  : 5   Adults Reverse  : 2
Children Forward: 2   Children Reverse: 1
Lateral Offset  : 0.8
Walk Mode       : PingPong
```

### 횡단보도 (1회 통과)
```
Adults Forward  : 3   Adults Reverse  : 0
Children Forward: 1   Children Reverse: 0
Lateral Offset  : 0.3
Walk Mode       : OneShot  ← PedestrianController Inspector에서 변경
```

---

## 주의사항

- 웨이포인트가 **2개 미만**이면 해당 그룹은 무시됩니다.
- 같은 씬에 `PedestrianSpawner`를 **여러 개** 배치해도 됩니다. (구역별 관리 가능)
- 보행자 수가 많을 때는 `Spawn Per Frame`을 `5` 이하로 설정해 스폰 부하를 분산하세요.
