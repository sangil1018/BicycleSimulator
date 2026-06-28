using System.Collections;
using UnityEngine;

namespace TrafficSystem
{
    // PedestrianState는 CrosswalkWaypoint가 직접 참조하므로 이 파일에 유지
    public enum PedestrianState { Red, Green }

    // 보행자 신호 전용. 차량 신호는 TrafficSignal 사용.
    // TrafficJunction이 ForcePedestrianState()로 구동.
    public class TrafficLight : MonoBehaviour
    {
        [Header("보행 신호 렌더러 (빨 / 녹)")]
        [SerializeField] Renderer pedestrianRedLight;
        [SerializeField] Renderer pedestrianGreenLight;

        [Header("보행 신호 깜빡임 간격 (seconds)")]
        [SerializeField] float blinkInterval = 0.2f;

        [SerializeField] PedestrianState pedestrianState = PedestrianState.Red;

        bool pedestrianOverridden;
        Coroutine pedestrianCoroutine;

        public PedestrianState PedestrianSignal    => pedestrianState;
        public bool            PedestrianOverridden => pedestrianOverridden;

        static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");
        static readonly MaterialPropertyBlock _mpb = new MaterialPropertyBlock();

        Color _redEmission, _greenEmission;

        void Start()
        {
            _redEmission   = InitEmission(pedestrianRedLight);
            _greenEmission = InitEmission(pedestrianGreenLight);
            ApplyPedestrianVisual();
        }

        // sharedMaterial에서 Emission 색상 캐시. 미설정(black)이면 white 반환.
        static Color InitEmission(Renderer r)
        {
            if (r == null || r.sharedMaterial == null) return Color.white;
            r.sharedMaterial.EnableKeyword("_EMISSION");
            Color c = r.sharedMaterial.GetColor(EmissionColorId);
            return (c.r + c.g + c.b) < 0.01f ? Color.white : c;
        }

        // TrafficJunction 사이클용 — 오버라이드 중이면 무시
        public void ForcePedestrianState(PedestrianState state, float greenCountdown = 0f)
        {
            if (pedestrianOverridden) return;

            StopBlink();
            pedestrianState = state;
            ApplyPedestrianVisual();

            if (state == PedestrianState.Green && greenCountdown > 0f)
                pedestrianCoroutine = StartCoroutine(PedestrianGreenRoutine(greenCountdown));
        }

        // 콘텐츠 제어용 — ClearPedestrianOverride() 전까지 Junction이 덮어쓰지 않음
        public void OverridePedestrianSignal(PedestrianState state)
        {
            pedestrianOverridden = true;
            StopBlink();
            pedestrianState = state;
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
            SetEmission(pedestrianRedLight,   _redEmission,   pedestrianState == PedestrianState.Red);
            SetEmission(pedestrianGreenLight, _greenEmission, pedestrianState == PedestrianState.Green);
        }

        IEnumerator PedestrianGreenRoutine(float duration)
        {
            float waitBeforeBlink = Mathf.Max(0f, duration - 1f);
            if (waitBeforeBlink > 0f)
                yield return new WaitForSeconds(waitBeforeBlink);

            float elapsed = 0f;
            float blinkDuration = Mathf.Min(1f, duration);
            while (elapsed < blinkDuration)
            {
                bool on = (Mathf.FloorToInt(elapsed / blinkInterval) % 2 == 0);
                SetEmission(pedestrianGreenLight, _greenEmission, on);
                SetEmission(pedestrianRedLight,   _redEmission,   false);
                yield return new WaitForSeconds(blinkInterval);
                elapsed += blinkInterval;
            }

            pedestrianState = PedestrianState.Red;
            ApplyPedestrianVisual();
            pedestrianCoroutine = null;
        }

        static void SetEmission(Renderer r, Color emColor, bool on)
        {
            if (r == null) return;
            r.GetPropertyBlock(_mpb);
            _mpb.SetColor(EmissionColorId, on ? emColor : Color.black);
            r.SetPropertyBlock(_mpb);
        }
    }
}
