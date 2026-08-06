# 노면 네비게이션 구성 가이드

> **도로 위에 셰브론(꺾쇠) 유도선을 깔아 주행 경로를 안내합니다.**
>
> 구 시스템(화면 상단 고정 방향 화살표 + Timeline Direction 마커)을 **대체**합니다.
> 방향 안내는 전적으로 이 컴포넌트가 담당하며, `SpeedUIController`는 속도 텍스트와 과속 경고만 맡습니다.

---

## 구성 파일

```
Assets/Scripts/Navigation/
├── RoadNavigationGuide.cs              런타임 — 셰브론 배치/풀링/페이드
├── RouteWaypointPath.cs                런타임 — 웨이포인트(+라운드) → 스플라인 데이터
├── NavigationGuide.md                  이 문서
├── Shaders/
│   └── NavChevron.shader               인스턴싱 언릿 (드로우콜 1~2개)
└── Editor/
    ├── RouteSplineBaker.cs             [타임라인 베이크] 탭 — 카메라 애니메이션 → 경로 스플라인
    ├── RouteSplineBaker.Waypoints.cs   [루트 스플라인 웨이포인트] 탭 — 직접 찍어 만드는 경로
    └── RoadNavigationGuideEditor.cs    미리보기 + 검증 + 씬뷰 기즈모
```

---

## 전체 작업 순서

```
① 셰브론 텍스처 준비    ← PNG 1장 (알파 + 글로우 베이크)
② 머티리얼 생성         ← Bicycle/NavChevron + GPU Instancing
③ 경로 스플라인 준비    ← Tools ▸ Navigation ▸ Route Spline Baker
                          [타임라인 베이크] 카메라 애니메이션에서 자동으로 굽기
                          [루트 스플라인 웨이포인트] 씬을 찍어 직접 구성하기
④ 컴포넌트 배치         ← RoadNavigationGuide
⑤ 미리보기 & 검증       ← Validate Setup 버튼
```

> ③의 두 방식은 결과물이 모두 같은 `SplineContainer`입니다. 편한 쪽을 쓰면 되고, 섞어 써도 됩니다.

---

## ① 셰브론 텍스처 준비

셰브론 1개짜리 PNG를 준비합니다. **정사각형에 가까운 비율**, 진행 방향이 **위쪽(+V)** 을 향하도록 그립니다.

```
Import Settings
  Texture Type   : Default
  Alpha Source   : Input Texture Alpha
  Alpha Is Transparency : ✔
  Wrap Mode      : Clamp        ← 필수. Repeat면 가장자리가 번집니다
  Filter Mode    : Bilinear
  sRGB           : ✔
```

> ⚠️ [GlobalVolumeProfile](../../Scenes/Level1/GlobalVolumeProfile.asset)의 **Bloom이 꺼져 있습니다**(`active: 0`).
> 참고 이미지 같은 발광은 블룸을 켜는 대신 **PNG에 외곽 글로우를 미리 그려 넣는 방식**을 권장합니다.
> 퀄리티 세팅을 건드리지 않고 같은 결과를 얻을 수 있습니다.

---

## ② 머티리얼 생성

```
Project 패널 우클릭 → Create → Material
이름: NavChevron_MAT
Shader: Bicycle / NavChevron
```

| 프로퍼티 | 설명 | 권장값 |
|----------|------|--------|
| `Chevron Texture` | ①에서 만든 PNG | — |
| `Color` | 기본 색 (런타임에 속도 등급 색으로 덮어씀) | 청록 `(0.25, 1, 0.85)` |
| `Intensity` | 색 배율. 블룸을 켠 경우에만 1 초과로 | `1` |
| `Emission` | 글로우 세기. 이미션 색은 `Color`(런타임 속도 등급 색)를 그대로 사용. 1 초과 시 HDR 영역으로 올라가 블룸이 붙음 | `0` (블룸 사용 시 `1~4`) |
| `Distance Fade Power` | 알파 감쇠 곡선. 클수록 가장자리가 빨리 사라짐 | `1` |
| **Enable GPU Instancing** | **반드시 체크** — 드로우콜이 1~2개로 합쳐집니다 | ✔ |

---

## ③ 경로 스플라인 베이크

씬에는 도로 중심선 데이터가 없지만, **카메라 Animation Track이 이미 도로를 정확히 따라갑니다.**
이걸 샘플링해 스플라인으로 굽습니다. 손으로 스플라인을 그릴 필요가 없습니다.

```
메뉴 → Tools ▸ Navigation ▸ Route Spline Baker
```

### 3-1. 소스 지정

```
[씬에서 자동 찾기] 버튼 클릭
  → Playable Director : 씬의 TimelineDirector 자동 할당
  → 샘플 대상          : Animation Track에 바인딩된 카메라 자동 할당
```

