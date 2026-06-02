using System;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public enum CrosswalkStep { WaitO_Dismount, Walking, WaitO_Remount }

/// <summary>
/// 게임 플레이 중 UI 패널을 관리하는 매니저.
/// </summary>
public class UIManager : MonoBehaviour
{
    // ── 패널 ─────────────────────────────────────────────────────
    [Header("Panels")]
    [SerializeField] GameObject gameReadyPanel;
    [SerializeField] GameObject ridingHUD;
    [SerializeField] GameObject eventPanel;
    [SerializeField] GameObject oxQuizPanel;
    [SerializeField] GameObject crosswalkPanel;
    [SerializeField] GameObject resultPanel;
    [SerializeField] GameObject quitPopupPanel;
    [SerializeField] GameObject directionArrowPanel;

    // ── 이벤트 패널 ──────────────────────────────────────────────
    [Header("Event Panel")]
    [SerializeField] TextMeshProUGUI eventWarningText;
    [SerializeField] TextMeshProUGUI countdownText;
    [SerializeField] GameObject      brakeSuccessPanel;
    [SerializeField] GameObject      brakeFailPanel;

    // ── OX 퀴즈 ──────────────────────────────────────────────────
    [Header("OX Quiz Panel")]
    [SerializeField] TextMeshProUGUI quizQuestionText;
    [SerializeField] TextMeshProUGUI quizTimerText;
    [SerializeField] Image           oBtnImage;
    [SerializeField] Image           xBtnImage;
    [SerializeField] GameObject      oBtnPressedOverlay;
    [SerializeField] GameObject      xBtnPressedOverlay;
    [SerializeField] TextMeshProUGUI quizExplanationText;
    [SerializeField] TextMeshProUGUI quizScoreFeedback;

    // ── 횡단보도 ─────────────────────────────────────────────────
    [Header("Crosswalk")]
    [SerializeField] TextMeshProUGUI crosswalkGuideText;

    // ── 결과 화면 ────────────────────────────────────────────────
    [Header("Result")]
    [SerializeField] TextMeshProUGUI resultScoreText;
    [SerializeField] TextMeshProUGUI resultGradeText;
    [SerializeField] TextMeshProUGUI resultGradeDescText;

    // ── HUD ──────────────────────────────────────────────────────
    [Header("Riding HUD")]
    [SerializeField] TextMeshProUGUI hudScoreText;
    [SerializeField] TextMeshProUGUI hudButtonHintText;

    // ── 종료 팝업 ────────────────────────────────────────────────
    [Header("Quit Popup")]
    [SerializeField] Button quitConfirmBtn;
    [SerializeField] Button quitCancelBtn;

    // ── 방향 화살표 ───────────────────────────────────────────────
    [Header("Direction Arrow")]
    [SerializeField] TextMeshProUGUI directionArrowText;

    // ── 색상 ─────────────────────────────────────────────────────
    [Header("Colors")]
    [SerializeField] Color colorCorrect   = Color.green;
    [SerializeField] Color colorIncorrect = Color.gray;
    [SerializeField] Color colorNeutral   = Color.white;

    Coroutine _arrowRoutine;
    bool _quizResultShown;

    void Start()
    {
        InputManager.Instance.OnSelectionChanged += OnSelectionChanged;
    }

    void OnDestroy()
    {
        if (InputManager.Instance != null)
            InputManager.Instance.OnSelectionChanged -= OnSelectionChanged;
    }

    void OnSelectionChanged(bool? selection)
    {
        if (_quizResultShown) return;
        oBtnPressedOverlay?.SetActive(selection == true);
        xBtnPressedOverlay?.SetActive(selection == false);
    }

    void HideAll()
    {
        gameReadyPanel?.SetActive(false);
        ridingHUD?.SetActive(false);
        eventPanel?.SetActive(false);
        oxQuizPanel?.SetActive(false);
        crosswalkPanel?.SetActive(false);
        resultPanel?.SetActive(false);
    }

    public void ShowGameReady()
    {
        HideAll();
        gameReadyPanel?.SetActive(true);
        if (hudButtonHintText) hudButtonHintText.text = "버튼❶ : START   버튼❷ 2번 : HOME";
    }

    public void ShowRidingHUD()
    {
        HideAll();
        ridingHUD?.SetActive(true);
        UpdateHUDScore(0);
        if (hudButtonHintText) hudButtonHintText.text = "버튼❷ 2번 : HOME";
    }

    public void UpdateHUDScore(int score)
    {
        if (hudScoreText) hudScoreText.text = $"{score} / 80";
    }

    public void ShowEventWarning(string warningText)
    {
        eventPanel?.SetActive(true);
        if (eventWarningText) eventWarningText.text = warningText;
        brakeSuccessPanel?.SetActive(false);
        brakeFailPanel?.SetActive(false);
        if (countdownText) countdownText.gameObject.SetActive(true);
    }

