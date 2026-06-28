using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using TrafficSystem;

[CustomEditor(typeof(PedestrianSpawner))]
public class PedestrianSpawnerEditor : Editor
{
    SerializedProperty adultPrefabsProp;
    SerializedProperty childPrefabsProp;
    SerializedProperty adultSpeedProp;
    SerializedProperty childSpeedProp;
    SerializedProperty groupsProp;
    SerializedProperty spawnPerFrameProp;

    string      validationResult = "";
    MessageType validationType   = MessageType.None;

    static readonly Color[] routeColors =
    {
        new Color(0.2f, 0.8f, 1f,   0.9f),
        new Color(1f,   0.5f, 0.2f, 0.9f),
        new Color(0.4f, 1f,   0.4f, 0.9f),
        new Color(1f,   0.3f, 0.6f, 0.9f),
        new Color(0.9f, 1f,   0.2f, 0.9f),
        new Color(0.6f, 0.3f, 1f,   0.9f),
    };

    void OnEnable()
    {
        if (!target) return;
        adultPrefabsProp  = serializedObject.FindProperty("adultPrefabs");
        childPrefabsProp  = serializedObject.FindProperty("childPrefabs");
        adultSpeedProp    = serializedObject.FindProperty("adultWalkSpeed");
        childSpeedProp    = serializedObject.FindProperty("childWalkSpeed");
        groupsProp        = serializedObject.FindProperty("groups");
        spawnPerFrameProp = serializedObject.FindProperty("spawnPerFrame");
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        EditorGUILayout.LabelField("── Prefabs ──────────────────────────", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(adultPrefabsProp, true);
        EditorGUILayout.PropertyField(childPrefabsProp, true);

        EditorGUILayout.Space(4);
        EditorGUILayout.LabelField("── Walk Speed ───────────────────────", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(adultSpeedProp);
        EditorGUILayout.PropertyField(childSpeedProp);

        EditorGUILayout.Space(4);
        EditorGUILayout.LabelField("── Performance ──────────────────────", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(spawnPerFrameProp);

        EditorGUILayout.Space(4);
        EditorGUILayout.PropertyField(groupsProp, true);

        EditorGUILayout.Space(6);
        EditorGUILayout.LabelField("── Validation ──────────────────────", EditorStyles.boldLabel);

        if (GUILayout.Button("✓ Validate Setup"))
            Validate();

        if (!string.IsNullOrEmpty(validationResult))
            EditorGUILayout.HelpBox(validationResult, validationType);

        // Group summary table
        if (groupsProp.arraySize > 0)
        {
            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("Group Summary:", EditorStyles.miniBoldLabel);

            for (int i = 0; i < groupsProp.arraySize; i++)
            {
                var    groupProp  = groupsProp.GetArrayElementAtIndex(i);
                var    wpsProp    = groupProp.FindPropertyRelative("waypoints");
                var    labelProp  = groupProp.FindPropertyRelative("label");
                var    afProp     = groupProp.FindPropertyRelative("adultsForward");
                var    arProp     = groupProp.FindPropertyRelative("adultsReverse");
                var    cfProp     = groupProp.FindPropertyRelative("childrenForward");
                var    crProp     = groupProp.FindPropertyRelative("childrenReverse");

                int total = afProp.intValue + arProp.intValue + cfProp.intValue + crProp.intValue;

                Rect r = GUILayoutUtility.GetRect(0, 18, GUILayout.ExpandWidth(true));
                EditorGUI.DrawRect(r, routeColors[i % routeColors.Length] * 0.4f);
                EditorGUI.LabelField(r,
                    $"  Group [{i}] \"{labelProp.stringValue}\"  WPs: {wpsProp.arraySize}  Peds: {total}",
                    EditorStyles.miniLabel);
            }
        }

        serializedObject.ApplyModifiedProperties();
    }

    // ── Scene View ──────────────────────────────────────────────────────────────

    // 선택 여부와 무관하게 항상 씬뷰에 웨이포인트 라인을 표시
    [DrawGizmo(GizmoType.NonSelected | GizmoType.Selected | GizmoType.InSelectionHierarchy)]
    static void DrawGizmosAlways(PedestrianSpawner spawner, GizmoType gizmoType)
    {
        if (!spawner) return;
        var so         = new SerializedObject(spawner);
        var groupsProp = so.FindProperty("groups");
        if (groupsProp == null) return;

        DrawGroupGizmos(groupsProp);
    }

    void OnSceneGUI()
    {
        if (!target) return;
        serializedObject.Update();
        DrawGroupGizmos(groupsProp);
    }

    static void DrawGroupGizmos(SerializedProperty groupsProp)
    {
        for (int gi = 0; gi < groupsProp.arraySize; gi++)
        {
            var groupProp       = groupsProp.GetArrayElementAtIndex(gi);
            var wpsProp         = groupProp.FindPropertyRelative("waypoints");
            var labelProp       = groupProp.FindPropertyRelative("label");
            var blendRadiusProp = groupProp.FindPropertyRelative("blendRadius");

            float blend = blendRadiusProp != null ? blendRadiusProp.floatValue : 1f;
            Color c     = routeColors[gi % routeColors.Length];

            // 웨이포인트 수집
            var wps = new List<Transform>();
            for (int wi = 0; wi < wpsProp.arraySize; wi++)
            {
                var t = wpsProp.GetArrayElementAtIndex(wi).objectReferenceValue as Transform;
                wps.Add(t); // null 포함 (인덱스 맞춤)
            }

            // 웨이포인트 핸들 표시
            Handles.color = c;
            for (int wi = 0; wi < wps.Count; wi++)
            {
                if (wps[wi] == null) continue;
                Handles.DotHandleCap(0, wps[wi].position, Quaternion.identity, 0.2f, EventType.Repaint);
            }

            // 실제 이동 경로 시각화 (직선 + 중간 웨이포인트 Bezier 보간)
            DrawCenterPath(wps, blend, c);

            if (wps.Count > 0 && wps[0] != null)
                Handles.Label(wps[0].position + Vector3.up * 1.2f,
                    $"  Group [{gi}] \"{labelProp.stringValue}\"");
        }
    }

    static void DrawCenterPath(List<Transform> wps, float blendRadius, Color c)
    {
        // 유효한 웨이포인트만 추출
        var valid = new List<Vector3>();
        foreach (var t in wps)
            if (t != null) valid.Add(t.position);
        if (valid.Count < 2) return;

        int n = valid.Count;
        Color straightColor = c;
        Color blendColor    = Color.Lerp(c, Color.white, 0.4f);

        for (int i = 0; i < n - 1; i++)
        {
            Vector3 a = valid[i];
            Vector3 b = valid[i + 1];

            bool isFirstSeg = i == 0;
            bool isLastSeg  = i == n - 2;

            Vector3 segDir = (b - a).normalized;
            float   segLen = Vector3.Distance(a, b);

            // 이 세그먼트 안에서 직선이 시작/끝나는 지점 계산
            Vector3 straightFrom = a;
            Vector3 straightTo   = b;

            if (!isFirstSeg)
            {
                // a는 중간 웨이포인트 → 이전 세그먼트에서 blend exit 이후부터 직선 시작
                float prevLen    = Vector3.Distance(valid[i - 1], a);
                float actBlend_a = Mathf.Min(blendRadius, prevLen * 0.5f, segLen * 0.5f);
                straightFrom     = a + segDir * actBlend_a;
            }

            if (!isLastSeg)
            {
                // b는 중간 웨이포인트 → blend entry까지만 직선
                float nextLen    = Vector3.Distance(b, valid[i + 2]);
                float actBlend_b = Mathf.Min(blendRadius, segLen * 0.5f, nextLen * 0.5f);
                straightTo       = b - segDir * actBlend_b;
            }

            // 직선 구간 그리기
            Handles.color = straightColor;
            Handles.DrawLine(straightFrom, straightTo, 2f);
        }

        // 중간 웨이포인트마다 Bezier 곡선 구간 그리기
        for (int i = 1; i < n - 1; i++)
        {
            Vector3 prev = valid[i - 1];
            Vector3 curr = valid[i];
            Vector3 next = valid[i + 1];

            Vector3 inDir  = (curr - prev).normalized;
            Vector3 outDir = (next - curr).normalized;

            float inLen    = Vector3.Distance(prev, curr);
            float outLen   = Vector3.Distance(curr, next);
            float actBlend = Mathf.Min(blendRadius, inLen * 0.5f, outLen * 0.5f);

            Vector3 entry = curr - inDir  * actBlend;
            Vector3 exit  = curr + outDir * actBlend;
            Vector3 cp1   = entry + inDir  * (actBlend * 0.55f);
            Vector3 cp2   = exit  - outDir * (actBlend * 0.55f);

            Handles.color = blendColor;
            Handles.DrawBezier(entry, exit, cp1, cp2, blendColor, null, 3f);

            // 보간 구간 경계 표시 (작은 틱)
            Handles.color = Color.white * 0.7f;
            Handles.DrawLine(entry + Vector3.up * 0.15f, entry - Vector3.up * 0.15f, 1.5f);
            Handles.DrawLine(exit  + Vector3.up * 0.15f, exit  - Vector3.up * 0.15f, 1.5f);
        }
    }

    static void DrawDiamond(Vector3 center, float size)
    {
        Vector3[] verts =
        {
            center + Vector3.forward * size,
            center + Vector3.right   * size,
            center - Vector3.forward * size,
            center - Vector3.right   * size,
        };
        for (int i = 0; i < 4; i++)
            Handles.DrawLine(verts[i], verts[(i + 1) % 4], 2f);
    }

    // ── Validation ──────────────────────────────────────────────────────────────

    void Validate()
    {
        var sb = new System.Text.StringBuilder();

        // Prefab 검사
        var adultPrefabs = adultPrefabsProp;
        var childPrefabs = childPrefabsProp;

        if (adultPrefabs.arraySize == 0)
            sb.AppendLine("✗ Adult Prefabs 미할당.");
        else
        {
            bool allOk = true;
            for (int i = 0; i < adultPrefabs.arraySize; i++)
            {
                var go = adultPrefabs.GetArrayElementAtIndex(i).objectReferenceValue as GameObject;
                if (go == null) { sb.AppendLine($"✗ adultPrefabs[{i}] null."); allOk = false; continue; }
                if (go.GetComponent<PedestrianController>() == null)
                { sb.AppendLine($"✗ adultPrefabs[{i}] '{go.name}' — PedestrianController 없음."); allOk = false; }
            }
            if (allOk) sb.AppendLine($"✓ Adult Prefabs OK ({adultPrefabs.arraySize}개)");
        }

        if (childPrefabs.arraySize == 0)
            sb.AppendLine("⚠ Child Prefabs 미할당 (아이 캐릭터 없음).");
        else
        {
            bool allOk = true;
            for (int i = 0; i < childPrefabs.arraySize; i++)
            {
                var go = childPrefabs.GetArrayElementAtIndex(i).objectReferenceValue as GameObject;
                if (go == null) { sb.AppendLine($"✗ childPrefabs[{i}] null."); allOk = false; continue; }
                if (go.GetComponent<PedestrianController>() == null)
                { sb.AppendLine($"✗ childPrefabs[{i}] '{go.name}' — PedestrianController 없음."); allOk = false; }
            }
            if (allOk) sb.AppendLine($"✓ Child Prefabs OK ({childPrefabs.arraySize}개)");
        }

        // Group 검사
        if (groupsProp.arraySize == 0)
        {
            sb.AppendLine("✗ Groups가 없습니다.");
        }
        else
        {
            for (int i = 0; i < groupsProp.arraySize; i++)
            {
                var groupProp = groupsProp.GetArrayElementAtIndex(i);
                var wpsProp   = groupProp.FindPropertyRelative("waypoints");
                var labelProp = groupProp.FindPropertyRelative("label");
                string lbl    = labelProp.stringValue;

                if (wpsProp.arraySize < 2)
                {
                    sb.AppendLine($"✗ Group [{i}] \"{lbl}\" — 웨이포인트 최소 2개 필요 (현재 {wpsProp.arraySize}개).");
                    continue;
                }

                sb.AppendLine($"✓ Group [{i}] \"{lbl}\" — {wpsProp.arraySize} waypoints");
            }
        }

        validationResult = sb.ToString().TrimEnd();
        validationType   = validationResult.Contains("✗") ? MessageType.Error : MessageType.Info;
    }
}
