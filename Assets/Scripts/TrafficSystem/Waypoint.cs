using UnityEngine;

namespace TrafficSystem
{
    public class Waypoint : MonoBehaviour
    {
        [Tooltip("연결할 다음 웨이포인트.\n[0] 직진  [1] 우회전  [2] 좌회전\n" +
                 "연결된 슬롯이 1개뿐이면 해당 방향 100%.\n" +
                 "2개 이상이면 아래 확률 필드를 사용합니다.")]
        [SerializeField] Waypoint[] nextWaypoints;

        [Range(0f, 100f)]
        [Tooltip("nextWaypoints[1](우회전) 선택 확률 (%).")]
        [SerializeField] float rightTurnChance = 30f;

        [Range(0f, 100f)]
        [Tooltip("nextWaypoints[2](좌회전) 선택 확률 (%).")]
        [SerializeField] float leftTurnChance = 20f;

        public Waypoint[] NextWaypoints => nextWaypoints;
        public float RightTurnChance => rightTurnChance;
        public float LeftTurnChance  => leftTurnChance;

        // index: 0=직진, 1=우회전, 2=좌회전
        public (Waypoint waypoint, int index) GetNextWithIndex()
        {
            if (nextWaypoints == null || nextWaypoints.Length == 0) return (null, 0);

            Waypoint straight = nextWaypoints.Length > 0 ? nextWaypoints[0] : null;
            Waypoint right    = nextWaypoints.Length > 1 ? nextWaypoints[1] : null;
            Waypoint left     = nextWaypoints.Length > 2 ? nextWaypoints[2] : null;

            bool hasS = straight != null;
            bool hasR = right    != null;
            bool hasL = left     != null;

            int count = (hasS ? 1 : 0) + (hasR ? 1 : 0) + (hasL ? 1 : 0);
            if (count == 0) return (null, 0);
            if (count == 1)
            {
                if (hasS) return (straight, 0);
                if (hasR) return (right, 1);
                return (left, 2);
            }

            float r      = Random.value * 100f;
            float cursor = 0f;
            if (hasR) { cursor += rightTurnChance; if (r < cursor) return (right, 1); }
            if (hasL) { cursor += leftTurnChance;  if (r < cursor) return (left,  2); }
            if (hasS) return (straight, 0);
            if (hasR) return (right, 1);
            return (left, 2);
        }

        public Waypoint GetNext() => GetNextWithIndex().waypoint;

        public void AutoOrient()
        {
            if (nextWaypoints == null || nextWaypoints.Length == 0 || nextWaypoints[0] == null) return;
            Vector3 dir = nextWaypoints[0].transform.position - transform.position;
            dir.y = 0f;
            if (dir.sqrMagnitude > 0.001f)
                transform.rotation = Quaternion.LookRotation(dir.normalized);
        }

#if UNITY_EDITOR
        void OnDrawGizmos()
        {
            if (nextWaypoints == null) return;
            for (int i = 0; i < nextWaypoints.Length; i++)
            {
                if (nextWaypoints[i] == null) continue;
                Gizmos.color = i == 0
                    ? new Color(1f, 0.85f, 0f, 0.8f)
                    : new Color(0f, 0.9f, 1f, 0.8f);
                Gizmos.DrawLine(transform.position, nextWaypoints[i].transform.position);
            }
            Gizmos.color = new Color(1f, 0.85f, 0f, 1f);
            Gizmos.DrawSphere(transform.position, 0.5f);
        }
#endif
    }
}
