using UnityEditor;
using UnityEngine;
using TrafficSystem;

[CustomEditor(typeof(TrafficJunction))]
public class TrafficJunctionEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        var junction = (TrafficJunction)target;

        // 현재 상태 바
        EditorGUILayout.Space(6);
        string phaseName = junction.CurrentPhaseName;
        Color  barColor  = junction.InYellow
            ? new Color(1f, 0.85f, 0f)
            : new Color(0.2f, 0.85f, 0.3f);
        DrawStatusBar(barColor, $"Phase {junction.CurrentPhaseIdx}: {phaseName}");

        if (!Application.isPlaying) return;

        // ── Play 모드 제어 ────────────────────────────────────────────────
        EditorGUILayout.Space(4);
        EditorGUILayout.LabelField("── 페이즈 강제 설정 ──────────────────", EditorStyles.boldLabel);

        EditorGUILayout.BeginHorizontal();
        for (int i = 0; i < junction.PhaseCount; i++)
        {
            bool isCurrent = (junction.CurrentPhaseIdx == i && !junction.InYellow);
            GUI.backgroundColor = isCurrent ? new Color(0.3f, 1f, 0.4f) : Color.white;
            if (GUILayout.Button($"Phase {i}")) junction.ForcePhase(i);
        }
        GUI.backgroundColor = Color.white;
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("⏸ Pause"))  junction.Pause();
        if (GUILayout.Button("▶ Resume")) junction.Resume();
        EditorGUILayout.EndHorizontal();

        // ── 보행자 오버라이드 ─────────────────────────────────────────────
        EditorGUILayout.Space(4);
        EditorGUILayout.LabelField("── 보행자 신호 오버라이드 ────────────", EditorStyles.boldLabel);
        EditorGUILayout.BeginHorizontal();
        GUI.backgroundColor = new Color(0.9f, 0.2f, 0.2f);
        if (GUILayout.Button("전체 빨강"))  junction.OverridePedestrianAll(PedestrianState.Red);
        GUI.backgroundColor = new Color(0.2f, 0.85f, 0.2f);
        if (GUILayout.Button("전체 초록"))  junction.OverridePedestrianAll(PedestrianState.Green);
        GUI.backgroundColor = Color.white;
        if (GUILayout.Button("오버라이드 해제")) junction.ClearPedestrianOverrideAll();
        EditorGUILayout.EndHorizontal();

        if (GUI.changed) EditorUtility.SetDirty(target);
        Repaint();
    }

    void OnSceneGUI()
    {
        var junction = (TrafficJunction)target;
        var so       = new SerializedObject(junction);
        var phasesProp = so.FindProperty("phases");
        if (phasesProp == null || phasesProp.arraySize == 0) return;

        Color[] palette = {
            new Color(0.2f, 0.9f, 0.3f, 0.8f),
            new Color(0.2f, 0.5f, 1f,   0.8f),
            new Color(1f,   0.6f, 0.1f, 0.8f),
            new Color(0.9f, 0.2f, 0.9f, 0.8f),
        };

        for (int pi = 0; pi < phasesProp.arraySize; pi++)
        {
            var phase = phasesProp.GetArrayElementAtIndex(pi);
            var vg    = phase.FindPropertyRelative("vehicleGreen");
            if (vg == null) continue;

            bool isCurrent = Application.isPlaying && junction.CurrentPhaseIdx == pi && !junction.InYellow;
            Color col = palette[pi % palette.Length];
            if (!isCurrent) col.a = 0.35f;

            for (int si = 0; si < vg.arraySize; si++)
            {
                var sigObj = vg.GetArrayElementAtIndex(si).objectReferenceValue as TrafficSignal;
                if (sigObj == null) continue;
                Handles.color = col;
                Handles.SphereHandleCap(0, sigObj.transform.position, Quaternion.identity, 0.5f, EventType.Repaint);
                Handles.Label(sigObj.transform.position + Vector3.up * 0.7f,
                    $"P{pi} Sig{si}");
                Handles.DrawLine(junction.transform.position, sigObj.transform.position, 1.5f);
            }
        }
    }

    static void DrawStatusBar(Color color, string label)
    {
        var rect = GUILayoutUtility.GetRect(0, 24, GUILayout.ExpandWidth(true));
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
}
