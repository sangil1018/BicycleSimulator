using UnityEditor;
using UnityEngine;
using TrafficSystem;

[CustomEditor(typeof(TrafficNode))]
public class TrafficNodeEditor : Editor
{
    SerializedProperty exitsProp;
    SerializedProperty stopSignalProp;

    static readonly Color colorNode     = new Color(1f,  0.85f, 0f,  1f);
    static readonly Color colorNodeStop = new Color(1f,  0.25f, 0.25f, 1f);
    static readonly Color colorExit     = new Color(1f,  0.85f, 0f,  0.7f);

    void OnEnable()
    {
        exitsProp      = serializedObject.FindProperty("exits");
        stopSignalProp = serializedObject.FindProperty("stopSignal");
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        var node = (TrafficNode)target;

        // ── 연결 설정 ────────────────────────────────────────────────────
        EditorGUILayout.LabelField("── 경로 연결 ─────────────────────────", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(exitsProp,
            new GUIContent("Exits", "다음 노드와 가중치 목록.\n가중치 합 기준으로 확률을 정규화합니다."), true);

        // 확률 바
        if (exitsProp.arraySize > 0)
        {
            EditorGUILayout.Space(4);
            DrawWeightBar(node);
        }
        else
        {
            DrawStatusBar(colorNodeStop, "경로 끝 — 차량이 여기서 리스폰됩니다");
        }

        // ── 신호 연결 ────────────────────────────────────────────────────
        EditorGUILayout.Space(6);
        EditorGUILayout.LabelField("── 신호 연결 ─────────────────────────", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(stopSignalProp,
            new GUIContent("Stop Signal", "null이면 자유 통과.\n할당하면 차량이 이 노드 앞에서 신호를 확인합니다."));

        // ── 도구 ────────────────────────────────────────────────────────
        EditorGUILayout.Space(6);
        EditorGUILayout.LabelField("── 도구 ────────────────────────────", EditorStyles.boldLabel);
        if (GUILayout.Button("↺ 자동 방향 설정  (exits[0] 방향으로 회전)"))
        {
            Undo.RecordObject(node.transform, "Auto Orient TrafficNode");
            node.AutoOrient();
        }

        serializedObject.ApplyModifiedProperties();
    }

    // ── Scene View ───────────────────────────────────────────────────────────

    [DrawGizmo(GizmoType.NonSelected | GizmoType.Selected | GizmoType.InSelectionHierarchy)]
    static void DrawGizmosAlways(TrafficNode node, GizmoType gizmoType)
    {
        bool isSelected = (gizmoType & GizmoType.Selected) != 0;
        float sphereSize = isSelected ? 0.55f : 0.35f;

        // 노드 구: stopSignal 있으면 빨강, 없으면 노랑
        bool hasStop = node.StopSignal != null;
        Color nodeColor = hasStop ? colorNodeStop : colorNode;
        nodeColor.a = isSelected ? 0.9f : 0.5f;
        Handles.color = nodeColor;
        Handles.SphereHandleCap(0, node.transform.position, Quaternion.identity, sphereSize, EventType.Repaint);

        // 정지선 표시
        if (hasStop)
        {
            bool canPass = Application.isPlaying && node.StopSignal.CanPass;
            Handles.color = canPass
                ? new Color(0.2f, 1f, 0.3f, 0.9f)
                : new Color(1f,   0.1f, 0.1f, 0.9f);
            Vector3 pos = node.transform.position;
            Handles.DrawLine(pos + node.transform.right * 1.5f,
                             pos - node.transform.right * 1.5f, 3f);

            if (isSelected)
            {
                Handles.color = new Color(1f, 0.4f, 0f, 0.7f);
                Handles.DrawDottedLine(pos, node.StopSignal.transform.position, 4f);
                Handles.Label(pos + Vector3.up * 0.9f, $"  → {node.StopSignal.name}");
            }
        }

        if (isSelected) return; // 선택 시 OnSceneGUI가 상세 표시

        // 비선택 시 간략 연결선
        // NOTE: exits는 private — Editor에서 직접 접근 불가. OnDrawGizmos가 MonoBehaviour에 있음.
    }

    void OnSceneGUI()
    {
        var node = (TrafficNode)target;
        var so   = new SerializedObject(node);
        var ep   = so.FindProperty("exits");
        if (ep == null || ep.arraySize == 0) return;

        int total = 0;
        for (int i = 0; i < ep.arraySize; i++)
        {
            var exitNode = ep.GetArrayElementAtIndex(i).FindPropertyRelative("node")
                             .objectReferenceValue as TrafficNode;
            int w = ep.GetArrayElementAtIndex(i).FindPropertyRelative("weight").intValue;
            if (exitNode != null) total += Mathf.Max(1, w);
        }

        for (int i = 0; i < ep.arraySize; i++)
        {
            var exitNode = ep.GetArrayElementAtIndex(i).FindPropertyRelative("node")
                             .objectReferenceValue as TrafficNode;
            int w = ep.GetArrayElementAtIndex(i).FindPropertyRelative("weight").intValue;
            if (exitNode == null) continue;

            float pct = total > 0 ? Mathf.Max(1, w) * 100f / total : 0f;

            Handles.color = colorExit;
            Vector3 from = node.transform.position;
            Vector3 to   = exitNode.transform.position;
            Handles.DrawLine(from, to, 3f);

            Vector3 dir = to - from; dir.y = 0f;
            if (dir.sqrMagnitude > 0.001f)
            {
                Vector3 mid = (from + to) * 0.5f;
                Handles.ArrowHandleCap(0, mid, Quaternion.LookRotation(dir.normalized), 2.5f, EventType.Repaint);
            }
            Handles.Label((from + to) * 0.5f + Vector3.up * 0.6f, $"{pct:F0}%");
        }

        Handles.color = new Color(1f, 0.85f, 0f, 0.4f);
        Handles.SphereHandleCap(0, node.transform.position, Quaternion.identity, 0.55f, EventType.Repaint);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    void DrawWeightBar(TrafficNode node)
    {
        var so = new SerializedObject(node);
        var ep = so.FindProperty("exits");

        int total = 0;
        for (int i = 0; i < ep.arraySize; i++)
        {
            var exitNode = ep.GetArrayElementAtIndex(i).FindPropertyRelative("node")
                             .objectReferenceValue;
            int w = ep.GetArrayElementAtIndex(i).FindPropertyRelative("weight").intValue;
            if (exitNode != null) total += Mathf.Max(1, w);
        }
        if (total <= 0) return;

        var rect = GUILayoutUtility.GetRect(0, 22, GUILayout.ExpandWidth(true));
        EditorGUI.DrawRect(rect, new Color(0.12f, 0.12f, 0.12f));

        float iX = rect.x + 2f, iY = rect.y + 2f, iW = rect.width - 4f, iH = rect.height - 4f;
        float cursor = iX;

        Color[] palette = {
            new Color(1f,  0.85f, 0f,  0.85f),
            new Color(0f,  0.9f,  1f,  0.85f),
            new Color(0.2f,1f,    0.4f,0.85f),
            new Color(1f,  0.4f,  0.8f,0.85f),
        };

        for (int i = 0; i < ep.arraySize; i++)
        {
            var exitNode = ep.GetArrayElementAtIndex(i).FindPropertyRelative("node")
                             .objectReferenceValue;
            int w = ep.GetArrayElementAtIndex(i).FindPropertyRelative("weight").intValue;
            if (exitNode == null) continue;

            float frac = Mathf.Max(1, w) / (float)total;
            float segW = iW * frac;
            EditorGUI.DrawRect(new Rect(cursor, iY, segW, iH), palette[i % palette.Length]);
            cursor += segW;
        }

        string info = "";
        for (int i = 0; i < ep.arraySize; i++)
        {
            var exitNode = ep.GetArrayElementAtIndex(i).FindPropertyRelative("node")
                             .objectReferenceValue as TrafficNode;
            int w = ep.GetArrayElementAtIndex(i).FindPropertyRelative("weight").intValue;
            if (exitNode == null) continue;
            float pct = Mathf.Max(1, w) * 100f / total;
            info += $"[{i}] {exitNode.name}: {pct:F1}%   ";
        }
        if (!string.IsNullOrEmpty(info))
            EditorGUILayout.HelpBox(info.TrimEnd(), MessageType.None);
    }

    static void DrawStatusBar(Color color, string label)
    {
        var rect = GUILayoutUtility.GetRect(0, 22, GUILayout.ExpandWidth(true));
        EditorGUI.DrawRect(rect, new Color(0.12f, 0.12f, 0.12f));
        var inner = new Rect(rect.x + 2, rect.y + 2, rect.width - 4, rect.height - 4);
        EditorGUI.DrawRect(inner, color);
        var style = new GUIStyle(EditorStyles.boldLabel)
        {
            alignment = TextAnchor.MiddleCenter,
            normal    = { textColor = Color.black }
        };
        EditorGUI.LabelField(inner, label, style);
    }

    [MenuItem("Tools/Traffic/모든 TrafficNode 자동 방향 설정")]
    static void AutoOrientAll()
    {
        var all = Object.FindObjectsByType<TrafficNode>(FindObjectsSortMode.None);
        if (all.Length == 0) { Debug.Log("[TrafficNodeEditor] 씬에 TrafficNode 없음."); return; }

        Undo.SetCurrentGroupName("Auto Orient All TrafficNodes");
        int group = Undo.GetCurrentGroup();
        foreach (var n in all)
        {
            Undo.RecordObject(n.transform, "Auto Orient");
            n.AutoOrient();
        }
        Undo.CollapseUndoOperations(group);
        Debug.Log($"[TrafficNodeEditor] {all.Length}개 TrafficNode 방향 설정 완료.");
    }
}
