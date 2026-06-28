using UnityEditor;
using UnityEngine;
using TrafficSystem;

[CustomEditor(typeof(TrafficSignal))]
public class TrafficSignalEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        var signal = (TrafficSignal)target;

        // 상태 컬러 바
        EditorGUILayout.Space(6);
        Color barColor = signal.State == SignalState.Green  ? new Color(0.1f, 0.9f, 0.2f) :
                         signal.State == SignalState.Yellow ? new Color(1f,   0.85f, 0f) :
                                                              new Color(0.9f, 0.15f, 0.15f);
        DrawStatusBar(barColor, $"● {signal.State}  (CanPass: {signal.CanPass})");

        // Play 모드 전용 강제 버튼
        if (!Application.isPlaying) return;
        EditorGUILayout.Space(4);
        EditorGUILayout.LabelField("── Play 모드 강제 설정 ──────────────", EditorStyles.boldLabel);
        EditorGUILayout.BeginHorizontal();
        GUI.backgroundColor = new Color(0.9f, 0.2f, 0.2f);
        if (GUILayout.Button("Red"))    signal.SetState(SignalState.Red);
        GUI.backgroundColor = new Color(1f, 0.85f, 0.1f);
        if (GUILayout.Button("Yellow")) signal.SetState(SignalState.Yellow);
        GUI.backgroundColor = new Color(0.2f, 0.85f, 0.2f);
        if (GUILayout.Button("Green"))  signal.SetState(SignalState.Green);
        GUI.backgroundColor = Color.white;
        EditorGUILayout.EndHorizontal();

        if (GUI.changed) EditorUtility.SetDirty(target);
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
