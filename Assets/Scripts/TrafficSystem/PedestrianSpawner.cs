using UnityEngine;
using UnityEngine.AI;

namespace TrafficSystem
{
    public class PedestrianSpawner : MonoBehaviour
    {
        [Header("Spawning")]
        [SerializeField] GameObject pedestrianPrefab;
        [SerializeField] int pedestrianCount = 20;

        [Header("Waypoint sets (one set per sidewalk route)")]
        [SerializeField] PedestrianRoute[] routes;

        [Header("Performance")]
        [Tooltip("Spawn this many pedestrians per frame to avoid a one-frame hitch (0 = all at once)")]
        [SerializeField] int spawnPerFrame = 5;

        void Start() => StartCoroutine(SpawnRoutine());

        System.Collections.IEnumerator SpawnRoutine()
        {
            if (pedestrianPrefab == null || routes == null || routes.Length == 0)
            {
                Debug.LogWarning("[PedestrianSpawner] pedestrianPrefab or routes not assigned.");
                yield break;
            }

            int spawnedThisFrame = 0;
            for (int i = 0; i < pedestrianCount; i++)
            {
                PedestrianRoute route = routes[i % routes.Length];
                if (route == null || route.waypoints == null || route.waypoints.Length == 0) continue;

                int       startIndex = i % route.waypoints.Length;
                Transform startWp    = route.waypoints[startIndex];
                if (startWp == null) continue;

                // NavMesh 위에 유효한 위치가 있는지 검증 후 보정
                Vector3 spawnPos = startWp.position;
                if (NavMesh.SamplePosition(startWp.position, out NavMeshHit navHit, 2f, NavMesh.AllAreas))
                {
                    spawnPos = navHit.position;
                }
                else
                {
                    Debug.LogWarning(
                        $"[PedestrianSpawner] Route[{i % routes.Length}] '{startWp.name}' is not on NavMesh — skipping.");
                    continue;
                }

                GameObject ped = Instantiate(pedestrianPrefab, spawnPos, Quaternion.identity, transform);
                ped.name = $"Pedestrian_{i:D2}";

                var controller = ped.GetComponent<PedestrianController>();
                if (controller != null)
                    controller.Init(route.waypoints, route.crosswalkLight, startIndex);

                if (spawnPerFrame > 0 && ++spawnedThisFrame >= spawnPerFrame)
                {
                    spawnedThisFrame = 0;
                    yield return null;
                }
            }
        }
    }

    [System.Serializable]
    public class PedestrianRoute
    {
        public Transform[] waypoints;
        [Tooltip("경로 공통 fallback 신호등. CrosswalkWaypoint.tLight 가 지정된 경우 그쪽이 우선됩니다.")]
        public TrafficLight crosswalkLight;
    }
}
