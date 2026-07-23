using System.Collections.Generic;
using Unity.Mathematics;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Splines;
using UnityEngine.Timeline;

/// <summary>
/// Timeline이 움직이는 카메라 경로를 샘플링해 주행 경로 SplineContainer로 굽는 도구.
/// 카메라 Animation Track이 이미 도로를 정확히 따라가므로, 별도로 스플라인을 그릴 필요가 없다.
///
/// 메뉴: Tools ▸ Navigation ▸ Route Spline Baker
/// </summary>
public class RouteSplineBaker : EditorWindow
{
    // ── 설정 ───────────────────────────────────────────────────────

    PlayableDirector director;
    Transform target;                 // 샘플링할 대상(보통 카메라)
    SplineContainer output;

    float sampleInterval = 0.05f;     // 타임라인 샘플 간격(초)
    float simplifyTolerance = 0.25f;  // 폴리라인 단순화 허용 오차(m)
    float minKnotSpacing = 2f;        // 노트 최소 간격(m)

    bool dropToGround = true;
    LayerMask groundMask = 1;
    float rayHeight = 5f;
    float rayLength = 20f;
    float groundOffset = 0f;

    string status = "";
    MessageType statusType = MessageType.None;
    Vector3[] previewPath;

    [MenuItem("Tools/Navigation/Route Spline Baker")]
    static void Open()
    {
        var win = GetWindow<RouteSplineBaker>("Route Spline Baker");
        win.minSize = new Vector2(380, 480);
        win.AutoFill();
    }

    void OnEnable()
    {
        SceneView.duringSceneGui += OnSceneGUI;
    }

    void OnDisable()
    {
        SceneView.duringSceneGui -= OnSceneGUI;
    }

    // ── GUI ────────────────────────────────────────────────────────

    void OnGUI()
    {
        EditorGUILayout.HelpBox(
            "카메라 Animation Track을 일정 간격으로 샘플링해 주행 경로 스플라인을 만듭니다.\n" +
            "베이크 중 씬의 타임라인 대상 오브젝트가 잠시 움직이며, 끝나면 원위치로 복원됩니다.",
            MessageType.Info);

        EditorGUILayout.Space(4);
        EditorGUILayout.LabelField("── Source ──────────────────────────", EditorStyles.boldLabel);

        EditorGUI.BeginChangeCheck();
        director = (PlayableDirector)EditorGUILayout.ObjectField("Playable Director", director, typeof(PlayableDirector), true);
        if (EditorGUI.EndChangeCheck())
            target = FindAnimatedTransform(director);

        target = (Transform)EditorGUILayout.ObjectField("샘플 대상 (카메라)", target, typeof(Transform), true);

        if (GUILayout.Button("씬에서 자동 찾기"))
            AutoFill();

        EditorGUILayout.Space(6);
        EditorGUILayout.LabelField("── Sampling ────────────────────────", EditorStyles.boldLabel);
        sampleInterval = EditorGUILayout.Slider(
            new GUIContent("샘플 간격(초)", "작을수록 정밀하지만 베이크가 느려집니다"),
            sampleInterval, 0.01f, 0.5f);
        simplifyTolerance = EditorGUILayout.Slider(
            new GUIContent("단순화 오차(m)", "이 오차 안에서 노트 개수를 줄입니다"),
            simplifyTolerance, 0.01f, 2f);
        minKnotSpacing = EditorGUILayout.Slider(
            new GUIContent("노트 최소 간격(m)", "너무 촘촘한 노트를 병합합니다"),
            minKnotSpacing, 0.5f, 20f);

        EditorGUILayout.Space(6);
        EditorGUILayout.LabelField("── Ground ──────────────────────────", EditorStyles.boldLabel);
        dropToGround = EditorGUILayout.Toggle(
            new GUIContent("노면으로 내리기", "카메라 높이가 아닌 도로 표면 높이로 노트를 내립니다"),
            dropToGround);

        using (new EditorGUI.DisabledScope(!dropToGround))
        {
            groundMask = LayerMaskField("노면 레이어", groundMask);
            rayHeight = EditorGUILayout.FloatField("레이 시작 높이(m)", rayHeight);
            rayLength = EditorGUILayout.FloatField("레이 길이(m)", rayLength);
            groundOffset = EditorGUILayout.FloatField("노면 오프셋(m)", groundOffset);
        }

        EditorGUILayout.Space(6);
        EditorGUILayout.LabelField("── Output ──────────────────────────", EditorStyles.boldLabel);
        output = (SplineContainer)EditorGUILayout.ObjectField("Spline Container", output, typeof(SplineContainer), true);

        if (output == null && GUILayout.Button("NavigationRoute 오브젝트 생성"))
            CreateOutput();

        EditorGUILayout.Space(8);

        using (new EditorGUI.DisabledScope(director == null || target == null || output == null))
        {
            if (GUILayout.Button("▶ 경로 베이크", GUILayout.Height(30)))
                Bake();
        }

        if (previewPath != null && previewPath.Length > 1)
        {
            EditorGUILayout.Space(4);
            if (GUILayout.Button("미리보기 지우기"))
            {
                previewPath = null;
                SceneView.RepaintAll();
            }
        }

        if (!string.IsNullOrEmpty(status))
        {
            EditorGUILayout.Space(6);
            EditorGUILayout.HelpBox(status, statusType);
        }
    }

