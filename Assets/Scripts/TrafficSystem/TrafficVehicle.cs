using System.Collections.Generic;
using UnityEngine;

namespace TrafficSystem
{
    [RequireComponent(typeof(Rigidbody))]
    public class TrafficVehicle : MonoBehaviour
    {
        [Header("Movement")]
        [SerializeField] float cruiseSpeed   = 8f;
        [SerializeField] float turnSpeed     = 5f;
        [Tooltip("이 거리 이내로 노드에 도달하면 다음 노드로 전진")]
        [SerializeField] float reachDistance = 1.5f;

        [Header("신호 정지")]
        [Tooltip("StopSignal 노드에서 이 거리 이내부터 신호·큐 검사 시작 (m)")]
        [SerializeField] float signalCheckDist = 6f;

        [Header("차량 감지")]
        [Tooltip("원거리 사전 감속 범위 — N프레임마다 BoxCast")]
        [SerializeField] float brakeDistance   = 8f;
        [Tooltip("근접 긴급 감속 범위 — 매 프레임 BoxCast")]
        [SerializeField] float emergencyDist   = 5f;
        [Tooltip("최소 안전거리 (범퍼~범퍼) — 이 거리 이내이면 무조건 정지")]
        [SerializeField] float minFollowDist   = 2.0f;
        [SerializeField] float acceleration    = 6f;
        [Tooltip("제동력 (acceleration 보다 강하게 설정할 것)")]
        [SerializeField] float deceleration    = 20f;
        [SerializeField] LayerMask vehicleLayer;

        [Header("차량 크기 (BoxCast 기준)")]
        [Tooltip("차량 중심~앞 범퍼 거리 (m). 인스펙터에서 실제 차량 반길이에 맞게 조정.")]
        [SerializeField] float vehicleHalfLength = 2.0f;
        [Tooltip("차량 좌우 반폭 (m). 이 값으로 BoxCast 폭이 결정됨.")]
        [SerializeField] float vehicleHalfWidth  = 0.8f;

        [Header("Performance")]
        [Tooltip("N 물리 프레임마다 1회 원거리 BoxCast 실행")]
        [SerializeField] int speedCalcInterval = 4;

        // ── Runtime ───────────────────────────────────────────────────────────
        Rigidbody   rb;
        TrafficNode currentNode;
        TrafficNode pendingNode;
        float       currentSpeed;
        float       targetSpeed;
        int         frameCounter;
        TrafficVehicle _leader;

        // BoxCast 반크기: X=반폭(조금 줄여 오탐 방지), Y=높이 무시, Z=최소
        Vector3 _castHalfExt;

        // ── Node Queue (정적 레지스트리) ──────────────────────────────────────
        static readonly Dictionary<TrafficNode, List<TrafficVehicle>> s_queues = new();
        TrafficNode _queuedAt;

        void EnqueueAt(TrafficNode node)
        {
            if (_queuedAt == node) return;
            Dequeue();
            _queuedAt = node;
            if (!s_queues.TryGetValue(node, out var list))
                s_queues[node] = list = new List<TrafficVehicle>();
            list.Add(this);
        }

        void Dequeue()
        {
            if (_queuedAt == null) return;
            if (s_queues.TryGetValue(_queuedAt, out var list)) list.Remove(this);
            _queuedAt = null;
        }

        // 같은 노드를 향해 나보다 노드에 더 가까운 차 중 최근접 차 = 바로 앞차
        TrafficVehicle FindLeader()
        {
            if (!s_queues.TryGetValue(currentNode, out var peers)) return null;
            float myDist  = PlanarDist(rb.position, currentNode.transform.position);
            TrafficVehicle best = null;
            float bestDist = float.MaxValue;
            foreach (var v in peers)
            {
                if (v == this) continue;
                float d = PlanarDist(v.rb.position, currentNode.transform.position);
                if (d < myDist && d < bestDist) { bestDist = d; best = v; }
            }
            return best;
        }

        // ── Public API ────────────────────────────────────────────────────────
        public void Init(TrafficNode startNode)
        {
            currentNode  = startNode;
            pendingNode  = null;
            currentSpeed = 0f;
            targetSpeed  = cruiseSpeed;
            frameCounter = Random.Range(0, speedCalcInterval);
            EnqueueAt(startNode);
        }

