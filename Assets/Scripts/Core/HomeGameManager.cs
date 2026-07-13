using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Video;

/// <summary>
/// 홈 화면 씬 전환 및 오디오 피드백을 담당하는 매니저.
/// 버튼 애니메이터는 각 버튼 오브젝트의 LevelSelectionBtn 컴포넌트가,
/// O/X 버튼 실행은 oButton/xButton 컴포넌트가 처리합니다.
/// </summary>
public class HomeGameManager : MonoBehaviour
{
    [Header("Home UI Panels")]
    [SerializeField] private GameObject homePanel;
    [SerializeField] private GameObject homeTransition;
    [SerializeField] private GameObject logo;

    [Header("Video")]
    [SerializeField] private VideoPlayer bgVideoPlayer;

    [Header("Level Buttons")]
    [SerializeField] private LevelSelectionBtn beginnerBtn;
    [SerializeField] private LevelSelectionBtn advancedBtn;

    [Header("Audio")]
    [SerializeField] private AudioClip clickClip;
    private AudioSource _audioSource;

    [Header("Transition Timing")]
    [Tooltip("버튼 클릭 후 트랜지션 시작까지의 최소 시간(초) — 버튼 선택 애니메이션 연출용 (로딩과 동시 진행)")]
    [SerializeField] private float selectFeedbackDelay = 0.5f;
    [Tooltip("트랜지션 애니메이션 시작 후 씬을 활성화하는 시점(초) — 스파크가 화면을 완전히 덮는 순간 (클립 2.6s 기준 1.6s)")]
    [SerializeField] private float sceneRevealDelay = 1.6f;
    [Tooltip("씬 활성화 후 트랜지션 잔여 연출이 끝나 Home 씬을 언로드하기까지 대기(초)")]
    [SerializeField] private float homeUnloadDelay = 1.0f;

    private bool _isTransitioning = false;

    void Awake()
    {
        _audioSource = GetComponent<AudioSource>();
        if (_audioSource == null)
        {
            _audioSource = gameObject.AddComponent<AudioSource>();
            _audioSource.playOnAwake = false;
        }

        // 레벨 대형 에셋 백그라운드 프리로드 — 씬 파일 수정 없이 코드로 부착
        if (GetComponent<PreloadManager>() == null)
            gameObject.AddComponent<PreloadManager>();
    }

    void Start()
    {
        ShowHome();

        if (InputManager.Instance != null)
            InputManager.Instance.OnSelectionChanged += OnSelectionChanged;
    }

    void OnDestroy()
    {
        if (InputManager.Instance != null)
            InputManager.Instance.OnSelectionChanged -= OnSelectionChanged;
    }

    public void ShowHome()
    {
        _isTransitioning = false;
        if (homePanel != null) homePanel.SetActive(true);
        if (homeTransition != null) homeTransition.SetActive(false);

        if (logo != null && InputManager.Instance != null)
            logo.SetActive(InputManager.Instance.ShowLogo);

        if (beginnerBtn != null) beginnerBtn.ResetToLoop();
        if (advancedBtn != null) advancedBtn.ResetToLoop();

        if (bgVideoPlayer != null)
        {
            StopAllCoroutines();
            StartCoroutine(PrepareAndPlayVideo());
        }
        else
        {
            // 비디오가 없으면 기다릴 이유가 없다 — 프리로드를 바로 풀어준다.
            PreloadManager.MarkHomeReady();
        }
    }

    private IEnumerator PrepareAndPlayVideo()
    {
        bgVideoPlayer.gameObject.SetActive(true);
        bgVideoPlayer.Prepare();

        // 최대 5초 대기 — 클립 누락/로딩 실패 시 무한 대기 방지
        float prepTimer = 0f;
        while (!bgVideoPlayer.isPrepared && prepTimer < 5f)
        {
            prepTimer += Time.deltaTime;
            yield return null;
        }

        if (bgVideoPlayer.isPrepared) bgVideoPlayer.Play();
        else Debug.LogWarning("[HomeGameManager] 배경 비디오 준비 시간 초과 — 재생 생략");

        // 성공하든 실패하든 여기서 프리로드를 풀어준다. Prepare()가 끝나기 전에 청크
        // 로딩이 끼어들면 캐시가 차가운 첫 진입에서 디스크·메인 스레드를 두고 경합한다.
        PreloadManager.MarkHomeReady();
    }

