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
    OXQuiz,
    CrosswalkWalk,
    GameResult,
}

/// <summary>
/// 게임의 전체적인 흐름을 제어하는 매니저 클래스.
/// Timeline 기반 시스템으로 전환됨 (VideoRailController → TimelineGameController).
/// </summary>
public class GameManager : Singleton<GameManager>
{
    [Header("Exit Menu")]
    [SerializeField] private GameObject exit_menu;

    [Header("Final Result UI")]
    [SerializeField] private GameObject resultObject;
    [SerializeField] private GameObject endTransitionObject;
    [SerializeField] private CanvasGroup endTransitionCanvasGroup;

    [Header("Timing")]
    [SerializeField] float brakeEventSec    = 9f;
    [SerializeField] float resultDisplaySec = 3f;
    [SerializeField] float crosswalkSec     = 4f;
    [SerializeField] float quizDurationSec  = 12f;
    [SerializeField] float finalResultWaitSec = 13f;
    [SerializeField] float totalEndWaitSec    = 15f;

    // ── 공개 상태 ──────────────────────────────────────────────────

    public GameState CurrentState { get; private set; } = GameState.Boot;
    public int Level { get; private set; } = 1;
    public bool CanMove { get; private set; } = false;

    public int EventScore { get; private set; }
    public int QuizScore  => QuizManager.Instance?.CurrentQuizScore ?? 0;
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
        InputManager.Instance.OnBtnX += HandleXButton;

        if (SceneManager.GetActiveScene().name.StartsWith("Level"))
            InitLevelScene();
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
            CurrentState == GameState.Home   ||
            CurrentState == GameState.Boot) return;

        if (exit_menu != null) exit_menu.SetActive(true);
    }

    public void GoHome()
    {
        StopAllCoroutines();
        EventScore = 0;
        QuizManager.Instance?.ResetScore();
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
            Debug.LogWarning("[GameManager] IntroManager를 찾을 수 없습니다.");
    }

    public void OnIntroFinished()
    {
        ChangeState(GameState.GameReady);
    }

    /// <summary>시작 메뉴 UI에서 "시작(O)"을 눌렀을 때 호출</summary>
    public void StartRiding()
    {
        if (CurrentState != GameState.GameReady) return;

        ChangeState(GameState.NormalRiding);
        TimelineController?.Play();
    }

    // ── 이벤트 트리거 (GameSignalReceiver에서 호출) ────────────────

    public void TriggerBrakeEvent()
    {
        StartCoroutine(BrakeEventRoutine());
    }

    public void TriggerOXQuiz(int quizIndex)
    {
        StartCoroutine(OXQuizRoutine(quizIndex));
    }

    public void TriggerCrosswalk()
    {
        StartCoroutine(CrosswalkRoutine());
    }

    public void TriggerDirectionHint(string direction)
    {
        Debug.Log($"[GameManager] Direction Hint: {direction}");
    }

    // ── 이벤트 루틴 ───────────────────────────────────────────────

    IEnumerator BrakeEventRoutine()
    {
        ChangeState(GameState.EventBrake);
        TimelineController?.Freeze();
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
        TimelineController?.Resume();
    }

    IEnumerator OXQuizRoutine(int quizIndex)
    {
        ChangeState(GameState.OXQuiz);
        TimelineController?.Freeze();

        QuizManager.Instance?.StartQuiz(quizIndex + 1);

        yield return new WaitForSeconds(quizDurationSec);

        ChangeState(GameState.NormalRiding);
        TimelineController?.Resume();
    }

    IEnumerator CrosswalkRoutine()
    {
        ChangeState(GameState.CrosswalkWalk);
        TimelineController?.SetAutoPlay(true);
        InputManager.Instance.SendVibrate(VibeState.Walk);

        yield return WaitForOButton(); // 내리기
        yield return new WaitForSeconds(crosswalkSec); // 걷기
        yield return WaitForOButton(); // 다시 타기

        TimelineController?.SetAutoPlay(false);
        ChangeState(GameState.NormalRiding);
    }

    IEnumerator WaitForOButton()
    {
        bool pressed = false;
        Action onO = () => pressed = true;
        InputManager.Instance.OnBtnO += onO;
        yield return new WaitUntil(() => pressed);
        InputManager.Instance.OnBtnO -= onO;
    }

    /// <summary>Timeline 재생이 끝났을 때 TimelineGameController에서 호출</summary>
    public void OnTimelineComplete()
    {
        StopAllCoroutines();
        ChangeState(GameState.GameResult);
        TimelineController?.Stop();
        InputManager.Instance.SendVibrate(VibeState.Success);

        ShowFinalResult();
    }

    public void ShowFinalResult()
    {
        if (resultObject != null) resultObject.SetActive(true);
        StartCoroutine(FinalResultRoutine());
    }

    private IEnumerator FinalResultRoutine()
    {
        yield return new WaitForSeconds(finalResultWaitSec);

        if (endTransitionObject != null)
        {
            endTransitionObject.SetActive(true);
            if (endTransitionCanvasGroup != null)
            {
                float duration = 20f / 30f;
                float elapsed  = 0f;
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
