using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

public enum DirectionType
{
    Normal,
    Left,
    Right,
    Right45,
}

public class DirectionMarker : Marker, INotification, INotificationOptionProvider
{
    [SerializeField] private DirectionType direction;

    [Space(20)]
    [SerializeField] private bool retroactive = false;
    [SerializeField] private bool emitOnce = false;

    public PropertyName id => new PropertyName();
    public DirectionType Direction => direction;

    public NotificationFlags flags =>
        (retroactive ? NotificationFlags.Retroactive : default) |
        (emitOnce ? NotificationFlags.TriggerOnce : default);
}
