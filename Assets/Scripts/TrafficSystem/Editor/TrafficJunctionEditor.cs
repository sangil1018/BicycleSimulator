using System.Collections.Generic;
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

        // ── T자형 판정 ────────────────────────────────────────────────────
        EditorGUILayout.Space(6);
        EditorGUILayout.LabelField("── 교차로 형태 ─────────────────────", EditorStyles.boldLabel);

        int armCount = CountApproachArms(junction, out string armDesc);
        string shape = armCount <= 0 ? "판정 불가 (stopSignal 연결된 노드 없음)"
                     : armCount <= 3 ? $"T자형 추정 — 진입 방향 {armCount}개 ({armDesc})"
                                     : $"교차형 추정 — 진입 방향 {armCount}개 ({armDesc})";
        EditorGUILayout.HelpBox(shape, armCount <= 0 ? MessageType.Warning : MessageType.None);

        if (armCount > 0 && GUILayout.Button($"노드 그래프 판정대로 T자 체크 {(armCount <= 3 ? "켜기" : "끄기")}"))
        {
            var sp = serializedObject.FindProperty("tShaped");
            if (sp != null)
            {
                sp.boolValue = armCount <= 3;
                serializedObject.ApplyModifiedProperties();   // Undo 자동 등록
            }
        }

        if (junction.TShaped)
            EditorGUILayout.HelpBox(
                "T자 모드 — 모든 차량 신호가 동시에 초록/황색/적색으로 바뀝니다.\n" +
                "짝수 페이즈 = 전체 통행, 홀수 페이즈 = 전체 정지 + 전체 보행 초록.\n" +
                "페이즈의 vehicleGreen/pedestrianGreen 목록은 대상 수집에만 쓰이고 방향 분리는 무시됩니다.",
                MessageType.Info);

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
        if (Application.isPlaying) Repaint();
    }

    void OnSceneGUI()
    {
        if (!target) return;
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

    // 이 교차로의 신호를 stopSignal로 참조하는 TrafficNode들의 진행 방향을 세어
    // 실제 진입 방향(arm) 개수를 구한다. 3개 이하면 T자형으로 본다.
    static int CountApproachArms(TrafficJunction junction, out string desc)
    {
        desc = "";

        // 이 교차로에 등록된 차량 신호 수집
        var mine = new HashSet<TrafficSignal>();
        var so = new SerializedObject(junction);
        var phasesProp = so.FindProperty("phases");
        if (phasesProp != null)
            for (int p = 0; p < phasesProp.arraySize; p++)
            {
                var vg = phasesProp.GetArrayElementAtIndex(p).FindPropertyRelative("vehicleGreen");
                if (vg == null) continue;
                for (int i = 0; i < vg.arraySize; i++)
                    if (vg.GetArrayElementAtIndex(i).objectReferenceValue is TrafficSignal s)
                        mine.Add(s);
            }
        if (mine.Count == 0) return 0;

        // 방향을 90° 단위 4분면으로 뭉쳐서 센다 (북/동/남/서)
        var buckets = new HashSet<int>();
        foreach (var node in Object.FindObjectsByType<TrafficNode>(FindObjectsSortMode.None))
        {
            if (node == null || node.StopSignal == null || !mine.Contains(node.StopSignal)) continue;
            float yaw = node.transform.eulerAngles.y;
            buckets.Add(Mathf.RoundToInt(yaw / 90f) & 3);
        }
        if (buckets.Count == 0) return 0;

        string[] names = { "북", "동", "남", "서" };
        var parts = new List<string>();
        for (int i = 0; i < 4; i++)
            if (buckets.Contains(i)) parts.Add(names[i] + "행");
        desc = string.Join(", ", parts);
        return buckets.Count;
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
