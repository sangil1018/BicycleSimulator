using UnityEngine;

namespace TrafficSystem
{
    [RequireComponent(typeof(Rigidbody))]
    public class CarController : MonoBehaviour
    {
        [Header("Path")]
        [SerializeField] Waypoint startWaypoint;

        [Header("Movement")]
        [SerializeField] float speed = 8f;
        [SerializeField] float turnSpeed = 5f;
        [SerializeField] float waypointReachDistance = 1.5f;

        [Header("신호등 감지")]
        [Tooltip("신호등 감지 거리 (m) — brakeDistance보다 크게 설정 권장")]
        [SerializeField] float lightDetectionDistance = 12f;
        [Tooltip("신호등 SphereCast 반경 — 차선 폭 절반 이상 (기본 2m)")]
        [SerializeField] float lightDetectionRadius   = 2f;
        [SerializeField] LayerMask trafficLightLayer;
        [Tooltip("교차로 통과 중 신호등 재감지 억제 시간 (초). 교차로 통과 시간보다 길게 설정.")]
        [SerializeField] float intersectionCooldown = 4f;

        [Header("차량 감지")]
        [SerializeField] float detectionRadius = 0.4f;
        [SerializeField] LayerMask vehicleLayer;

        [Header("Braking")]
        [Tooltip("전방 차량 감지 시 감속 시작 거리 (m)")]
        [SerializeField] float brakeDistance = 8f;
        [Tooltip("완전 정차 유지 최소 차간 거리 (m)")]
        [SerializeField] float minFollowDist = 2.5f;
        [SerializeField] float acceleration  = 6f;

        [Header("Performance")]
        [Tooltip("N 물리 프레임마다 1회 속도 계산 (기본 4 = 50Hz 기준 12.5회/초)")]
        [SerializeField] int stopCheckInterval = 4;

        // ── Runtime state ────────────────────────────────────────────────────────
        Waypoint  currentWaypoint;
        float     targetSpeed;
        float     currentSpeed;
        int       stopCheckFrame;
        float     lightPassCooldown; // > 0이면 신호등 감지 억제 (교차로 통과 중)
        Waypoint  pendingNext;       // 웨이포인트 도달 시 미리 결정된 다음 목표
        int       pendingNextIdx = -1; // -1=미결정, 0=직진, 1=우회전, 2=좌회전
        Rigidbody rb;

        // 경로 종료 시 리스폰 정보 (TrafficSpawner에서 주입)
        Waypoint[] respawnPoints;
        float      spawnHeightOffset;

        // 원호(회전) 이동 상태
        bool    isOnArc;
        Vector3 arcCenter;
        Vector3 arcStartVec;
        float   arcRadius;
        float   arcTotalDeg;
        float   arcSweptDeg;

        static readonly Collider[] overlapBuffer = new Collider[8];

        // ── Public API ───────────────────────────────────────────────────────────

        public void Init(Waypoint waypoint)
        {
            currentWaypoint = waypoint;
            stopCheckFrame  = Random.Range(0, stopCheckInterval);
            targetSpeed     = speed;
        }

        public void SetRespawnPoints(Waypoint[] points, float heightOffset)
        {
            respawnPoints     = points;
            spawnHeightOffset = heightOffset;
        }

        // ── Unity Lifecycle ──────────────────────────────────────────────────────

        void Awake()
        {
            rb = GetComponent<Rigidbody>();
            rb.constraints   = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            currentWaypoint  = startWaypoint;
            targetSpeed      = speed;
        }

        void FixedUpdate()
        {
            if (currentWaypoint == null) return;

            // 교차로 쿨다운 감소
            if (lightPassCooldown > 0f)
                lightPassCooldown -= Time.fixedDeltaTime;

            // 속도 계산 (스로틀: N 프레임마다 1회)
            stopCheckFrame++;
            if (stopCheckFrame >= stopCheckInterval)
            {
                stopCheckFrame = 0;
                targetSpeed = CalcTargetSpeed();
            }

            currentSpeed = Mathf.MoveTowards(currentSpeed, targetSpeed, acceleration * Time.fixedDeltaTime);

            if (isOnArc) MoveOnArc();
            else         MoveTowardWaypoint();
        }

        // ── Movement ────────────────────────────────────────────────────────────