    void OnSceneGUI(SceneView view)
    {
        if (previewPath == null || previewPath.Length < 2) return;

        Handles.color = new Color(0.2f, 1f, 0.85f, 0.9f);
        Handles.DrawAAPolyLine(4f, previewPath);

        Handles.color = Color.white;
        for (int i = 0; i < previewPath.Length; i++)
            Handles.DotHandleCap(0, previewPath[i], Quaternion.identity, 0.15f, EventType.Repaint);
    }

    // ── 자동 채우기 ────────────────────────────────────────────────

    void AutoFill()
    {
        if (director == null)
            director = Object.FindFirstObjectByType<PlayableDirector>();

        if (target == null)
            target = FindAnimatedTransform(director);

        if (output == null)
            output = Object.FindFirstObjectByType<SplineContainer>();

        Repaint();
    }

    /// <summary>Timeline의 Animation Track에 바인딩된 Transform을 찾는다.</summary>
    static Transform FindAnimatedTransform(PlayableDirector dir)
    {
        if (dir == null || dir.playableAsset is not TimelineAsset timeline) return null;

        foreach (var track in timeline.GetOutputTracks())
        {
            if (track is not AnimationTrack) continue;

            var binding = dir.GetGenericBinding(track);
            if (binding is Animator animator) return animator.transform;
            if (binding is GameObject go) return go.transform;
            if (binding is Component comp) return comp.transform;
        }

        return null;
    }

    void CreateOutput()
    {
        var go = new GameObject("NavigationRoute");
        Undo.RegisterCreatedObjectUndo(go, "Create NavigationRoute");
        output = go.AddComponent<SplineContainer>();
        Selection.activeGameObject = go;
    }

    // ── 베이크 ─────────────────────────────────────────────────────

    void Bake()
    {
        if (director.playableAsset == null)
        {
            SetStatus("✗ PlayableDirector에 Timeline 에셋이 연결되어 있지 않습니다.", MessageType.Error);
            return;
        }

        double duration = director.duration;
        if (duration <= 0.0 || double.IsInfinity(duration))
        {
            SetStatus("✗ Timeline 길이를 읽을 수 없습니다.", MessageType.Error);
            return;
        }

        int steps = Mathf.Max(2, Mathf.CeilToInt((float)duration / Mathf.Max(0.001f, sampleInterval)));
        var raw = new List<Vector3>(steps + 1);

        double originalTime = director.time;
        bool animationModeStarted = false;

        try
        {
            if (!AnimationMode.InAnimationMode())
            {
                AnimationMode.StartAnimationMode();
                animationModeStarted = true;
            }

            director.RebuildGraph();

            for (int i = 0; i <= steps; i++)
            {
                if (i % 32 == 0 &&
                    EditorUtility.DisplayCancelableProgressBar(
                        "Route Spline Baker", $"타임라인 샘플링 중… {i}/{steps}", i / (float)steps))
                {
                    SetStatus("사용자가 취소했습니다.", MessageType.Warning);
                    return;
                }

                double t = System.Math.Min(duration * i / steps, duration);
                director.time = t;
                director.Evaluate();

                Vector3 p = target.position;

                // 거의 같은 자리는 버린다 (정지 이벤트 구간에서 점이 뭉치는 것 방지)
                if (raw.Count == 0 || (raw[raw.Count - 1] - p).sqrMagnitude > 0.0025f)
                    raw.Add(p);
            }
        }
        finally
        {
            EditorUtility.ClearProgressBar();

            director.time = originalTime;
            director.Evaluate();

            if (animationModeStarted)
                AnimationMode.StopAnimationMode();
        }

        if (raw.Count < 2)
        {
            SetStatus("✗ 유효한 샘플이 없습니다. 대상 오브젝트가 Animation Track으로 움직이는지 확인하세요.", MessageType.Error);
            return;
        }

        // 1) 노면으로 내리기
        int groundMiss = 0;
        if (dropToGround)
        {
            for (int i = 0; i < raw.Count; i++)
            {
                Vector3 p = raw[i];
                if (Physics.Raycast(p + Vector3.up * rayHeight, Vector3.down, out RaycastHit hit,
                                    rayHeight + rayLength, groundMask, QueryTriggerInteraction.Ignore))
                    raw[i] = hit.point + Vector3.up * groundOffset;
                else
                    groundMiss++;
            }
        }

        // 2) 단순화 → 3) 최소 간격 병합
        var simplified = Simplify(raw, simplifyTolerance);
        var knots = EnforceMinSpacing(simplified, minKnotSpacing);

        if (knots.Count < 2)
        {
            SetStatus("✗ 단순화 결과 노트가 2개 미만입니다. 오차/최소 간격 값을 줄이세요.", MessageType.Error);
            return;
        }

        // 4) 스플라인에 기록
        Undo.RecordObject(output, "Bake Route Spline");

        var spline = output.Spline;
        spline.Clear();

        Transform ct = output.transform;
        foreach (var p in knots)
            spline.Add(new BezierKnot((float3)ct.InverseTransformPoint(p)), TangentMode.AutoSmooth);

        EditorUtility.SetDirty(output);
        EditorSceneManager.MarkSceneDirty(output.gameObject.scene);

        previewPath = knots.ToArray();
        SceneView.RepaintAll();

        float length = 0f;
        for (int i = 1; i < knots.Count; i++) length += Vector3.Distance(knots[i - 1], knots[i]);

        string msg = $"✓ 베이크 완료\n" +
                     $"  샘플 {raw.Count}개 → 노트 {knots.Count}개\n" +
                     $"  경로 길이 약 {length:F1} m (Timeline {duration:F1}초)";

        if (groundMiss > 0)
            msg += $"\n⚠ 노면 레이캐스트 실패 {groundMiss}개 — 원래 높이 유지됨. 노면 레이어/레이 길이를 확인하세요.";

        SetStatus(msg, groundMiss > 0 ? MessageType.Warning : MessageType.Info);
    }

