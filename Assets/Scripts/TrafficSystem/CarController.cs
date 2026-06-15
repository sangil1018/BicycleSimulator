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

        [Header("Traffic Detection")]
        [Tooltip("전방 신호등/차량 감지 거리")]
        [SerializeField] float detectionDistance = 8f;
        [SerializeField] float detectionRadius   = 0.4f;
        [SerializeField] LayerMask trafficLightLayer;
        [SerializeField] LayerMask vehicleLayer;

        [Header("Braking")]
        [SerializeField] float brakeDistance  = 5f;
        [SerializeField] float acceleration   = 6f;

        [Header("Performance")]
        [Tooltip("N 물리 프레임마다 1회 감지 (기본 4 = 50Hz 기준 12.5회/초)")]
        [SerializeField] int stopCheckInterval = 4;

        Waypoint  currentWaypoint;
        bool      isStopped;
        float     currentSpeed;
        int       stopCheckFrame;
        Rigidbody rb;

        // 원호 이동 상태
        bool    isOnArc;
        Vector3 arcCenter;
        Vector3 arcStartVec;  // arcCenter → 시작점, normalized
        float   arcRadius;
        float   arcTotalDeg;  // 부호: 양수=CCW(좌회전), 음수=CW(우회전)
        float   arcSweptDeg;

        public void Init(Waypoint waypoint)
        {
            currentWaypoint = waypoint;
            stopCheckFrame  = Random.Range(0, stopCheckInterval);
        }

        void Awake()
        {
            rb = GetComponent<Rigidbody>();
            rb.constraints   = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            currentWaypoint  = startWaypoint;
        }

        void FixedUpdate()
        {
            if (currentWaypoint == null) return;

            stopCheckFrame++;
            if (stopCheckFrame >= stopCheckInterval)
            {
                stopCheckFrame = 0;
                isStopped = ShouldStop();
            }

            float targetSpeed = isStopped ? 0f : speed;
            currentSpeed = Mathf.MoveTowards(currentSpeed, targetSpeed, acceleration * Time.fixedDeltaTime);

            if (isOnArc)
                MoveOnArc();
            else
                MoveTowardWaypoint();
        }

        void MoveTowardWaypoint()
        {
            Transform target = currentWaypoint.transform;
            Vector3 dir = target.position - rb.position;
            dir.y = 0f;
            if (dir.sqrMagnitude > 0.001f)
            {
                Quaternion targetRot = Quaternion.LookRotation(dir.normalized);
                rb.MoveRotation(Quaternion.Slerp(rb.rotation, targetRot, turnSpeed * Time.fixedDeltaTime));
            }
            rb.MovePosition(rb.position + transform.forward * currentSpeed * Time.fixedDeltaTime);

            float dist = Vector3.Distance(
                new Vector3(rb.position.x, 0f, rb.position.z),
                new Vector3(target.position.x, 0f, target.position.z));
            if (dist < waypointReachDistance)
                AdvanceToNextWaypoint();
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

            // Unity Y축: 양수=CCW=우회전, 음수=CW=좌회전
            Vector3 tangent = arcTotalDeg > 0f
                ? Vector3.Cross(Vector3.up, radial).normalized   // CCW = 우회전
                : Vector3.Cross(radial, Vector3.up).normalized;  // CW  = 좌회전

            rb.MovePosition(newPos);
            if (tangent.sqrMagnitude > 0.001f)
                rb.MoveRotation(Quaternion.LookRotation(tangent));

            if (done)
                isOnArc = false;
            // 호 완료 후 다음 프레임에 MoveTowardWaypoint 가 도달 판정 → AdvanceToNextWaypoint 호출
        }

        void AdvanceToNextWaypoint()
        {
            var (next, index) = currentWaypoint.GetNextWithIndex();
            if (next == null) { currentWaypoint = null; return; }
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

            // 전진 방향에서 수직인 쪽 (회전 방향)
            Vector3 n = rightTurn
                ?  Vector3.Cross(Vector3.up, fwd)   // right
                : -Vector3.Cross(Vector3.up, fwd);  // left

            Vector3 diff = startPos - endPos;
            float   dot  = Vector3.Dot(diff, n);
            if (Mathf.Abs(dot) < 0.001f) return;

            float r = -diff.sqrMagnitude / (2f * dot);
            if (r < 0.1f) return;  // 반경 너무 작거나 반대쪽 → 직진 처리

            arcCenter   = startPos + n * r;
            arcRadius   = r;
            arcStartVec = (startPos - arcCenter).normalized;

            arcTotalDeg = Vector3.SignedAngle(arcStartVec, (endPos - arcCenter).normalized, Vector3.up);
            // Unity Y축: 양수=CCW=우회전, 음수=CW=좌회전 → 좁은 쪽 호로 보정
            if (rightTurn  && arcTotalDeg < 0f) arcTotalDeg += 360f;
            if (!rightTurn && arcTotalDeg > 0f) arcTotalDeg -= 360f;
            if (arcTotalDeg >  180f) arcTotalDeg -= 360f;
            if (arcTotalDeg < -180f) arcTotalDeg += 360f;

            arcSweptDeg = 0f;
            isOnArc     = true;
        }

        bool ShouldStop()
        {
            Vector3 origin = rb.position + Vector3.up * 0.5f;

            if (Physics.SphereCast(origin, detectionRadius, transform.forward,
                    out RaycastHit lightHit, detectionDistance,
                    trafficLightLayer, QueryTriggerInteraction.Collide))
            {
                var light = lightHit.collider.GetComponentInParent<TrafficLight>();
                if (light != null && light.VehicleState != TrafficLightState.Green)
                    return true;
            }

            if (Physics.Raycast(origin, transform.forward, out RaycastHit carHit,
                    brakeDistance, vehicleLayer, QueryTriggerInteraction.Ignore))
            {
                if (carHit.collider.gameObject != gameObject)
                    return true;
            }

            return false;
        }

#if UNITY_EDITOR
        void OnDrawGizmosSelected()
        {
            Vector3 origin = transform.position + Vector3.up * 0.5f;
            Gizmos.color = Color.red;
            Gizmos.DrawRay(origin, transform.forward * detectionDistance);
            Gizmos.DrawWireSphere(origin, detectionRadius);
            Gizmos.DrawWireSphere(origin + transform.forward * detectionDistance, detectionRadius);
            Gizmos.color = Color.yellow;
            Gizmos.DrawRay(origin, transform.forward * brakeDistance);

            // 현재 원호 시각화
            if (isOnArc && arcRadius > 0.01f)
            {
                Gizmos.color = new Color(0f, 1f, 0.5f, 0.6f);
                int   steps    = 32;
                float remaining = arcTotalDeg - arcSweptDeg;
                Vector3 prev = arcCenter + Quaternion.AngleAxis(arcSweptDeg, Vector3.up) * arcStartVec * arcRadius;
                for (int i = 1; i <= steps; i++)
                {
                    float a = arcSweptDeg + remaining * (i / (float)steps);
                    Vector3 p = arcCenter + Quaternion.AngleAxis(a, Vector3.up) * arcStartVec * arcRadius;
                    Gizmos.DrawLine(prev + Vector3.up * 0.3f, p + Vector3.up * 0.3f);
                    prev = p;
                }
            }
        }
#endif
    }
}
