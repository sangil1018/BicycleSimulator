using UnityEngine;

namespace TrafficSystem
{
    [System.Serializable]
    public struct PathBranch
    {
        [Tooltip("이 인덱스 웨이포인트 도달 시 분기 평가")]
        public int atWaypointIndex;
        [Tooltip("분기할 대상 경로 (우회전 경로 등)")]
        public WaypointPath targetPath;
        [Range(0f, 1f), Tooltip("분기 확률 (0=직진만, 1=항상 분기)")]
        public float probability;
    }

    /// <summary>
    /// Defines a sequence of waypoints for vehicle navigation.
    /// Assign child Transform objects as waypoints; order matters.
    /// </summary>
    public class WaypointPath : MonoBehaviour
    {
        [SerializeField] Transform[] waypoints;
        [SerializeField] bool loop = true;

        [Header("분기 설정 (우회전 등)")]
        [SerializeField] PathBranch[] branches;

        public bool Loop => loop;
        public int Count => waypoints == null ? 0 : waypoints.Length;

        public Transform GetWaypoint(int index)
        {
            if (waypoints == null || waypoints.Length == 0) return null;
            return waypoints[index % waypoints.Length];
        }

        /// <summary>
        /// 해당 웨이포인트 인덱스에 분기가 설정되어 있으면 반환한다.
        /// </summary>
        public bool TryGetBranch(int waypointIndex, out PathBranch branch)
        {
            if (branches != null)
                foreach (var b in branches)
                    if (b.atWaypointIndex == waypointIndex && b.targetPath != null)
                    {
                        branch = b;
                        return true;
                    }
            branch = default;
            return false;
        }

        // Populate waypoints automatically from all direct children (Editor helper)
        [ContextMenu("Collect Children as Waypoints")]
        void CollectChildren()
        {
            waypoints = new Transform[transform.childCount];
            for (int i = 0; i < transform.childCount; i++)
                waypoints[i] = transform.GetChild(i);
        }

#if UNITY_EDITOR
        void OnDrawGizmos()
        {
            if (waypoints == null || waypoints.Length < 2) return;

            Gizmos.color = Color.yellow;
            for (int i = 0; i < waypoints.Length - 1; i++)
            {
                if (waypoints[i] == null || waypoints[i + 1] == null) continue;
                Gizmos.DrawLine(waypoints[i].position, waypoints[i + 1].position);
                Gizmos.DrawSphere(waypoints[i].position, 0.3f);
            }

            var last = waypoints[waypoints.Length - 1];
            if (last != null)
            {
                Gizmos.DrawSphere(last.position, 0.3f);
                if (loop && waypoints[0] != null)
                    Gizmos.DrawLine(last.position, waypoints[0].position);
            }

            // 분기 경로 시각화 (청록색)
            if (branches != null)
            {
                Gizmos.color = Color.cyan;
                foreach (var b in branches)
                {
                    if (b.targetPath == null) continue;
                    var from = GetWaypoint(b.atWaypointIndex);
                    var to   = b.targetPath.GetWaypoint(0);
                    if (from != null && to != null)
                    {
                        Gizmos.DrawLine(from.position, to.position);
                        Gizmos.DrawSphere(from.position, 0.5f);
                    }
                }
            }
        }
#endif
    }
}
