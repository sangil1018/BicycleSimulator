using System;
using System.Collections.Generic;
using UnityEngine;

namespace TrafficSystem
{
    [Serializable]
    public class JunctionPhase
    {
        [Tooltip("Inspector 식별용 이름")]
        public string label = "Phase";
        [Tooltip("이 페이즈에서 초록이 되는 차량 신호들")]
        public TrafficSignal[] vehicleGreen;
        [Tooltip("이 페이즈에서 보행 초록이 되는 신호등들 (선택)")]
        public TrafficSignal[] pedestrianGreen;
        [Tooltip("초록 지속 시간 (초)")]
        public float greenDuration = 20f;
        [Tooltip("보행 신호 깜빡임 카운트다운 시간 (초). 0이면 깜빡임 없음.")]
        public float pedestrianCountdown = 10f;
    }

    public class TrafficJunction : MonoBehaviour
    {
        [SerializeField] JunctionPhase[] phases;
        [SerializeField] float yellowDuration = 4f;

        [Header("T자형 교차로")]
        [Tooltip("T자형 교차로로 취급한다. 체크하면 페이즈별로 방향을 나누지 않고 " +
                 "모든 차량 신호가 동시에 초록/황색/적색으로 바뀐다.\n" +
                 "짝수 페이즈 = 전체 통행, 홀수 페이즈 = 전체 정지 + 전체 보행 초록.\n" +
                 "페이즈 개수와 지속 시간(사이클 길이)은 그대로 사용한다.")]
        [SerializeField] bool tShaped;

        // 모든 페이즈의 합집합 (Start에서 수집)
        TrafficSignal[] allSignals;      // vehicleGreen 합집합
        TrafficSignal[] allPedSignals;   // pedestrianGreen 합집합
        TrafficSignal[] allAnySignals;   // 위 둘의 합집합 — T 모드에서 전체 동시 전환 대상

        int   currentIdx;
        bool  inYellow;
        float timer;
        bool  paused;

        public int    PhaseCount      => phases?.Length ?? 0;
        public int    CurrentPhaseIdx => currentIdx;
        public bool   InYellow        => inYellow;
        public bool   TShaped         { get => tShaped; set => tShaped = value; }

        public string CurrentPhaseName
        {
            get
            {
                if (phases == null || phases.Length == 0) return "—";
                string label = tShaped
                    ? (IsGoPhase(currentIdx) ? "전체 통행" : "전체 정지 + 보행")
                    : phases[currentIdx].label;
                return inYellow ? $"{label}_Yellow" : label;
            }
        }

        // T 모드에서 차량이 통행하는 페이즈인지 (짝수 = 통행, 홀수 = 정지)
        static bool IsGoPhase(int idx) => (idx & 1) == 0;

        // 신호 상태를 적용할 대상 — T 모드에서는 차량/보행 모두 전체 신호
        TrafficSignal[] VehicleTargets    => tShaped ? allAnySignals : allSignals;
        TrafficSignal[] PedestrianTargets => tShaped ? allAnySignals : allPedSignals;

        // 페이즈 idx에서 이 차량 신호가 초록이어야 하는지
        bool ShouldVehicleGreen(int idx, TrafficSignal s) =>
            tShaped ? IsGoPhase(idx) : Contains(phases[idx].vehicleGreen, s);

        // 페이즈 idx에서 이 보행 신호가 초록이어야 하는지
        bool ShouldPedestrianGreen(int idx, TrafficSignal l) =>
            tShaped ? !IsGoPhase(idx) : Contains(phases[idx].pedestrianGreen, l);

        void Start()
        {
            if (phases == null || phases.Length == 0) return;
            CollectAll();
            ApplyGreenPhase(0);
        }

        void Update()
        {
            if (paused || phases == null || phases.Length == 0) return;
            timer -= Time.deltaTime;
            if (timer <= 0f) AdvanceState();
        }

        // ── Public API ────────────────────────────────────────────────────────

        // 지정 페이즈로 즉시 전환 (타이머 자동 고정)
        public void ForcePhase(int idx)
        {
            if (phases == null || idx < 0 || idx >= phases.Length) return;
            ApplyGreenPhase(idx);
            paused = true;
        }

        public void Pause()  => paused = true;
        public void Resume() => paused = false;

        public void OverridePedestrianAll(PedestrianState state)
        {
            var targets = PedestrianTargets;
            if (targets == null) return;
            foreach (var l in targets) if (l != null) l.OverridePedestrianSignal(state);
        }

        public void ClearPedestrianOverrideAll()
        {
            var targets = PedestrianTargets;
            if (targets == null) return;
            foreach (var l in targets) if (l != null) l.ClearPedestrianOverride();
        }

        public void OverrideVehicleAll(SignalState state)
        {
            var targets = VehicleTargets;
            if (targets == null) return;
            foreach (var s in targets) if (s != null) s.OverrideVehicleSignal(state);
        }

        public void ClearVehicleOverrideAll()
        {
            var targets = VehicleTargets;
            if (targets == null) return;
            foreach (var s in targets) if (s != null) s.ClearVehicleOverride();
        }

