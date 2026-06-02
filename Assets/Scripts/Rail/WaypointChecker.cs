using UnityEngine;

/// <summary>
/// 영상 재생 시각을 모니터링하여 웨이포인트 이벤트를 발사.
/// VideoWaypointTable ScriptableObject에 정의된 triggerTime을 순서대로 체크.
/// </summary>
public class WaypointChecker : MonoBehaviour
{
    [SerializeField] VideoRailController  rail;
    [SerializeField] VideoWaypointTable   tableLevel1;
    [SerializeField] VideoWaypointTable   tableLevel2;

    VideoWaypointTable _activeTable;
    int  _nextIndex = 0;
    bool _active    = false;

    // ── API ───────────────────────────────────────────────────────
    public void StartChecking(int level)
    {
        _activeTable = level == 1 ? tableLevel1 : tableLevel2;
        _nextIndex   = 0;
        _active      = true;

        if (_activeTable == null)
            Debug.LogWarning($"[WaypointChecker] Level {level} 웨이포인트 테이블이 없습니다.");
    }

    public void StopChecking()
    {
        _active    = false;
        _nextIndex = 0;
    }

    // ── 업데이트 ──────────────────────────────────────────────────
    void Update()
    {
        if (!_active || _activeTable == null) return;
        if (GameManager.Instance.CurrentState != GameState.NormalRiding) return;
        if (_nextIndex >= _activeTable.waypoints.Count) return;

        var wp = _activeTable.waypoints[_nextIndex];
        if (rail.CurrentTime >= wp.triggerTime)
        {
            Debug.Log($"[WaypointChecker] 트리거: {wp.label} @ {wp.triggerTime:F1}s");
            GameManager.Instance.TriggerWaypoint(wp);
            _nextIndex++;
        }
    }
}
