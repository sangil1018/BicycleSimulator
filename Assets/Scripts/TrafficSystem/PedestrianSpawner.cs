using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace TrafficSystem
{
    public class PedestrianSpawner : MonoBehaviour
    {
        [Header("Prefabs")]
        [SerializeField] GameObject[] adultPrefabs;
        [SerializeField] GameObject[] childPrefabs;

        [Header("Walk Speed")]
        [SerializeField] float adultWalkSpeed = 1.4f;
        [SerializeField] float childWalkSpeed = 0.85f;

        [Header("Groups")]
        [SerializeField] PedestrianGroup[] groups;

        [Header("Performance")]
        [Tooltip("프레임당 최대 스폰 수 (0 = 일괄 스폰)")]
        [SerializeField] int spawnPerFrame = 5;

        struct SpawnPoint
        {
            public Vector3 position;
            public int     nextWpIndex;
        }

        void Start() => StartCoroutine(SpawnRoutine());

        IEnumerator SpawnRoutine()
        {
            if (groups == null || groups.Length == 0)
            {
                Debug.LogWarning("[PedestrianSpawner] groups가 설정되지 않았습니다.");
                yield break;
            }

            int spawnedThisFrame = 0;

            foreach (PedestrianGroup g in groups)
            {
                if (g == null || g.waypoints == null || g.waypoints.Length < 2) continue;

                float[] cum      = BuildCumLengths(g.waypoints);
                float   totalLen = cum[g.waypoints.Length - 1];
                if (totalLen < 0.1f) continue;

                int fwdTotal = g.adultsForward + g.childrenForward;
                int revTotal = g.adultsReverse + g.childrenReverse;

                // 경로 선분 위 랜덤 위치 샘플링 (층화 샘플링 - 구간별 1개 보장)
                List<SpawnPoint> fwd = SamplePath(g.waypoints, cum, totalLen, fwdTotal, false);
                List<SpawnPoint> rev = SamplePath(g.waypoints, cum, totalLen, revTotal, true);

                for (int i = 0; i < g.adultsForward; i++)
                {
                    Spawn(g, fwd[i], false, PedestrianType.Adult);
                    if (Yield(ref spawnedThisFrame)) yield return null;
                }
                for (int i = 0; i < g.childrenForward; i++)
                {
                    Spawn(g, fwd[g.adultsForward + i], false, PedestrianType.Child);
                    if (Yield(ref spawnedThisFrame)) yield return null;
                }
                for (int i = 0; i < g.adultsReverse; i++)
                {
                    Spawn(g, rev[i], true, PedestrianType.Adult);
                    if (Yield(ref spawnedThisFrame)) yield return null;
                }
                for (int i = 0; i < g.childrenReverse; i++)
                {
                    Spawn(g, rev[g.adultsReverse + i], true, PedestrianType.Child);
                    if (Yield(ref spawnedThisFrame)) yield return null;
                }
            }
        }

        // 경로 전체 길이를 count 구간으로 나눠 각 구간 내 랜덤 위치 샘플링
        // 오프셋은 PedestrianController가 이동 방향 기준으로 동적 적용하므로 여기선 중심선 위치만 반환
        static List<SpawnPoint> SamplePath(Transform[] wps, float[] cum, float totalLen,
                                           int count, bool reverse)
        {
            var list      = new List<SpawnPoint>(count);
            if (count <= 0) return list;

            float slotSize = totalLen / count;

            for (int i = 0; i < count; i++)
            {
                float dist = Random.Range(i * slotSize, (i + 1) * slotSize);
                (Vector3 pos, _, int nextIdx) = EvalPath(wps, cum, dist, reverse);
                list.Add(new SpawnPoint { position = pos, nextWpIndex = nextIdx });
            }
            return list;
        }

        // 누적 거리 배열 생성: cum[0]=0, cum[i] = 0번~i번 세그먼트 합산 길이
        static float[] BuildCumLengths(Transform[] wps)
        {
            float[] cum = new float[wps.Length];
            cum[0] = 0f;
            for (int i = 1; i < wps.Length; i++)
                cum[i] = cum[i - 1] + Vector3.Distance(wps[i - 1].position, wps[i].position);
            return cum;
        }

        // dist 거리에서의 경로 위치 / 세그먼트 방향 / 다음 웨이포인트 인덱스 반환
        static (Vector3 pos, Vector3 segDir, int nextIdx) EvalPath(
            Transform[] wps, float[] cum, float dist, bool reverse)
        {
            dist = Mathf.Clamp(dist, 0f, cum[wps.Length - 1]);

            for (int i = 1; i < wps.Length; i++)
            {
                if (dist <= cum[i] || i == wps.Length - 1)
                {
                    float segLen = cum[i] - cum[i - 1];
                    float t      = segLen > 0f ? (dist - cum[i - 1]) / segLen : 0f;
                    Vector3 pos  = Vector3.Lerp(wps[i - 1].position, wps[i].position, t);
                    Vector3 dir  = (wps[i].position - wps[i - 1].position).normalized;
                    // 역방향은 낮은 인덱스로, 정방향은 높은 인덱스로 이동
                    int nextIdx  = reverse ? Mathf.Max(i - 1, 0) : Mathf.Min(i, wps.Length - 1);
                    return (pos, dir, nextIdx);
                }
            }

            int last       = wps.Length - 1;
            Vector3 endDir = (wps[last].position - wps[last - 1].position).normalized;
            return (wps[last].position, endDir, reverse ? Mathf.Max(last - 1, 0) : last);
        }

        void Spawn(PedestrianGroup g, SpawnPoint sp, bool reverse, PedestrianType type)
        {
            GameObject[] prefabs = type == PedestrianType.Adult ? adultPrefabs : childPrefabs;
            if (prefabs == null || prefabs.Length == 0)
            {
                Debug.LogWarning($"[PedestrianSpawner] {type} 프리팹이 없습니다.");
                return;
            }

            GameObject prefab = prefabs[Random.Range(0, prefabs.Length)];
            if (prefab == null) return;

            float walkSpeed = type == PedestrianType.Adult ? adultWalkSpeed : childWalkSpeed;

            GameObject ped = Instantiate(prefab, sp.position, Quaternion.identity, transform);
            ped.name = $"Ped_{type}_{(reverse ? "L" : "R")}_{sp.nextWpIndex:D2}";

            if (ped.TryGetComponent(out PedestrianController ctrl))
                ctrl.Init(g.waypoints, sp.position, sp.nextWpIndex, reverse, walkSpeed, g.lateralOffset, g.blendRadius);
            else
                Debug.LogWarning($"[PedestrianSpawner] {prefab.name}에 PedestrianController가 없습니다.");
        }

        bool Yield(ref int count)
        {
            if (spawnPerFrame <= 0) return false;
            if (++count >= spawnPerFrame) { count = 0; return true; }
            return false;
        }
    }

    [System.Serializable]
    public class PedestrianGroup
    {
        [Tooltip("그룹 식별 이름 (에디터 정리용)")]
        public string label = "Group";

        [Tooltip("연속된 웨이포인트. 정방향(우측) = index 0→N, 역방향(좌측) = N→0.")]
        public Transform[] waypoints;

        [Header("Spawn Count")]
        [Tooltip("우측(정방향) 어른 수")]
        public int adultsForward   = 2;
        [Tooltip("좌측(역방향) 어른 수")]
        public int adultsReverse   = 2;
        [Tooltip("우측(정방향) 아이 수")]
        public int childrenForward = 1;
        [Tooltip("좌측(역방향) 아이 수")]
        public int childrenReverse = 1;

        [Header("Spacing")]
        [Tooltip("웨이포인트 중심선에서 좌우로 떨어지는 거리 (m). 0 = 중심선 위.")]
        [Min(0f)]
        public float lateralOffset = 0.5f;

        [Header("Path Smoothing")]
        [Tooltip("중간 웨이포인트 앞뒤로 Bezier 보간을 적용할 반경 (m). 0 = 직선만 사용.")]
        [Min(0f)]
        public float blendRadius = 1f;
    }
}
