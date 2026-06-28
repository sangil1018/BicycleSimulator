using System.Collections.Generic;
using UnityEngine;

namespace TrafficSystem
{
    public enum PedestrianType { Adult, Child }
    public enum WalkMode { PingPong, Loop, OneShot }

    public class PedestrianController : MonoBehaviour
    {
        [Header("Movement")]
        [SerializeField] WalkMode walkMode = WalkMode.PingPong;
        [Tooltip("스플라인 포인트 도달 판정 거리 (m)")]
        [SerializeField] float reachDist   = 0.15f;
        [Tooltip("초당 회전 각도 (낮을수록 느리게 꺽임)")]
        [SerializeField] float turnSpeed   = 120f;
        [Tooltip("Bezier 곡선 구간 분할 수 (높을수록 부드러움)")]
        [SerializeField] int   splineSteps = 12;

        // 중심선 경로 (오프셋 미포함, 직선 + Bezier 혼합)
        Vector3[] centerPath;
        int   pathIdx;
        int   pathDir    = 1;
        float sideOffset;
        float lateralMag;
        float speed;

        // CrosswalkWaypoint 신호 대기: pathIdx → TrafficLight 매핑
        Dictionary<int, TrafficLight> crosswalkGates;
        bool waitingAtCrosswalk;

        Animator anim;
        static readonly int HashSpeed = Animator.StringToHash("Speed");

        public void Init(Transform[] wps, Vector3 spawnPos, int nextWpIndex, bool reverse,
                         float walkSpeed, float lateralOffset, float blendRadius)
        {
            speed      = walkSpeed;
            lateralMag = lateralOffset;
            pathDir    = reverse ? -1 : 1;
            sideOffset = reverse ? -lateralOffset : lateralOffset;

            centerPath = BakePath(wps, blendRadius, splineSteps);

            // CrosswalkWaypoint가 있는 원본 웨이포인트 위치를 centerPath 인덱스에 매핑
            crosswalkGates = new Dictionary<int, TrafficLight>();
            foreach (var wp in wps)
            {
                if (wp == null || !wp.TryGetComponent(out CrosswalkWaypoint cwp)) continue;
                if (cwp.tLight != null)
                    crosswalkGates[NearestIdx(wp.position)] = cwp.tLight;
            }

            pathIdx            = NearestIdx(spawnPos);
            transform.position = OffsetPos(pathIdx);

            int peek = Mathf.Clamp(pathIdx + pathDir, 0, centerPath.Length - 1);
            Vector3 initDir = centerPath[peek] - centerPath[pathIdx];
            initDir.y = 0f;
            if (initDir.sqrMagnitude > 0.0001f)
                transform.rotation = Quaternion.LookRotation(initDir);

            if (TryGetComponent(out anim))
                anim.SetFloat(HashSpeed, speed);
        }

        // 중간 웨이포인트만 Bezier 보간, 나머지는 직선
        // blendRadius : 각 중간 웨이포인트 앞뒤로 보간 적용할 거리 (m)
        public static Vector3[] BakePath(Transform[] wps, float blendRadius, int bezierSteps)
        {
            var pts = new List<Vector3>();
            int n   = wps.Length;
            if (n < 2) return pts.ToArray();

            pts.Add(wps[0].position);

            for (int i = 1; i < n - 1; i++)
            {
                Vector3 prev = wps[i - 1].position;
                Vector3 curr = wps[i].position;
                Vector3 next = wps[i + 1].position;

                Vector3 inDir  = (curr - prev).normalized;
                Vector3 outDir = (next - curr).normalized;

                float inLen    = Vector3.Distance(prev, curr);
                float outLen   = Vector3.Distance(curr, next);
                float actBlend = Mathf.Min(blendRadius, inLen * 0.5f, outLen * 0.5f);

                Vector3 entry = curr - inDir  * actBlend; // 보간 시작점
                Vector3 exit  = curr + outDir * actBlend; // 보간 끝점

                // 직선 구간: 마지막 점 → entry
                AddStraight(pts, pts[pts.Count - 1], entry);

                // Bezier 곡선 구간: entry → exit
                Vector3 cp1 = entry + inDir  * (actBlend * 0.55f);
                Vector3 cp2 = exit  - outDir * (actBlend * 0.55f);
                AddBezier(pts, entry, cp1, cp2, exit, bezierSteps);
            }

            // 마지막 직선: 마지막 점 → wps[n-1]
            AddStraight(pts, pts[pts.Count - 1], wps[n - 1].position);

            return pts.ToArray();
        }

