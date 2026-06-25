using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

public class QuizMarker : Marker, INotification, INotificationOptionProvider
{
    [Tooltip("퀴즈 인덱스 (0~3)")]
    [SerializeField] private int quizIndex;

    [Space(20)]
    [SerializeField] private bool retroactive = false;
    [SerializeField] private bool emitOnce = false;

    public PropertyName id => new PropertyName();
    public int QuizIndex => quizIndex;

    public NotificationFlags flags =>
        (retroactive ? NotificationFlags.Retroactive : default) |
        (emitOnce ? NotificationFlags.TriggerOnce : default);
}
