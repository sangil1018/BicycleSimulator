using System.Collections;
using UnityEngine;
using UnityEngine.UI;

public class BlackBoard : MonoBehaviour
{
    [Header("Bell & Bike")]
    [SerializeField] private GameObject bell;
    [SerializeField] private GameObject bike;

    [Header("Score")]
    [SerializeField] private Animator scoreAnimator;

    [Header("Quiz Text")]
    [SerializeField] private GameObject quizText;
    [SerializeField] private Sprite[] quizSprites;
    [SerializeField] private Sprite[] answerSprites;

    [Header("O/X button")]
    [SerializeField] private Button oButton;
    [SerializeField] private Button xButton;
    [SerializeField] private AudioClip clickClip;

    [Header("Timing")]
    [SerializeField] private float countdownDuration = 10f;

    [Header("Answer Effect")]
    [SerializeField] private GameObject correctMark;
    [SerializeField] private GameObject wrongMark;
    [SerializeField] private GameObject correctScore;
    [SerializeField] private GameObject wrongScore;
    [SerializeField] private GameObject countDown;
    [SerializeField] private AudioClip correctSound;
    [SerializeField] private AudioClip wrongSound;
    [SerializeField] private AudioClip endingBellSound;
    [SerializeField] private AudioSource audioSource;

    private Image quizImage;
    private Animator oButtonAnimator;
    private Animator xButtonAnimator;

    [Header("Auto Result")]
    [SerializeField] private bool autoResult = false;

    private bool isAnswered = false;

    private void Awake()
    {
        quizImage = quizText.GetComponent<Image>();
        oButtonAnimator = oButton.GetComponent<Animator>();
        xButtonAnimator = xButton.GetComponent<Animator>();

        // 버튼 클릭 리스너 추가 (마우스 클릭 대응)
        oButton.onClick.AddListener(() => SubmitAnswer(true));
        xButton.onClick.AddListener(() => SubmitAnswer(false));
    }

    private void OnEnable()
    {
        // 입력 매니저 이벤트 연결
        if (InputManager.Instance != null)
        {
            InputManager.Instance.OnBtnO += OnBtnOClicked;
            InputManager.Instance.OnBtnX += OnBtnXClicked;
            InputManager.Instance.OnSelectionChanged += OnSelectionChanged;
        }

        isAnswered = false;

        // UI 요소 및 시각적 상태 초기화
        UpdateSelectionVisual(null);

        if (correctMark != null) correctMark.SetActive(false);
        if (wrongMark != null) wrongMark.SetActive(false);
        if (correctScore != null) correctScore.SetActive(false);
        if (wrongScore != null) wrongScore.SetActive(false);
        if (countDown != null) countDown.SetActive(false);
    }

    private void OnDisable()
    {
        // 이벤트 연결 해제
        if (InputManager.Instance != null)
        {
            InputManager.Instance.OnBtnO -= OnBtnOClicked;
            InputManager.Instance.OnBtnX -= OnBtnXClicked;
            InputManager.Instance.OnSelectionChanged -= OnSelectionChanged;
        }

        // StopAllCoroutines();

        // 모든 Animator 초기 상태로 리셋
        // if (scoreAnimator != null) { scoreAnimator.Rebind(); scoreAnimator.Update(0f); }
        // if (oButtonAnimator != null) { oButtonAnimator.Rebind(); oButtonAnimator.Update(0f); }
        // if (xButtonAnimator != null) { xButtonAnimator.Rebind(); xButtonAnimator.Update(0f); }

        // quizImage.sprite = quizSprites[0];
    }

    private void OnSelectionChanged(bool? isO)
    {
        if (isAnswered) return;

        // 키보드 방향키 이동 시 사운드 재생
        if (isO != null) PlayClickSound();

        UpdateSelectionVisual(isO);
    }

    private void PlayClickSound()
    {
        if (audioSource && clickClip)
        {
            audioSource.PlayOneShot(clickClip);
        }
    }

    /// <summary>
    /// 선택 상태에 따른 버튼 애니메이션 업데이트
    /// </summary>
    private void UpdateSelectionVisual(bool? isO)
    {
        if (isO == true)
        {
            // 키보드 좌버튼 (O 선택)
            if (oButtonAnimator != null) oButtonAnimator.SetTrigger("select");
            if (xButtonAnimator != null) xButtonAnimator.SetTrigger("normal");
        }
        else if (isO == false)
        {
            // 키보드 우버튼 (X 선택)
            if (xButtonAnimator != null) xButtonAnimator.SetTrigger("select");
            if (oButtonAnimator != null) oButtonAnimator.SetTrigger("normal");
        }
        else
        {
            // 선택 없음 또는 초기화 (null)
            if (oButtonAnimator != null)
            {
                foreach (AnimatorControllerParameter param in oButtonAnimator.parameters)
                {
                    if (param.type == AnimatorControllerParameterType.Trigger)
                    {
                        oButtonAnimator.ResetTrigger(param.name);
                    }
                }
            }
            if (xButtonAnimator != null)
            {
                foreach (AnimatorControllerParameter param in xButtonAnimator.parameters)
                {
                    if (param.type == AnimatorControllerParameterType.Trigger)
                    {
                        xButtonAnimator.ResetTrigger(param.name);
                    }
                }
            }
        }
    }

