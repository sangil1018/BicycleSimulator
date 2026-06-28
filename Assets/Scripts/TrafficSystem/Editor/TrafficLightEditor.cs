using UnityEditor;
using UnityEngine;
using TrafficSystem;

[CustomEditor(typeof(TrafficLight))]
public class TrafficLightEditor : Editor
{
    void OnEnable()  => EditorApplication.update += RepaintIfPlaying;
    void OnDisable() => EditorApplication.update -= RepaintIfPlaying;
    void RepaintIfPlaying() { if (Application.isPlaying) Repaint(); }

    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        var light = (TrafficLight)target;

        EditorGUILayout.Space(8);
        EditorGUILayout.LabelField("── 보행 신호 상태 ─────────────────", EditorStyles.boldLabel);
        DrawPedestrianIndicator(light.PedestrianSignal);

        if (Application.isPlaying)
        {
            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("── 보행 신호 오버라이드 (콘텐츠용) ──────", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                light.PedestrianOverridden
                    ? "오버라이드 활성 — TrafficJunction이 보행 신호를 변경하지 않습니다."
                    : "TrafficJunction이 보행 신호를 제어 중입니다.",
                light.PedestrianOverridden ? MessageType.Warning : MessageType.None);

            EditorGUILayout.BeginHorizontal();
            var prev = GUI.backgroundColor;

            GUI.backgroundColor = new Color(1f, 0.3f, 0.3f);
            if (GUILayout.Button("고정: 빨강"))
                light.OverridePedestrianSignal(PedestrianState.Red);

            GUI.backgroundColor = new Color(0.3f, 1f, 0.4f);
            if (GUILayout.Button("고정: 초록"))
                light.OverridePedestrianSignal(PedestrianState.Green);

            GUI.enabled = light.PedestrianOverridden;
            GUI.backgroundColor = new Color(0.8f, 0.8f, 0.8f);
            if (GUILayout.Button("오버라이드 해제"))
                light.ClearPedestrianOverride();
            GUI.enabled = true;

            GUI.backgroundColor = prev;
            EditorGUILayout.EndHorizontal();
        }
        else
        {
            EditorGUILayout.HelpBox("오버라이드 버튼은 Play mode에서 사용 가능합니다.", MessageType.None);
        }
    }

    static void DrawPedestrianIndicator(PedestrianState state)
    {
        Color color = state == PedestrianState.Green
            ? new Color(0.2f, 1f,   0.3f)
            : new Color(1f,   0.2f, 0.2f);
        DrawBar(color, $"보행: {(state == PedestrianState.Green ? "GREEN" : "RED")}");
    }

    static void DrawBar(Color color, string label)
    {
        var rect = GUILayoutUtility.GetRect(0, 24, GUILayout.ExpandWidth(true));
        EditorGUI.DrawRect(rect, new Color(0.15f, 0.15f, 0.15f));
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
