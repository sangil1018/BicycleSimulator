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

        [Header("차량 신호 연동")]
        [Tooltip("보행 신호 변경 시 차량 신호도 반대 상태로 함께 변경 (보행 초록→차량 빨강, 보행 빨강→차량 초록)")]
        [SerializeField] bool linkVehicleSignal = true;

        [Tooltip("ClearOverride 시 현재 신호 상태와 가장 일치하는 페이즈부터 사이클 재개. 끄면 백그라운드에서 진행되던 페이즈로 복귀.")]
        [SerializeField] bool resumeFromCurrentState = true;

        // ── Timeline Signal / UnityEvent 콜백 ────────────────────────────────
        // 파라미터가 없어야 Signal Receiver의 Dynamic → Static 모드로 연결됩니다.

        public void SetRed()
        {
            if (junction != null)
            {
                junction.OverridePedestrianAll(PedestrianState.Red);
                if (linkVehicleSignal) junction.OverrideVehicleAll(SignalState.Green);
            }
            if (lights != null)
                foreach (var l in lights)
                {
                    if (l == null) continue;
                    l.OverridePedestrianSignal(PedestrianState.Red);
                    if (linkVehicleSignal) l.OverrideVehicleSignal(SignalState.Green);
                }
        }

        public void SetGreen()
        {
            if (junction != null)
            {
                junction.OverridePedestrianAll(PedestrianState.Green);
                if (linkVehicleSignal) junction.OverrideVehicleAll(SignalState.Red);
            }
            if (lights != null)
                foreach (var l in lights)
                {
                    if (l == null) continue;
                    l.OverridePedestrianSignal(PedestrianState.Green);
                    if (linkVehicleSignal) l.OverrideVehicleSignal(SignalState.Red);
                }
        }

        // Junction 자동 사이클 복원
        public void ClearOverride()
        {
            if (junction != null)
            {
                junction.ClearPedestrianOverrideAll();
                junction.ClearVehicleOverrideAll();
                // 오버라이드 해제 후 호출해야 페이즈 적용이 차단되지 않음
                if (resumeFromCurrentState) junction.ResumeFromCurrentState();
            }
            if (lights != null)
                foreach (var l in lights)
                {
                    if (l == null) continue;
                    l.ClearPedestrianOverride();
                    l.ClearVehicleOverride();
                }
        }
    }
}