자동 검출이 안 되면 직접 드래그합니다.

### 3-2. 출력 지정

```
[NavigationRoute 오브젝트 생성] 버튼 클릭
  → 씬에 SplineContainer를 가진 "NavigationRoute" 오브젝트가 생성됩니다
```

### 3-3. 베이크

```
[▶ 경로 베이크] 클릭
```

| 파라미터 | 설명 | 권장값 |
|----------|------|--------|
| `샘플 간격(초)` | 작을수록 정밀·느림. 60초 코스에서 0.05 = 1200 샘플 | `0.05` |
| `단순화 오차(m)` | 이 오차 안에서 노트 개수를 줄임 | `0.25` |
| `노트 최소 간격(m)` | 너무 촘촘한 노트 병합 | `2` |
| `노면으로 내리기` | 카메라 높이 → 도로 표면 높이로 하강 | ✔ |
| `노면 레이어` | 도로 메시 레이어. **Vehicle / TrafficLight 제외** | `Default` |

베이크가 끝나면 씬뷰에 청록색 경로 라인이 그려지고, 결과 요약(노트 개수·경로 길이)이 표시됩니다.

> 베이크 중에는 타임라인이 씬 오브젝트를 움직이지만, `AnimationMode`로 감싸 두어
> 끝나면 원래 상태로 복원됩니다. 이상해 보이면 Timeline 창을 한 번 열었다 닫으세요.

> **코스를 수정하면 반드시 다시 구워야 합니다.** (카메라 Animation Clip 변경 시)

---

## ③′ 경로를 직접 찍어서 만들기 — [루트 스플라인 웨이포인트] 탭

타임라인이 없는 구간이거나 경로를 손으로 잡고 싶을 때 씁니다.
같은 창의 두 번째 탭이며, **결과물이 똑같은 SplineContainer**라서 ④부터는 구분 없이 그대로 씁니다.

```
메뉴 → Tools ▸ Navigation ▸ Route Spline Baker → [루트 스플라인 웨이포인트] 탭
```

### 3′-1. 대상 준비

탭 화면은 **준비 상태에 따라 3단계로 갈라집니다.** 아래 두 버튼은 서로 다른 단계에 나오며 같이 보이지 않습니다.

```
[1] Spline Container 가 비어 있을 때
    Spline Container : None
    [NavigationRoute 오브젝트 생성]          ← 여기까지만 표시

[2] 컨테이너는 있는데 웨이포인트 데이터가 없을 때
    ⚠ 이 오브젝트에 웨이포인트 데이터가 없습니다…
    [Route Waypoint Path 컴포넌트 추가]      ← 여기까지만 표시. 데이터 그릇을 만드는 1회성 버튼

[3] 컴포넌트까지 붙은 편집 상태
    ── Placement ──  ── Points ──  ── Spline ──   ← 아래 3′-2 ~ 3′-4 전체가 표시
```

`[Route Waypoint Path 컴포넌트 추가]`를 누르면 **배치 모드가 자동으로 켜지므로** 바로 씬을 클릭하면 됩니다.

포인트 데이터는 씬의 `RouteWaypointPath` 컴포넌트에 남으므로 **창을 닫아도 유지**되고, 나중에 다시 열어 수정할 수 있습니다.
`Spline Container` 필드는 [타임라인 베이크] 탭과 **공유**됩니다. 한쪽에서 지정하면 다른 쪽에도 그대로 잡힙니다.

### 3′-2. 포인트 찍기 — ── Placement ── 섹션

```
[▶ 씬 뷰 클릭으로 포인트 찍기]   ← 토글 버튼. 켜면 "■ 배치 모드 켜짐"으로 바뀝니다
  씬 뷰 클릭 = 포인트 추가 (클릭할 때마다 끝에 붙습니다)
  ESC 또는 버튼 재클릭 = 배치 모드 종료
```

배치 모드에서는 씬 뷰의 오브젝트 선택이 잠기고, 커서 위치에 초록 점 + 마지막 포인트에서 이어지는 점선이 표시됩니다.
클릭 지점은 **콜라이더 레이캐스트 → 렌더러 지오메트리 → 마지막 포인트 높이의 수평면** 순으로 찾습니다.

| 파라미터 | 설명 | 권장값 |
|----------|------|--------|
| `기본 라운드(m)` | 새로 찍는 포인트에 들어갈 모서리 반경 | `2` |
| `노면에 붙이기` | 클릭·이동한 지점을 도로 표면으로 하강 | ✔ |
| `노면 레이어` | 도로 메시 레이어. **Vehicle / TrafficLight 제외** | `Default` |
| `노면 오프셋(m)` | 노면에서 띄울 높이 | `0` |
| `순환 경로` | 마지막 ↔ 첫 포인트를 이어 닫힌 경로 (포인트 3개 이상부터) | ✘ |

