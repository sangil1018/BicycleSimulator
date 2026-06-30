using System.Collections.Generic;
using UnityEngine;

namespace TrafficSystem
{
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

        [Header("차량 간격 제어")]
        [Tooltip("앞차와 유지할 목표 범퍼~범퍼 거리 (m). 크게 설정할수록 차량 사이 간격이 넓어짐.")]
        [SerializeField] float followGap = 5f;
        [Tooltip("절대 최소 안전거리 (범퍼~범퍼). 이 거리 이내면 무조건 정지.")]
        [SerializeField] float minFollowDist = 2.0f;
        [SerializeField] float acceleration  = 6f;
        [Tooltip("제동력. acceleration 보다 강하게 설정할 것.")]
        [SerializeField] float deceleration  = 20f;
        [SerializeField] LayerMask vehicleLayer;

        [Header("차량 감지 범위 (BoxCast)")]
        [Tooltip("원거리 사전 감속 BoxCast 범위 (m). N프레임마다 실행. followGap보다 크게 설정.")]
        [SerializeField] float brakeDistance = 12f;
        [Tooltip("근접 긴급 감속 BoxCast 범위 (m). 매 프레임 실행. followGap 이상으로 유지.")]
        [SerializeField] float emergencyDist = 6f;

        [Header("차량 크기 (BoxCast 기준)")]
        [Tooltip("차량 중심~앞 범퍼 거리 (m)")]
        [SerializeField] float vehicleHalfLength = 2.0f;
        [Tooltip("차량 좌우 반폭 (m)")]
        [SerializeField] float vehicleHalfWidth  = 0.8f;

        [Header("Performance")]
        [Tooltip("N 프레임마다 1회 원거리 BoxCast 실행")]
        [SerializeField] int speedCalcInterval = 4;

        // ── Runtime ───────────────────────────────────────────────────────────
        TrafficNode    currentNode;
        TrafficNode    pendingNode;
        float          currentSpeed;
        float          targetSpeed;
        int            frameCounter;
        TrafficVehicle _leader;
        Vector3        _castHalfExt;

        // ── Node Queue (정적 레지스트리) ──────────────────────────────────────
        static readonly Dictionary<TrafficNode, List<TrafficVehicle>> s_queues = new();
        TrafficNode _queuedAt;

        // TrafficManager.Awake에서 호출 — 씬 전환 시 잔류 데이터 초기화
        public static void ClearQueues() => s_queues.Clear();

