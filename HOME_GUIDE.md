# 🏠 홈 화면 구성 가이드 (Home Scene Configuration Guide) - v1.1

이 문서는 자전거 시뮬레이션 프로그램의 **홈 화면(Home Scene)** 구성과 씬 전환 시스템을 설명합니다.

---

## 1. 필수 선행 설정 (Core Setup)
`HomeGameManager`가 정상 작동하려면 다음 매니저들이 `Home` 씬에 배치되어야 합니다.

1. **InputManager (필수)**: 하드웨어/키보드 입력 처리 및 로고 표시 제어.
2. **GameManager (필수)**: 전체 게임 상태 및 씬 전환 데이터 관리.
3. **진동**: O/X 버튼 실행 시 `oButton`/`xButton`의 `HandleExecute()`가 `InputManager.SendVibrate(VibeState.Click)`을 호출해 ESP32로 `V3`를 보냅니다(GPIO2 구동). 진동은 버튼 컴포넌트가 담당하므로 Home·Level 모든 씬에서 동일하게 동작하며, `HomeGameManager.PlayClickSound()`는 사운드만 재생합니다. 방향키로 선택만 옮길 때는 사운드만 나고 진동은 없습니다. ESP32가 미연결이면 진동 없이 사운드만 재생됩니다. (씬에 남아 있는 `VibrationRelay` 오브젝트는 폐기된 USB 릴레이용 껍데기로, 지워도 무방합니다.)
4. **수명**: `InputManager`는 `Singleton`(`DontDestroyOnLoad`)으로 씬 전환 후에도 유지됩니다. `GameManager`는 `SceneSingleton`이라 씬마다 새 인스턴스가 존재하므로 Home·Level 각 씬에 배치되어야 합니다.

---

## 2. 홈 씬 구성 요소

### 2.1. 배경 비디오 (Background Video)
*   **컴포넌트**: `VideoPlayer`
*   **영상 파일**: `Home_BG_1min_loop.mp4`
*   **재생 로직**: 씬 시작 시 즉시 재생하지 않고 `Prepare()`를 통해 로딩을 완료한 후, `isPrepared` 상태에서 실제 재생을 시작합니다. (끊김 없는 연출 보장)

### 2.2. 버튼 및 애니메이션
*   **Beginner/Advanced Buttons**: `Animator`를 통해 상태 제어.
    *   `StartLoop`: 대기 중 10초 주기 루프 애니메이션.
    *   `Select`: 선택 시 확정 애니메이션.
    *   `Inactive`: 선택되지 않은 버튼의 비활성화 상태.
*   **OX 리액션**: 하드웨어 버튼과 연동된 시각적 아이콘 표시.

---

## 3. 정밀 씬 전환 프로세스 (Home → Level)

고정 대기 시간이 아니라 **실제 로드 진행률 기반**으로 동작하며, 트랜지션 애니메이션(2.6초 원샷 클립)이 항상 전환을 가리도록 보장합니다.

1. **로딩 시작 (0.0s)**: 버튼 클릭 즉시 다음 씬을 `Additive`(중첩) 모드로 로딩 시작 (`allowSceneActivation = false`). 동시에 `PreloadManager`가 로딩 우선순위·GPU 업로드 슬라이스를 끌어올리고 프리로드 청크 발행을 중단.
2. **로드 완료 대기**: `progress ≥ 0.9`(활성화 대기 상태)에 도달할 때까지 홈 화면 유지. 버튼 선택 연출을 위한 최소 시간 `Select Feedback Delay`(기본 0.5s)는 로딩과 동시 진행. 에셋이 프리로드로 상주 중이면 사실상 0.5초에 완료됨.
3. **트랜지션 재생**: 로드가 끝난 뒤에야 `Home Transition`을 활성화해 클립을 처음부터 재생.
4. **커버 홀드 & 씬 교체**: 스파크가 화면을 완전히 덮는 `Scene Reveal Delay`(기본 1.6s) 지점에서 `Animator.speed = 0`으로 일시정지 → 씬 활성화(`allowSceneActivation = true`) → 완료 후 재개. 활성화가 길어져도 홈 화면이 다시 비치지 않음.
5. **지연 해제**: `SetActiveScene` 설정, 홈 패널 숨김, `Home Unload Delay`(기본 1.0s = 클립 잔여 리빌) 후 `Home` 씬 언로드(`UnloadSceneAsync`).

각 단계의 소요 시간은 빌드 `Player.log`에 기록됩니다 (`로드 준비 완료` / `활성화 시작` / `활성화 통계` / `씬 활성화 완료`).

---

## 3-1. 레벨 에셋 프리로드 (PreloadManager)

빌드에서 씬 전환이 느렸던 원인은 `GoHome()`의 `LoadScene(Single)`이 매번 미사용 에셋을 언로드해 **매 진입마다 에셋(특히 URP 셰이더)을 다시 로드**했기 때문입니다. 이를 막기 위해 레벨 씬 의존성 전체를 홈 대기 중 미리 로드해 상주시킵니다.

*   **매니페스트 생성**: 에디터 메뉴 `Tools → Build Preload Manifests` — 레벨 씬 의존성 전체(스크립트·씬 제외)를 청크 프리팹(`Assets/Resources/Preload/LevelN_Preload_XXX`)으로 생성.
*   **런타임**: `PreloadManager`(HomeGameManager.Awake에서 자동 부착)가 홈에서 청크를 낮은 우선순위로 순차 로드하고 static 참조로 유지 — 홈 재방문·레벨 재진입에도 다시 로드하지 않음.
*   ⚠️ **레벨 씬의 에셋 구성을 바꾸면 반드시 `Tools → Build Preload Manifests`를 재실행한 후 빌드**해야 합니다. 누락하면 새 에셋이 상주 목록에서 빠져 해당 부분만 다시 느려집니다.
*   부팅 직후 첫 프리로드(십수 초, 홈 화면 뒤 백그라운드) 중에 클릭하면 진행 중인 청크 하나만큼만 대기가 생깁니다. 프리로드 완료 후에는 항상 즉시 전환됩니다.

---

## 4. 인스펙터 설정 가이드 (HomeGameManager)
*   **Home Transition**: 활성화 시 애니메이션이 실행되는 오브젝트 할당.
*   **Bg Video Player**: 루프 설정이 켜져 있어야 하며, `Play On Awake`는 끕니다.
*   **Transition Timing**:
    *   `Select Feedback Delay` (0.5s): 클릭 후 트랜지션 시작까지의 최소 시간 (버튼 선택 애니메이션 연출용, 로딩과 동시 진행).
    *   `Scene Reveal Delay` (1.6s): 트랜지션 시작 후 씬을 활성화하는 시점 — 클립에서 스파크가 화면을 완전히 덮는 순간에 맞춤.
    *   `Home Unload Delay` (1.0s): 씬 활성화 후 Home 언로드까지 대기 — 클립 잔여 리빌 길이에 맞춤.

---

## 5. 주의 사항
*   **중첩 로딩**: 홈 씬이 해제되기 전까지 두 씬의 오브젝트가 공존하므로 메모리 및 카메라 레이어 설정에 유의하십시오.
*   **활성 씬**: 반드시 `SetActiveScene`을 통해 새 씬을 활성화해야 물리/라이팅 설정이 정상 적용됩니다.