        static void AddStraight(List<Vector3> pts, Vector3 from, Vector3 to)
        {
            const float step = 0.25f;
            float dist = Vector3.Distance(from, to);
            if (dist < 0.001f) return;
            int cnt = Mathf.Max(1, Mathf.RoundToInt(dist / step));
            for (int i = 1; i <= cnt; i++)
                pts.Add(Vector3.Lerp(from, to, (float)i / cnt));
        }

        static void AddBezier(List<Vector3> pts, Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, int steps)
        {
            for (int i = 1; i <= steps; i++)
            {
                float t  = (float)i / steps;
                float u  = 1f - t;
                float uu = u * u;
                float tt = t * t;
                pts.Add(uu * u * p0 + 3f * uu * t * p1 + 3f * u * tt * p2 + tt * t * p3);
            }
        }

        // centerPath[idx]에 현재 sideOffset 적용한 월드 위치 반환
        Vector3 OffsetPos(int idx)
        {
            if (Mathf.Approximately(sideOffset, 0f)) return centerPath[idx];

            int last = centerPath.Length - 1;
            int a    = Mathf.Max(idx - 1, 0);
            int b    = Mathf.Min(idx + 1, last);
            Vector3 tangent = centerPath[b] - centerPath[a];
            tangent.y = 0f;
            if (tangent.sqrMagnitude < 0.0001f) return centerPath[idx];

            Vector3 right = Vector3.Cross(Vector3.up, tangent.normalized);
            return centerPath[idx] + right * sideOffset;
        }

        int NearestIdx(Vector3 pos)
        {
            int   best    = 0;
            float bestSqr = float.MaxValue;
            for (int i = 0; i < centerPath.Length; i++)
            {
                float d = (centerPath[i] - pos).sqrMagnitude;
                if (d < bestSqr) { bestSqr = d; best = i; }
            }
            return best;
        }

        void Update()
        {
            if (centerPath == null || centerPath.Length == 0) return;

            // CrosswalkWaypoint 신호 대기
            if (waitingAtCrosswalk)
            {
                crosswalkGates.TryGetValue(pathIdx, out var gateLight);
                if (gateLight == null || gateLight.PedestrianSignal == PedestrianState.Green)
                    waitingAtCrosswalk = false;
                else
                    return;
            }

            Vector3 goal   = OffsetPos(pathIdx);
            Vector3 toGoal = goal - transform.position;
            toGoal.y = 0f;

            if (toGoal.sqrMagnitude > 0.0001f)
            {
                Quaternion targetRot = Quaternion.LookRotation(toGoal);
                transform.rotation   = Quaternion.RotateTowards(
                    transform.rotation, targetRot, turnSpeed * Time.deltaTime);
            }

            transform.position = Vector3.MoveTowards(transform.position, goal, speed * Time.deltaTime);

            if (Vector3.Distance(transform.position, goal) <= reachDist)
                Advance();
        }

        void Advance()
        {
            int next = pathIdx + pathDir;
            int last = centerPath.Length - 1;

            switch (walkMode)
            {
                case WalkMode.PingPong:
                    if (next < 0 || next > last)
                    {
                        pathDir    = -pathDir;
                        sideOffset = -sideOffset;
                        next       = Mathf.Clamp(pathIdx + pathDir, 0, last);
                    }
                    pathIdx = next;
                    break;

                case WalkMode.Loop:
                    pathIdx = ((next % centerPath.Length) + centerPath.Length) % centerPath.Length;
                    break;

                case WalkMode.OneShot:
                    if (next < 0 || next > last)
                    {
                        if (anim != null) anim.SetFloat(HashSpeed, 0f);
                        enabled = false;
                        return;
                    }
                    pathIdx = next;
                    break;
            }

            CheckCrosswalkGate();
        }

        void CheckCrosswalkGate()
        {
            if (crosswalkGates == null) return;
            if (!crosswalkGates.TryGetValue(pathIdx, out var light)) return;
            if (light != null && light.PedestrianSignal != PedestrianState.Green)
                waitingAtCrosswalk = true;
        }
    }
}
