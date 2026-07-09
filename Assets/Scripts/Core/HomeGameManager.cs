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

    private bool _isTransitioning = false;

    void Awake()
    {
        _audioSource = GetComponent<AudioSource>();
        if (_audioSource == null)
        {
            _audioSource = gameObject.AddComponent<AudioSource>();
            _audioSource.playOnAwake = false;
        }
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
        StartCoroutine(LoadLevelSequence(level));
    }

    private IEnumerator LoadLevelSequence(int level)
    {
        string sceneName = level == 1 ? "Level1" : "Level2";
        AsyncOperation asyncLoad = SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Additive);
        asyncLoad.allowSceneActivation = false;

        yield return new WaitForSeconds(0.5f);

        if (homeTransition)
        {
            homeTransition.SetActive(true);
            yield return new WaitForSeconds(1.6f);
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

        yield return new WaitForSeconds(1.0f);

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
