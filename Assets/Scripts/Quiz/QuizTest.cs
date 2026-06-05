using UnityEngine;

public class QuizTest : MonoBehaviour
{
#if TEST && UNITY_EDITOR
    [Header("TEST")]
    [SerializeField] private int testScore = 0;
    [SerializeField] private int testQuizNumber = 1;
#endif

    private void Awake()
    {
#if TEST && UNITY_EDITOR
        // 퀴즈 시작 시 1번 퀴즈로 초기화 테스트용.
        QuizManager.Instance.StartQuiz(testQuizNumber);
        for (int i = 0; i < testScore; i++)
        {
            QuizManager.Instance.SetQuizResult(true);
        }
#endif
    }
}