    /// <summary>키보드 방향키 입력 시 사운드 피드백 (애니메이터는 LevelSelectionBtn이 처리)</summary>
    private void OnSelectionChanged(bool? selection)
    {
        if (_isTransitioning || homePanel == null || !homePanel.activeSelf) return;
        if (selection != null) PlayClickSound();
    }

    private void PlayClickSound()
    {
        if (_audioSource && clickClip)
            _audioSource.PlayOneShot(clickClip);
    }

    /// <summary>초급 버튼 UI의 oButton.onExecute에서 호출</summary>
    public void SelectBeginnerLevel()
    {
        if (_isTransitioning) return;
        PlayClickSound();
        if (beginnerBtn != null) beginnerBtn.SetSelected(true);
        if (advancedBtn != null) advancedBtn.SetSelected(false);
        StartTransition(1);
    }

    /// <summary>고급 버튼 UI의 xButton.onExecute에서 호출</summary>
    public void SelectAdvancedLevel()
    {
        if (_isTransitioning) return;
        PlayClickSound();
        if (beginnerBtn != null) beginnerBtn.SetSelected(false);
        if (advancedBtn != null) advancedBtn.SetSelected(true);
        StartTransition(2);
    }

    private void StartTransition(int level)
    {
        _isTransitioning = true;

        // 레벨 진입 시점에 컨트롤러 입력단을 재설정한다. 장시간 가동 중 PAS 인터럽트만
        // 죽어 페달이 안 먹는 사례가 있어(버튼·브레이크는 정상), 주행 시작 전에 미리 되살린다.
        // 트랜지션·인트로가 뒤따르므로 핸들이 중립인 상태에서 조향 원점도 함께 다시 잡힌다.
        if (InputManager.Instance != null)
            InputManager.Instance.ResetControllerInput();

        // 로딩 우선순위·GPU 업로드 타임슬라이스를 끌어올리고 프리로드 청크 발행을 중단해
        // 씬 로드를 가속한다. 원복은 씬 활성화 완료 후 OnLevelLoadFinished에서.
        PreloadManager.BoostLoadingPriority();
        StartCoroutine(LoadLevelSequence(level));
    }

    private IEnumerator LoadLevelSequence(int level)
    {
        var loadTimer = System.Diagnostics.Stopwatch.StartNew();
        string sceneName = level == 1 ? "Level1" : "Level2";
        AsyncOperation asyncLoad = SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Additive);
        asyncLoad.allowSceneActivation = false;

        // 로드가 활성화 대기 상태(progress 0.9)에 도달할 때까지 홈 화면을 유지한다.
        // selectFeedbackDelay는 버튼 선택 애니메이션이 보일 최소 시간 (로딩과 동시 진행).
        float elapsed = 0f;
        while (asyncLoad.progress < 0.9f || elapsed < selectFeedbackDelay)
        {
            elapsed += Time.deltaTime;
            yield return null;
        }

        // UnityEngine.Debug 정규화 — 전역 Debug 래퍼(Plugins/Debug.cs)가 빌드에서 로그를
        // 제거하므로, 빌드 Player.log에 남아야 하는 측정/진단 로그는 원본 API를 직접 호출한다.
        UnityEngine.Debug.Log($"[HomeGameManager] {sceneName} 로드 준비 완료: {loadTimer.Elapsed.TotalSeconds:F2}s (버튼 입력 기준)");

        // 로드 완료 후에야 트랜지션 재생 — 2.6s 원샷 클립이 처음부터 온전히 보이도록.
        // 스파크가 화면을 완전히 덮는 시점(sceneRevealDelay)에 씬을 활성화해 전환을 숨긴다.
        Animator transitionAnimator = null;
        if (homeTransition)
        {
            homeTransition.SetActive(true);
            transitionAnimator = homeTransition.GetComponent<Animator>();
            yield return new WaitForSeconds(sceneRevealDelay);
            // 씬 활성화(오브젝트 생성/Awake)가 클립 잔여 길이보다 오래 걸릴 수 있으므로,
            // 화면이 덮인 프레임에서 일시정지해 활성화 중에 홈이 다시 비치지 않게 한다.
            if (transitionAnimator) transitionAnimator.speed = 0f;
        }

