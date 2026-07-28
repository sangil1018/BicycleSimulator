using System;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Splines;

/// <summary>
/// 손으로 찍은 웨이포인트로 주행 경로를 구성하는 오써링 데이터.
/// 각 포인트는 라운드(모서리 반경) 값을 가지며, 직선 구간 + 모서리 곡선으로
/// 같은 오브젝트의 SplineContainer에 그대로 구워진다.
///
/// 결과물이 SplineContainer이므로 Route Spline Baker(타임라인 베이크)로 만든 경로와
/// 완전히 동일하게 RoadNavigationGuide 등에서 사용할 수 있다.
///
/// 편집: Tools ▸ Navigation ▸ Route Spline Baker ▸ [루트 스플라인 웨이포인트] 탭
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(SplineContainer))]
[AddComponentMenu("Navigation/Route Waypoint Path")]
public class RouteWaypointPath : MonoBehaviour
{
    [Serializable]
    public class Waypoint
    {
        [Tooltip("경로 오브젝트 로컬 좌표")]
        public Vector3 position;

        [Tooltip("이 포인트의 모서리 라운드 반경(m). 0 = 각진 모서리")]
        [Min(0f)] public float round = 2f;

        public Waypoint() { }
        public Waypoint(Vector3 localPosition, float round)
        {
            position   = localPosition;
            this.round = round;
        }
    }

    [Tooltip("경로를 구성하는 웨이포인트. 순서 = 주행 방향")]
    [SerializeField] List<Waypoint> waypoints = new();

    [Tooltip("마지막 포인트와 첫 포인트를 이어 순환 경로로 만듭니다")]
    [SerializeField] bool closed;

    [Tooltip("새 포인트를 찍을 때 기본으로 들어가는 라운드 값(m)")]
    [Min(0f)]
    [SerializeField] float defaultRound = 2f;

    public List<Waypoint> Waypoints => waypoints;

    public bool Closed
    {
        get => closed;
        set => closed = value;
    }

    public float DefaultRound
    {
        get => defaultRound;
        set => defaultRound = Mathf.Max(0f, value);
    }

    SplineContainer _container;
    public SplineContainer Container => _container != null ? _container : _container = GetComponent<SplineContainer>();

    // ── 스플라인 굽기 ──────────────────────────────────────────────

    /// <summary>웨이포인트를 같은 오브젝트의 SplineContainer에 굽는다.</summary>
    public void ApplyToSpline()
    {
        var container = Container;
        if (container == null) return;

        var spline = container.Spline;
        spline.Clear();
        spline.Closed = closed && waypoints.Count >= 3;

        if (waypoints.Count < 2)
        {
            // 포인트 1개짜리는 경로가 아니지만, 찍는 도중 상태이므로 그대로 남겨둔다.
            if (waypoints.Count == 1)
                spline.Add(new BezierKnot((float3)waypoints[0].position), TangentMode.Linear);
            return;
        }

        var knots = new List<BezierKnot>(waypoints.Count * 2);
        BuildKnots(waypoints, closed, knots);

        foreach (var knot in knots)
            spline.Add(knot, TangentMode.Broken);   // Broken = 우리가 계산한 탄젠트를 그대로 유지
    }