        void MoveTowardWaypoint()
        {
            Transform target = currentWaypoint.transform;
            Vector3 dir = target.position - rb.position;
            dir.y = 0f;
            if (dir.sqrMagnitude > 0.001f)
                rb.MoveRotation(Quaternion.Slerp(rb.rotation,
                    Quaternion.LookRotation(dir.normalized), turnSpeed * Time.fixedDeltaTime));

            rb.MovePosition(rb.position + transform.forward * currentSpeed * Time.fixedDeltaTime);

            float dist = Vector3.Distance(
                new Vector3(rb.position.x, 0f, rb.position.z),
                new Vector3(target.position.x, 0f, target.position.z));

            if (dist < waypointReachDistance)
            {
                // 방향 한 번만 결정 (랜덤 중복 호출 방지)
                if (pendingNextIdx < 0)
                    (pendingNext, pendingNextIdx) = currentWaypoint.GetNextWithIndex();

                // 신호등 게이트 (쿨다운 중이면 이미 통과 허가 상태 → 검사 생략)
                if (lightPassCooldown <= 0f)
                {
                    var light = DetectLightAhead();
                    if (light != null)
                    {
                        // 우회전(index 1)은 빨간불에도 통과 허용
                        if (pendingNextIdx != 1 && light.VehicleState != TrafficLightState.Green)
                            return; // 빨간불 → 대기

                        // 신호등 통과 허가 → 교차로 쿨다운 시작
                        lightPassCooldown = intersectionCooldown;
                    }
                }

                ApplyNextWaypoint(pendingNext, pendingNextIdx);
                pendingNextIdx = -1;
                pendingNext    = null;
            }
        }

        void MoveOnArc()
        {
            if (arcRadius < 0.01f) { isOnArc = false; return; }

            float angularSpeedDeg = (currentSpeed / arcRadius) * Mathf.Rad2Deg;
            arcSweptDeg += Mathf.Sign(arcTotalDeg) * angularSpeedDeg * Time.fixedDeltaTime;

            bool  done  = Mathf.Abs(arcSweptDeg) >= Mathf.Abs(arcTotalDeg);
            float angle = done ? arcTotalDeg : arcSweptDeg;

            Vector3 radial = Quaternion.AngleAxis(angle, Vector3.up) * arcStartVec;
            Vector3 newPos = arcCenter + radial * arcRadius;
            newPos.y = rb.position.y;

            Vector3 tangent = arcTotalDeg > 0f
                ? Vector3.Cross(Vector3.up, radial).normalized
                : Vector3.Cross(radial, Vector3.up).normalized;

            rb.MovePosition(newPos);
            if (tangent.sqrMagnitude > 0.001f)
                rb.MoveRotation(Quaternion.LookRotation(tangent));

            if (done) isOnArc = false;
        }

        void ApplyNextWaypoint(Waypoint next, int index)
        {
            if (next == null) { TryRespawn(); return; }
            currentWaypoint = next;
            if (index != 0)
                TryStartArc(rightTurn: index == 1);
        }

        void TryStartArc(bool rightTurn)
        {
            Vector3 startPos = rb.position;      startPos.y = 0f;
            Vector3 endPos   = currentWaypoint.transform.position; endPos.y = 0f;
            Vector3 fwd      = transform.forward; fwd.y = 0f;
            if (fwd.sqrMagnitude < 0.001f) return;
            fwd.Normalize();

            Vector3 n = rightTurn
                ?  Vector3.Cross(Vector3.up, fwd)
                : -Vector3.Cross(Vector3.up, fwd);

            Vector3 diff = startPos - endPos;
            float   dot  = Vector3.Dot(diff, n);
            if (Mathf.Abs(dot) < 0.001f) return;

            float r = -diff.sqrMagnitude / (2f * dot);
            if (r < 0.1f) return;

            arcCenter   = startPos + n * r;
            arcRadius   = r;
            arcStartVec = (startPos - arcCenter).normalized;

            arcTotalDeg = Vector3.SignedAngle(arcStartVec, (endPos - arcCenter).normalized, Vector3.up);
            if ( rightTurn && arcTotalDeg < 0f) arcTotalDeg += 360f;
            if (!rightTurn && arcTotalDeg > 0f) arcTotalDeg -= 360f;
            if (arcTotalDeg >  180f) arcTotalDeg -= 360f;
            if (arcTotalDeg < -180f) arcTotalDeg += 360f;

            arcSweptDeg = 0f;
            isOnArc     = true;
        }

        // ── Respawn ──────────────────────────────────────────────────────────────

        void TryRespawn()
        {
            if (respawnPoints == null || respawnPoints.Length == 0)
            {
                currentWaypoint = null;
                return;
            }

            int      startIdx = Random.Range(0, respawnPoints.Length);
            Waypoint chosen   = null;

            for (int i = 0; i < respawnPoints.Length; i++)
            {
                var candidate = respawnPoints[(startIdx + i) % respawnPoints.Length];
                if (candidate == null) continue;
                var checkPos = candidate.transform.position + Vector3.up * spawnHeightOffset;
                if (!Physics.CheckSphere(checkPos, minFollowDist, vehicleLayer, QueryTriggerInteraction.Ignore))
                {
                    chosen = candidate;
                    break;
                }
            }

            if (chosen == null)
                chosen = respawnPoints[Random.Range(0, respawnPoints.Length)];

            Teleport(chosen);
        }

