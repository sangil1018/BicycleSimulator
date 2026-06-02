# 🏠 홈 화면 구성 가이드 (Home Scene Configuration Guide) - v1.0

이 문서는 자전거 시뮬레이션 프로그램의 **홈 화면(Home Scene)** 구성과 씬 전환 시스템을 설명합니다.

---

## 1. 필수 선행 설정 (Core Setup)
`HomeGameManager`가 정상 작동하려면 다음 매니저들이 `Home` 씬에 배치되어야 합니다.

1. **InputManager (필수)**: 하드웨어/키보드 입력 처리 및 로고 표시 제어.
2. **GameManager (필수)**: 전체 게임 상태 및 씬 전환 데이터 관리.
3. **영속성**: 위 매니저들은 `Singleton`으로 설계되어 씬 전환 후에도 유지됩니다.

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

트랜지션 애니메이션(2.6초)에 맞춰 다음과 같이 비동기 중첩 로딩을 수행합니다.

1. **로딩 시작 (0.0s)**: 버튼 클릭 즉시 다음 씬을 `Additive`(중첩) 모드로 로딩 시작 (`allowSceneActivation = false`).
2. **선택 연출 (0.5s)**: 버튼 확정 비주얼을 위해 대기.
3. **화면 가리기 (0.5s ~ 2.1s)**: `Home Transition` 애니메이션 시작.
4. **씬 교체 (2.1s / 커버 1.6s 지점)**: 화면이 완전히 가려진 시점에 새 씬 활성화 및 `SetActiveScene` 설정.
5. **지연 해제 (3.1s)**: 나머지 애니메이션(1.0s) 완료 후 `Home` 씬 언로드(`UnloadSceneAsync`).

---

## 4. 인스펙터 설정 가이드 (HomeGameManager)
*   **Home Transition**: 활성화 시 애니메이션이 실행되는 오브젝트 할당.
*   **Bg Video Player**: 루프 설정이 켜져 있어야 하며, `Play On Awake`는 끕니다.

---

## 5. 주의 사항
*   **중첩 로딩**: 홈 씬이 해제되기 전까지 두 씬의 오브젝트가 공존하므로 메모리 및 카메라 레이어 설정에 유의하십시오.
*   **활성 씬**: 반드시 `SetActiveScene`을 통해 새 씬을 활성화해야 물리/라이팅 설정이 정상 적용됩니다.
