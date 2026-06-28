using UnityEngine;

namespace TrafficSystem
{
    [RequireComponent(typeof(Rigidbody))]
    public class TrafficVehicle : MonoBehaviour
    {
        [Header("Movement")]
        [SerializeField] float cruiseSpeed    = 8f;
        [SerializeField] float turnSpeed      = 5f;
        [Tooltip("이 거리 이내로 노드에 도달하면 다음 노드로 전진")]
        [SerializeField] float reachDistance  = 1.5f;

        [Header("신호 정지")]
        [Tooltip("stopSignal을 가진 노드에서 이 거리 이내부터 신호 검사 시작 (m)")]
        [SerializeField] float signalCheckDist = 6f;

        [Header("차량 감지")]
        [SerializeField] float brakeDistance   = 8f;
        [SerializeField] float minFollowDist   = 2.5f;
        [SerializeField] float acceleration    = 6f;
        [SerializeField] float detectionRadius = 0.4f;
        [SerializeField] LayerMask vehicleLayer;

        [Header("Performance")]
        [Tooltip("N 물리 프레임마다 1회 속도 계산")]
        [SerializeField] int speedCalcInterval = 4;

        // ── Runtime state ────────────────────────────────────────────────────
        Rigidbody   rb;
        TrafficNode currentNode;
        TrafficNode pendingNode;
        float       currentSpeed;
        float       targetSpeed;
        int         frameCounter;

        static readonly Collider[] overlapBuffer = new Collider[8];

        // ── Public API ───────────────────────────────────────────────────────

        // TrafficManager가 스폰/리스폰 시 호출
        public void Init(TrafficNode startNode)
        {
            currentNode  = startNode;
            pendingNode  = null;
            currentSpeed = 0f;
            targetSpeed  = cruiseSpeed;
            frameCounter = Random.Range(0, speedCalcInterval);
        }

        // ── Unity Lifecycle ──────────────────────────────────────────────────

        void Awake()
        {
            rb = GetComponent<Rigidbody>();
            rb.constraints   = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
        }

        void FixedUpdate()
        {
            if (currentNode == null) return;

            // N 프레임마다 목표 속도 계산
            frameCounter++;
            if (frameCounter >= speedCalcInterval)
            {
                frameCounter = 0;
                targetSpeed  = CalcTargetSpeed();
            }

            currentSpeed = Mathf.MoveTowards(currentSpeed, targetSpeed, acceleration * Time.fixedDeltaTime);

            Steer();
            Move();
            CheckArrival();
        }

        // ── Movement ────────────────────────────────────────────────────────

        void Steer()
        {
            Vector3 dir = currentNode.transform.position - rb.position;
            dir.y = 0f;
            if (dir.sqrMagnitude < 0.001f) return;
            rb.MoveRotation(Quaternion.Slerp(rb.rotation,
                Quaternion.LookRotation(dir.normalized),
                turnSpeed * Time.fixedDeltaTime));
        }

        void Move()
        {
            rb.MovePosition(rb.position + transform.forward * currentSpeed * Time.fixedDeltaTime);
        }

        void CheckArrival()
        {
            Vector3 myPos  = rb.position; myPos.y = 0f;
            Vector3 tgtPos = currentNode.transform.position; tgtPos.y = 0f;
            if (Vector3.Distance(myPos, tgtPos) >= reachDistance) return;

            // 다음 노드 한 번만 결정
            if (pendingNode == null)
                pendingNode = currentNode.PickNext();

            // 신호 게이트
            if (currentNode.StopSignal != null && !currentNode.StopSignal.CanPass)
                return; // 빨간불 → 노드 앞 대기

            // 경로 끝 → 리스폰 요청
            if (pendingNode == null)
            {
                TrafficManager.Instance?.OnVehicleNeedsRespawn(this);
                return;
            }

            currentNode = pendingNode;
            pendingNode = null;
        }

        // ── Speed Calculation ────────────────────────────────────────────────

        float CalcTargetSpeed()
        {
            // ① 신호 룩어헤드 — 목표 노드에 stopSignal이 있고 빨간불이면 서행 후 정지
            if (currentNode.StopSignal != null && !currentNode.StopSignal.CanPass)
            {
                float dist = PlanarDist(rb.position, currentNode.transform.position);
                if (dist <= signalCheckDist) return 0f;
            }

            var origin = rb.position + Vector3.up * 0.5f;

            // ② 근거리 차량 겹침 (SphereCast가 놓치는 이미 겹친 경우)
            var frontCenter = origin + transform.forward * minFollowDist;
            int cnt = Physics.OverlapSphereNonAlloc(frontCenter, detectionRadius,
                          overlapBuffer, vehicleLayer, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < cnt; i++)
                if (overlapBuffer[i].gameObject != gameObject) return 0f;

            // ③ 전방 차량 SphereCast — sqrt 곡선 감속
            if (Physics.SphereCast(origin, detectionRadius, transform.forward,
                    out var hit, brakeDistance, vehicleLayer, QueryTriggerInteraction.Ignore))
            {
                if (hit.collider.gameObject != gameObject)
                {
                    float d = hit.distance;
                    if (d <= minFollowDist) return 0f;
                    float t = Mathf.Clamp01((d - minFollowDist) / (brakeDistance - minFollowDist));
                    return cruiseSpeed * Mathf.Sqrt(t);
                }
            }

            return cruiseSpeed;
        }

        static float PlanarDist(Vector3 a, Vector3 b)
        {
            float dx = a.x - b.x, dz = a.z - b.z;
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        // ── Editor Gizmos ────────────────────────────────────────────────────
#if UNITY_EDITOR
        void OnDrawGizmosSelected()
        {
            var origin = transform.position + Vector3.up * 0.5f;

            // 신호 감지 범위 (빨강)
            if (currentNode != null && currentNode.StopSignal != null)
            {
                Gizmos.color = new Color(1f, 0.2f, 0.2f, 0.8f);
                Gizmos.DrawWireSphere(currentNode.transform.position, signalCheckDist);
            }

            // 차량 감지 범위 (노랑)
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(origin + transform.forward * brakeDistance, detectionRadius);
            var right = Vector3.Cross(transform.forward, Vector3.up).normalized;
            Gizmos.DrawLine(origin + right * detectionRadius,
                            origin + transform.forward * brakeDistance + right * detectionRadius);
            Gizmos.DrawLine(origin - right * detectionRadius,
                            origin + transform.forward * brakeDistance - right * detectionRadius);

            // 완전 정차 구간 (주황)
            Gizmos.color = new Color(1f, 0.5f, 0f);
            Gizmos.DrawWireSphere(origin + transform.forward * minFollowDist, detectionRadius);
        }
#endif
    }
}
