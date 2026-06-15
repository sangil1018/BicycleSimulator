using UnityEditor;
using UnityEngine;
using TrafficSystem;

[CustomEditor(typeof(TrafficIntersection))]
public class TrafficIntersectionEditor : Editor
{
    SerializedProperty groupAProp;
    SerializedProperty groupBProp;
    SerializedProperty greenDurationProp;
    SerializedProperty yellowDurationProp;

    void OnEnable()
    {
        groupAProp         = serializedObject.FindProperty("groupA");
        groupBProp         = serializedObject.FindProperty("groupB");
        greenDurationProp  = serializedObject.FindProperty("greenDuration");
        yellowDurationProp = serializedObject.FindProperty("yellowDuration");

        EditorApplication.update += RepaintIfPlaying;
    }

    void OnDisable() => EditorApplication.update -= RepaintIfPlaying;
    void RepaintIfPlaying() { if (Application.isPlaying) Repaint(); }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        var intersection = (TrafficIntersection)target;

        EditorGUILayout.PropertyField(groupAProp,         new GUIContent("Group A — N-S (4등)"), true);
        EditorGUILayout.PropertyField(groupBProp,         new GUIContent("Group B — E-W (4등)"), true);
        EditorGUILayout.PropertyField(greenDurationProp,  new GUIContent("Green Duration"));
        EditorGUILayout.PropertyField(yellowDurationProp, new GUIContent("Yellow Duration"));

        EditorGUILayout.Space(6);
        EditorGUILayout.LabelField("── Validation ──────────────────────", EditorStyles.boldLabel);

        int nullA = CountNulls(groupAProp);
        int nullB = CountNulls(groupBProp);
        bool emptyA = groupAProp.arraySize == 0;
        bool emptyB = groupBProp.arraySize == 0;

        if (emptyA || emptyB)
            EditorGUILayout.HelpBox("Group A 또는 B에 신호등이 할당되지 않았습니다.", MessageType.Warning);
        else if (nullA > 0 || nullB > 0)
            EditorGUILayout.HelpBox($"Null 항목 — A: {nullA}개  B: {nullB}개", MessageType.Warning);
        else
            EditorGUILayout.HelpBox($"Group A: {groupAProp.arraySize}등   Group B: {groupBProp.arraySize}등   ✓", MessageType.Info);

        EditorGUILayout.Space(6);
        EditorGUILayout.LabelField("── Intersection State ──────────────", EditorStyles.boldLabel);

        if (Application.isPlaying)
        {
            DrawPhaseIndicator(intersection.CurrentPhaseName);

            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("페이즈 강제 전환:", EditorStyles.miniBoldLabel);

            using (new EditorGUILayout.HorizontalScope())
            {
                var prev = GUI.backgroundColor;

                GUI.backgroundColor = new Color(0.3f, 1f, 0.4f);
                if (GUILayout.Button("A Green"))  intersection.ForcePhaseAGreen();

                GUI.backgroundColor = new Color(1f, 0.95f, 0.2f);
                if (GUILayout.Button("A Yellow")) intersection.ForcePhaseAYellow();

                GUI.backgroundColor = new Color(0.3f, 0.8f, 1f);
                if (GUILayout.Button("B Green"))  intersection.ForcePhaseBGreen();

                GUI.backgroundColor = new Color(1f, 0.7f, 0.2f);
                if (GUILayout.Button("B Yellow")) intersection.ForcePhaseBYellow();

                GUI.backgroundColor = prev;
            }

            EditorGUILayout.Space(2);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("▶ Next Phase"))
                    intersection.AdvancePhase();

                if (GUILayout.Button("▶ Resume Timer"))
                    intersection.ResumeTimer();
            }
        }
        else
        {
            EditorGUILayout.HelpBox("페이즈 제어는 Play mode에서 사용 가능합니다.", MessageType.None);
        }

        serializedObject.ApplyModifiedProperties();
    }

    // ── Scene View ──────────────────────────────────────────────────────────
    void OnSceneGUI()
    {
        DrawGroupHandles(groupAProp, new Color(0.3f, 0.6f, 1f,   0.9f), "A");
        DrawGroupHandles(groupBProp, new Color(1f,   0.55f, 0.1f, 0.9f), "B");
    }

    static void DrawGroupHandles(SerializedProperty prop, Color color, string label)
    {
        if (prop.arraySize < 1) return;

        Handles.color = color;
        Vector3? prev = null;
        for (int i = 0; i < prop.arraySize; i++)
        {
            var obj = prop.GetArrayElementAtIndex(i).objectReferenceValue as TrafficLight;
            if (obj == null) continue;

            Vector3 pos = obj.transform.position;
            Handles.SphereHandleCap(0, pos, Quaternion.identity, 0.4f, EventType.Repaint);
            Handles.Label(pos + Vector3.up * 1.5f, $"[{label}{i}]");

            if (prev.HasValue)
                Handles.DrawLine(prev.Value, pos, 2f);
            prev = pos;
        }
    }

    static void DrawPhaseIndicator(string phase)
    {
        Color color = phase switch
        {
            "AGreen"  => new Color(0.2f, 1f,   0.3f),
            "AYellow" => new Color(1f,   0.9f, 0.1f),
            "BGreen"  => new Color(0.3f, 0.8f, 1f),
            "BYellow" => new Color(1f,   0.7f, 0.1f),
            _         => Color.gray
        };

        var rect = GUILayoutUtility.GetRect(0, 28, GUILayout.ExpandWidth(true));
        EditorGUI.DrawRect(rect, new Color(0.15f, 0.15f, 0.15f));
        var inner = new Rect(rect.x + 2, rect.y + 2, rect.width - 4, rect.height - 4);
        EditorGUI.DrawRect(inner, color);

        var style = new GUIStyle(EditorStyles.boldLabel)
        {
            alignment = TextAnchor.MiddleCenter,
            normal    = { textColor = Color.black }
        };
        EditorGUI.LabelField(inner, $"Phase: {phase}", style);
    }

    static int CountNulls(SerializedProperty arrayProp)
    {
        int count = 0;
        for (int i = 0; i < arrayProp.arraySize; i++)
            if (arrayProp.GetArrayElementAtIndex(i).objectReferenceValue == null) count++;
        return count;
    }
}
