using UnityEngine;

namespace TrafficSystem
{
    public class Waypoint : MonoBehaviour
    {
        [Tooltip("연결할 다음 웨이포인트.\n[0] 직진  [1] 우회전  [2] 좌회전\n" +
                 "연결된 슬롯만 아래 가중치로 정규화해 분배합니다.\n" +
                 "1개만 연결되면 해당 방향 100%.")]
        [SerializeField] Waypoint[] nextWaypoints;

        [Range(0f, 100f)]
        [Tooltip("직진(nextWaypoints[0]) 가중치 (%).")]
        [SerializeField] float straightWeight = 50f;

        [Range(0f, 100f)]
        [Tooltip("우회전(nextWaypoints[1]) 가중치 (%).")]
        [SerializeField] float rightWeight = 30f;

        [Range(0f, 100f)]
        [Tooltip("좌회전(nextWaypoints[2]) 가중치 (%).")]
        [SerializeField] float leftWeight = 20f;

        public Waypoint[] NextWaypoints => nextWaypoints;
        public float StraightWeight => straightWeight;
        public float RightWeight    => rightWeight;
        public float LeftWeight     => leftWeight;

        // index: 0=직진, 1=우회전, 2=좌회전
        // 연결된 슬롯의 가중치만 합산해 정규화 후 선택.
        // 예) 직진 없이 우(30)+좌(20)만 있으면 → 우 60%, 좌 40%
        public (Waypoint waypoint, int index) GetNextWithIndex()
        {
            if (nextWaypoints == null || nextWaypoints.Length == 0) return (null, 0);

            Waypoint straight = nextWaypoints.Length > 0 ? nextWaypoints[0] : null;
            Waypoint right    = nextWaypoints.Length > 1 ? nextWaypoints[1] : null;
            Waypoint left     = nextWaypoints.Length > 2 ? nextWaypoints[2] : null;

            float wS = straight != null ? straightWeight : 0f;
            float wR = right    != null ? rightWeight    : 0f;
            float wL = left     != null ? leftWeight     : 0f;
            float total = wS + wR + wL;

            if (total <= 0f)
            {
                // 가중치가 모두 0이면 연결된 것 중 균등 선택
                if (straight != null) return (straight, 0);
                if (right    != null) return (right,    1);
                if (left     != null) return (left,     2);
                return (null, 0);
            }

            float r = Random.value * total;
            if (straight != null) { r -= wS; if (r <= 0f) return (straight, 0); }
            if (right    != null) { r -= wR; if (r <= 0f) return (right,    1); }
            if (left     != null) { r -= wL; if (r <= 0f) return (left,     2); }

            // 부동소수점 오차 보정 — 마지막 연결된 슬롯 반환
            if (left     != null) return (left,     2);
            if (right    != null) return (right,    1);
            return (straight, 0);
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
