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
        // 프리로드가 점유 중일 수 있는 백그라운드 로딩 우선순위를 끌어올려 씬 로드를 가속
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

        Debug.Log($"[HomeGameManager] {sceneName} 로드 준비 완료: {loadTimer.Elapsed.TotalSeconds:F2}s (버튼 입력 기준)");

        // 로드 완료 후에야 트랜지션 재생 — 2.6s 원샷 클립이 처음부터 온전히 보이도록.
        // 스파크가 화면을 완전히 덮는 시점(sceneRevealDelay)에 씬을 활성화해 전환을 숨긴다.
        if (homeTransition)
        {
            homeTransition.SetActive(true);
            yield return new WaitForSeconds(sceneRevealDelay);
        }

        if (GameManager.Instance != null)
            GameManager.Instance.PrepareLevel(level);

        asyncLoad.allowSceneActivation = true;
        while (!asyncLoad.isDone)
            yield return null;

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
