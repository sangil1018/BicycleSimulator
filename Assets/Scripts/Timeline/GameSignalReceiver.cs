using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

/// <summary>
/// PlayableDirector와 같은 오브젝트에 추가.
/// Timeline Signal Track의 신호를 받아 GameManager / SpeedUIController 이벤트를 호출한다.
/// </summary>
public class GameSignalReceiver : MonoBehaviour, INotificationReceiver
{
    [SerializeField] SpeedUIController speedUIController;

    public void OnNotify(Playable origin, INotification notification, object context)
    {
        switch (notification)
        {
            case GameEventSignal gameSignal:
                HandleGameEvent(gameSignal);
                break;

            case DirectionSignal dirSignal:
                speedUIController?.SetDirection(dirSignal.direction);
                break;
        }
    }

    void HandleGameEvent(GameEventSignal signal)
    {
        var gm = GameManager.Instance;
        if (gm == null) return;

        switch (signal.eventType)
        {
            case GameEventType.BrakeEvent:
                gm.TriggerBrakeEvent();
                break;
            case GameEventType.OXQuiz:
                gm.TriggerOXQuiz(signal.quizIndex);
                break;
            case GameEventType.CrosswalkStart:
                gm.TriggerCrosswalk();
                break;
            case GameEventType.AutoPlayStart:
                gm.TimelineController?.SetAutoPlay(true);
                break;
            case GameEventType.AutoPlayEnd:
                gm.TimelineController?.SetAutoPlay(false);
                break;
        }
    }
}
