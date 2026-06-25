using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

public enum GameEventType
{
    BrakeEvent,
    WarningStop,
    CrosswalkStart,
    AutoPlayStart,
    AutoPlayEnd,
}

public class GameEventMarker : Marker, INotification, INotificationOptionProvider
{
    [SerializeField] private GameEventType eventType;
    [Tooltip("OXQuiz 타입일 때 사용 (0~3)")]
    [SerializeField] private int quizIndex;

    [Space(20)]
    [SerializeField] private bool retroactive = false;
    [SerializeField] private bool emitOnce = false;

    public PropertyName id => new PropertyName();
    public GameEventType EventType => eventType;
    public int QuizIndex => quizIndex;

    public NotificationFlags flags =>
        (retroactive ? NotificationFlags.Retroactive : default) |
        (emitOnce ? NotificationFlags.TriggerOnce : default);
}
