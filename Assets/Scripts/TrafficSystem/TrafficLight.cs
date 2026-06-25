using System.Collections;
using UnityEngine;

namespace TrafficSystem
{
    public enum TrafficLightState { Red, Yellow, Green }
    public enum PedestrianState { Red, Green }

    public class TrafficLight : MonoBehaviour
    {
        [Header("차량 신호 렌더러 (빨 / 노 / 녹)")]
        [SerializeField] Renderer vehicleRedLight;
        [SerializeField] Renderer vehicleYellowLight;
        [SerializeField] Renderer vehicleGreenLight;

        [Header("보행 신호 렌더러 (빨 / 녹)")]
        [SerializeField] Renderer pedestrianRedLight;
        [SerializeField] Renderer pedestrianGreenLight;

        [Header("Timing — standalone 모드 전용 (seconds)")]
        [SerializeField] float greenDuration = 20f;
        [SerializeField] float yellowDuration = 4f;
        [SerializeField] float redDuration = 20f;

        [Header("보행 신호 깜빡임 간격 (seconds)")]
        [SerializeField] float blinkInterval = 0.2f;

        [Header("State (read-only in Play)")]
        [SerializeField] TrafficLightState vehicleState = TrafficLightState.Red;
        [SerializeField] PedestrianState pedestrianState = PedestrianState.Red;

        float timer;
        bool managedExternally;
        Coroutine pedestrianCoroutine;

        // ── Public API ────────────────────────────────────────────────────────
        public TrafficLightState VehicleState => vehicleState;
        public PedestrianState PedestrianSignal => pedestrianState;

        public void ForceVehicleState(TrafficLightState state)
        {
            managedExternally = true;
            vehicleState = state;
            ApplyVehicleVisual();
        }

        // greenCountdown > 0 이면 해당 시간 후 자동 Red 전환 (마지막 1초 깜빡임 포함)
        public void ForcePedestrianState(PedestrianState state, float greenCountdown = 0f)
        {
            managedExternally = true;
            if (pedestrianCoroutine != null)
            {
                StopCoroutine(pedestrianCoroutine);
                pedestrianCoroutine = null;
            }
            pedestrianState = state;
            ApplyPedestrianVisual();

            if (state == PedestrianState.Green && greenCountdown > 0f)
                pedestrianCoroutine = StartCoroutine(PedestrianGreenRoutine(greenCountdown));
        }

        // ── Internal ─────────────────────────────────────────────────────────
        void Start()
        {
            timer = DurationOf(vehicleState);
            ApplyVehicleVisual();
            ApplyPedestrianVisual();
        }

        void Update()
        {
            if (managedExternally) return;
            timer -= Time.deltaTime;
            if (timer <= 0f) AdvanceState();
        }

        void AdvanceState()
        {
            vehicleState = vehicleState switch
            {
                TrafficLightState.Green => TrafficLightState.Yellow,
                TrafficLightState.Yellow => TrafficLightState.Red,
                TrafficLightState.Red => TrafficLightState.Green,
                _ => TrafficLightState.Red
            };
            // standalone: 차량 빨강일 때만 보행 초록
            pedestrianState = vehicleState == TrafficLightState.Red
                ? PedestrianState.Green
                : PedestrianState.Red;

            timer = DurationOf(vehicleState);
            ApplyVehicleVisual();
            ApplyPedestrianVisual();
        }

        float DurationOf(TrafficLightState state) => state switch
        {
            TrafficLightState.Green => greenDuration,
            TrafficLightState.Yellow => yellowDuration,
            TrafficLightState.Red => redDuration,
            _ => redDuration
        };

        void ApplyVehicleVisual()
        {
            SetEmission(vehicleRedLight, vehicleState == TrafficLightState.Red);
            SetEmission(vehicleYellowLight, vehicleState == TrafficLightState.Yellow);
            SetEmission(vehicleGreenLight, vehicleState == TrafficLightState.Green);
        }

        void ApplyPedestrianVisual()
        {
            SetEmission(pedestrianRedLight,  pedestrianState == PedestrianState.Red);
            SetEmission(pedestrianGreenLight, pedestrianState == PedestrianState.Green);
        }

        IEnumerator PedestrianGreenRoutine(float duration)
        {
            float waitBeforeBlink = Mathf.Max(0f, duration - 1f);
            if (waitBeforeBlink > 0f)
                yield return new WaitForSeconds(waitBeforeBlink);

            // 적색 전환 1초 전부터 초록 렌더러 깜빡임
            float blinkDuration = Mathf.Min(1f, duration);
            float elapsed = 0f;
            while (elapsed < blinkDuration)
            {
                bool on = (Mathf.FloorToInt(elapsed / blinkInterval) % 2 == 0);
                SetEmission(pedestrianGreenLight, on);
                SetEmission(pedestrianRedLight, false);
                yield return new WaitForSeconds(blinkInterval);
                elapsed += blinkInterval;
            }

            pedestrianState = PedestrianState.Red;
            ApplyPedestrianVisual();
            pedestrianCoroutine = null;
        }

        static void SetEmission(Renderer r, bool on)
        {
            if (r == null) return;
            var mat = r.sharedMaterial;
            if (on) mat.EnableKeyword("_EMISSION");
            else mat.DisableKeyword("_EMISSION");
        }
    }
}
