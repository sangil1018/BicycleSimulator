using UnityEngine;

/// <summary>
/// 이벤트 UI 오브젝트에 부착.
/// OnEnable → _canMove = false, OnDisable → _canMove = true.
/// </summary>
public class TimelineEventGate : MonoBehaviour
{
    void OnEnable()
    {
        if (GameManager.Instance != null && GameManager.Instance.TimelineController != null)
            GameManager.Instance.TimelineController.Freeze();
    }

    void OnDisable()
    {
        if (GameManager.Instance != null && GameManager.Instance.TimelineController != null)
            GameManager.Instance.TimelineController.Resume();
    }
}
