using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

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

        // Scale Variation — 프리팹 원본 스케일에 곱해지는 랜덤 배율 범위.
        // 라벨/헤더는 PedestrianSpawnerEditor가 직접 그리므로 속성을 달지 않는다.
        [SerializeField] float adultScaleMin = 0.95f;
        [SerializeField] float adultScaleMax = 1.05f;
        [SerializeField] float childScaleMin = 0.9f;
        [SerializeField] float childScaleMax = 1.1f;

        [Header("Groups")]
        [SerializeField] PedestrianGroup[] groups;

        [Header("Performance")]
        [Tooltip("프레임당 최대 스폰 수 (0 = 일괄 스폰)")]
        [SerializeField] int spawnPerFrame = 5;

        struct SpawnPoint
        {
            public Vector3 position;
            public int nextWpIndex;
        }

        void Start() => StartCoroutine(SpawnRoutine());

        IEnumerator SpawnRoutine()
        {
            if (groups == null || groups.Length == 0)
            {
                Debug.LogWarning("[PedestrianSpawner] groups가 설정되지 않았습니다.");
                yield break;
            }

            // 프리팹 유효성은 여기서 1회만 검사·로그하고, 없으면 해당 타입만 조용히 건너뜀
            GameObject[] validAdults = FilterValidPrefabs(adultPrefabs, "Adult");
            GameObject[] validChildren = FilterValidPrefabs(childPrefabs, "Child");

            int spawnedThisFrame = 0;

            foreach (PedestrianGroup g in groups)
            {
                if (g == null || g.waypoints == null || g.waypoints.Length < 2) continue;

                if (HasNullWaypoint(g.waypoints))
                {
                    Debug.LogWarning($"[PedestrianSpawner] Group \"{g.label}\" waypoints에 null 요소가 있어 건너뜁니다.");
                    continue;
                }

                if (!g.right && !g.left)
                {
                    Debug.LogWarning($"[PedestrianSpawner] Group \"{g.label}\" 좌/우 방향이 모두 꺼져 있어 스폰하지 않습니다.");
                    continue;
                }

                float[] cum = BuildCumLengths(g.waypoints);
                float totalLen = cum[g.waypoints.Length - 1];
                if (totalLen < 0.1f) continue;

                int adultCount = validAdults.Length > 0 ? g.adults : 0;
                int childCount = validChildren.Length > 0 ? g.children : 0;
                int total = adultCount + childCount;
                if (total <= 0) continue;

                // 우측(정방향) = index 0→N, 좌측(역방향) = N→0. 각 방향 일방통행.
                for (int d = 0; d < 2; d++)
                {
                    bool reverse = d == 1;
                    if (reverse ? !g.left : !g.right) continue;

                    // 경로 선분 위 랜덤 위치 샘플링 (층화 샘플링 - 구간별 1개 보장)
                    List<SpawnPoint> pts = SamplePath(g.waypoints, cum, totalLen, total, reverse);

                    for (int i = 0; i < adultCount; i++)
                    {
                        Spawn(validAdults, g, pts[i], reverse, PedestrianType.Adult);
                        if (Yield(ref spawnedThisFrame)) yield return null;
                    }
                    for (int i = 0; i < childCount; i++)
                    {
                        Spawn(validChildren, g, pts[adultCount + i], reverse, PedestrianType.Child);
                        if (Yield(ref spawnedThisFrame)) yield return null;
                    }
                }
            }
        }

        // null 요소를 걸러낸 프리팹 배열 반환. 문제는 로그만 남기고 예외는 내지 않는다.
        static GameObject[] FilterValidPrefabs(GameObject[] src, string label)
        {
            if (src == null || src.Length == 0)
            {
                Debug.LogWarning($"[PedestrianSpawner] {label} 프리팹이 등록되지 않아 해당 타입은 스폰하지 않습니다.");
                return System.Array.Empty<GameObject>();
            }

            var valid = new List<GameObject>(src.Length);
            foreach (GameObject p in src)
                if (p != null) valid.Add(p);

            if (valid.Count < src.Length)
                Debug.LogWarning($"[PedestrianSpawner] {label} 프리팹 배열에 null 요소 {src.Length - valid.Count}개가 있어 제외합니다.");
            if (valid.Count == 0)
                Debug.LogWarning($"[PedestrianSpawner] {label} 유효한 프리팹이 없어 해당 타입은 스폰하지 않습니다.");

            return valid.ToArray();
        }

        static bool HasNullWaypoint(Transform[] wps)
        {
            foreach (Transform t in wps)
                if (t == null) return true;
            return false;
        }

        // 경로 전체 길이를 count 구간으로 나눠 각 구간 내 랜덤 위치 샘플링
        // 오프셋은 PedestrianController가 이동 방향 기준으로 동적 적용하므로 여기선 중심선 위치만 반환
        static List<SpawnPoint> SamplePath(Transform[] wps, float[] cum, float totalLen,
                                           int count, bool reverse)
        {
            var list = new List<SpawnPoint>(count);
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
                    float t = segLen > 0f ? (dist - cum[i - 1]) / segLen : 0f;
                    Vector3 pos = Vector3.Lerp(wps[i - 1].position, wps[i].position, t);
                    Vector3 dir = (wps[i].position - wps[i - 1].position).normalized;
                    // 역방향은 낮은 인덱스로, 정방향은 높은 인덱스로 이동
                    int nextIdx = reverse ? Mathf.Max(i - 1, 0) : Mathf.Min(i, wps.Length - 1);
                    return (pos, dir, nextIdx);
                }
            }

            int last = wps.Length - 1;
            Vector3 endDir = (wps[last].position - wps[last - 1].position).normalized;
            return (wps[last].position, endDir, reverse ? Mathf.Max(last - 1, 0) : last);
        }

        void Spawn(GameObject[] prefabs, PedestrianGroup g, SpawnPoint sp, bool reverse, PedestrianType type)
        {
            if (prefabs.Length == 0) return; // SpawnRoutine에서 이미 로그 처리

            GameObject prefab = prefabs[Random.Range(0, prefabs.Length)];

            float walkSpeed = type == PedestrianType.Adult ? adultWalkSpeed : childWalkSpeed;

            GameObject ped = Instantiate(prefab, sp.position, Quaternion.identity, transform);
            ped.name = $"Ped_{type}_{(reverse ? "L" : "R")}_{sp.nextWpIndex:D2}";

            // 프리팹 원본 스케일 * 랜덤 배율 (min > max로 넣어도 동작하도록 정렬)
            float sMin = type == PedestrianType.Adult ? adultScaleMin : childScaleMin;
            float sMax = type == PedestrianType.Adult ? adultScaleMax : childScaleMax;
            float scale = Random.Range(Mathf.Min(sMin, sMax), Mathf.Max(sMin, sMax));
            ped.transform.localScale = prefab.transform.localScale * scale;

            if (ped.TryGetComponent(out PedestrianController ctrl))
                ctrl.Init(g.waypoints, sp.position, sp.nextWpIndex, reverse, walkSpeed, g.lateralOffset, g.blendRadius, g.heightOffset);
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

        [Tooltip("연속된 웨이포인트. 우측 = index 0→N, 좌측 = N→0. 각 방향 일방통행.")]
        public Transform[] waypoints;

        [Header("Direction")]
        [Tooltip("우측 통행 스폰 (index 0→N 방향 일방통행)")]
        public bool right = true;
        [Tooltip("좌측 통행 스폰 (index N→0 방향 일방통행)")]
        public bool left = true;

        [Header("Spawn Count")]
        [Tooltip("방향당 어른 수 (체크된 방향마다 이 수만큼 스폰)")]
        [FormerlySerializedAs("adultsForward")]
        [Min(0)] public int adults = 2;
        [Tooltip("방향당 아이 수 (체크된 방향마다 이 수만큼 스폰)")]
        [FormerlySerializedAs("childrenForward")]
        [Min(0)] public int children = 1;

        [Header("Spacing")]
        [Tooltip("웨이포인트 중심선에서 좌우로 떨어지는 거리 (m). 0 = 중심선 위.")]
        [Min(0f)]
        public float lateralOffset = 0.5f;

        [Tooltip("바닥 기준 높이 오프셋 (m). 모델이 바닥에 파묻히거나 떠 있을 때 조정. 음수 가능.")]
        public float heightOffset = 0f;

        [Header("Path Smoothing")]
        [Tooltip("중간 웨이포인트 앞뒤로 Bezier 보간을 적용할 반경 (m). 0 = 직선만 사용.")]
        [Min(0f)]
        public float blendRadius = 1f;
    }
}
