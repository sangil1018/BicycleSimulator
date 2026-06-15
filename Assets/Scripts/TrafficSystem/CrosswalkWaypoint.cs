using UnityEngine;

namespace TrafficSystem
{
    /// <summary>
    /// Marker component — attach to a waypoint Transform that sits at a crosswalk entrance.
    /// PedestrianController checks this component before proceeding.
    /// Assign <tLight> for per-crosswalk signal control.
    /// If <tLight> is null, PedestrianController falls back to the route-level crosswalkLight.
    /// </summary>
    public class CrosswalkWaypoint : MonoBehaviour
    {
        [Tooltip("이 횡단보도 전용 신호등. 비워두면 경로 공통 신호등(fallback)을 사용합니다.")]
        public TrafficLight tLight;
    }
}
