using UnityEditor;
using UnityEngine;
using TrafficSystem;

[CustomEditor(typeof(TrafficSignal))]
public class TrafficSignalEditor : Editor
{
    void OnEnable()  => EditorApplication.update += RepaintIfPlaying;
    void OnDisable() => EditorApplication.update -= RepaintIfPlaying;
    void RepaintIfPlaying() { if (Application.isPlaying) Repaint(); }

    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        var signal = (TrafficSignal)target;

        // 차량 신호 상태 바
        EditorGUILayout.Space(6);
        Color vehicleColor = signal.State == SignalState.Green  ? new Color(0.1f, 0.9f, 0.2f) :
                             signal.State == SignalState.Yellow ? new Color(1f,   0.85f, 0f) :
                                                                  new Color(0.9f, 0.15f, 0.15f);
        DrawBar(vehicleColor, $"차량: {signal.State}  (CanPass: {signal.CanPass})");

        // 보행자 신호 상태 바
        EditorGUILayout.Space(2);
        Color pedColor = signal.PedestrianSignal == PedestrianState.Green
            ? new Color(0.2f, 1f, 0.3f)
            : new Color(1f, 0.2f, 0.2f);
        DrawBar(pedColor, $"보행: {(signal.PedestrianSignal == PedestrianState.Green ? "GREEN" : "RED")}");

        if (!Application.isPlaying)
        {
            EditorGUILayout.Space(4);
            EditorGUILayout.HelpBox("강제 설정 버튼은 Play mode에서 사용 가능합니다.", MessageType.None);
            if (GUI.changed) EditorUtility.SetDirty(target);
            return;
        }

        // ── 차량 신호 강제 설정 ───────────────────────────────────────────
        EditorGUILayout.Space(4);
        EditorGUILayout.LabelField("── 차량 신호 강제 설정 ──────────────────", EditorStyles.boldLabel);
        EditorGUILayout.BeginHorizontal();
        GUI.backgroundColor = new Color(0.9f, 0.2f, 0.2f);
        if (GUILayout.Button("Red"))    signal.SetState(SignalState.Red);
        GUI.backgroundColor = new Color(1f, 0.85f, 0.1f);
        if (GUILayout.Button("Yellow")) signal.SetState(SignalState.Yellow);
        GUI.backgroundColor = new Color(0.2f, 0.85f, 0.2f);
        if (GUILayout.Button("Green"))  signal.SetState(SignalState.Green);
        GUI.backgroundColor = Color.white;
        EditorGUILayout.EndHorizontal();

        // ── 보행자 신호 오버라이드 ────────────────────────────────────────
        EditorGUILayout.Space(4);
        EditorGUILayout.LabelField("── 보행자 신호 오버라이드 (콘텐츠용) ──────", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            signal.PedestrianOverridden
                ? "오버라이드 활성 — TrafficJunction이 보행 신호를 변경하지 않습니다."
                : "TrafficJunction이 보행 신호를 제어 중입니다.",
            signal.PedestrianOverridden ? MessageType.Warning : MessageType.None);

        EditorGUILayout.BeginHorizontal();
        var prev = GUI.backgroundColor;

        GUI.backgroundColor = new Color(1f, 0.3f, 0.3f);
        if (GUILayout.Button("고정: 빨강"))
            signal.OverridePedestrianSignal(PedestrianState.Red);

        GUI.backgroundColor = new Color(0.3f, 1f, 0.4f);
        if (GUILayout.Button("고정: 초록"))
            signal.OverridePedestrianSignal(PedestrianState.Green);

        GUI.enabled = signal.PedestrianOverridden;
        GUI.backgroundColor = new Color(0.8f, 0.8f, 0.8f);
        if (GUILayout.Button("오버라이드 해제"))
            signal.ClearPedestrianOverride();
        GUI.enabled = true;

        GUI.backgroundColor = prev;
        EditorGUILayout.EndHorizontal();

        if (GUI.changed) EditorUtility.SetDirty(target);
    }

    static void DrawBar(Color color, string label)
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
