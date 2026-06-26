using UnityEngine;

namespace TrafficSystem
{
    public class TrafficIntersection : MonoBehaviour
    {
        [Header("신호등 그룹 (프리팹 1개 = 차량+보행 통합, 각 그룹 4등)")]
        [SerializeField] TrafficLight[] groupA;   // N-S 방향 4등
        [SerializeField] TrafficLight[] groupB;   // E-W 방향 4등

        [Header("Timing (seconds)")]
        [SerializeField] float greenDuration = 20f;
        [SerializeField] float yellowDuration = 4f;

        public enum Phase { AGreen, AYellow, BGreen, BYellow }

        Phase currentPhase;
        float timer;
        bool timerPaused;

        public string CurrentPhaseName => currentPhase.ToString();
        public Phase CurrentPhase => currentPhase;

        void Start() => SetPhase(Phase.AGreen);

        void Update()
        {
            if (timerPaused) return;
            timer -= Time.deltaTime;
            if (timer <= 0f) AdvancePhase();
        }

        // ── Public API ────────────────────────────────────────────────────────

        // Timeline Signal — 페이즈 강제 전환 + 타이머 자동 고정
        public void ForcePhase(Phase phase)
        {
            SetPhase(phase);
            timerPaused = true;
        }

        // Timeline Signal 편의 메서드 (파라미터 없는 버전)
        public void ForcePhaseAGreen() => ForcePhase(Phase.AGreen);
        public void ForcePhaseAYellow() => ForcePhase(Phase.AYellow);
        public void ForcePhaseBGreen() => ForcePhase(Phase.BGreen);
        public void ForcePhaseBYellow() => ForcePhase(Phase.BYellow);

        // Timeline Signal — 남은 타이머부터 자동 순환 재개
        public void ResumeTimer() => timerPaused = false;

        public void AdvancePhase()
        {
            Phase next = currentPhase switch
            {
                Phase.AGreen => Phase.AYellow,
                Phase.AYellow => Phase.BGreen,
                Phase.BGreen => Phase.BYellow,
                Phase.BYellow => Phase.AGreen,
                _ => Phase.AGreen
            };
            SetPhase(next);
        }

        // ── 콘텐츠 제어용 보행 신호 오버라이드 ────────────────────────────────

        // 교차로 전체 보행 신호 고정 (인터섹션 사이클은 계속 진행)
        public void OverridePedestrianAll(PedestrianState state)
        {
            OverridePedestrianGroup(groupA, state);
            OverridePedestrianGroup(groupB, state);
        }

        public void OverridePedestrianGroupA(PedestrianState state) => OverridePedestrianGroup(groupA, state);
        public void OverridePedestrianGroupB(PedestrianState state) => OverridePedestrianGroup(groupB, state);

        // 오버라이드 해제 — 이후 인터섹션 페이즈 전환 시 자동으로 신호 복원됨
        public void ClearPedestrianOverrideAll()
        {
            ClearGroupOverride(groupA);
            ClearGroupOverride(groupB);
        }

        static void OverridePedestrianGroup(TrafficLight[] group, PedestrianState state)
        {
            if (group == null) return;
            foreach (var l in group) l?.OverridePedestrianSignal(state);
        }

        static void ClearGroupOverride(TrafficLight[] group)
        {
            if (group == null) return;
            foreach (var l in group) l?.ClearPedestrianOverride();
        }

        // ── Internal ─────────────────────────────────────────────────────────
        void SetPhase(Phase phase)
        {
            currentPhase = phase;
            switch (phase)
            {
                case Phase.AGreen:
                    ForceGroup(groupA, TrafficLightState.Green, PedestrianState.Red);
                    ForceGroup(groupB, TrafficLightState.Red, PedestrianState.Green, greenDuration / 2f);
                    timer = greenDuration;
                    break;
                case Phase.AYellow:
                    ForceGroup(groupA, TrafficLightState.Yellow, PedestrianState.Red);
                    ForceGroup(groupB, TrafficLightState.Red, PedestrianState.Green); // 보행 유지, 카운트다운 없음
                    timer = yellowDuration;
                    break;
                case Phase.BGreen:
                    ForceGroup(groupA, TrafficLightState.Red, PedestrianState.Green, greenDuration / 2f);
                    ForceGroup(groupB, TrafficLightState.Green, PedestrianState.Red);
                    timer = greenDuration;
                    break;
                case Phase.BYellow:
                    ForceGroup(groupA, TrafficLightState.Red, PedestrianState.Green); // 보행 유지, 카운트다운 없음
                    ForceGroup(groupB, TrafficLightState.Yellow, PedestrianState.Red);
                    timer = yellowDuration;
                    break;
            }
        }

        static void ForceGroup(TrafficLight[] group, TrafficLightState vState, PedestrianState pState, float pedGreenDuration = 0f)
        {
            if (group == null) return;
            foreach (var light in group)
            {
                if (light == null) continue;
                light.ForceVehicleState(vState);
                light.ForcePedestrianState(pState, pedGreenDuration);
            }
        }
    }
}
