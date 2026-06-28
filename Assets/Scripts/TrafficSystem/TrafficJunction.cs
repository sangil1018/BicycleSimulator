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