        void EnqueueAt(TrafficNode node)
        {
            if (node == null || _queuedAt == node) return;
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

        // 나보다 노드에 가까운 차(=앞차) 중, 3D 거리가 가장 가까운 차 = 직전 앞차
        TrafficVehicle FindLeader()
        {
            if (!s_queues.TryGetValue(currentNode, out var peers)) return null;
            float myDistToNode = PlanarDist(transform.position, currentNode.transform.position);
            TrafficVehicle best    = null;
            float          bestGap = float.MaxValue;
            foreach (var v in peers)
            {
                if (v == null || v == this) continue;
                float theirDistToNode = PlanarDist(v.transform.position, currentNode.transform.position);
                if (theirDistToNode >= myDistToNode) continue;   // 나보다 뒤 → 무시
                float gap = PlanarDist(v.transform.position, transform.position);
                if (gap < bestGap) { bestGap = gap; best = v; }
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
            // 기존 프리팹에 Rigidbody가 있으면 kinematic으로 중화 (하위호환)
            var rb = GetComponent<Rigidbody>();
            if (rb != null) { rb.isKinematic = true; rb.useGravity = false; }

            _castHalfExt = new Vector3(vehicleHalfWidth * 0.85f, 0.3f, 0.05f);
        }

        void OnDisable() => Dequeue();
        void OnDestroy() => Dequeue();

        void Update()
        {
            if (currentNode == null) return;

            EnqueueAt(currentNode);
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
            currentSpeed = Mathf.MoveTowards(currentSpeed, effective, rate * Time.deltaTime);

            Steer();
            Move();
            CheckArrival();
        }

        // ── Movement ──────────────────────────────────────────────────────────
        void Steer()
        {
            Vector3 dir = currentNode.transform.position - transform.position;
            dir.y = 0f;
            if (dir.sqrMagnitude < 0.001f) return;
            Quaternion target = Quaternion.LookRotation(dir.normalized);
            transform.rotation = Quaternion.Slerp(transform.rotation, target, turnSpeed * Time.deltaTime);
        }

        void Move()
        {
            transform.position += transform.forward * currentSpeed * Time.deltaTime;
        }

        void CheckArrival()
        {
            Vector3 myPos  = transform.position; myPos.y  = 0f;
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

        // 매 프레임 — 앞 범퍼 기준 BoxCast 근접 감지
        float CalcEmergencySpeed()
        {
            float castDist  = Mathf.Max(emergencyDist, followGap + 1f);
            var frontBumper = transform.position + transform.forward * vehicleHalfLength + Vector3.up * 0.5f;

            if (Physics.BoxCast(frontBumper, _castHalfExt, transform.forward,
                    out var hit, transform.rotation, castDist, vehicleLayer, QueryTriggerInteraction.Ignore)
                && hit.collider.transform.root != transform)  // 자기 자신의 자식 Collider 제외
            {
                return SpeedFromGap(hit.distance);
            }

            return cruiseSpeed;
        }

        // N프레임마다 — 원거리 사전 감속
        float CalcFollowSpeed()
        {
            float castDist  = Mathf.Max(brakeDistance, followGap + 1f);
            var frontBumper = transform.position + transform.forward * vehicleHalfLength + Vector3.up * 0.5f;

            if (Physics.BoxCast(frontBumper, _castHalfExt, transform.forward,
                    out var hit, transform.rotation, castDist, vehicleLayer, QueryTriggerInteraction.Ignore)
                && hit.collider.transform.root != transform)
            {
                return SpeedFromGap(hit.distance, smooth: true);
            }

            return cruiseSpeed;
        }

        // 매 프레임 — 경로 거리 기반 큐 안전거리 (followGap 기준)
        float CalcSignalQueueSpeed()
        {
            if (_leader == null) return cruiseSpeed;

            float myDist     = PlanarDist(transform.position,         currentNode.transform.position);
            float leaderDist = PlanarDist(_leader.transform.position, currentNode.transform.position);
            float pathGap    = myDist - leaderDist;
            float bumperGap  = pathGap - (vehicleHalfLength + _leader.vehicleHalfLength);

            return SpeedFromGap(bumperGap);
        }

        // 매 프레임 — 선두 차량의 빨간불 정지
        float CalcSignalSpeed()
        {
            if (currentNode.StopSignal == null || currentNode.StopSignal.CanPass) return cruiseSpeed;
            float dist = PlanarDist(transform.position, currentNode.transform.position);
            if (dist > signalCheckDist) return cruiseSpeed;
            if (_leader != null) return cruiseSpeed; // 후속 차 → 큐 속도가 담당
            return 0f;
        }

        // gap(범퍼~범퍼 거리) → 목표 속도
        // smooth=true : Sqrt 커브(완만한 감속), false : 선형 커브(빠른 감속)
        float SpeedFromGap(float gap, bool smooth = false)
        {
            if (gap <= minFollowDist) return 0f;
            float safeRange = Mathf.Max(0.01f, followGap - minFollowDist);
            if (gap >= followGap + safeRange) return cruiseSpeed; // 충분히 멀면 전속력
            float t = Mathf.Clamp01((gap - minFollowDist) / safeRange);
            return cruiseSpeed * (smooth ? Mathf.Sqrt(t) : t);
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

            // 원거리 감지 범위 (노랑)
            Gizmos.color = Color.yellow;
            Gizmos.DrawLine(frontBumper + right * w, frontBumper + transform.forward * brakeDistance + right * w);
            Gizmos.DrawLine(frontBumper - right * w, frontBumper + transform.forward * brakeDistance - right * w);
            Gizmos.DrawWireCube(frontBumper + transform.forward * brakeDistance, new Vector3(w * 2f, 0.6f, 0.1f));

            // followGap — 목표 차간 거리 (하늘색)
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireCube(frontBumper + transform.forward * followGap, new Vector3(w * 2f, 0.6f, 0.1f));

            // 최소 안전거리 경계 (빨강)
            Gizmos.color = Color.red;
            Gizmos.DrawWireCube(frontBumper + transform.forward * minFollowDist, new Vector3(w * 2f, 0.6f, 0.1f));

            // 앞 범퍼 위치 (초록)
            Gizmos.color = Color.green;
            Gizmos.DrawWireCube(frontBumper, new Vector3(w * 2f, 0.6f, 0.1f));

            // 큐 리더 연결선 (보라)
            if (_leader != null)
            {
                Gizmos.color = Color.magenta;
                Gizmos.DrawLine(transform.position + Vector3.up * 0.5f,
                                _leader.transform.position + Vector3.up * 0.5f);
            }
        }
#endif
    }
}
