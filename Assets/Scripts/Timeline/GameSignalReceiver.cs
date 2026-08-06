using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

/// <summary>
/// PlayableDirector와 같은 오브젝트에 추가.
/// - GameEvent : 내장 SignalReceiver의 UnityEvent → 아래 public 메서드 호출
/// - Quiz : 커스텀 마커로 직접 수신
/// GameTime 모드에서만 정상 동작.
/// </summary>
public class GameSignalReceiver : MonoBehaviour, INotificationReceiver
{
    // ── INotificationReceiver ──────────────────────────────────

    public void OnNotify(Playable origin, INotification notification, object context)
    {
        if (notification is QuizMarker quiz)
            HandleQuiz(quiz);
    }

    // ── Game Event public API (SignalReceiver → UnityEvent 연결용) ──

    public void TriggerBrakeEvent()
    {
        Debug.Log("[GSR] BrakeEvent");
        if (GameManager.Instance != null) GameManager.Instance.TriggerBrakeEvent();
    }

    public void TriggerWarningStop()
    {
        Debug.Log("[GSR] WarningStop");
        if (GameManager.Instance != null) GameManager.Instance.TriggerWarningStop();
    }

    public void TriggerBicycleStop()
    {
        Debug.Log("[GSR] BicycleStop");
        if (GameManager.Instance != null) GameManager.Instance.TriggerBicycleStop();
    }

    public void TriggerAutoPlayStart()
    {
        Debug.Log("[GSR] AutoPlayStart");
        if (GameManager.Instance != null) GameManager.Instance.TriggerAutoPlayStart();
    }

    public void TriggerAutoPlayEnd()
    {
        Debug.Log("[GSR] AutoPlayEnd");
        if (GameManager.Instance != null) GameManager.Instance.TriggerAutoPlayEnd();
    }

    // ── 커스텀 마커 처리 ───────────────────────────────────────

    void HandleQuiz(QuizMarker marker)
    {
        Debug.Log($"[GSR] OXQuiz [{marker.QuizIndex}]");
        if (GameManager.Instance != null) GameManager.Instance.TriggerOXQuiz(marker.QuizIndex);
    }
}
