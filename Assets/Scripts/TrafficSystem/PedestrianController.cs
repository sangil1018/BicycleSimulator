using UnityEngine;
using UnityEngine.AI;

namespace TrafficSystem
{
    [RequireComponent(typeof(NavMeshAgent))]
    public class PedestrianController : MonoBehaviour
    {
        [Header("Waypoints")]
        [SerializeField] Transform[] waypoints;
        [SerializeField] bool loop = true;

        [Header("Movement")]
        [SerializeField] float walkSpeed = 1.4f;
        [SerializeField] float waypointReachDistance = 0.6f;

        [Header("Crosswalk — 경로 공통 fallback 신호등")]
        [Tooltip("CrosswalkWaypoint.tLight 가 비어 있을 때 사용하는 기본 신호등")]
        [SerializeField] TrafficLight crosswalkLight;

        NavMeshAgent agent;
        int  currentIndex;
        bool waitingForSignal;
        TrafficLight currentCrosswalkSignal; // 현재 대기 중인 신호등 캐시

        public void Init(Transform[] assignedWaypoints, TrafficLight fallbackLight, int offset = 0)
        {
            waypoints      = assignedWaypoints;
            crosswalkLight = fallbackLight;
            currentIndex   = offset % Mathf.Max(1, waypoints.Length);
            // 재호출 시 이전 대기 상태 초기화
            waitingForSignal       = false;
            currentCrosswalkSignal = null;
            if (agent != null) agent.isStopped = false;
        }

        void Awake()
        {
            agent = GetComponent<NavMeshAgent>();
            agent.speed           = walkSpeed;
            agent.angularSpeed    = 240f;
            agent.acceleration    = 8f;
            agent.stoppingDistance = waypointReachDistance;
        }

        void Start()
        {
            if (waypoints != null && waypoints.Length > 0)
                SetDestination(waypoints[currentIndex]);
        }

        void Update()
        {
            if (waypoints == null || waypoints.Length == 0) return;
            if (agent.pathPending) return;

            if (waitingForSignal)
            {
                bool canCross = currentCrosswalkSignal == null
                    || currentCrosswalkSignal.PedestrianSignal == PedestrianState.Green;
                if (canCross)
                {
                    waitingForSignal = false;
                    agent.isStopped  = false;
                    SetDestination(waypoints[currentIndex]);
                }
                return;
            }

            if (!agent.hasPath) return;
            if (agent.remainingDistance > waypointReachDistance) return;

            // loop=false: 마지막 waypoint 도달 시 정지
            if (!loop && currentIndex >= waypoints.Length - 1)
            {
                agent.isStopped = true;
                return;
            }

            AdvanceWaypoint();
        }

        void AdvanceWaypoint()
        {
            currentIndex++;
            if (loop)
                currentIndex %= waypoints.Length;
            else
                currentIndex = Mathf.Min(currentIndex, waypoints.Length - 1);

            var wp = waypoints[currentIndex];
            if (wp == null) return;

            var marker = wp.GetComponent<CrosswalkWaypoint>();
            if (marker != null)
            {
                // 전용 신호등 우선, 없으면 경로 공통 fallback 사용
                var signal = marker.tLight != null ? marker.tLight : crosswalkLight;
                bool canCross = signal == null
                    || signal.PedestrianSignal == PedestrianState.Green;

                if (!canCross)
                {
                    currentCrosswalkSignal = signal;
                    waitingForSignal       = true;
                    agent.isStopped        = true;
                    agent.ResetPath();
                    return;
                }
            }

            SetDestination(wp);
        }

        void SetDestination(Transform wp)
        {
            if (wp == null) return;
            agent.isStopped = false;
            agent.SetDestination(wp.position);
        }
    }
}