        // 오버라이드 해제 직후 호출 — 현재 신호 상태와 가장 일치하는 페이즈를 찾아
        // 그 페이즈부터 사이클을 재개 (해제 시 신호가 튀지 않고 자연스럽게 이어짐)
        public void ResumeFromCurrentState()
        {
            if (phases == null || phases.Length == 0) return;

            int bestIdx = 0, bestScore = int.MinValue, currentScore = int.MinValue;

            for (int i = 0; i < phases.Length; i++)
            {
                int score = MatchScore(i);
                if (i == currentIdx) currentScore = score;
                if (score > bestScore) { bestScore = score; bestIdx = i; }
            }

            // 동점이면 진행 중이던 페이즈 유지
            if (currentScore == bestScore) bestIdx = currentIdx;

            ApplyGreenPhase(bestIdx);
            paused = false;
        }

        // 현재 신호 상태가 해당 페이즈 정의와 일치하는 정도 (일치 +1 / 불일치 -1)
        int MatchScore(int idx)
        {
            int score = 0;

            var vTargets = VehicleTargets;
            if (vTargets != null)
                foreach (var s in vTargets)
                {
                    if (s == null) continue;
                    bool shouldGreen = ShouldVehicleGreen(idx, s);
                    bool isGreen     = s.State == SignalState.Green;
                    score += shouldGreen == isGreen ? 1 : -1;
                }

            var pTargets = PedestrianTargets;
            if (pTargets != null)
                foreach (var l in pTargets)
                {
                    if (l == null) continue;
                    bool shouldGreen = ShouldPedestrianGreen(idx, l);
                    bool isGreen     = l.PedestrianSignal == PedestrianState.Green;
                    score += shouldGreen == isGreen ? 1 : -1;
                }

            return score;
        }

        static bool Contains(TrafficSignal[] arr, TrafficSignal s)
        {
            if (arr == null) return false;
            foreach (var x in arr)
                if (x == s) return true;
            return false;
        }

        // ── Internal ─────────────────────────────────────────────────────────

        void AdvanceState()
        {
            // 초록이던 차량 신호가 없는 페이즈는 황색 단계를 건너뜀 (아무것도 안 바뀌는 대기 시간 제거)
            if (!inYellow && NeedsYellow(currentIdx))
            {
                // 초록이던 차량 신호 → 황색, 보행 신호 건드리지 않음 (카운트다운 진행 중)
                // T 모드에서는 전체가 초록이었으므로 전체를 황색으로 바꾼다.
                var targets = tShaped ? VehicleTargets : phases[currentIdx].vehicleGreen;
                foreach (var s in targets)
                    if (s != null) s.SetState(SignalState.Yellow);
                inYellow = true;
                timer = yellowDuration;
            }
            else
            {
                currentIdx = (currentIdx + 1) % phases.Length;
                ApplyGreenPhase(currentIdx);
            }
        }

        // 이 페이즈 종료 시 황색 단계가 필요한지 — 초록이던 차량 신호가 있어야 의미가 있다
        bool NeedsYellow(int idx) =>
            tShaped ? IsGoPhase(idx) : HasVehicleSignal(phases[idx]);

        static bool HasVehicleSignal(JunctionPhase phase)
        {
            if (phase.vehicleGreen == null) return false;
            foreach (var s in phase.vehicleGreen)
                if (s != null) return true;
            return false;
        }

        void ApplyGreenPhase(int idx)
        {
            currentIdx = idx;
            inYellow   = false;

            var phase = phases[idx];

            // 차량 신호 — 일반 모드는 페이즈에 등록된 것만, T 모드는 전체가 동시에 초록/적색
            var vTargets = VehicleTargets;
            if (vTargets != null)
                foreach (var s in vTargets)
                    if (s != null)
                        s.SetState(ShouldVehicleGreen(idx, s) ? SignalState.Green : SignalState.Red);

            // 보행 신호 — 초록일 때만 카운트다운 시작
            var pTargets = PedestrianTargets;
            if (pTargets != null)
                foreach (var l in pTargets)
                {
                    if (l == null) continue;
                    if (ShouldPedestrianGreen(idx, l))
                        l.ForcePedestrianState(PedestrianState.Green, phase.pedestrianCountdown);
                    else
                        l.ForcePedestrianState(PedestrianState.Red);
                }

            timer = phase.greenDuration;
        }

        void CollectAll()
        {
            var sigSet   = new HashSet<TrafficSignal>();
            var lightSet = new HashSet<TrafficSignal>();

            foreach (var phase in phases)
            {
                if (phase.vehicleGreen != null)
                    foreach (var s in phase.vehicleGreen)
                        if (s != null) sigSet.Add(s);

                if (phase.pedestrianGreen != null)
                    foreach (var l in phase.pedestrianGreen)
                        if (l != null) lightSet.Add(l);
            }

            allSignals = new TrafficSignal[sigSet.Count];
            sigSet.CopyTo(allSignals);
            allPedSignals = new TrafficSignal[lightSet.Count];
            lightSet.CopyTo(allPedSignals);

            // T 모드에서 동시 전환할 전체 신호 — 차량/보행 어느 쪽에든 등록된 것 모두
            sigSet.UnionWith(lightSet);
            allAnySignals = new TrafficSignal[sigSet.Count];
            sigSet.CopyTo(allAnySignals);
        }
    }
}