    /// <summary>
    /// 웨이포인트 → 베지어 노트. 모서리마다 라운드 반경만큼 앞뒤로 물러난
    /// 진입/진출 노트를 만들어 직선 구간은 완전한 직선으로, 모서리는 곡선으로 남긴다.
    /// (보행자 웨이포인트의 blendRadius 보간과 같은 방식)
    /// </summary>
    public static void BuildKnots(IReadOnlyList<Waypoint> points, bool closed, List<BezierKnot> result)
    {
        result.Clear();

        int n = points.Count;
        if (n < 2) return;

        // 포인트가 2개뿐이면 순환 경로가 성립하지 않는다 (앞뒤 이웃이 같은 점)
        if (n < 3) closed = false;

        var nodes = new List<Node>(n * 2);

        if (!closed)
        {
            // 시작점: 두 번째 포인트를 향하는 직선 탄젠트
            Vector3 startDir = SafeDir(points[0].position, points[1].position);
            nodes.Add(new Node { pos = points[0].position, dirIn = startDir, dirOut = startDir });

            for (int i = 1; i < n - 1; i++)
                AddCorner(points, i, n, nodes);

            Vector3 endDir = SafeDir(points[n - 2].position, points[n - 1].position);
            nodes.Add(new Node { pos = points[n - 1].position, dirIn = endDir, dirOut = endDir });
        }
        else
        {
            for (int i = 0; i < n; i++)
                AddCorner(points, i, n, nodes);
        }

        int m = nodes.Count;
        if (m < 2) return;

        // 구간별 탄젠트 길이 결정
        //  · 모서리 곡선 구간: 라운드 반경 × 0.55 (원호에 가까운 베지어)
        //  · 직선 구간       : 구간 길이 / 3 (양끝 탄젠트가 일직선 → 완전한 직선)
        int segments = closed ? m : m - 1;
        for (int k = 0; k < segments; k++)
        {
            int next = (k + 1) % m;

            Node a = nodes[k];
            Node b = nodes[next];

            float len = a.arcWithNext
                ? a.arcTangent
                : Vector3.Distance(a.pos, b.pos) / 3f;

            a.tangentOutLen = len;
            b.tangentInLen  = len;

            nodes[k]    = a;
            nodes[next] = b;
        }

        if (!closed)
        {
            // 열린 경로의 양 끝은 바깥쪽 탄젠트를 쓰지 않지만, 0으로 두면
            // 에디터에서 노트를 만졌을 때 튀므로 안쪽 길이를 그대로 복사해둔다.
            Node first = nodes[0];
            first.tangentInLen = first.tangentOutLen;
            nodes[0] = first;

            Node last = nodes[m - 1];
            last.tangentOutLen = last.tangentInLen;
            nodes[m - 1] = last;
        }

        foreach (var node in nodes)
        {
            result.Add(new BezierKnot(
                (float3)node.pos,
                -(float3)(node.dirIn * node.tangentInLen),
                 (float3)(node.dirOut * node.tangentOutLen),
                quaternion.identity));
        }
    }

    /// <summary>웨이포인트를 따라가는 폴리라인. 씬 미리보기·길이 계산용.</summary>
    public static void BuildPolyline(IReadOnlyList<Waypoint> points, bool closed, int cornerSteps, List<Vector3> result)
    {
        result.Clear();

        var knots = new List<BezierKnot>();
        BuildKnots(points, closed, knots);
        if (knots.Count < 2) return;

        cornerSteps = Mathf.Max(2, cornerSteps);

        int segments = closed ? knots.Count : knots.Count - 1;
        result.Add((Vector3)knots[0].Position);

        for (int i = 0; i < segments; i++)
        {
            BezierKnot a = knots[i];
            BezierKnot b = knots[(i + 1) % knots.Count];

            Vector3 p0 = (Vector3)a.Position;
            Vector3 p1 = p0 + (Vector3)a.TangentOut;
            Vector3 p3 = (Vector3)b.Position;
            Vector3 p2 = p3 + (Vector3)b.TangentIn;

            // 제어점이 선분 위에 있으면(직선 구간) 분할하지 않는다
            bool straight = Vector3.Distance(p1, Vector3.Lerp(p0, p3, 1f / 3f)) < 0.01f &&
                            Vector3.Distance(p2, Vector3.Lerp(p0, p3, 2f / 3f)) < 0.01f;

            int steps = straight ? 1 : cornerSteps;
            for (int s = 1; s <= steps; s++)
            {
                float t  = (float)s / steps;
                float u  = 1f - t;
                float uu = u * u;
                float tt = t * t;
                result.Add(uu * u * p0 + 3f * uu * t * p1 + 3f * u * tt * p2 + tt * t * p3);
            }
        }
    }