    public void UpdateCountdown(int seconds)
    {
        if (countdownText) countdownText.text = seconds.ToString();
    }

    public void ShowBrakeResult(bool success, int pts)
    {
        if (countdownText) countdownText.gameObject.SetActive(false);
        brakeSuccessPanel?.SetActive(success);
        brakeFailPanel?.SetActive(!success);
    }

    public void HideEventUI() => eventPanel?.SetActive(false);

    public void ShowOXQuiz(QuizData quiz)
    {
        oxQuizPanel?.SetActive(true);
        if (quizQuestionText) quizQuestionText.text = quiz.question;
        if (quizExplanationText)
        {
            quizExplanationText.gameObject.SetActive(false);
            quizExplanationText.text = "";
        }
        if (quizScoreFeedback) quizScoreFeedback.gameObject.SetActive(false);
        _quizResultShown = false;
        if (oBtnImage) oBtnImage.color = colorNeutral;
        if (xBtnImage) xBtnImage.color = colorNeutral;
        oBtnPressedOverlay?.SetActive(false);
        xBtnPressedOverlay?.SetActive(false);
    }

    public void UpdateQuizTimer(int seconds)
    {
        if (quizTimerText) quizTimerText.text = seconds.ToString();
    }

    public void ShowQuizResult(bool? answer, bool correct, QuizData quiz, int pts)
    {
        _quizResultShown = true;
        bool answerO = quiz.correctAnswer;
        if (oBtnImage) oBtnImage.color = answerO ? colorCorrect : colorIncorrect;
        if (xBtnImage) xBtnImage.color = !answerO ? colorCorrect : colorIncorrect;
        if (answer.HasValue)
        {
            oBtnPressedOverlay?.SetActive(answer.Value);
            xBtnPressedOverlay?.SetActive(!answer.Value);
        }
        if (quizExplanationText)
        {
            quizExplanationText.gameObject.SetActive(true);
            quizExplanationText.text = quiz.explanation;
        }
        if (quizScoreFeedback)
        {
            quizScoreFeedback.gameObject.SetActive(true);
            quizScoreFeedback.text = correct ? $"+{pts}" : "0";
            quizScoreFeedback.color = correct ? colorCorrect : colorIncorrect;
        }
    }

    public void HideOXQuiz() => oxQuizPanel?.SetActive(false);

    public void ShowCrosswalkStep(CrosswalkStep step)
    {
        crosswalkPanel?.SetActive(true);
        if (crosswalkGuideText == null) return;
        crosswalkGuideText.text = step switch
        {
            CrosswalkStep.WaitO_Dismount => "잠시 후 좌회전합니다.\n횡단보도에서는 자전거에서 내려 끌고 이동하세요!\n\n❶버튼을 누르면 자전거에서 내려 이동합니다.",
            CrosswalkStep.Walking        => "횡단보도를 건너는 중...",
            CrosswalkStep.WaitO_Remount  => "반대편에 도착했습니다.\n❶버튼을 누르면 다시 자전거를 탈 수 있습니다.",
            _                            => "",
        };
    }

    public void HideCrosswalkUI() => crosswalkPanel?.SetActive(false);

    public void ShowResult(int score, string grade, string gradeDesc)
    {
        HideAll();
        resultPanel?.SetActive(true);
        if (resultScoreText)    resultScoreText.text    = $"{score} / 80점";
        if (resultGradeText)    resultGradeText.text    = grade;
        if (resultGradeDescText) resultGradeDescText.text = gradeDesc;
        if (hudButtonHintText) hudButtonHintText.text   = "버튼❷ : 처음으로";
    }

    public void ShowQuitPopup(Action confirmCallback, Action cancelCallback)
    {
        quitPopupPanel?.SetActive(true);
        quitConfirmBtn?.onClick.RemoveAllListeners();
        quitCancelBtn?.onClick.RemoveAllListeners();
        quitConfirmBtn?.onClick.AddListener(() => { quitPopupPanel.SetActive(false); confirmCallback?.Invoke(); });
        quitCancelBtn?.onClick.AddListener(() => { quitPopupPanel.SetActive(false); cancelCallback?.Invoke(); });
    }

    public void HideQuitPopup() => quitPopupPanel?.SetActive(false);

    public void ShowDirectionArrow(string direction, float duration)
    {
        if (_arrowRoutine != null) StopCoroutine(_arrowRoutine);
        _arrowRoutine = StartCoroutine(ArrowRoutine(direction, duration));
    }

    IEnumerator ArrowRoutine(string direction, float duration)
    {
        directionArrowPanel?.SetActive(true);
        if (directionArrowText) directionArrowText.text = direction;
        yield return new WaitForSeconds(duration);
        directionArrowPanel?.SetActive(false);
    }
}