        // ── Unity Lifecycle ───────────────────────────────────────────────────
        void Awake()
        {
            rb = GetComponent<Rigidbody>();
            rb.constraints   = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;
            rb.interpolation = RigidbodyInterpolation.Interpolate;

            _castHalfExt = new Vector3(vehicleHalfWidth * 0.85f, 0.3f, 0.05f);
        }

        void OnDisable() => Dequeue();
        void OnDestroy() => Dequeue();

        void FixedUpdate()
        {
            if (currentNode == null) return;

            EnqueueAt(currentNode);

            // 신호 구역 제한 없이 항상 리더 탐색 (전 구간 큐 간격 유지)
            _leader = FindLeader();

            // 원거리 추종 — N프레임마다 (BoxCast 비용 분산)
            frameCounter++;
            if (frameCounter >= speedCalcInterval)
            {
                frameCounter = 0;
                targetSpeed  = CalcFollowSpeed();
            }

            float emergency  = CalcEmergencySpeed();
            float queueSpeed = CalcSignalQueueSpeed();
            float signal     = CalcSignalSpeed();
            float effective  = Mathf.Min(emergency, Mathf.Min(queueSpeed, Mathf.Min(targetSpeed, signal)));

            float rate = effective < currentSpeed ? deceleration : acceleration;
            currentSpeed = Mathf.MoveTowards(currentSpeed, effective, rate * Time.fixedDeltaTime);

            // 완전 정지 시 관성 강제 제거 (MovePosition 이후 남은 velocity로 인한 밀림 방지)
            if (currentSpeed < 0.01f)
                rb.linearVelocity = Vector3.zero;

            Steer();
            Move();
            CheckArrival();
        }

        // ── Movement ──────────────────────────────────────────────────────────
        void Steer()
        {
            Vector3 dir = currentNode.transform.position - rb.position;
            dir.y = 0f;
            if (dir.sqrMagnitude < 0.001f) return;
            Quaternion target = Quaternion.LookRotation(dir.normalized);
            rb.MoveRotation(Quaternion.Slerp(rb.rotation, target, turnSpeed * Time.fixedDeltaTime));
        }

        void Move()
        {
            // linearVelocity 방식: 물리 엔진이 충돌을 직접 해결하므로
            // 속도 계산 로직이 실패해도 Collider가 최후 방어선 역할을 함
            Vector3 vel = transform.forward * currentSpeed;
            vel.y = rb.linearVelocity.y; // 중력 성분 유지
            rb.linearVelocity = vel;
        }

        void CheckArrival()
        {
            Vector3 myPos  = rb.position; myPos.y = 0f;
            Vector3 tgtPos = currentNode.transform.position; tgtPos.y = 0f;
            if (Vector3.Distance(myPos, tgtPos) >= reachDistance) return;

            if (pendingNode == null)
                pendingNode = currentNode.PickNext();

            if (currentNode.StopSignal != null && !currentNode.StopSignal.CanPass)
                return; // 빨간불 → 정지선 대기

            if (pendingNode == null)
            {
                if (TrafficManager.Instance != null)
                    TrafficManager.Instance.OnVehicleNeedsRespawn(this);
                else
                {
                    Debug.LogWarning($"[TrafficVehicle] {name}: 경로 끝 도달, TrafficManager 없음.", this);
                    enabled = false;
                }
                return;
            }

            currentNode = pendingNode;
            pendingNode = null;
        }

        // ── Speed Calculation ─────────────────────────────────────────────────

        // 매 프레임 — 앞 범퍼 기준 BoxCast 근접 감지 (전 구간 겹침 방지)
        // hit.distance = 내 앞 범퍼 ~ 상대 차 표면 = 실제 범퍼간 거리
        float CalcEmergencySpeed()
        {
            var frontBumper = rb.position + transform.forward * vehicleHalfLength + Vector3.up * 0.5f;

            if (Physics.BoxCast(frontBumper, _castHalfExt, transform.forward,
                    out var hit, rb.rotation, emergencyDist, vehicleLayer, QueryTriggerInteraction.Ignore)
                && hit.collider.gameObject != gameObject)
            {
                float gap = hit.distance;
                if (gap <= minFollowDist) return 0f;
                float t = Mathf.Clamp01((gap - minFollowDist) / (emergencyDist - minFollowDist));
                return cruiseSpeed * t;
            }

            return cruiseSpeed;
        }