    private void OnBtnOClicked() => SubmitAnswer(true);
    private void OnBtnXClicked() => SubmitAnswer(false);

    /// <summary>
    /// 정답 제출 및 결과 처리
    /// </summary>
    private void SubmitAnswer(bool isO)
    {
        if (isAnswered) return;
        isAnswered = true;

        // 정답 판별 (o=true, x=false)
        var isCorrect = isO == QuizManager.Instance.CurrentQuizAnswer;

        // 애니메이터 트리거 실행
        if (isO)
        {
            if (oButtonAnimator != null) oButtonAnimator.SetTrigger(isCorrect ? "correct" : "wrong");
            if (xButtonAnimator != null) xButtonAnimator.SetTrigger("disable");
        }
        else
        {
            if (xButtonAnimator != null) xButtonAnimator.SetTrigger(isCorrect ? "correct" : "wrong");
            if (oButtonAnimator != null) oButtonAnimator.SetTrigger("disable");
        }

        // 결과 반응 연출
        if (isCorrect)
        {
            if (audioSource != null && correctSound != null) audioSource.PlayOneShot(correctSound);
            if (correctMark != null) correctMark.SetActive(true);
            if (correctScore != null) correctScore.SetActive(true);
            QuizManager.Instance.SetQuizResult(true);
        }
        else
        {
            if (audioSource != null && wrongSound != null) audioSource.PlayOneShot(wrongSound);
            if (wrongMark != null) wrongMark.SetActive(true);
            if (wrongScore != null) wrongScore.SetActive(true);
        }

        // 정답 이미지
        quizImage.sprite = answerSprites[QuizManager.Instance.CurrentQuizNumber];
        // 스코어 현재 점수 표시
        var triggerScoreName = $"cnt{QuizManager.Instance.CurrentQuizScore}";
        scoreAnimator.SetTrigger(triggerScoreName);


        // 카운트다운 활성화 및 타이머 시작
        if (countDown != null)
        {
            countDown.SetActive(true);
            StartCoroutine(EndingCountdown());
        }
    }

    /// <summary>
    /// 10초 카운트다운 후 퀴즈 종료
    /// </summary>
    private IEnumerator EndingCountdown()
    {
        yield return new WaitForSeconds(countdownDuration);

        if (audioSource != null && endingBellSound != null)
            audioSource.PlayOneShot(endingBellSound);

        bool isFinalQuiz = QuizManager.Instance != null
                        && GameManager.Instance != null
                        && QuizManager.Instance.CurrentQuizNumber == GameManager.Instance.FinalQuizIndex + 1;

        // 퀴즈 종료 후 타임라인 완료 자동 이벤트 호출 여부
        if (isFinalQuiz && autoResult) GameManager.Instance.OnTimelineComplete();

        GameManager.Instance.ChangeState(GameState.NormalRiding);
        GameManager.Instance.TimelineController.Resume();

        // 퀴즈 오브젝트 초기화
        if (correctScore != null) correctScore.SetActive(false);
        if (wrongScore != null) wrongScore.SetActive(false);
        // 모든 Animator 초기 상태로 리셋
        if (scoreAnimator != null) { scoreAnimator.Rebind(); scoreAnimator.Update(0f); }
        if (oButtonAnimator != null) { oButtonAnimator.Rebind(); oButtonAnimator.Update(0f); }
        if (xButtonAnimator != null) { xButtonAnimator.Rebind(); xButtonAnimator.Update(0f); }
        quizImage.sprite = quizSprites[0];

        transform.parent.parent.gameObject.SetActive(false);
    }

    public void BellAndBike()
    {
        bell.SetActive(true);
        bike.SetActive(true);
    }

    public void EnableQuiz()
    {
        // 스코어 현재 점수 표시
        var triggerScoreName = $"cnt{QuizManager.Instance.CurrentQuizScore}";
        scoreAnimator.SetTrigger(triggerScoreName);

        // 퀴즈 번호에 맞는 퀴즈 텍스트 표시
        quizImage.sprite = quizSprites[QuizManager.Instance.CurrentQuizNumber];
        quizText.SetActive(true);
    }
}