        if (GameManager.Instance != null)
            GameManager.Instance.PrepareLevel(level);

        UnityEngine.Debug.Log($"[HomeGameManager] {sceneName} 활성화 시작: {loadTimer.Elapsed.TotalSeconds:F2}s (트랜지션 연출 종료)");

        // 활성화 대기 프레임 통계 — 소수의 초장 프레임이면 동기식 버스트(오디오 디코딩/
        // 셰이더 생성/Awake), 다수의 짧은 프레임이면 타임슬라이스된 통합 작업이 원인.
        asyncLoad.allowSceneActivation = true;
        int actFrames = 0;
        float actMaxGapMs = 0f, prevT = Time.realtimeSinceStartup;
        while (!asyncLoad.isDone)
        {
            yield return null;
            float t = Time.realtimeSinceStartup;
            actMaxGapMs = Mathf.Max(actMaxGapMs, (t - prevT) * 1000f);
            prevT = t;
            actFrames++;
        }
        UnityEngine.Debug.Log($"[HomeGameManager] {sceneName} 활성화 통계: {actFrames}프레임, 최장 프레임 {actMaxGapMs:F0}ms");

        // 활성화가 끝났으니 트랜지션 잔여 연출(리빌)을 재개한다
        if (transitionAnimator) transitionAnimator.speed = 1f;

        // 로딩 가속 원복 + 남은 프리로드 재개 (낮은 우선순위)
        PreloadManager.OnLevelLoadFinished();
        UnityEngine.Debug.Log($"[HomeGameManager] {sceneName} 씬 활성화 완료: {loadTimer.Elapsed.TotalSeconds:F2}s (버튼 입력 기준)");

        Scene newScene = SceneManager.GetSceneByName(sceneName);
        if (newScene.IsValid())
            SceneManager.SetActiveScene(newScene);

        if (homePanel) homePanel.SetActive(false);

        // Additive 오버랩(약 1초) 동안 Home 씬과 레벨 씬의 AudioListener/EventSystem이
        // 동시에 활성화되어 "2 audio listeners / 2 event systems" 경고가 뜬다.
        // 레벨 씬에 해당 서비스가 있을 때만 Home 씬 것을 비활성화한다.
        if (newScene.IsValid())
            DisableDuplicateSceneServices(newScene);

        // 트랜지션 잔여 연출(클립 후반부)이 끝날 때까지 대기 후 Home 언로드
        yield return new WaitForSeconds(homeUnloadDelay);

        Debug.Log("[Home] Unloading Home scene after transition complete.");
        SceneManager.UnloadSceneAsync("Home");
    }

    // 레벨 씬(keepScene)에 활성 서비스가 있으면 Home 씬의 같은 서비스를 비활성화한다.
    // (레벨에 없으면 그대로 둬서 "0개" 상태를 만들지 않는다.)
    void DisableDuplicateSceneServices(Scene keepScene)
    {
        Scene homeScene = gameObject.scene;

        var listeners = FindObjectsByType<AudioListener>(FindObjectsSortMode.None);
        if (HasActiveInScene(listeners, keepScene, l => l.isActiveAndEnabled, l => l.gameObject.scene))
            foreach (var l in listeners)
                if (l.gameObject.scene == homeScene) l.enabled = false;

        var systems = FindObjectsByType<UnityEngine.EventSystems.EventSystem>(FindObjectsSortMode.None);
        if (HasActiveInScene(systems, keepScene, e => e.isActiveAndEnabled, e => e.gameObject.scene))
            foreach (var e in systems)
                if (e.gameObject.scene == homeScene) e.enabled = false;
    }

    static bool HasActiveInScene<T>(T[] items, Scene scene,
                                    System.Func<T, bool> isActive, System.Func<T, Scene> sceneOf)
    {
        foreach (var it in items)
            if (isActive(it) && sceneOf(it) == scene) return true;
        return false;
    }
}