        void Teleport(Waypoint wp)
        {
            rb.position        = wp.transform.position + Vector3.up * spawnHeightOffset;
            rb.rotation        = wp.transform.rotation;
            rb.linearVelocity  = Vector3.zero;
            rb.angularVelocity = Vector3.zero;

            currentWaypoint   = wp;
            isOnArc           = false;
            currentSpeed      = 0f;
            targetSpeed       = speed;
            lightPassCooldown = 0f;
            pendingNextIdx    = -1;
            pendingNext       = null;
        }

        // ── Detection ────────────────────────────────────────────────────────────

        // 전방 신호등 반환. 없으면 null. 쿨다운 여부는 호출 측에서 판단.
        TrafficLight DetectLightAhead()
        {
            var origin = rb.position + Vector3.up * 0.5f;
            if (!Physics.SphereCast(origin, lightDetectionRadius, transform.forward,
                    out var hit, lightDetectionDistance,
                    trafficLightLayer, QueryTriggerInteraction.Collide))
                return null;
            return hit.collider.GetComponentInParent<TrafficLight>();
        }

        // 현재 프레임의 목표 속도 계산 (신호등 억제 중이면 차량 감지만 수행)
        float CalcTargetSpeed()
        {
            var origin = rb.position + Vector3.up * 0.5f;

            // ① 신호등 감지 — 교차로 쿨다운 중이면 건너뜀
            if (lightPassCooldown <= 0f)
            {
                var light = DetectLightAhead();
                if (light != null && light.VehicleState != TrafficLightState.Green)
                    return 0f;
            }

            // ② 근거리 차량 겹침 (SphereCast가 놓치는 이미 겹친 경우)
            var frontCenter = origin + transform.forward * minFollowDist;
            int cnt = Physics.OverlapSphereNonAlloc(frontCenter, detectionRadius,
                          overlapBuffer, vehicleLayer, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < cnt; i++)
                if (overlapBuffer[i].gameObject != gameObject) return 0f;

            // ③ 전방 차량 SphereCast — sqrt 곡선 감속
            if (Physics.SphereCast(origin, detectionRadius, transform.forward,
                    out var carHit, brakeDistance, vehicleLayer, QueryTriggerInteraction.Ignore))
            {
                if (carHit.collider.gameObject != gameObject)
                {
                    float d = carHit.distance;
                    if (d <= minFollowDist) return 0f;
                    float t = Mathf.Clamp01((d - minFollowDist) / (brakeDistance - minFollowDist));
                    return speed * Mathf.Sqrt(t);
                }
            }

            return speed;
        }

        // ── Editor Gizmos ────────────────────────────────────────────────────────
#if UNITY_EDITOR
        void OnDrawGizmosSelected()
        {
            var origin = transform.position + Vector3.up * 0.5f;

            // 신호등 감지 범위 (빨강)
            Gizmos.color = new Color(1f, 0.2f, 0.2f, 0.8f);
            Gizmos.DrawWireSphere(origin + transform.forward * lightDetectionDistance, lightDetectionRadius);
            var right = Vector3.Cross(transform.forward, Vector3.up).normalized;
            Gizmos.DrawLine(origin + right * lightDetectionRadius,
                            origin + transform.forward * lightDetectionDistance + right * lightDetectionRadius);
            Gizmos.DrawLine(origin - right * lightDetectionRadius,
                            origin + transform.forward * lightDetectionDistance - right * lightDetectionRadius);

            // 차량 감지 범위 (노랑)
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(origin, detectionRadius);
            Gizmos.DrawWireSphere(origin + transform.forward * brakeDistance, detectionRadius);
            right = Vector3.Cross(transform.forward, Vector3.up).normalized;
            Gizmos.DrawLine(origin + right * detectionRadius,
                            origin + transform.forward * brakeDistance + right * detectionRadius);
            Gizmos.DrawLine(origin - right * detectionRadius,
                            origin + transform.forward * brakeDistance - right * detectionRadius);

            // 완전 정차 구간 (주황)
            Gizmos.color = new Color(1f, 0.5f, 0f);
            Gizmos.DrawWireSphere(origin + transform.forward * minFollowDist, detectionRadius);

            // 원호 시각화
            if (isOnArc && arcRadius > 0.01f)
            {
                Gizmos.color = new Color(0f, 1f, 0.5f, 0.6f);
                float remaining = arcTotalDeg - arcSweptDeg;
                Vector3 prev = arcCenter + Quaternion.AngleAxis(arcSweptDeg, Vector3.up) * arcStartVec * arcRadius;
                for (int i = 1; i <= 32; i++)
                {
                    float   a = arcSweptDeg + remaining * (i / 32f);
                    Vector3 p = arcCenter + Quaternion.AngleAxis(a, Vector3.up) * arcStartVec * arcRadius;
                    Gizmos.DrawLine(prev + Vector3.up * 0.3f, p + Vector3.up * 0.3f);
                    prev = p;
                }
            }
        }
#endif
    }
}
