using System;
using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

public enum GameState
{
    Boot,
    Home,
    Intro,          // 가이드 영상(인트로) 상태
    GameReady,
    NormalRiding,
    EventBrake,
    OXQuiz,         // OX 퀴즈 진행 상태
    CrosswalkWalk,
    GameResult,
}

/// <summary>
/// 게임의 전체적인 흐름을 제어하는 매니저 클래스.
/// UI와 데이터 의존성을 분리하여 핵심 로직만 담당합니다.
/// </summary>
public class GameManager : Singleton<GameManager>
{
    [Header("References")]
    // 씬 전환 후 다시 찾아야 할 참조들
    private VideoRailController rail;
    private WaypointChecker waypointChecker;

    [Header("Exit Menu")]
    [SerializeField] private GameObject exit_menu;

    [Header("Final Result UI")]
    [SerializeField] private GameObject resultObject;
    [SerializeField] private GameObject endTransitionObject;
    [SerializeField] private CanvasGroup endTransitionCanvasGroup;

    [Header("Timing")]
    [SerializeField] float brakeEventSec = 9f;
    [SerializeField] float resultDisplaySec = 3f;
    [SerializeField] float crosswalkSec = 4f;
    [SerializeField] float xDoublePressSec = 3f;
    [SerializeField] float quizDurationSec = 12f; // 퀴즈 UI 연출 대기 시간
    [SerializeField] float finalResultWaitSec = 13f; // 결과물 표시 시간 (이관됨)
    [SerializeField] float totalEndWaitSec = 15f;    // 최종 홈 이동 시간 (이관됨)

    public GameState CurrentState { get; private set; } = GameState.Boot;
    public int Level { get; private set; } = 1;
    public bool CanMove { get; private set; } = false;

    public int EventScore { get; private set; }
    public int QuizScore => QuizManager.Instance.CurrentQuizScore;
    public int TotalScore => EventScore + QuizScore;

    int _xPressCount = 0;
    Coroutine _xResetRoutine = null;

    void OnEnable()
    {
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    void OnDisable()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    void Start()
    {
        InputManager.Instance.OnBtnX += HandleXButton;

        string sceneName = SceneManager.GetActiveScene().name;
        if (sceneName == "Home")
        {
            ChangeState(GameState.Home);
        }
        else if (sceneName.StartsWith("Level"))
        {
            InitLevelScene();
        }
    }

    void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (scene.name == "Home")
        {
            ChangeState(GameState.Home);
            return;
        }

        if (scene.name.StartsWith("Level"))
        {
            InitLevelScene();
        }
    }

    private void InitLevelScene()
    {
        rail = FindFirstObjectByType<VideoRailController>();
        waypointChecker = FindFirstObjectByType<WaypointChecker>();

        if (rail)
        {
            StartLevelIntro();
        }
    }

    public void ChangeState(GameState next)
    {
        CanMove = next == GameState.NormalRiding;
        CurrentState = next;
        Debug.Log($"[GameManager] ◀ {next} ▶");
    }

    void HandleXButton()
    {
        // 퀴즈 중이거나 홈일 때는 무시
        if (CurrentState == GameState.OXQuiz || CurrentState == GameState.Home || CurrentState == GameState.Boot) return;

        // 결과 화면에서는 바로 홈으로
        // if (CurrentState == GameState.GameResult) { GoHome(); return; }

        // 주행 중 X를 누르면 종료 메뉴를 띄우기 위한 이벤트성 로그 (UI에서 oButton/xButton으로 처리 권장)
        exit_menu.SetActive(true);
        Debug.Log("[GameManager] X Button Pressed - Exit Menu UI should appear");
    }

    public void GoHome()
    {
        StopAllCoroutines();
        EventScore = 0;
        QuizManager.Instance.ResetScore();
        SceneManager.LoadScene("Home");
    }

    public void PrepareLevel(int level)
    {
        Level = level;
    }

    private void StartLevelIntro()
    {
        ChangeState(GameState.Intro);
        rail.LoadVideo(Level);
    }

    public void OnIntroFinished()
    {
        ChangeState(GameState.GameReady);
        // 여기서 "시작 메뉴 UI"를 활성화하는 로직이 필요할 수 있습니다.
    }

    /// <summary>
    /// 시작 메뉴 UI에서 "시작(O)"을 눌렀을 때 호출
    /// </summary>
    public void StartRiding()
    {
        if (CurrentState != GameState.GameReady) return;

        ChangeState(GameState.NormalRiding);
        rail.Play();
        waypointChecker.StartChecking(Level);
    }