### 3′-3. 위치·라운드 조정 — ── Points ── 섹션

- **씬 뷰** — 포인트를 클릭해 선택 → 이동 핸들로 위치, 원형 핸들로 라운드 조절
  (배치 모드가 켜져 있으면 클릭이 전부 "추가"로 가므로, 조정할 때는 배치 모드를 끄세요)
- **창 목록** — `#i` 버튼으로 선택, 옆 칸에 **월드 좌표** 직접 입력, `라운드(m)`에 값 입력
- 행 버튼: `▲ ▼` 순서 변경 / `＋` 다음 포인트와의 중간에 삽입 / `✕` 삭제
- 목록 아래: `[＋ 끝에 추가]`(마지막 진행 방향으로 10 m 연장) / `[모두 지우기]`

라운드는 **보행자 웨이포인트의 blendRadius와 같은 방식**입니다.
모서리에서 반경만큼 앞뒤로 물러난 지점을 베지어로 이어, 직선 구간은 직선 그대로 두고 모서리만 둥글게 만듭니다.

> 라운드는 **양옆 구간 길이의 절반까지만** 적용됩니다. 잘리는 경우 목록에 `→ 실제값`이 표시되고,
> 씬 뷰의 하늘색 원이 실제로 먹는 반경입니다. 열린 경로의 첫/끝 포인트는 모서리가 아니므로 라운드가 0입니다.

### 3′-4. 스플라인 반영 — ── Spline ── 섹션

`자동 적용`(기본 ✔)이 켜져 있으면 포인트를 추가·이동·삭제·정렬하거나 라운드를 바꿀 때마다 바로 다시 구워집니다.
꺼두고 작업했다면 `[▶ 스플라인 갱신]`으로 반영합니다. (포인트 2개 이상부터 활성화)

> `[현재 스플라인에서 웨이포인트 가져오기]` — 타임라인 베이크 결과를 웨이포인트로 변환해
> 손으로 다듬을 때 씁니다. 노트 위치가 그대로 포인트가 되고 라운드는 `기본 라운드` 값이 들어가므로,
> 노트가 많으면 포인트도 그만큼 생깁니다. (베이크 시 `노트 최소 간격`을 키워두면 편합니다)

모든 편집은 **Undo(Ctrl+Z)** 로 되돌아갑니다. 웨이포인트와 구워진 스플라인이 함께 복원됩니다.

---

## ④ 컴포넌트 배치

```
Hierarchy → Create Empty
이름: "RoadNavigation"
Add Component → Road Navigation Guide
```

### 필수 연결

```
Route             : ③에서 만든 NavigationRoute 드래그
Viewer            : 비워두면 Camera.main 자동 사용
Chevron Material  : ②에서 만든 NavChevron_MAT 드래그
```

### 파라미터

| 그룹 | 필드 | 설명 | 권장값 |
|------|------|------|--------|
| **Chevron** | `Chevron Width` | 가로 폭(m). 자전거도로 폭에 맞춤 | `2.2` |
| | `Chevron Length` | 진행 방향 길이(m) | `1.4` |
| | `Pool Size` | `(Far - Near) / Spacing` 보다 커야 함 | `24` |
| **Placement** | `Spacing` | 셰브론 간격(m) | `3` |
| | `Near Distance` | 라이더 앞 몇 m부터 표시 | `4` |
| | `Far Distance` | 라이더 앞 몇 m까지 표시 | `40` |
| | `Lateral Offset` | 경로 기준 좌우 이동(m). +가 오른쪽 | `0` |
| **Ground Fit** | `Conform To Ground` | 노면 높이/기울기에 맞춤 (경사·캠버 대응) | ✔ |
| | `Ground Mask` | 노면 레이어. **Vehicle 제외** | `Default` |
| | `Ground Offset` | Z-파이팅 회피 띄움(m) | `0.02` |
| **Flow** | `Flow Speed` | 0이면 노면에 그려진 것처럼 고정 | `0` |
| | `Flow Follows Bike Speed` | 체크 시 Flow Speed가 자전거 속도 배율로 동작 | ✗ |
| **Fade** | `Near Fade` / `Far Fade` | 표시 구간 양 끝 페이드 길이(m) | `3` / `12` |
| **Speed Tier Color** | `Tint By Speed` | 속도 등급별 색 전환. **끄더라도 등급 판정 자체는 계속**되어 `SpeedUIController`의 과속 UI로 전달됨 | ✔ |
| | `Normal/Yellow/Red Color` | 등급별 색 | 청록/노랑/빨강 |
| **Visibility** | `Hide On Non Riding` | `NormalRiding` 상태에서만 표시 | ✔ |
| | `Visible Speed Threshold` | 이 속도 미만이면 숨김(km/h) | `1` |