    // ── 폴리라인 처리 ──────────────────────────────────────────────

    /// <summary>Ramer–Douglas–Peucker 단순화.</summary>
    static List<Vector3> Simplify(List<Vector3> points, float tolerance)
    {
        if (points.Count < 3 || tolerance <= 0f) return new List<Vector3>(points);

        var keep = new bool[points.Count];
        keep[0] = true;
        keep[points.Count - 1] = true;

        var stack = new Stack<(int, int)>();
        stack.Push((0, points.Count - 1));

        while (stack.Count > 0)
        {
            var (first, last) = stack.Pop();
            if (last <= first + 1) continue;

            float maxDist = -1f;
            int index = -1;

            for (int i = first + 1; i < last; i++)
            {
                float d = PointSegmentDistance(points[i], points[first], points[last]);
                if (d > maxDist) { maxDist = d; index = i; }
            }

            if (maxDist > tolerance && index > 0)
            {
                keep[index] = true;
                stack.Push((first, index));
                stack.Push((index, last));
            }
        }

        var result = new List<Vector3>();
        for (int i = 0; i < points.Count; i++)
            if (keep[i]) result.Add(points[i]);

        return result;
    }

    static float PointSegmentDistance(Vector3 p, Vector3 a, Vector3 b)
    {
        Vector3 ab = b - a;
        float len2 = ab.sqrMagnitude;
        if (len2 < 1e-8f) return Vector3.Distance(p, a);

        float t = Mathf.Clamp01(Vector3.Dot(p - a, ab) / len2);
        return Vector3.Distance(p, a + ab * t);
    }

    /// <summary>너무 가까운 노트를 걸러낸다. 끝점은 항상 유지.</summary>
    static List<Vector3> EnforceMinSpacing(List<Vector3> points, float minSpacing)
    {
        if (points.Count < 3 || minSpacing <= 0f) return points;

        var result = new List<Vector3> { points[0] };

        for (int i = 1; i < points.Count - 1; i++)
        {
            if (Vector3.Distance(result[result.Count - 1], points[i]) >= minSpacing)
                result.Add(points[i]);
        }

        result.Add(points[points.Count - 1]);
        return result;
    }

    // ── 유틸 ───────────────────────────────────────────────────────

    void SetStatus(string msg, MessageType type)
    {
        status = msg;
        statusType = type;
        Repaint();
    }

    static LayerMask LayerMaskField(string label, LayerMask mask)
    {
        int field = EditorGUILayout.MaskField(label, UnityEditorInternal.InternalEditorUtility.LayerMaskToConcatenatedLayersMask(mask),
                                              UnityEditorInternal.InternalEditorUtility.layers);
        return UnityEditorInternal.InternalEditorUtility.ConcatenatedLayersMaskToLayerMask(field);
    }
}
