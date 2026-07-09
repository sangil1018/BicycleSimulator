using System;
using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

public enum GameState
{
    Boot,
    Home,
    Intro,
    GameReady,
    NormalRiding,
    EventBrake,
    EventWarning,
    BicycleStop,
    OXQuiz,
    CrosswalkWalk,
    GameResult,
}

/// <summary>
/// 게임의 전체적인 흐름을 제어하는 매니저 클래스.
/// Timeline 기반 시스템으로 전환됨 (VideoRailController → TimelineGameController).
/// </summary>
public class GameManager : SceneSingleton<GameManager>
{
    [Header("Start Menu")]
    [SerializeField] private GameObject start_menu;

    [Header("Exit Menu")]
    [SerializeField] private GameObject exit_menu;

    [Header("Final Result UI")]
    [SerializeField] private GameObject resultObject;
    [SerializeField] private GameObject endTransitionObject;
    [SerializeField] private CanvasGroup endTransitionCanvasGroup;

    [Header("Timing")]
    [SerializeField] float delayStart = 2.5f;
    [SerializeField] float brakeEventSec = 5f;
    [SerializeField] float warningStopSec = 3f;
    [SerializeField] float resultDisplaySec = 3f;
    [SerializeField] float quizDurationSec = 10f;
    [SerializeField] int finalQuizIndex = 3;    // 0-based, 4번째 퀴즈
    [SerializeField] float finalResultWaitSec = 13f;
    [SerializeField] float totalEndWaitSec = 15f;

    // ── 공개 상태 ──────────────────────────────────────────────────

    public GameState CurrentState { get; private set; } = GameState.Boot;
    public int Level { get; private set; } = 1;
    public int FinalQuizIndex => finalQuizIndex;
    public bool CanMove { get; private set; } = false;

    public int EventScore { get; private set; }
    public int QuizScore => QuizManager.Instance != null ? QuizManager.Instance.CurrentQuizScore : 0;
    public int TotalScore => EventScore + QuizScore;

    /// <summary>GameState가 변경될 때 발행. SpeedUIController 등에서 구독.</summary>
    public event Action<GameState> OnStateChanged;

    /// <summary>씬에서 찾은 TimelineGameController 참조 (GameSignalReceiver에서 접근용).</summary>
    public TimelineGameController TimelineController { get; private set; }

    // ── 내부 상태 ──────────────────────────────────────────────────

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
        // 영속 InputManager 이벤트 구독 — GameManager는 씬 스코프라 레벨 종료 시 파괴되므로
        // 반드시 OnDestroy에서 해제해야 영속 InputManager에 죽은 참조가 남지 않는다.
        InputManager.Instance.OnBtnX += HandleXButton;

