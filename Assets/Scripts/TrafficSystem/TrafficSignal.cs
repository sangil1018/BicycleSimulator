using System.Collections;
using UnityEngine;

namespace TrafficSystem
{
    public enum SignalState { Red, Yellow, Green }
    public enum PedestrianState { Red, Green }

    public class TrafficSignal : MonoBehaviour
    {
        [Header("차량 신호 렌더러 (빨 / 노 / 녹)")]
        [SerializeField] Renderer redRenderer;
        [SerializeField] Renderer yellowRenderer;
        [SerializeField] Renderer greenRenderer;

        [Header("보행자 신호 렌더러 (빨 / 녹)")]
        [SerializeField] Renderer pedestrianRedLight;
        [SerializeField] Renderer pedestrianGreenLight;

        [Header("차량 신호 상태 (read-only in Play)")]
        [SerializeField] SignalState state = SignalState.Red;

        [Header("보행자 신호")]
        [SerializeField] float blinkInterval = 0.2f;
        [SerializeField] PedestrianState pedestrianState = PedestrianState.Red;

        bool pedestrianOverridden;
        bool vehicleOverridden;
        Coroutine pedestrianCoroutine;

        public SignalState     State                => state;
        public bool            CanPass              => state == SignalState.Green;
        public PedestrianState PedestrianSignal     => pedestrianState;
        public bool            PedestrianOverridden => pedestrianOverridden;
        public bool            VehicleOverridden    => vehicleOverridden;

        static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");
        MaterialPropertyBlock _mpb;

        Color _redEmission, _yellowEmission, _greenEmission;
        Color _pedRedEmission, _pedGreenEmission;

        void Awake()
        {
            _mpb = new MaterialPropertyBlock();
        }

        void Start()
        {
            _redEmission      = InitEmission(redRenderer);
            _yellowEmission   = InitEmission(yellowRenderer);
            _greenEmission    = InitEmission(greenRenderer);
            _pedRedEmission   = InitEmission(pedestrianRedLight);
            _pedGreenEmission = InitEmission(pedestrianGreenLight);
            ApplyVehicleVisual();
            ApplyPedestrianVisual();
        }

        static Color InitEmission(Renderer r)
        {
            if (r == null || r.sharedMaterial == null) return Color.white;
            r.sharedMaterial.EnableKeyword("_EMISSION");
            Color c = r.sharedMaterial.GetColor(EmissionColorId);
            return (c.r + c.g + c.b) < 0.01f ? Color.white : c;
        }

        // TrafficJunction 사이클용 — 오버라이드 중이면 무시
        public void SetState(SignalState newState)
        {
            if (vehicleOverridden) return;
            state = newState;
            ApplyVehicleVisual();
        }

        // 콘텐츠 제어용 — ClearVehicleOverride() 전까지 Junction이 덮어쓰지 않음
        public void OverrideVehicleSignal(SignalState newState)
        {
            vehicleOverridden = true;
            state = newState;
            ApplyVehicleVisual();
        }

        public void ClearVehicleOverride() => vehicleOverridden = false;

        void ApplyVehicleVisual()
        {
            SetEmission(redRenderer,    _redEmission,    state == SignalState.Red);
            SetEmission(yellowRenderer, _yellowEmission, state == SignalState.Yellow);
            SetEmission(greenRenderer,  _greenEmission,  state == SignalState.Green);
        }

        // TrafficJunction 사이클용 — 오버라이드 중이면 무시
        public void ForcePedestrianState(PedestrianState newState, float greenCountdown = 0f)
        {
            if (pedestrianOverridden) return;
            StopBlink();
            pedestrianState = newState;
            ApplyPedestrianVisual();
            if (newState == PedestrianState.Green && greenCountdown > 0f)
                pedestrianCoroutine = StartCoroutine(PedestrianGreenRoutine(greenCountdown));
        }

        // 콘텐츠 제어용 — ClearPedestrianOverride() 전까지 Junction이 덮어쓰지 않음
        public void OverridePedestrianSignal(PedestrianState newState)
        {
            pedestrianOverridden = true;
            StopBlink();
            pedestrianState = newState;
            ApplyPedestrianVisual();
        }

        public void ClearPedestrianOverride() => pedestrianOverridden = false;

        void StopBlink()
        {
            if (pedestrianCoroutine == null) return;
            StopCoroutine(pedestrianCoroutine);
            pedestrianCoroutine = null;
        }

        void ApplyPedestrianVisual()
        {
            SetEmission(pedestrianRedLight,   _pedRedEmission,   pedestrianState == PedestrianState.Red);
            SetEmission(pedestrianGreenLight, _pedGreenEmission, pedestrianState == PedestrianState.Green);
        }

        IEnumerator PedestrianGreenRoutine(float duration)
        {
            float waitBeforeBlink = Mathf.Max(0f, duration - 1f);
            if (waitBeforeBlink > 0f)
                yield return new WaitForSeconds(waitBeforeBlink);

            float elapsed     = 0f;
            float blinkDuration = Mathf.Min(1f, duration);
            while (elapsed < blinkDuration)
            {
                bool on = (Mathf.FloorToInt(elapsed / blinkInterval) % 2 == 0);
                SetEmission(pedestrianGreenLight, _pedGreenEmission, on);
                SetEmission(pedestrianRedLight,   _pedRedEmission,   false);
                yield return new WaitForSeconds(blinkInterval);
                elapsed += blinkInterval;
            }

            pedestrianState = PedestrianState.Red;
            ApplyPedestrianVisual();
            pedestrianCoroutine = null;
        }

        void SetEmission(Renderer r, Color emColor, bool on)
        {
            if (r == null) return;
            r.GetPropertyBlock(_mpb);
            _mpb.SetColor(EmissionColorId, on ? emColor : Color.black);
            r.SetPropertyBlock(_mpb);
        }

#if UNITY_EDITOR
        void OnDrawGizmosSelected()
        {
            Gizmos.color = state == SignalState.Green  ? Color.green
                         : state == SignalState.Yellow ? Color.yellow
                         : Color.red;
            Gizmos.DrawWireSphere(transform.position, 0.4f);
        }
#endif
    }
}
