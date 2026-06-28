using UnityEngine;

namespace TrafficSystem
{
    // 레거시 컴포넌트 — 사용하지 마세요.
    // 차량 신호 정지선 개념은 TrafficNode.stopSignal(TrafficSignal)로 대체되었습니다.
    [System.Obsolete("Use TrafficNode.stopSignal instead.")]
    public class TrafficLightStop : MonoBehaviour
    {
        [Tooltip("레거시 필드. TrafficNode.stopSignal 사용 권장.")]
        public TrafficLight trafficLight;
    }
}
