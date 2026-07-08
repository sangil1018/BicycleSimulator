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

        // 모든 페이즈의 합집합 (Start에서 수집)
        TrafficSignal[] allSignals;
        TrafficSignal[] allPedSignals;

        int   currentIdx;
        bool  inYellow;
        float timer;
        bool  paused;

        public int    PhaseCount      => phases?.Length ?? 0;
        public int    CurrentPhaseIdx => currentIdx;
        public bool   InYellow        => inYellow;
        public string CurrentPhaseName => (phases != null && phases.Length > 0)
            ? (inYellow ? $"{phases[currentIdx].label}_Yellow" : phases[currentIdx].label)
            : "—";

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
            if (allPedSignals == null) return;
            foreach (var l in allPedSignals) l?.OverridePedestrianSignal(state);
        }

        public void ClearPedestrianOverrideAll()
        {
            if (allPedSignals == null) return;
            foreach (var l in allPedSignals) l?.ClearPedestrianOverride();
        }

        public void OverrideVehicleAll(SignalState state)
        {
            if (allSignals == null) return;
            foreach (var s in allSignals) s?.OverrideVehicleSignal(state);
        }

        public void ClearVehicleOverrideAll()
        {
            if (allSignals == null) return;
            foreach (var s in allSignals) s?.ClearVehicleOverride();
        }

        // 오버라이드 해제 직후 호출 — 현재 신호 상태와 가장 일치하는 페이즈를 찾아
        // 그 페이즈부터 사이클을 재개 (해제 시 신호가 튀지 않고 자연스럽게 이어짐)
        public void ResumeFromCurrentState()
        {
            if (phases == null || phases.Length == 0) return;

            int bestIdx = 0, bestScore = int.MinValue, currentScore = int.MinValue;

            for (int i = 0; i < phases.Length; i++)
            {
                int score = MatchScore(phases[i]);
                if (i == currentIdx) currentScore = score;
                if (score > bestScore) { bestScore = score; bestIdx = i; }
            }

            // 동점이면 진행 중이던 페이즈 유지
            if (currentScore == bestScore) bestIdx = currentIdx;

            ApplyGreenPhase(bestIdx);
            paused = false;
        }

        // 현재 신호 상태가 해당 페이즈 정의와 일치하는 정도 (일치 +1 / 불일치 -1)
        int MatchScore(JunctionPhase phase)
        {
            int score = 0;

            if (allSignals != null)
                foreach (var s in allSignals)
                {
                    if (s == null) continue;
                    bool shouldGreen = Contains(phase.vehicleGreen, s);
                    bool isGreen     = s.State == SignalState.Green;
                    score += shouldGreen == isGreen ? 1 : -1;
                }

            if (allPedSignals != null)
                foreach (var l in allPedSignals)
                {
                    if (l == null) continue;
                    bool shouldGreen = Contains(phase.pedestrianGreen, l);
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
            if (!inYellow)
            {
                // 현재 페이즈의 차량 신호 → 황색, 보행 신호 건드리지 않음 (카운트다운 진행 중)
                if (phases[currentIdx].vehicleGreen != null)
                    foreach (var s in phases[currentIdx].vehicleGreen)
                        s?.SetState(SignalState.Yellow);
                inYellow = true;
                timer = yellowDuration;
            }
            else
            {
                currentIdx = (currentIdx + 1) % phases.Length;
                ApplyGreenPhase(currentIdx);
            }
        }

        void ApplyGreenPhase(int idx)
        {
            currentIdx = idx;
            inYellow   = false;

            // 모든 차량 신호 → 적색
            if (allSignals != null)
                foreach (var s in allSignals) s?.SetState(SignalState.Red);

            // 모든 보행 신호 → 적색
            if (allPedSignals != null)
                foreach (var l in allPedSignals) l?.ForcePedestrianState(PedestrianState.Red);

            var phase = phases[idx];

            // 이 페이즈의 차량 신호 → 초록
            if (phase.vehicleGreen != null)
                foreach (var s in phase.vehicleGreen)
                    s?.SetState(SignalState.Green);

            // 이 페이즈의 보행 신호 → 초록 (카운트다운 포함)
            if (phase.pedestrianGreen != null)
                foreach (var l in phase.pedestrianGreen)
                    l?.ForcePedestrianState(PedestrianState.Green, phase.pedestrianCountdown);

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
        }
    }
}