    /// <summary>
    /// 실제로 적용되는 라운드 반경. 입력값은 양옆 구간 길이의 절반으로 잘리고,
    /// 열린 경로의 양 끝 포인트는 모서리가 아니므로 0이다. (에디터 표시용)
    /// </summary>
    public static float EffectiveRound(IReadOnlyList<Waypoint> points, bool closed, int i)
    {
        int n = points.Count;
        if (n < 3 || i < 0 || i >= n) return 0f;
        if (!closed && (i == 0 || i == n - 1)) return 0f;

        Vector3 prev = points[(i - 1 + n) % n].position;
        Vector3 curr = points[i].position;
        Vector3 next = points[(i + 1) % n].position;

        float inLen  = Vector3.Distance(prev, curr);
        float outLen = Vector3.Distance(curr, next);
        if (inLen < 1e-4f || outLen < 1e-4f) return 0f;

        return Mathf.Min(points[i].round, inLen * 0.5f, outLen * 0.5f);
    }

    // ── 내부 ───────────────────────────────────────────────────────

    struct Node
    {
        public Vector3 pos;
        public Vector3 dirIn;         // 이 노트로 들어오는 진행 방향
        public Vector3 dirOut;        // 이 노트에서 나가는 진행 방향
        public bool    arcWithNext;   // 다음 노트와의 구간이 모서리 곡선인가
        public float   arcTangent;    // 곡선 구간 탄젠트 길이
        public float   tangentInLen;
        public float   tangentOutLen;
    }

    static void AddCorner(IReadOnlyList<Waypoint> points, int i, int n, List<Node> nodes)
    {
        Vector3 prev = points[(i - 1 + n) % n].position;
        Vector3 curr = points[i].position;
        Vector3 next = points[(i + 1) % n].position;

        Vector3 inV  = curr - prev;
        Vector3 outV = next - curr;

        float inLen  = inV.magnitude;
        float outLen = outV.magnitude;

        if (inLen < 1e-4f || outLen < 1e-4f)
        {
            // 겹친 포인트 — 각진 노트 하나로 처리
            Vector3 d = inLen >= 1e-4f ? inV / inLen : (outLen >= 1e-4f ? outV / outLen : Vector3.forward);
            nodes.Add(new Node { pos = curr, dirIn = d, dirOut = d });
            return;
        }

        Vector3 inDir  = inV / inLen;
        Vector3 outDir = outV / outLen;

        // 라운드는 양옆 구간 길이의 절반을 넘을 수 없다 (모서리끼리 겹치지 않게)
        float blend = Mathf.Min(points[i].round, inLen * 0.5f, outLen * 0.5f);

        if (blend < 1e-3f || Vector3.Angle(inDir, outDir) < 0.5f)
        {
            // 라운드 없음(각진 모서리) 또는 사실상 직진
            nodes.Add(new Node { pos = curr, dirIn = inDir, dirOut = outDir });
            return;
        }

        nodes.Add(new Node
        {
            pos         = curr - inDir * blend,
            dirIn       = inDir,
            dirOut      = inDir,
            arcWithNext = true,
            arcTangent  = blend * 0.55f,
        });

        nodes.Add(new Node
        {
            pos    = curr + outDir * blend,
            dirIn  = outDir,
            dirOut = outDir,
        });
    }

    static Vector3 SafeDir(Vector3 from, Vector3 to)
    {
        Vector3 d = to - from;
        return d.sqrMagnitude < 1e-8f ? Vector3.forward : d.normalized;
    }

#if UNITY_EDITOR
    void OnDrawGizmosSelected()
    {
        if (waypoints == null || waypoints.Count == 0) return;

        Gizmos.matrix = transform.localToWorldMatrix;
        Gizmos.color  = new Color(1f, 0.8f, 0.2f, 0.9f);

        foreach (var wp in waypoints)
            Gizmos.DrawWireSphere(wp.position, 0.3f);

        Gizmos.matrix = Matrix4x4.identity;
    }
#endif
}
