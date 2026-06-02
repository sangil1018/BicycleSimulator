using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Video;

/// <summary>
/// 홈 화면 로직 및 씬 전환을 관리하는 매니저.
/// 하드웨어 버튼(즉시 실행)과 키보드(선택 후 실행) 조작을 통합 관리하며,
/// 인스펙터에 등록된 레벨 버튼 및 OX 버튼 UI와 연동됩니다.
/// </summary>
public class HomeGameManager : MonoBehaviour
{
    [Header("Home UI Panels")]
    [SerializeField] private GameObject homePanel;
    [SerializeField] private GameObject homeTransition;
    [SerializeField] private GameObject logo;

    [Header("Video")]
    [SerializeField] private VideoPlayer bgVideoPlayer;

    [Header("Level Buttons (Animators)")]
    [SerializeField] private Animator beginnerBtnAnimator;
    [SerializeField] private Animator advancedBtnAnimator;

    [Header("OX Button Icons (Hardware Mapping)")]
    [SerializeField] private GameObject oButtonReaction;
    [SerializeField] private GameObject xButtonReaction;

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

        // InputManager 이벤트 연결
        if (InputManager.Instance != null)
        {
            InputManager.Instance.OnBtnO += HandleOButton;
            InputManager.Instance.OnBtnX += HandleXButton;
            InputManager.Instance.OnSelectionChanged += OnSelectionChanged;
        }
    }

    void OnDestroy()
    {
        // 리스너 해제
        if (InputManager.Instance != null)
        {
            InputManager.Instance.OnBtnO -= HandleOButton;
            InputManager.Instance.OnBtnX -= HandleXButton;
            InputManager.Instance.OnSelectionChanged -= OnSelectionChanged;
        }
    }

    /// <summary>
    /// 홈 화면 초기화
    /// </summary>
    public void ShowHome()
    {
        _isTransitioning = false;
        homePanel?.SetActive(true);
        homeTransition?.SetActive(false);

        // 로고 표시 설정 (config.ini 참조)
        if (logo != null && InputManager.Instance != null)
        {
            logo.SetActive(InputManager.Instance.ShowLogo);
        }

        // 버튼 애니메이션 초기화 (루프 시작)
        if (beginnerBtnAnimator) beginnerBtnAnimator.SetTrigger("StartLoop");
        if (advancedBtnAnimator) advancedBtnAnimator.SetTrigger("StartLoop");

        // UI 상태 초기화
        UpdateSelectionVisual(null);

        // 하드웨어 RGB 상태 설정 (S0: 대기)
        InputManager.Instance?.SendRgbState(0);

        // 비디오 재생 시작
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
        {
            yield return null;
        }

        bgVideoPlayer.Play();
    }

    /// <summary>
    /// 키보드 방향키 입력 시 시각적 피드백 제공
    /// </summary>
    private void OnSelectionChanged(bool? selection)
    {
        if (_isTransitioning || homePanel == null || !homePanel.activeSelf) return;

        // 키보드 방향키 이동 시 사운드 재생
        if (selection != null) PlayClickSound();

        UpdateSelectionVisual(selection);
    }

    private void PlayClickSound()
    {
        if (_audioSource && clickClip)
        {
            _audioSource.PlayOneShot(clickClip);
        }
    }

    /// <summary>
    /// 선택 상태에 따라 레벨 버튼 애니메이션 및 OX 버튼 아이콘 상태를 업데이트합니다.
    /// </summary>
    private void UpdateSelectionVisual(bool? selection)
    {
        // 1. 레벨 버튼 애니메이터 피드백 (Select / Inactive / StartLoop)
        if (selection == true)
        {
            beginnerBtnAnimator?.SetTrigger("Select");
            advancedBtnAnimator?.SetTrigger("Inactive");
        }
        else if (selection == false)
        {
            beginnerBtnAnimator?.SetTrigger("Inactive");
            advancedBtnAnimator?.SetTrigger("Select");
        }
        else
        {
            beginnerBtnAnimator?.SetTrigger("StartLoop");
            advancedBtnAnimator?.SetTrigger("StartLoop");
        }

        // 2. OX 버튼 아이콘 리액션 (PDF 가이드 준수)
        if (oButtonReaction)
        {
            oButtonReaction.SetActive(selection == true);
        }
        if (xButtonReaction)
        {
            xButtonReaction.SetActive(selection == false);
        }
    }

    private void HandleOButton()
    {
        // 하드웨어 O 버튼 또는 키보드 확인(1) 입력 시
        if (homePanel.activeSelf && !_isTransitioning)
        {
            PlayClickSound();
            // 하드웨어 버튼은 누르는 즉시 선택 비주얼 적용 후 실행
            UpdateSelectionVisual(true);
            SelectLevel(1);
        }
    }

    private void HandleXButton()
    {
        // 하드웨어 X 버튼 또는 키보드 종료(Esc) 입력 시
        if (homePanel.activeSelf && !_isTransitioning)
        {
            PlayClickSound();
            // 하드웨어 버튼은 누르는 즉시 선택 비주얼 적용 후 실행
            UpdateSelectionVisual(false);
            SelectLevel(2);
        }
    }

    private void SelectLevel(int level)
    {
        _isTransitioning = true;
        StartCoroutine(LoadLevelSequence(level));
    }

    private IEnumerator LoadLevelSequence(int level)
    {
        // 1. 비동기 중첩 로딩 시작 (홈 씬을 유지한 상태에서 레벨 로드)
        string sceneName = level == 1 ? "Level1" : "Level2";
        AsyncOperation asyncLoad = SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Additive);
        asyncLoad.allowSceneActivation = false;

        // 2. 선택 연출을 위한 대기 (0.5초)
        yield return new WaitForSeconds(0.5f);

        // 3. 트랜지션 애니메이션 시작 (화면 가리기 시작)
        if (homeTransition)
        {
            homeTransition.SetActive(true);

            // 4. 화면이 완전히 가려지는 지점(1.6초)까지 대기
            yield return new WaitForSeconds(1.6f);
        }

        // 5. 게임 매니저 데이터 준비 (전환 직전)
        if (GameManager.Instance != null)
        {
            GameManager.Instance.PrepareLevel(level);
        }

        // 6. 씬 활성화 (교체)
        // 화면이 완전히 가려진 이 시점에 로드된 씬을 활성화합니다.
        asyncLoad.allowSceneActivation = true;
        while (!asyncLoad.isDone)
        {
            yield return null;
        }

        // 7. 새 씬을 활성 씬(Active Scene)으로 설정
        Scene newScene = SceneManager.GetSceneByName(sceneName);
        if (newScene.IsValid())
        {
            SceneManager.SetActiveScene(newScene);
        }

        // 8. 홈 UI 패널 숨기기 (트랜지션 UI는 계속 보여야 하므로 씬 해제는 지연)
        if (homePanel) homePanel.SetActive(false);

        // 9. 씬 교체 후 1초 대기 (트랜지션의 나머지 애니메이션 1초 진행)
        yield return new WaitForSeconds(1.0f);

        // 10. 홈 씬 해제
        Debug.Log("[Home] Unloading Home scene after transition complete.");
        SceneManager.UnloadSceneAsync("Home");
    }
}
