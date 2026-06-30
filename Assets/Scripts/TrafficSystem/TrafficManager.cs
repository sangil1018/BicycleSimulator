using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

namespace TrafficSystem
{
    public class TrafficManager : MonoBehaviour
    {
        public static TrafficManager Instance { get; private set; }

        [Header("Pool")]
        [SerializeField] GameObject[] vehiclePrefabs;
        [SerializeField] int vehicleCount = 10;
        [SerializeField] float heightOffset = 0.5f;

        [Header("Spawn Nodes")]
        [Tooltip("스폰/리스폰 기준 노드. 각 노드에서 순환 배치됩니다.")]
        [SerializeField] TrafficNode[] spawnNodes;

        [Header("Performance")]
        [Tooltip("프레임당 최대 스폰 수 (0 = 한 번에 전부)")]
        [SerializeField] int spawnPerFrame = 5;

        [Header("Collision Check")]
        [SerializeField] LayerMask vehicleLayer;
        [Tooltip("같은 노드에 여러 차량 스폰 시 뒤로 띄우는 간격 (m)")]
        [SerializeField] float spawnSpacing = 6f;

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            // 씬 전환 후 잔류하는 정적 큐 데이터 초기화
            TrafficVehicle.ClearQueues();
        }

        void Start() => StartCoroutine(SpawnAll());

        IEnumerator SpawnAll()
        {
            if (vehiclePrefabs == null || vehiclePrefabs.Length == 0 ||
                spawnNodes == null || spawnNodes.Length == 0)
            {
                Debug.LogWarning("[TrafficManager] vehiclePrefabs 또는 spawnNodes 미설정.");
                yield break;
            }

            // 노드별 스폰 횟수 추적 — 같은 노드에 겹쳐 스폰되는 것 방지
            var nodeUseCount = new Dictionary<TrafficNode, int>();
            int spawned = 0;

            for (int i = 0; i < vehicleCount; i++)
            {
                var node   = spawnNodes[i % spawnNodes.Length];
                if (node == null) continue;

                var prefab = vehiclePrefabs[Random.Range(0, vehiclePrefabs.Length)];
                if (prefab == null) continue;

                if (!nodeUseCount.TryGetValue(node, out int useCount))
                    useCount = 0;
                nodeUseCount[node] = useCount + 1;

                // 같은 노드에 여러 번 스폰될 때 뒤로 간격을 두어 겹침 방지
                Vector3 backDir  = -(node.transform.forward);
                var spawnPos = node.transform.position
                               + Vector3.up  * heightOffset
                               + backDir * (useCount * spawnSpacing);

                var go = Instantiate(prefab, spawnPos, node.transform.rotation, transform);
                go.name = $"Vehicle_{i:D2}";

                if (go.TryGetComponent<TrafficVehicle>(out var v))
                    v.Init(node);

                if (spawnPerFrame > 0 && ++spawned >= spawnPerFrame)
                {
                    spawned = 0;
                    yield return null;
                }
            }
        }

        // TrafficVehicle이 경로 끝(IsEndOfPath)에 도달하면 호출
        public void OnVehicleNeedsRespawn(TrafficVehicle vehicle)
        {
            if (spawnNodes == null || spawnNodes.Length == 0) return;

            int      start  = Random.Range(0, spawnNodes.Length);
            TrafficNode chosen = null;

            for (int i = 0; i < spawnNodes.Length; i++)
            {
                var candidate = spawnNodes[(start + i) % spawnNodes.Length];
                if (candidate == null) continue;
                var checkPos = candidate.transform.position + Vector3.up * heightOffset;
                if (!Physics.CheckSphere(checkPos, 2.5f, vehicleLayer, QueryTriggerInteraction.Ignore))
                {
                    chosen = candidate;
                    break;
                }
            }

            if (chosen == null)
                chosen = spawnNodes[Random.Range(0, spawnNodes.Length)];

            Teleport(vehicle, chosen);
        }

        void Teleport(TrafficVehicle vehicle, TrafficNode node)
        {
            vehicle.transform.position = node.transform.position + Vector3.up * heightOffset;
            vehicle.transform.rotation = node.transform.rotation;
            vehicle.Init(node);
        }
    }
}