        // 매 프레임 — 경로 거리 기반 큐 안전거리 (전 구간)
        float CalcSignalQueueSpeed()
        {
            if (_leader == null) return cruiseSpeed;

            float myDist     = PlanarDist(rb.position,         currentNode.transform.position);
            float leaderDist = PlanarDist(_leader.rb.position, currentNode.transform.position);
            float gap        = myDist - leaderDist;

            if (gap <= minFollowDist) return 0f;

            float maxGap = currentNode.StopSignal != null ? signalCheckDist : brakeDistance;
            float t = Mathf.Clamp01((gap - minFollowDist) / (maxGap - minFollowDist));
            return cruiseSpeed * Mathf.Sqrt(t);
        }

        // 매 프레임 — 선두 차량의 빨간불 정지
        float CalcSignalSpeed()
        {
            if (currentNode.StopSignal == null || currentNode.StopSignal.CanPass) return cruiseSpeed;
            float dist = PlanarDist(rb.position, currentNode.transform.position);
            if (dist > signalCheckDist) return cruiseSpeed;
            if (_leader != null) return cruiseSpeed; // 후속 차 → 큐 속도가 담당
            return 0f;
        }

        // N프레임마다 — 원거리 사전 감속 (앞 범퍼 기준 BoxCast)
        float CalcFollowSpeed()
        {
            var frontBumper = rb.position + transform.forward * vehicleHalfLength + Vector3.up * 0.5f;

            if (Physics.BoxCast(frontBumper, _castHalfExt, transform.forward,
                    out var hit, rb.rotation, brakeDistance, vehicleLayer, QueryTriggerInteraction.Ignore)
                && hit.collider.gameObject != gameObject)
            {
                float gap = hit.distance;
                if (gap <= minFollowDist) return 0f;
                float t = Mathf.Clamp01((gap - minFollowDist) / (brakeDistance - minFollowDist));
                return cruiseSpeed * Mathf.Sqrt(t);
            }

            return cruiseSpeed;
        }

        static float PlanarDist(Vector3 a, Vector3 b)
        {
            float dx = a.x - b.x, dz = a.z - b.z;
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        // ── Editor Gizmos ─────────────────────────────────────────────────────
#if UNITY_EDITOR
        void OnDrawGizmosSelected()
        {
            var frontBumper = transform.position + transform.forward * vehicleHalfLength + Vector3.up * 0.5f;
            var right = Vector3.Cross(transform.forward, Vector3.up).normalized;
            float w = vehicleHalfWidth;

            if (currentNode != null && currentNode.StopSignal != null)
            {
                Gizmos.color = new Color(1f, 0.2f, 0.2f, 0.4f);
                Gizmos.DrawWireSphere(currentNode.transform.position, signalCheckDist);
            }

            // 원거리 감지 (노랑)
            Gizmos.color = Color.yellow;
            Gizmos.DrawLine(frontBumper + right * w, frontBumper + transform.forward * brakeDistance + right * w);
            Gizmos.DrawLine(frontBumper - right * w, frontBumper + transform.forward * brakeDistance - right * w);
            Gizmos.DrawWireCube(frontBumper + transform.forward * brakeDistance, new Vector3(w * 2f, 0.6f, 0.1f));

            // 긴급 감속 구간 (주황)
            Gizmos.color = new Color(1f, 0.5f, 0f);
            Gizmos.DrawWireCube(frontBumper + transform.forward * emergencyDist, new Vector3(w * 2f, 0.6f, 0.1f));

            // 최소 안전거리 경계 (빨강)
            Gizmos.color = Color.red;
            Gizmos.DrawWireCube(frontBumper + transform.forward * minFollowDist, new Vector3(w * 2f, 0.6f, 0.1f));

            // 앞 범퍼 위치 (초록)
            Gizmos.color = Color.green;
            Gizmos.DrawWireCube(frontBumper, new Vector3(w * 2f, 0.6f, 0.1f));

            // 큐 리더 연결선 (하늘색)
            if (_leader != null)
            {
                Gizmos.color = Color.cyan;
                Gizmos.DrawLine(transform.position + Vector3.up * 0.5f,
                                _leader.transform.position + Vector3.up * 0.5f);
            }
        }
#endif
    }
}
