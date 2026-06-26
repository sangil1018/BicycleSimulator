using UnityEngine;

namespace TrafficSystem
{
    /// <summary>
    /// 차량 정지선 웨이포인트에 추가하는 마커.
    /// (현재 미사용 — CarController는 trafficLightLayer SphereCast로 직접 감지)
    /// 씬 뷰 시각화는 WaypointEditor.DrawGizmosAlways 에서 처리.
    /// </summary>
    public class TrafficLightStop : MonoBehaviour
    {
        [Tooltip("이 정지선을 제어하는 신호등 (참조용)")]
        public TrafficLight trafficLight;
    }
}
