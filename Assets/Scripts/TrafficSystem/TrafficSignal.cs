using UnityEngine;

namespace TrafficSystem
{
    public enum SignalState { Red, Yellow, Green }

    public class TrafficSignal : MonoBehaviour
    {
        [Header("렌더러 (빨 / 노 / 녹)")]
        [SerializeField] Renderer redRenderer;
        [SerializeField] Renderer yellowRenderer;
        [SerializeField] Renderer greenRenderer;

        [Header("State (read-only in Play)")]
        [SerializeField] SignalState state = SignalState.Red;

        public SignalState State => state;
        public bool CanPass => state == SignalState.Green;

        static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");
        static readonly MaterialPropertyBlock _mpb = new MaterialPropertyBlock();

        Color _redEmission, _yellowEmission, _greenEmission;

        void Start()
        {
            _redEmission    = InitEmission(redRenderer);
            _yellowEmission = InitEmission(yellowRenderer);
            _greenEmission  = InitEmission(greenRenderer);
            ApplyVisual();
        }

        // sharedMaterial에서 Emission 색상 캐시. 미설정(black)이면 white 반환.
        static Color InitEmission(Renderer r)
        {
            if (r == null || r.sharedMaterial == null) return Color.white;
            r.sharedMaterial.EnableKeyword("_EMISSION");
            Color c = r.sharedMaterial.GetColor(EmissionColorId);
            return (c.r + c.g + c.b) < 0.01f ? Color.white : c;
        }

        // TrafficJunction에서만 호출
        public void SetState(SignalState newState)
        {
            state = newState;
            ApplyVisual();
        }

        void ApplyVisual()
        {
            SetEmission(redRenderer,    _redEmission,    state == SignalState.Red);
            SetEmission(yellowRenderer, _yellowEmission, state == SignalState.Yellow);
            SetEmission(greenRenderer,  _greenEmission,  state == SignalState.Green);
        }

        static void SetEmission(Renderer r, Color emColor, bool on)
        {
            if (r == null) return;
            r.GetPropertyBlock(_mpb);
            _mpb.SetColor(EmissionColorId, on ? emColor : Color.black);
            r.SetPropertyBlock(_mpb);
        }

#if UNITY_EDITOR
        void OnDrawGizmosSelected()
        {
            Gizmos.color = state == SignalState.Green ? Color.green
                         : state == SignalState.Yellow ? Color.yellow
                         : Color.red;
            Gizmos.DrawWireSphere(transform.position, 0.4f);
        }
#endif
    }
}
