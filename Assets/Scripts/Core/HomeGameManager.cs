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
        homePanel?.SetActive(true);
        homeTransition?.SetActive(false);

        if (logo != null && InputManager.Instance != null)
            logo.SetActive(InputManager.Instance.ShowLogo);

        beginnerBtn?.ResetToLoop();
        advancedBtn?.ResetToLoop();

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
        while (!bgVideoPlayer.isPrepared)
            yield return null;
        bgVideoPlayer.Play();
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
        beginnerBtn?.SetSelected(true);
        advancedBtn?.SetSelected(false);
        StartTransition(1);
    }

    /// <summary>고급 버튼 UI의 xButton.onExecute에서 호출</summary>
    public void SelectAdvancedLevel()
    {
        if (_isTransitioning) return;
        PlayClickSound();
        beginnerBtn?.SetSelected(false);
        advancedBtn?.SetSelected(true);
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

        yield return new WaitForSeconds(1.0f);

        Debug.Log("[Home] Unloading Home scene after transition complete.");
        SceneManager.UnloadSceneAsync("Home");
    }
}
