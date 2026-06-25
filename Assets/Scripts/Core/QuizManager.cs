using UnityEngine;

/// <summary>
/// 퀴즈의 상태와 데이터를 전역적으로 관리하는 매니저 클래스.
/// UI 로직은 GameManager로 이관되었습니다.
/// </summary>
public class QuizManager : Singleton<QuizManager>
{
    [Header("Quiz State")]
    // 현재 퀴즈에서 얻은 점수
    public int CurrentQuizScore { get; private set; }

    // 현재 진행 중인 퀴즈 데이터
    public int CurrentQuizNumber { get; private set; }
    public bool CurrentQuizAnswer { get; private set; }

    [SerializeField] private bool[] answerList = { false, false, true, true };

    [SerializeField] private GameObject quizUI; // 퀴즈 UI 오브젝트

    public void InitialQuizUI()
    {
        if (quizUI != null)
        {
            quizUI.SetActive(false);
        }
    }

    /// <summary>
    /// 퀴즈 시작 시 데이터 설정
    /// </summary>
    /// <param name="number">표시할 퀴즈 데이터</param>
    public void StartQuiz(int number)
    {
        CurrentQuizNumber = number;
        CurrentQuizAnswer = answerList[number - 1];
        Debug.Log($"[QuizManager] 퀴즈 시작: 퀴즈 {CurrentQuizNumber}, 정답: {CurrentQuizAnswer}");
        if (quizUI != null)
        {
            quizUI.SetActive(true);
        }
    }

    /// <summary>
    /// 정답 처리 및 점수 합산
    /// </summary>
    /// <param name="isCorrect">정답 여부</param>
    public void SetQuizResult(bool isCorrect)
    {
        if (isCorrect)
        {
            CurrentQuizScore++;
        }

        Debug.Log($"[QuizManager] 결과 처리 - 정답: {isCorrect}, 누적 점수: {CurrentQuizScore}");
    }

    /// <summary>
    /// 게임 리셋 시 또는 홈으로 이동 시 데이터 초기화
    /// </summary>
    public void ResetScore()
    {
        CurrentQuizScore = 0;
        CurrentQuizNumber = 0;
        Debug.Log("[QuizManager] 데이터 초기화 완료");
    }
}
