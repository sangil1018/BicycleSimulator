# 노면 네비게이션 구성 가이드

> **도로 위에 셰브론(꺾쇠) 유도선을 깔아 주행 경로를 안내합니다.**
>
> 기존 `SpeedUIController`의 화면 고정 방향 아이콘은 그대로 두고,
> 노면 유도선을 **추가**하는 구조입니다. (참고 이미지처럼 둘이 공존)

---

## 구성 파일

```
Assets/Scripts/Navigation/
├── RoadNavigationGuide.cs              런타임 — 셰브론 배치/풀링/페이드
├── NavigationGuide.md                  이 문서
├── Shaders/
│   └── NavChevron.shader               인스턴싱 언릿 (드로우콜 1~2개)
└── Editor/
    ├── RouteSplineBaker.cs             카메라 애니메이션 → 경로 스플라인 굽기
    └── RoadNavigationGuideEditor.cs    미리보기 + 검증 + 씬뷰 기즈모
```

---

## 전체 작업 순서

```
① 셰브론 텍스처 준비    ← PNG 1장 (알파 + 글로우 베이크)
② 머티리얼 생성         ← Bicycle/NavChevron + GPU Instancing
③ 경로 스플라인 베이크  ← Tools ▸ Navigation ▸ Route Spline Baker
④ 컴포넌트 배치         ← RoadNavigationGuide
⑤ 미리보기 & 검증       ← Validate Setup 버튼
```

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
| **Speed Tier Color** | `Tint By Speed` | 속도 등급별 색 전환 (SpeedUIController와 동일 기준) | ✔ |
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
| 셰브론이 도로를 벗어남 | 경로 스플라인이 낡음 | 카메라 애니메이션 수정 후 재베이크 |
| 색이 안 바뀜 | 셰이더에 `_BaseColor` 없음 | Color Property를 셰이더 프로퍼티명과 일치시킴 |
| 드로우콜이 셰브론 수만큼 늘어남 | GPU Instancing 미체크 | 머티리얼에서 Enable GPU Instancing 체크 |
| 베이크 시 "유효한 샘플 없음" | 샘플 대상이 Animation Track으로 안 움직임 | 대상 Transform을 카메라로 재지정 |
| 정지 구간에서 셰브론이 사라짐 | `Visible Speed Threshold` | 0으로 두면 속도와 무관하게 표시 |

---

## 관련 문서

- [Timeline 구성 가이드](../Timeline/TimelineGuide.md) — 방향 마커·이벤트 배치
- [SpeedUIController](../UI/SpeedUIController.cs) — 화면 고정 방향 아이콘 / 속도 등급 기준
