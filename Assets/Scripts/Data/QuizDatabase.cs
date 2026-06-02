using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public class QuizData
{
    [TextArea(2, 4)]
    public string question;

    [Tooltip("true = O 정답, false = X 정답")]
    public bool correctAnswer;

    [TextArea(2, 5)]
    public string explanation;
}

/// <summary>
/// 초급/고급 OX 퀴즈 데이터베이스 ScriptableObject.
/// 생성: Project 패널 우클릭 → Create → BicycleSim → Quiz Database
/// </summary>
[CreateAssetMenu(fileName = "QuizDatabase",
                 menuName  = "BicycleSim/Quiz Database")]
public class QuizDatabase : ScriptableObject
{
    [Header("초급 퀴즈 (4문제 — 인덱스 0~3)")]
    public List<QuizData> beginnerQuizzes = new()
    {
        new QuizData
        {
            question      = "횡단보도에서는 자전거를 타고 건너도 된다.",
            correctAnswer = false,
            explanation   = "자전거는 법적으로 '차'에 해당하므로 횡단보도를 타고 건널 수 없으며,\n내려서 끌고 갈 때만 보행자로 인정됩니다.",
        },
        new QuizData
        {
            question      = "자전거도로가 없는 곳에서는 인도 통행이 가능하다.",
            correctAnswer = false,
            explanation   = "자전거도로가 없는 곳에서는 도로 우측 가장자리를 이용해야 합니다.\n(예외: 13세 미만, 65세 이상, 신체 장애인은 보도 통행 가능)",
        },
        new QuizData
        {
            question      = "자전거는 자동차와 같은 방향으로 도로의 오른쪽으로 주행해야 한다.",
            correctAnswer = true,
            explanation   = "자전거는 차에 해당하므로 차도에서 도로 오른쪽 가장자리로\n자동차와 같은 방향으로 주행해야 합니다.",
        },
        new QuizData
        {
            question      = "야간에는 밝은 색 옷이 더 잘 보인다.",
            correctAnswer = true,
            explanation   = "밝은 색 옷은 어두운 환경에서도 시인성이 높아\n다른 운전자나 보행자가 자전거를 쉽게 인식할 수 있습니다.",
        },
    };

    [Header("고급 퀴즈 (4문제 — 인덱스 0~3)")]
    public List<QuizData> advancedQuizzes = new()
    {
        new QuizData
        {
            question      = "자전거는 도로교통법상 '차'에 해당한다.",
            correctAnswer = true,
            explanation   = "도로교통법에서는 자전거를 '차'의 한 종류로 규정하고 있어\n자전거 운전자도 차량 운전자와 동일하게 교통법규를 지켜야 합니다.",
        },
        new QuizData
        {
            question      = "자전거를 타고 스마트폰을 조작하면 도로교통법 위반이다.",
            correctAnswer = true,
            explanation   = "자전거도 운전에 해당하므로 주의 의무가 있으며,\n주행 중 스마트폰을 조작하는 행위는 안전운전 의무 위반에 해당합니다.",
        },
        new QuizData
        {
            question      = "자전거 사고 시 12대 중과실 사고는 적용되지 않는다.",
            correctAnswer = false,
            explanation   = "자전거도 차에 해당하므로 신호위반, 중앙선 침범 등\n12대 중과실에 해당하는 경우 적용될 수 있습니다.",
        },
        new QuizData
        {
            question      = "음주 후 자전거를 타는 것은 처벌 대상이 아니다.",
            correctAnswer = false,
            explanation   = "자전거도 도로교통법상 '차'이므로\n음주 후 운전하면 범칙금 등의 처벌을 받을 수 있습니다.",
        },
    };
}
