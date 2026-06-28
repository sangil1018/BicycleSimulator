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

        void Start() => ApplyVisual();

        // TrafficJunction에서만 호출
        public void SetState(SignalState newState)
        {
            state = newState;
            ApplyVisual();
        }

        void ApplyVisual()
        {
            SetEmission(redRenderer,    state == SignalState.Red);
            SetEmission(yellowRenderer, state == SignalState.Yellow);
            SetEmission(greenRenderer,  state == SignalState.Green);
        }

        static void SetEmission(Renderer r, bool on)
        {
            if (r == null) return;
            var mat = r.material;
            if (on) mat.EnableKeyword("_EMISSION");
            else    mat.DisableKeyword("_EMISSION");
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
