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
        [SerializeField] float signalCheckDist = 8f;
        [Tooltip("정지선(노드)에서 앞 범퍼까지 남길 거리 (m)")]
        [SerializeField] float stopLineOffset = 1.0f;

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

        [Header("바퀴")]
        [Tooltip("이동 거리에 따라 굴러갈 바퀴들. 여기에 등록한 Transform이 회전한다.")]
        [SerializeField] Transform[] wheels;
        [Tooltip("바퀴 반지름 (m). 이동 거리 / (2π·r) 만큼 회전한다.")]
        [SerializeField] float wheelRadius = 0.35f;
        [Tooltip("바퀴가 굴러가는 로컬 회전축 (보통 X축).")]
        [SerializeField] Vector3 wheelSpinAxis = Vector3.right;

        // ── Runtime ───────────────────────────────────────────────────────────
        TrafficNode    currentNode;
        TrafficNode    pendingNode;
        float          currentSpeed;
        float          targetSpeed;
        int            frameCounter;
        TrafficVehicle _leader;
        float          _leaderPathGap;       // 경로 기준 중심 간 거리 (m)
        bool           _leaderInSameSegment; // false = 다음 세그먼트에서 찾은 체인 리더
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

        // 직전 앞차 탐색 — 같은 세그먼트 우선, 없으면 다음 세그먼트(노드 체인)까지 확장.
        // 결과는 _leader / _leaderPathGap(경로 기준 중심 간 거리) / _leaderInSameSegment에 기록.
        void FindLeader()
        {
            _leader              = null;
            _leaderPathGap       = float.MaxValue;
            _leaderInSameSegment = false;

            Vector3 nodePos = currentNode.transform.position;
            float   myDist  = PlanarDist(transform.position, nodePos);

            // 1) 같은 세그먼트: currentNode 큐에서 나보다 노드에 가까운 차
            if (s_queues.TryGetValue(currentNode, out var peers))
            {
                foreach (var v in peers)
                {
                    if (v == null || v == this) continue;
                    float theirDist = PlanarDist(v.transform.position, nodePos);
                    if (theirDist >= myDist) continue;   // 나보다 뒤 → 무시
                    float gap = myDist - theirDist;
                    if (gap < _leaderPathGap)
                    {
                        _leaderPathGap       = gap;
                        _leader              = v;
                        _leaderInSameSegment = true;
                    }
                }
                if (_leader != null) return;
            }

            // 2) 다음 세그먼트: currentNode의 각 exit 노드 큐에서
            //    내 노드~exit 사이에 있는 차량 중 내 노드에 가장 가까운(후미) 차
            for (int i = 0; i < currentNode.ExitCount; i++)
            {
                var next = currentNode.GetExitNode(i);
                if (next == null || !s_queues.TryGetValue(next, out var nextPeers)) continue;

                float segLen = PlanarDist(nodePos, next.transform.position);
                foreach (var v in nextPeers)
                {
                    if (v == null || v == this) continue;
                    float fromNode = PlanarDist(v.transform.position, nodePos);
                    if (fromNode > segLen) continue; // 구간 밖(다른 방향에서 합류하는 차 등) 제외
                    float gap = myDist + fromNode;   // 내 위치 → 노드 → 앞차 경로 거리
                    if (gap < _leaderPathGap) { _leaderPathGap = gap; _leader = v; }
                }
            }
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
            FindLeader();

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
            float delta = currentSpeed * Time.deltaTime;
            transform.position += transform.forward * delta;
            SpinWheels(delta);
        }

        // 이동 거리(m)만큼 바퀴를 굴린다. 회전각(deg) = 거리 / 원주 × 360
        void SpinWheels(float distance)
        {
            if (distance <= 0f || wheels == null || wheels.Length == 0 || wheelRadius <= 0f) return;

            float degrees = distance / (2f * Mathf.PI * wheelRadius) * 360f;
            for (int i = 0; i < wheels.Length; i++)
            {
                if (wheels[i] != null)
                    wheels[i].Rotate(wheelSpinAxis, degrees, Space.Self);
            }
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
            float bumperGap = _leaderPathGap - (vehicleHalfLength + _leader.vehicleHalfLength);
            return SpeedFromGap(bumperGap);
        }

        // 매 프레임 — 선두 차량의 빨간불 정지
        float CalcSignalSpeed()
        {
            if (currentNode.StopSignal == null || currentNode.StopSignal.CanPass) return cruiseSpeed;
            float dist = PlanarDist(transform.position, currentNode.transform.position);
            if (dist > signalCheckDist) return cruiseSpeed;
            // 같은 세그먼트 앞차만 큐 속도에 위임 — 노드 너머 체인 리더는 빨간불 정지를 대신할 수 없음
            if (_leader != null && _leaderInSameSegment) return cruiseSpeed;

            // 남은 거리에 맞춘 등감속 — 앞 범퍼가 정지선 앞 stopLineOffset 지점에 오도록 정지
            // v = √(2·a·gap), 0.8 = 제동 여유 마진
            float gap = dist - vehicleHalfLength - stopLineOffset;
            if (gap <= 0f) return 0f;
            return Mathf.Min(cruiseSpeed, Mathf.Sqrt(2f * deceleration * 0.8f * gap));
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

            DrawWheelGizmos();
        }

        // 각 바퀴의 wheelRadius를 굴러가는 평면 위의 원으로 표시 (주황)
        void DrawWheelGizmos()
        {
            if (wheels == null || wheelRadius <= 0f) return;

            Gizmos.color = new Color(1f, 0.55f, 0f); // 주황
            for (int i = 0; i < wheels.Length; i++)
            {
                var w = wheels[i];
                if (w == null) continue;

                // 회전축(로컬)을 월드로 변환 — 원은 이 축에 수직인 평면에 그린다
                Vector3 axis = w.TransformDirection(wheelSpinAxis.normalized);
                if (axis.sqrMagnitude < 0.0001f) continue;
                axis.Normalize();

                // 축에 수직인 두 기준 벡터
                Vector3 u = Vector3.Cross(axis, Vector3.up);
                if (u.sqrMagnitude < 0.0001f) u = Vector3.Cross(axis, Vector3.forward);
                u.Normalize();
                Vector3 v = Vector3.Cross(axis, u);

                const int seg = 24;
                Vector3 center = w.position;
                Vector3 prev = center + u * wheelRadius;
                for (int s = 1; s <= seg; s++)
                {
                    float ang = s / (float)seg * Mathf.PI * 2f;
                    Vector3 next = center + (u * Mathf.Cos(ang) + v * Mathf.Sin(ang)) * wheelRadius;
                    Gizmos.DrawLine(prev, next);
                    prev = next;
                }

                // 회전축 표시 (짧은 선)
                Gizmos.DrawLine(center - axis * wheelRadius * 0.5f, center + axis * wheelRadius * 0.5f);
            }
        }
#endif
    }
}