> 속도 임계값(`YellowThreshold`/`RedThreshold`)은 `Start()`에서 `InputManager`(config.ini) 값으로 덮어씌워집니다.

---

## ⑤ 미리보기 & 검증

Play 없이 인스펙터에서 바로 확인할 수 있습니다.

```
Road Navigation Guide 인스펙터
  [셰브론 미리보기]  ← Viewer(또는 Main Camera) 위치 기준으로 배치
  [미리보기 정리]    ← 미리보기 오브젝트 제거
  [✓ Validate Setup] ← 경로/머티리얼/풀 크기/레이어 검사
```

미리보기 오브젝트는 `HideFlags.DontSave`라 씬에 저장되지 않습니다.

씬뷰에서는 컴포넌트 선택 시 경로 라인과 Near/Far 반경 디스크가 표시됩니다.

---

## 동작 방식

```
매 프레임 (LateUpdate)
  1. 경로 LUT에서 카메라 최근접 지점의 "경로상 거리" 탐색
     → 직전 프레임 ±30 m만 국소 탐색, 경계에 걸리면 전역 재탐색(타임라인 시크 대응)
  2. 거리 + Near ~ 거리 + Far 구간을 Spacing 격자에 스냅해 순회
  3. 각 지점에서 아래로 레이캐스트 → 노면 위치/법선 획득
  4. 풀에서 셰브론 꺼내 배치, 양 끝 페이드 알파를 MPB로 주입
```

경로는 `Awake()`에서 **거리 등간격 룩업 테이블**로 한 번만 변환합니다.
이후 스플라인 평가가 없어 매 프레임 비용은 레이캐스트 20여 회 + 배열 조회뿐입니다.

---

## 문제 해결

| 증상 | 원인 | 해결책 |
|------|------|--------|
| 셰브론이 아예 안 보임 | Material 미할당 | 콘솔에 경고 로그 확인 → Chevron Material 연결 |
| 셰브론이 도로 아래에 묻힘 | Ground Offset 부족 | `0.02` → `0.05`로 증가 |
| 셰브론이 차량 위에 올라감 | Ground Mask에 Vehicle 포함 | Ground Mask에서 Vehicle/TrafficLight 해제 |
| 먼 쪽 셰브론이 잘림 | Pool Size 부족 | Validate Setup이 필요 개수를 알려줌 |
| 셰브론이 도로를 벗어남 | 경로 스플라인이 낡음 | 카메라 애니메이션 수정 후 재베이크 (또는 웨이포인트 탭에서 해당 구간 포인트 수정) |
| 색이 안 바뀜 | 셰이더에 `_BaseColor` 없음 | Color Property를 셰이더 프로퍼티명과 일치시킴 |
| 드로우콜이 셰브론 수만큼 늘어남 | GPU Instancing 미체크 | 머티리얼에서 Enable GPU Instancing 체크 |
| 베이크 시 "유효한 샘플 없음" | 샘플 대상이 Animation Track으로 안 움직임 | 대상 Transform을 카메라로 재지정 |
| 정지 구간에서 셰브론이 사라짐 | `Visible Speed Threshold` | 0으로 두면 속도와 무관하게 표시 |
| 씬을 클릭해도 포인트가 안 생김 | 배치 모드 꺼짐 / 다른 탭 / 창 닫힘 | [루트 스플라인 웨이포인트] 탭에서 `[▶ 씬 뷰 클릭으로 포인트 찍기]`를 켠 상태여야 함 (창을 닫으면 자동 해제) |
| 씬 뷰에 포인트·경로가 안 보임 | 창이 닫혔거나 베이크 탭 | 창을 열고 웨이포인트 탭을 선택. 컴포넌트 선택 시에는 기즈모로만 표시됨 |
| 라운드를 키워도 모서리가 그대로 | 구간 길이의 절반으로 잘림 / 경로 끝점 | 목록의 `→ 실제값` 확인. 포인트 간격을 넓히거나 라운드를 줄임 |
| 포인트를 고쳤는데 셰브론 경로가 그대로 | `자동 적용` 꺼짐 | `[▶ 스플라인 갱신]` 클릭 |

---

## 관련 문서

- [Timeline 구성 가이드](../Timeline/TimelineGuide.md) — 이벤트·퀴즈·체크포인트 배치
- [SpeedUIController](../UI/SpeedUIController.cs) — 속도 텍스트 / 과속 경고 UI.
  속도 등급은 **이 컴포넌트가 단독 판정**하고 `OnTierChanged` 이벤트로 넘겨준다 (씬에서 자동 탐색, 인스펙터 연결 불필요)
