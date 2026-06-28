using UnityEngine;

namespace TrafficSystem
{
    /// <summary>
    /// 횡단보도 입구 웨이포인트에 부착하는 마커 컴포넌트.
    /// PedestrianController가 이 컴포넌트를 감지하면 tLight 신호를 확인 후 통과합니다.
    /// tLight가 null이면 신호 대기 없이 자유 통과합니다.
    /// </summary>
    public class CrosswalkWaypoint : MonoBehaviour
    {
        [Tooltip("이 횡단보도 전용 신호등. 비워두면 경로 공통 신호등(fallback)을 사용합니다.")]
        public TrafficLight tLight;
    }
}
