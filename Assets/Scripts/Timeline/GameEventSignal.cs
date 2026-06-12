using UnityEngine;
using UnityEngine.Timeline;
using UnityEngine.Playables;

public enum GameEventType
{
    BrakeEvent,
    OXQuiz,
    CrosswalkStart,
    AutoPlayStart,
    AutoPlayEnd,
}

/// <summary>
/// Timeline Signal Track에 배치할 게임 이벤트 시그널 에셋.
/// Inspector에서 eventType과 quizIndex를 설정한다.
/// </summary>
[System.Serializable]
public class GameEventSignal : ScriptableObject, INotification
{
    public PropertyName id => new PropertyName(name);

    public GameEventType eventType;
    [Tooltip("OXQuiz 타입일 때 사용 (0~3)")]
    public int quizIndex;
}