    public void TriggerWaypoint(VideoWaypoint wp)
    {
        switch (wp.type)
        {
            case WaypointType.BrakeEvent:
                StartCoroutine(BrakeEventRoutine(wp));
                break;
            case WaypointType.OXQuiz:
                StartCoroutine(OXQuizRoutine(wp));
                break;
            case WaypointType.Crosswalk:
                StartCoroutine(CrosswalkRoutine());
                break;
            case WaypointType.DirectionHint:
                Debug.Log($"[GameManager] Direction Hint: {wp.label}");
                break;
        }
    }

    IEnumerator BrakeEventRoutine(VideoWaypoint wp)
    {
        ChangeState(GameState.EventBrake);
        rail.Freeze();
        InputManager.Instance.SendVibrate(VibeState.Danger);

        bool braked = false;
        float timer = brakeEventSec;

        while (timer > 0f)
        {
            if (InputManager.Instance.BrakeAny) { braked = true; break; }
            timer -= Time.deltaTime;
            yield return null;
        }

        int pts = braked ? 10 : 0;
        EventScore += pts;
        if (braked) InputManager.Instance.SendVibrate(VibeState.Success);

        yield return new WaitForSeconds(resultDisplaySec);

        ChangeState(GameState.NormalRiding);
        rail.Resume();
    }

    IEnumerator OXQuizRoutine(VideoWaypoint wp)
    {
        ChangeState(GameState.OXQuiz);
        rail.Freeze();

        if (QuizManager.Instance != null)
        {
            // wp.quizIndex는 0~3, QuizManager.StartQuiz는 1~4를 기대함
            QuizManager.Instance.StartQuiz(wp.quizIndex + 1);
        }

        // 퀴즈 UI(BlackBoard)의 연출 및 대기 시간만큼 기다린 후 게임 재개
        yield return new WaitForSeconds(quizDurationSec);

        ChangeState(GameState.NormalRiding);
        rail.Resume();
    }

    IEnumerator CrosswalkRoutine()
    {
        ChangeState(GameState.CrosswalkWalk);
        rail.Freeze();
        InputManager.Instance.SendVibrate(VibeState.Walk);

        yield return WaitForOButton(); // 내리기
        yield return new WaitForSeconds(crosswalkSec); // 걷기
        yield return WaitForOButton(); // 다시 타기

        ChangeState(GameState.NormalRiding);
        rail.Resume();
    }

    IEnumerator WaitForOButton()
    {
        bool pressed = false;
        Action onO = () => pressed = true;
        InputManager.Instance.OnBtnO += onO;
        yield return new WaitUntil(() => pressed);
        InputManager.Instance.OnBtnO -= onO;
    }

    public void OnVideoComplete()
    {
        StopAllCoroutines();
        waypointChecker.StopChecking();
        ChangeState(GameState.GameResult);
        rail.Stop();
        InputManager.Instance.SendVibrate(VibeState.Success);

        ShowFinalResult();
    }

    /// <summary>
    /// 최종 결과 화면을 표시하고 일정 시간 후 홈으로 이동합니다. (QuizManager에서 이관)
    /// </summary>
    public void ShowFinalResult()
    {
        if (resultObject != null) resultObject.SetActive(true);
        StartCoroutine(FinalResultRoutine());
    }

    private IEnumerator FinalResultRoutine()
    {
        // 결과물 표시 시간 대기
        yield return new WaitForSeconds(finalResultWaitSec);

        // 엔딩 트랜지션 활성화 및 페이드아웃 시작
        if (endTransitionObject != null)
        {
            endTransitionObject.SetActive(true);
            if (endTransitionCanvasGroup != null)
            {
                float duration = 20f / 30f; // 약 0.66초
                float elapsed = 0f;
                endTransitionCanvasGroup.alpha = 0f;

                while (elapsed < duration)
                {
                    elapsed += Time.deltaTime;
                    endTransitionCanvasGroup.alpha = Mathf.Clamp01(elapsed / duration);
                    yield return null;
                }
                endTransitionCanvasGroup.alpha = 1f;
            }
        }

        // 전체 대기 시간이 채워질 때까지 대기 후 홈으로 이동
        float currentElapsed = finalResultWaitSec + (20f / 30f);
        if (totalEndWaitSec > currentElapsed)
        {
            yield return new WaitForSeconds(totalEndWaitSec - currentElapsed);
        }

        GoHome();
    }
}
