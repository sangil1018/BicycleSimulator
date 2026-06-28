using UnityEngine;

namespace TrafficSystem
{
    // Timeline Signal Receiver의 UnityEvent에 SetRed / SetGreen / ClearOverride 를 연결하세요.
    // junction 또는 lights 중 하나만 써도 되고 둘 다 써도 됩니다.
    public class PedestrianSignalController : MonoBehaviour
    {
        [Header("제어 대상")]
        [Tooltip("교차로 전체 보행 신호를 일괄 제어합니다. (선택)")]
        [SerializeField] TrafficJunction junction;

        [Tooltip("개별 보행 신호등을 직접 제어합니다. (선택)")]
        [SerializeField] TrafficSignal[] lights;

        // ── Timeline Signal / UnityEvent 콜백 ────────────────────────────────
        // 파라미터가 없어야 Signal Receiver의 Dynamic → Static 모드로 연결됩니다.

        public void SetRed()
        {
            junction?.OverridePedestrianAll(PedestrianState.Red);
            if (lights != null)
                foreach (var l in lights)
                    l?.OverridePedestrianSignal(PedestrianState.Red);
        }

        public void SetGreen()
        {
            junction?.OverridePedestrianAll(PedestrianState.Green);
            if (lights != null)
                foreach (var l in lights)
                    l?.OverridePedestrianSignal(PedestrianState.Green);
        }

        // Junction 자동 사이클 복원
        public void ClearOverride()
        {
            junction?.ClearPedestrianOverrideAll();
            if (lights != null)
                foreach (var l in lights)
                    l?.ClearPedestrianOverride();
        }
    }
}
