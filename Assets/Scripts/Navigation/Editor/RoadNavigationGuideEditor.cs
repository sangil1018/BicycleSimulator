using UnityEditor;
using UnityEngine;
using UnityEngine.Splines;

[CustomEditor(typeof(RoadNavigationGuide))]
public class RoadNavigationGuideEditor : Editor
{
    string validationResult = "";
    MessageType validationType = MessageType.None;

    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        var guide = (RoadNavigationGuide)target;

        EditorGUILayout.Space(8);
        EditorGUILayout.LabelField("── Preview ─────────────────────────", EditorStyles.boldLabel);

        using (new EditorGUI.DisabledScope(Application.isPlaying))
        {
            EditorGUILayout.BeginHorizontal();

            if (GUILayout.Button("셰브론 미리보기"))
            {
                guide.EditorPreview();
                SceneView.RepaintAll();
            }

            if (GUILayout.Button("미리보기 정리"))
            {
                guide.EditorClearPreview();
                SceneView.RepaintAll();
            }

            EditorGUILayout.EndHorizontal();
        }

        EditorGUILayout.HelpBox(
            "미리보기는 Viewer(또는 Main Camera) 위치를 기준으로 배치합니다. " +
            "미리보기 오브젝트는 씬에 저장되지 않습니다.",
            MessageType.None);

        EditorGUILayout.Space(6);
        EditorGUILayout.LabelField("── Validation ──────────────────────", EditorStyles.boldLabel);

        if (GUILayout.Button("✓ Validate Setup"))
            Validate(guide);

        if (!string.IsNullOrEmpty(validationResult))
            EditorGUILayout.HelpBox(validationResult, validationType);
    }

    // ── Scene View ──────────────────────────────────────────────────────────────

    [DrawGizmo(GizmoType.Selected | GizmoType.InSelectionHierarchy)]
    static void DrawRouteGizmo(RoadNavigationGuide guide, GizmoType type)
    {
        var route = guide.Route;
        if (route == null || route.Spline == null || route.Spline.Count < 2) return;

        const int Segments = 256;
        var pts = new Vector3[Segments + 1];
        for (int i = 0; i <= Segments; i++)
            pts[i] = (Vector3)route.EvaluatePosition(i / (float)Segments);

        Handles.color = new Color(0.2f, 1f, 0.85f, 0.75f);
        Handles.DrawAAPolyLine(3f, pts);

        // 표시 구간(near ~ far)을 뷰어 기준으로 표시
        Transform v = Camera.main != null ? Camera.main.transform : null;
        if (v == null) return;

        Handles.color = new Color(1f, 0.9f, 0.2f, 0.9f);
        Handles.DrawWireDisc(v.position, Vector3.up, guide.EditorNearDistance);
        Handles.color = new Color(1f, 0.5f, 0.2f, 0.6f);
        Handles.DrawWireDisc(v.position, Vector3.up, guide.EditorFarDistance);
    }

    // ── Validation ──────────────────────────────────────────────────────────────

    void Validate(RoadNavigationGuide guide)
    {
        var sb = new System.Text.StringBuilder();

        // 경로
        SplineContainer route = guide.Route;
        if (route == null)
        {
            sb.AppendLine("✗ Route 미할당. Tools ▸ Navigation ▸ Route Spline Baker로 생성하세요.");
        }
        else if (route.Spline == null || route.Spline.Count < 2)
        {
            sb.AppendLine($"✗ Route \"{route.name}\" — 노트가 2개 미만입니다.");
        }
        else
        {
            float len = 0f;
            Vector3 prev = (Vector3)route.EvaluatePosition(0f);
            for (int i = 1; i <= 256; i++)
            {
                Vector3 p = (Vector3)route.EvaluatePosition(i / 256f);
                len += Vector3.Distance(prev, p);
                prev = p;
            }
            sb.AppendLine($"✓ Route OK — 노트 {route.Spline.Count}개, 길이 약 {len:F0} m");
        }

        // 머티리얼
        var mat = guide.EditorMaterial;
        if (mat == null)
        {
            sb.AppendLine("✗ Chevron Material 미할당. 셰브론이 생성되지 않습니다.");
        }
        else
        {
            sb.AppendLine($"✓ Material \"{mat.name}\" ({mat.shader.name})");

            var colorProp = serializedObject.FindProperty("colorProperty").stringValue;
            if (!mat.HasProperty(colorProp))
                sb.AppendLine($"✗ 셰이더에 '{colorProp}' 프로퍼티가 없습니다. Color Property를 셰이더에 맞게 수정하세요.");

            if (!mat.enableInstancing)
                sb.AppendLine("⚠ GPU Instancing이 꺼져 있습니다. 머티리얼에서 켜면 드로우콜이 1~2개로 줄어듭니다.");

            if (mat.renderQueue < 2450)
                sb.AppendLine("⚠ Render Queue가 불투명 범위입니다. Transparent(3000) 권장 — 도로와 Z-파이팅이 생길 수 있습니다.");
        }

        // 풀 크기
        int need = Mathf.CeilToInt((guide.EditorFarDistance - guide.EditorNearDistance) / Mathf.Max(0.05f, guide.EditorSpacing)) + 1;
        if (guide.EditorPoolSize < need)
            sb.AppendLine($"✗ Pool Size 부족 — 최소 {need}개 필요 (현재 {guide.EditorPoolSize}개). 먼 쪽 셰브론이 잘립니다.");
        else
            sb.AppendLine($"✓ Pool Size OK ({guide.EditorPoolSize} ≥ {need})");

        // 노면
        var conform = serializedObject.FindProperty("conformToGround");
        if (conform != null && conform.boolValue)
        {
            var maskProp = serializedObject.FindProperty("groundMask");
            int mask = maskProp.intValue;
            int vehicleLayer = LayerMask.NameToLayer("Vehicle");

            if (mask == 0)
                sb.AppendLine("✗ Ground Mask가 비어 있습니다. 노면 레이어를 지정하세요.");
            else if (vehicleLayer >= 0 && (mask & (1 << vehicleLayer)) != 0)
                sb.AppendLine("⚠ Ground Mask에 Vehicle 레이어가 포함되어 있습니다. 차량 위에 셰브론이 올라갈 수 있습니다.");
        }

        validationResult = sb.ToString().TrimEnd();
        validationType = validationResult.Contains("✗") ? MessageType.Error
                       : validationResult.Contains("⚠") ? MessageType.Warning
                       : MessageType.Info;
    }
}
