using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace TrafficSystem
{
    public class TrafficSpawner : MonoBehaviour
    {
        [Header("Spawning")]
        [SerializeField] GameObject[] carPrefabs;
        [SerializeField] int carCount = 10;

        [Header("Spawn Waypoints")]
        [Tooltip("각 경로의 시작 웨이포인트. 교차로 직전까지 경로를 탐색해 차량을 분산 배치합니다.")]
        [SerializeField] Waypoint[] spawnPoints;

        [Header("Spawn Height Offset")]
        [SerializeField] float heightOffset = 0.5f;

        [Header("Performance")]
        [Tooltip("프레임당 최대 스폰 수 (0 = 한 번에 전부)")]
        [SerializeField] int spawnPerFrame = 5;

        [Header("Path Sampling")]
        [Tooltip("경로 탐색 최대 웨이포인트 수 (루프 도로 대응)")]
        [SerializeField] int maxPathSteps = 60;

        void Start() => StartCoroutine(SpawnRoutine());

        IEnumerator SpawnRoutine()
        {
            if (carPrefabs == null || carPrefabs.Length == 0 ||
                spawnPoints == null || spawnPoints.Length == 0)
            {
                Debug.LogWarning("[TrafficSpawner] carPrefabs 또는 spawnPoints 미설정.");
                yield break;
            }

            int pointCount        = spawnPoints.Length;
            int baseCount         = carCount / pointCount;
            int remainder         = carCount % pointCount;
            int spawnedThisFrame  = 0;

            for (int pi = 0; pi < pointCount; pi++)
            {
                Waypoint startWp = spawnPoints[pi];
                if (startWp == null) continue;

                int countForPath = baseCount + (pi < remainder ? 1 : 0);
                if (countForPath <= 0) continue;

                // 교차로 직전까지 직진 경로 수집
                List<Waypoint> path = BuildSpawnPath(startWp, maxPathSteps);
                if (path.Count < 2)
                {
                    Debug.LogWarning($"[TrafficSpawner] spawnPoints[{pi}] '{startWp.name}' 경로가 너무 짧습니다 (웨이포인트 2개 이상 필요).");
                    continue;
                }

                float[] cum      = BuildCumLengths(path);
                float   totalLen = cum[path.Count - 1];
                if (totalLen < 0.1f) continue;

                // 경로 전반부(50%)에만 배치 — 차량이 출발 직후 교차로에 밀집되는 것 방지
                float spawnLen = totalLen * 0.5f;
                float slotSize = spawnLen / countForPath;
                for (int i = 0; i < countForPath; i++)
                {
                    float dist = Random.Range(i * slotSize, (i + 1) * slotSize);

                    var (pos, nextWp, rot) = EvalPath(path, cum, dist);
                    Vector3 spawnPos = pos + Vector3.up * heightOffset;

                    GameObject prefab = carPrefabs[Random.Range(0, carPrefabs.Length)];
                    if (prefab == null) continue;

                    GameObject car = Instantiate(prefab, spawnPos, rot, transform);
                    car.name = $"Car_{prefab.name}_{pi:D2}_{i:D2}";

                    if (car.TryGetComponent(out CarController ctrl))
                    {
                        ctrl.Init(nextWp);
                        ctrl.SetRespawnPoints(spawnPoints, heightOffset);
                    }

                    if (spawnPerFrame > 0 && ++spawnedThisFrame >= spawnPerFrame)
                    {
                        spawnedThisFrame = 0;
                        yield return null;
                    }
                }
            }
        }

        // ── Path Building ────────────────────────────────────────────────────────

        // start에서 nextWaypoints[0](직진)을 따라 경로 수집.
        // 교차로 노드(분기 2개 이상)를 만나면 해당 노드를 마지막 방향 참조로만 추가하고 중단.
        // 교차로 내부(교차로 노드와 교차로 노드 사이 구간)는 포함되지 않음.
        List<Waypoint> BuildSpawnPath(Waypoint start, int maxSteps)
        {
            var path    = new List<Waypoint>();
            var visited = new HashSet<Waypoint>();
            Waypoint cur = start;

            while (cur != null && !visited.Contains(cur) && path.Count < maxSteps)
            {
                visited.Add(cur);
                path.Add(cur);

                Waypoint[] nexts = cur.NextWaypoints;
                if (nexts == null || nexts.Length == 0 || nexts[0] == null) break;

                Waypoint nextStraight = nexts[0];

                // 다음 노드가 교차로면 방향 참조로 추가 후 중단
                if (IsJunctionNode(nextStraight))
                {
                    path.Add(nextStraight);
                    break;
                }

                cur = nextStraight;
            }

            return path;
        }

        // 분기가 2개 이상인 노드 = 교차로 결정 노드
        static bool IsJunctionNode(Waypoint wp)
        {
            Waypoint[] nexts = wp.NextWaypoints;
            if (nexts == null || nexts.Length <= 1) return false;
            int nonNull = 0;
            foreach (var n in nexts) if (n != null) nonNull++;
            return nonNull > 1;
        }

        // ── Path Math (PedestrianSpawner 방식과 동일) ────────────────────────────

        static float[] BuildCumLengths(List<Waypoint> wps)
        {
            var cum = new float[wps.Count];
            cum[0] = 0f;
            for (int i = 1; i < wps.Count; i++)
                cum[i] = cum[i - 1] + Vector3.Distance(
                    wps[i - 1].transform.position, wps[i].transform.position);
            return cum;
        }

        // dist 위치의 월드 좌표, 다음 목표 웨이포인트, 진행 방향 회전값 반환
        static (Vector3 pos, Waypoint nextWp, Quaternion rot) EvalPath(
            List<Waypoint> wps, float[] cum, float dist)
        {
            dist = Mathf.Clamp(dist, 0f, cum[wps.Count - 1]);

            for (int i = 1; i < wps.Count; i++)
            {
                if (dist <= cum[i] || i == wps.Count - 1)
                {
                    float   segLen = cum[i] - cum[i - 1];
                    float   t      = segLen > 0f ? (dist - cum[i - 1]) / segLen : 0f;
                    Vector3 pos    = Vector3.Lerp(
                        wps[i - 1].transform.position, wps[i].transform.position, t);

                    Vector3 dir = wps[i].transform.position - wps[i - 1].transform.position;
                    dir.y = 0f;
                    Quaternion rot = dir.sqrMagnitude > 0.001f
                        ? Quaternion.LookRotation(dir.normalized)
                        : Quaternion.identity;

                    return (pos, wps[i], rot);
                }
            }

            int last = wps.Count - 1;
            return (wps[last].transform.position, wps[last], Quaternion.identity);
        }
    }
}