        if (SceneManager.GetActiveScene().name.StartsWith("Level"))
            InitLevelScene();
    }

    void OnDestroy()
    {
        if (InputManager.Instance != null)
            InputManager.Instance.OnBtnX -= HandleXButton;
    }

    void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (scene.name.StartsWith("Level"))
            InitLevelScene();
    }

    private void InitLevelScene()
    {
        TimelineController = FindFirstObjectByType<TimelineGameController>();

        if (TimelineController != null)
            StartLevelIntro();
        else
            Debug.LogWarning("[GameManager] TimelineGameController를 찾을 수 없습니다.");

        if (QuizManager.Instance != null) QuizManager.Instance.InitialQuizUI();
    }

    public void ChangeState(GameState next)
    {
        CanMove = next == GameState.NormalRiding;
        CurrentState = next;
        OnStateChanged?.Invoke(next);
        Debug.Log($"[GameManager] ◀ {next} ▶");
    }

    void HandleXButton()
    {
        if (CurrentState == GameState.OXQuiz ||
            CurrentState == GameState.Home ||
            CurrentState == GameState.Boot) return;

        if (exit_menu != null) exit_menu.SetActive(true);
    }

    public void GoHome()
    {
        StopAllCoroutines();
        EventScore = 0;
        if (QuizManager.Instance != null) QuizManager.Instance.ResetScore();
        ChangeState(GameState.Home);
        SceneManager.LoadScene("Home");
    }

    public void PrepareLevel(int level)
    {
        Level = level;
    }

    private void StartLevelIntro()
    {
        ChangeState(GameState.Intro);
        var introManager = FindFirstObjectByType<IntroManager>();
        if (introManager != null)
            introManager.StartIntro();
        else
        {
            Debug.LogWarning("[GameManager] IntroManager를 찾을 수 없습니다. GameReady로 즉시 전환합니다.");
            OnIntroFinished();
        }
    }

    public void OnIntroFinished()
    {
        ChangeState(GameState.GameReady);
        StartCoroutine(ShowStartMenuRoutine());
    }

    IEnumerator ShowStartMenuRoutine()
    {
        yield return new WaitForSeconds(delayStart);
        if (start_menu != null) start_menu.SetActive(true);
    }

    /// <summary>시작 메뉴 UI에서 "시작(O)"을 눌렀을 때 호출</summary>
    public void StartRiding()
    {
        Debug.Log($"[GM] StartRiding called — CurrentState:{CurrentState}  TimelineController:{TimelineController != null}");
        if (CurrentState != GameState.GameReady) return;

        ChangeState(GameState.NormalRiding);
        if (TimelineController != null) TimelineController.Play();
    }

    // ── 이벤트 트리거 (GameSignalReceiver에서 호출) ────────────────

    public void TriggerBrakeEvent()
    {
        StartCoroutine(BrakeEventRoutine());
    }

    public void TriggerWarningStop()
    {
        StartCoroutine(WarningStopRoutine());
    }

    public void TriggerBicycleStop()
    {
        ChangeState(GameState.BicycleStop);
        if (TimelineController != null) TimelineController.Freeze();
    }

    public void TriggerOXQuiz(int quizIndex)
    {
        StartCoroutine(OXQuizRoutine(quizIndex));
    }

    public void TriggerAutoPlayStart()
    {
        ChangeState(GameState.CrosswalkWalk);
        if (TimelineController != null) TimelineController.SetAutoPlay(true);
        InputManager.Instance.SendVibrate(VibeState.Walk);
    }

    public void TriggerAutoPlayEnd()
    {
        if (TimelineController != null) TimelineController.SetAutoPlay(false);
        ChangeState(GameState.NormalRiding);
        if (TimelineController != null) TimelineController.Resume();
    }

    public void TriggerDirectionHint(string direction)
    {
        Debug.Log($"[GameManager] Direction Hint: {direction}");
    }

    // ── 이벤트 루틴 ───────────────────────────────────────────────

    IEnumerator WarningStopRoutine()
    {
        ChangeState(GameState.EventWarning);
        if (TimelineController != null) TimelineController.Freeze();
        InputManager.Instance.SendVibrate(VibeState.Danger);

        yield return new WaitForSeconds(warningStopSec);

        ChangeState(GameState.NormalRiding);
        if (TimelineController != null) TimelineController.Resume();
    }

    IEnumerator BrakeEventRoutine()
    {
        ChangeState(GameState.EventBrake);
        if (TimelineController != null) TimelineController.Freeze();
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
        if (TimelineController != null) TimelineController.Resume();
    }

    IEnumerator OXQuizRoutine(int quizIndex)
    {
        ChangeState(GameState.OXQuiz);
        if (TimelineController != null) TimelineController.Freeze();

        if (QuizManager.Instance != null) QuizManager.Instance.StartQuiz(quizIndex + 1);

        if (quizIndex == finalQuizIndex && SceneManager.GetActiveScene().name == "Level2") // Level1에서 마지막 퀴즈일 경우
            yield break; // BlackBoard.EndingCountdown에서 OnTimelineComplete 호출

        yield return new WaitForSeconds(quizDurationSec);
        // ChangeState(GameState.NormalRiding);
        // if (TimelineController != null) TimelineController.Resume();
    }

    /// <summary>Timeline 재생이 끝났을 때 TimelineGameController에서 호출</summary>
    public void OnTimelineComplete(float delay = 5f)
    {
        // StopAllCoroutines();
        ChangeState(GameState.GameResult);
        if (TimelineController != null) TimelineController.Stop();

        StartCoroutine(FinalResultRoutine(delay));
    }

    private IEnumerator FinalResultRoutine(float delay)
    {
        yield return new WaitForSeconds(delay);

        InputManager.Instance.SendVibrate(VibeState.Success);
        if (resultObject != null) resultObject.SetActive(true);

        yield return new WaitForSeconds(finalResultWaitSec);

        if (endTransitionObject != null)
        {
            endTransitionObject.SetActive(true);
            if (endTransitionCanvasGroup != null)
            {
                float duration = 20f / 30f;
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

        float currentElapsed = finalResultWaitSec + (20f / 30f);
        if (totalEndWaitSec > currentElapsed)
            yield return new WaitForSeconds(totalEndWaitSec - currentElapsed);

        GoHome();
    }
}
