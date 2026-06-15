using UnityEngine;

namespace TrafficSystem
{
    public class TrafficSpawner : MonoBehaviour
    {
        [Header("Spawning")]
        [SerializeField] GameObject carPrefab;
        [SerializeField] int carCount = 10;

        [Header("Spawn Waypoints")]
        [Tooltip("차량을 생성할 시작 웨이포인트 목록. 라운드로빈으로 배분됩니다.")]
        [SerializeField] Waypoint[] spawnPoints;

        [Header("Spawn Height Offset")]
        [SerializeField] float heightOffset = 0.5f;

        [Header("Performance")]
        [Tooltip("프레임당 최대 스폰 수 (0 = 한 번에 전부)")]
        [SerializeField] int spawnPerFrame = 5;

        void Start() => StartCoroutine(SpawnRoutine());

        System.Collections.IEnumerator SpawnRoutine()
        {
            if (carPrefab == null || spawnPoints == null || spawnPoints.Length == 0)
            {
                Debug.LogWarning("[TrafficSpawner] carPrefab 또는 spawnPoints 미설정.");
                yield break;
            }

            int spawnedThisFrame = 0;
            for (int i = 0; i < carCount; i++)
            {
                Waypoint wp = spawnPoints[i % spawnPoints.Length];
                if (wp == null) continue;

                // 웨이포인트 방향(transform.forward)으로 차량 초기 회전
                Quaternion spawnRot = wp.transform.rotation;
                Vector3    spawnPos = wp.transform.position + Vector3.up * heightOffset;

                GameObject car = Instantiate(carPrefab, spawnPos, spawnRot, transform);
                car.name = $"Car_{i:D2}";

                var controller = car.GetComponent<CarController>();
                if (controller != null)
                    controller.Init(wp);

                if (spawnPerFrame > 0 && ++spawnedThisFrame >= spawnPerFrame)
                {
                    spawnedThisFrame = 0;
                    yield return null;
                }
            }
        }
    }
}
