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
    OXQuiz,
    CrosswalkWalk,
    GameResult,
}

public class GameManager : Singleton<GameManager>
{
    [Header("References")]
    [SerializeField] QuizDatabase quizDB;

    // 씬 전환 후 다시 찾아야 할 참조들
    private VideoRailController rail;
    private WaypointChecker waypointChecker;
    private UIManager ui;
    private IntroManager intro;

    [Header("Timing")]
    [SerializeField] float brakeEventSec = 9f;
    [SerializeField] float quizSec = 10f;
    [SerializeField] float resultDisplaySec = 3f;
    [SerializeField] float crosswalkSec = 4f;
    [SerializeField] float xDoublePressSec = 3f;

    public GameState CurrentState { get; private set; } = GameState.Boot;
    public int Level { get; private set; } = 1;
    public bool CanMove { get; private set; } = false;

    public int EventScore { get; private set; }
    public int QuizScore { get; private set; }
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
            // 에디터에서 레벨 씬을 직접 실행했을 때를 위한 초기화
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
        // Level 씬인 경우 컴포넌트 찾기
        rail = FindFirstObjectByType<VideoRailController>();
        waypointChecker = FindFirstObjectByType<WaypointChecker>();
        ui = FindFirstObjectByType<UIManager>();

        if (rail && ui)
        {
            StartLevelIntro();
        }
        else
        {
            Debug.LogWarning($"[GameManager] Required components missing: rail={rail != null}, ui={ui != null}");
        }
    }

    public void ChangeState(GameState next)
    {
        CanMove = (next == GameState.NormalRiding);
        CurrentState = next;
        Debug.Log($"[GameManager] ◀ {next} ▶");
    }

    void HandleXButton()
    {
        if (CurrentState == GameState.OXQuiz) return;
        if (CurrentState == GameState.Home || CurrentState == GameState.Boot) return;
        if (CurrentState == GameState.GameResult) { GoHome(); return; }

        _xPressCount++;
        if (_xResetRoutine != null) StopCoroutine(_xResetRoutine);

        if (_xPressCount >= 2)
        {
            _xPressCount = 0;
            ui?.HideQuitPopup();
            GoHome();
        }
        else
        {
            ui?.ShowQuitPopup(
                confirmCallback: () => { _xPressCount = 0; GoHome(); },
                cancelCallback: () => { _xPressCount = 0; });
            _xResetRoutine = StartCoroutine(ResetXCount());
        }
    }

    IEnumerator ResetXCount()
    {
        yield return new WaitForSeconds(xDoublePressSec);
        _xPressCount = 0;
        ui?.HideQuitPopup();
    }

    public void GoHome()
    {
        StopAllCoroutines();
        EventScore = QuizScore = 0;
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
        
        // 더 이상 여기서 IntroManager를 직접 호출하지 않음.
        // 각 씬의 IntroManager가 Start()에서 스스로 시작하고,
        // 종료 시 OnIntroFinished()를 호출해줄 것임.
    }

    public void OnIntroFinished()
    {
        ChangeState(GameState.GameReady);
        ui.ShowGameReady();
        InputManager.Instance.OnBtnO += OnGameReadyOPressed;
    }

    void OnGameReadyOPressed()
    {
        InputManager.Instance.OnBtnO -= OnGameReadyOPressed;
        ChangeState(GameState.NormalRiding);
        InputManager.Instance.SendRgbState(1);
        rail.Play();
        waypointChecker.StartChecking(Level);
        ui.ShowRidingHUD();
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
                ui.ShowDirectionArrow(wp.label, 3f);
                break;
        }
    }

    IEnumerator BrakeEventRoutine(VideoWaypoint wp)
    {
        ChangeState(GameState.EventBrake);
        rail.Freeze();
        ui.ShowEventWarning(wp.label);
        InputManager.Instance.SendVibrate(1);
        InputManager.Instance.SendRgbState(2);

        bool braked = false;
        float timer = brakeEventSec;

        while (timer > 0f)
        {
            if (InputManager.Instance.BrakeAny) { braked = true; break; }
            timer -= Time.deltaTime;
            ui.UpdateCountdown(Mathf.CeilToInt(timer));
            yield return null;
        }

        int pts = braked ? 10 : 0;
        EventScore += pts;
        if (braked) InputManager.Instance.SendVibrate(2);

        ui.ShowBrakeResult(braked, pts);
        yield return new WaitForSeconds(resultDisplaySec);
        ui.HideEventUI();

        ChangeState(GameState.NormalRiding);
        InputManager.Instance.SendRgbState(1);
        rail.Resume();
    }

    IEnumerator OXQuizRoutine(VideoWaypoint wp)
    {
        ChangeState(GameState.OXQuiz);
        rail.Freeze();

        var quizList = Level == 1 ? quizDB.beginnerQuizzes : quizDB.advancedQuizzes;
        int idx = Mathf.Clamp(wp.quizIndex, 0, quizList.Count - 1);
        var quiz = quizList[idx];

        ui.ShowOXQuiz(quiz);
        InputManager.Instance.SendRgbState(3);

        bool? answer = null;
        bool answered = false;
        float timer = quizSec;

        Action onO = () => { answer = true; answered = true; };
        Action onX = () => { answer = false; answered = true; };

        InputManager.Instance.OnBtnO += onO;
        InputManager.Instance.OnBtnX += onX;

        while (!answered && timer > 0f)
        {
            timer -= Time.deltaTime;
            ui.UpdateQuizTimer(Mathf.CeilToInt(timer));
            yield return null;
        }

        InputManager.Instance.OnBtnO -= onO;
        InputManager.Instance.OnBtnX -= onX;

        bool correct = answer.HasValue && (answer.Value == quiz.correctAnswer);
        int pts = correct ? 10 : 0;
        QuizScore += pts;

        InputManager.Instance.SendVibrate(correct ? 3 : 4);
        ui.ShowQuizResult(answer, correct, quiz, pts);
        ui.UpdateHUDScore(TotalScore);
        yield return new WaitForSeconds(resultDisplaySec);
        ui.HideOXQuiz();

        ChangeState(GameState.NormalRiding);
        InputManager.Instance.SendRgbState(1);
        rail.Resume();
    }

    IEnumerator CrosswalkRoutine()
    {
        ChangeState(GameState.CrosswalkWalk);
        rail.Freeze();
        InputManager.Instance.SendVibrate(5);
        InputManager.Instance.SendRgbState(2);

        ui.ShowCrosswalkStep(CrosswalkStep.WaitO_Dismount);
        yield return WaitForOButton();

        ui.ShowCrosswalkStep(CrosswalkStep.Walking);
        yield return new WaitForSeconds(crosswalkSec);

        ui.ShowCrosswalkStep(CrosswalkStep.WaitO_Remount);
        yield return WaitForOButton();

        ui.HideCrosswalkUI();
        ChangeState(GameState.NormalRiding);
        InputManager.Instance.SendRgbState(1);
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
        InputManager.Instance.SendVibrate(2);
        InputManager.Instance.SendRgbState(0);
        ui.ShowResult(TotalScore, GetGrade(TotalScore), GetGradeDesc(TotalScore));
    }

    static string GetGrade(int score) => score switch
    {
        80 => "그랜드 마스터",
        >= 60 => "프로라이더",
        >= 40 => "위험한 초보",
        _ => "걸어다니세요",
    };

    static string GetGradeDesc(int score) => score switch
    {
        80 => "도로 위의 신사, 사고확률 0%!\n완벽한 라이더!",
        >= 60 => "안전지식이 우수합니다!\n사소한 습관만 고치면 완벽!",
        >= 40 => "언제 사고가 나도 이상하지 않음\n헬멧부터 다시 쓰세요!",
        _ => "본인뿐만 아니라 타인에게도 위험\n기초교육 필수!",
    };
}
