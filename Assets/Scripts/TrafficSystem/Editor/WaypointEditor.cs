using UnityEditor;
using UnityEngine;
using TrafficSystem;

[CustomEditor(typeof(Waypoint))]
public class WaypointEditor : Editor
{
    SerializedProperty nextWaypointsProp;
    SerializedProperty rightTurnChanceProp;
    SerializedProperty leftTurnChanceProp;

    static readonly Color colorStraight = new Color(1f, 0.85f, 0f, 1f);
    static readonly Color colorRightTurn = new Color(0f, 0.9f, 1f, 1f);
    static readonly Color colorLeftTurn = new Color(0.2f, 1f, 0.4f, 1f);
    static readonly Color colorNone = new Color(1f, 0.3f, 0.3f, 1f);

    void OnEnable()
    {
        nextWaypointsProp = serializedObject.FindProperty("nextWaypoints");
        rightTurnChanceProp = serializedObject.FindProperty("rightTurnChance");
        leftTurnChanceProp = serializedObject.FindProperty("leftTurnChance");
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        var wp = (Waypoint)target;

        // ── 연결 설정 ───────────────────────────────────────────────────
        EditorGUILayout.LabelField("── 웨이포인트 연결 ────────────────────", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(nextWaypointsProp,
            new GUIContent("Next Waypoints",
                "[0] 직진  [1] 우회전  [2] 좌회전\n" +
                "연결된 슬롯이 1개뿐이면 해당 방향 100%."), true);

        int size = nextWaypointsProp.arraySize;

        // 실제로 연결된 슬롯 파악
        bool hasS = size > 0 && nextWaypointsProp.GetArrayElementAtIndex(0).objectReferenceValue != null;
        bool hasR = size > 1 && nextWaypointsProp.GetArrayElementAtIndex(1).objectReferenceValue != null;
        bool hasL = size > 2 && nextWaypointsProp.GetArrayElementAtIndex(2).objectReferenceValue != null;
        int connected = (hasS ? 1 : 0) + (hasR ? 1 : 0) + (hasL ? 1 : 0);

        EditorGUILayout.Space(4);

        // ── 상태 표시 ───────────────────────────────────────────────────
        if (connected == 0)
        {
            DrawStatusBar(colorNone, "연결 없음 — 차량이 이 지점에서 정지합니다");
        }
        else if (connected == 1)
        {
            string dir = hasR ? "우회전 100%" : (hasL ? "좌회전 100%" : "직진 전용");
            DrawStatusBar(hasR ? colorRightTurn : (hasL ? colorLeftTurn : colorStraight), dir);
        }
        else
        {
            EditorGUILayout.LabelField("── 방향 확률 ────────────────────", EditorStyles.boldLabel);
            if (hasR) EditorGUILayout.PropertyField(rightTurnChanceProp, new GUIContent("우회전 확률 (%)"));
            if (hasL) EditorGUILayout.PropertyField(leftTurnChanceProp, new GUIContent("좌회전 확률 (%)"));

            float r = hasR ? rightTurnChanceProp.floatValue : 0f;
            float l = hasL ? leftTurnChanceProp.floatValue : 0f;
            float s = hasS ? Mathf.Max(0f, 100f - r - l) : 0f;
            DrawChanceBar(s / 100f, r / 100f, l / 100f);

            string info = "";
            if (hasS) info += $"[0] 직진: {s:F0}%   ";
            if (hasR) info += $"[1] 우회전: {r:F0}%   ";
            if (hasL) info += $"[2] 좌회전: {l:F0}%";
            EditorGUILayout.HelpBox(info.TrimEnd(), MessageType.None);
        }

        // ── 자동 방향 ───────────────────────────────────────────────────
        EditorGUILayout.Space(6);
        EditorGUILayout.LabelField("── 도구 ────────────────────────────", EditorStyles.boldLabel);

        GUI.enabled = connected > 0;
        if (GUILayout.Button("↺ 자동 방향 설정  (첫 번째 연결 방향으로 회전)"))
        {
            Undo.RecordObject(wp.transform, "Auto Orient Waypoint");
            wp.AutoOrient();
        }
        GUI.enabled = true;

        serializedObject.ApplyModifiedProperties();
    }

    // ── Scene View ──────────────────────────────────────────────────────────
    void OnSceneGUI()
    {
        var wp = (Waypoint)target;
        var nexts = wp.NextWaypoints;
        if (nexts == null || nexts.Length == 0) return;

        Vector3 from    = wp.transform.position;
        Vector3 forward = wp.transform.forward;

        for (int i = 0; i < nexts.Length; i++)
        {
            if (nexts[i] == null) continue;

            Vector3 to  = nexts[i].transform.position;
            Color   col = i == 0 ? colorStraight
                        : i == 1 ? colorRightTurn
                        :           colorLeftTurn;
            Handles.color = col;

            string label = i == 0 ? "직진"
                         : i == 1 ? $"우회전 {wp.RightTurnChance:F0}%"
                         :           $"좌회전 {wp.LeftTurnChance:F0}%";

            if (i == 0)
            {
                // 직진 — 직선 + 중점 화살표
                Handles.DrawLine(from, to, 3f);
                Vector3 mid = (from + to) * 0.5f;
                Vector3 dir = to - from; dir.y = 0f;
                if (dir.sqrMagnitude > 0.001f)
                    Handles.ArrowHandleCap(0, mid, Quaternion.LookRotation(dir.normalized), 3f, EventType.Repaint);
                Handles.Label(mid + Vector3.up * 0.6f, label);
            }
            else
            {
                // 회전 — 원호
                DrawArcHandle(from, forward, to, rightTurn: i == 1, col, label);
            }
        }

        // 선택된 웨이포인트 강조
        Handles.color = new Color(1f, 0.85f, 0f, 0.4f);
        Handles.SphereHandleCap(0, from, Quaternion.identity, 0.6f, EventType.Repaint);
    }

    // 원호 핸들 그리기: CarController.TryStartArc 와 동일한 원 방정식
    static void DrawArcHandle(Vector3 startPos, Vector3 startForward,
                               Vector3 endPos, bool rightTurn, Color col, string label)
    {
        Vector3 s = startPos; s.y = 0f;
        Vector3 e = endPos;   e.y = 0f;
        Vector3 fwd = startForward; fwd.y = 0f;
        if (fwd.sqrMagnitude < 0.001f) { Handles.DrawLine(startPos, endPos, 2f); return; }
        fwd.Normalize();

        Vector3 n = rightTurn
            ?  Vector3.Cross(Vector3.up, fwd)
            : -Vector3.Cross(Vector3.up, fwd);

        Vector3 diff = s - e;
        float   dot  = Vector3.Dot(diff, n);
        if (Mathf.Abs(dot) < 0.001f) { Handles.DrawLine(startPos, endPos, 2f); return; }

        float r = -diff.sqrMagnitude / (2f * dot);
        if (r < 0.1f) { Handles.DrawLine(startPos, endPos, 2f); return; }

        Vector3 center   = s + n * r;
        Vector3 startVec = (s - center).normalized;
        float   totalDeg = Vector3.SignedAngle(startVec, (e - center).normalized, Vector3.up);
        if (rightTurn  && totalDeg < 0f) totalDeg += 360f;
        if (!rightTurn && totalDeg > 0f) totalDeg -= 360f;
        if (totalDeg >  180f) totalDeg -= 360f;
        if (totalDeg < -180f) totalDeg += 360f;

        float   y    = startPos.y;
        int     steps = 32;
        Vector3 prev = center + startVec * r; prev.y = y;

        Handles.color = col;
        for (int k = 1; k <= steps; k++)
        {
            float   angle  = totalDeg * (k / (float)steps);
            Vector3 radial = Quaternion.AngleAxis(angle, Vector3.up) * startVec;
            Vector3 p = center + radial * r; p.y = y;
            Handles.DrawLine(prev, p, 3f);
            prev = p;
        }

        // 중점 화살표
        {
            float   halfAngle  = totalDeg * 0.5f;
            Vector3 midRadial  = Quaternion.AngleAxis(halfAngle, Vector3.up) * startVec;
            Vector3 midPos     = center + midRadial * r; midPos.y = y;
            Vector3 tangent    = totalDeg > 0f
                ? Vector3.Cross(Vector3.up, midRadial).normalized   // CCW = 우회전
                : Vector3.Cross(midRadial, Vector3.up).normalized;  // CW  = 좌회전
            if (tangent.sqrMagnitude > 0.001f)
                Handles.ArrowHandleCap(0, midPos, Quaternion.LookRotation(tangent), 3f, EventType.Repaint);
            Handles.Label(midPos + Vector3.up * 0.6f, label);
        }
    }

    // ── Helpers ──────────────────────────────────────────────────────────────
    static void DrawStatusBar(Color color, string label)
    {
        var rect = GUILayoutUtility.GetRect(0, 22, GUILayout.ExpandWidth(true));
        EditorGUI.DrawRect(rect, new Color(0.12f, 0.12f, 0.12f));
        var inner = new Rect(rect.x + 2, rect.y + 2, rect.width - 4, rect.height - 4);
        EditorGUI.DrawRect(inner, color);
        var style = new GUIStyle(EditorStyles.boldLabel)
        {
            alignment = TextAnchor.MiddleCenter,
            normal = { textColor = Color.black }
        };
        EditorGUI.LabelField(inner, label, style);
    }

    static void DrawChanceBar(float straightF, float rightF, float leftF)
    {
        var rect = GUILayoutUtility.GetRect(0, 22, GUILayout.ExpandWidth(true));
        EditorGUI.DrawRect(rect, new Color(0.12f, 0.12f, 0.12f));

        float iW = rect.width - 4f;
        float iX = rect.x + 2f;
        float iY = rect.y + 2f;
        float iH = rect.height - 4f;

        float cursor = iX;
        if (straightF > 0f)
        {
            float w = iW * straightF;
            EditorGUI.DrawRect(new Rect(cursor, iY, w, iH), new Color(1f, 0.85f, 0f, 0.85f));
            cursor += w;
        }
        if (rightF > 0f)
        {
            float w = iW * rightF;
            EditorGUI.DrawRect(new Rect(cursor, iY, w, iH), new Color(0f, 0.9f, 1f, 0.85f));
            cursor += w;
        }
        if (leftF > 0f)
        {
            float w = iW * leftF;
            EditorGUI.DrawRect(new Rect(cursor, iY, w, iH), new Color(0.2f, 1f, 0.4f, 0.85f));
        }
    }

    // ── 전체 씬 자동 방향 메뉴 ─────────────────────────────────────────────
    [MenuItem("Tools/Traffic/모든 웨이포인트 자동 방향 설정")]
    static void AutoOrientAll()
    {
        var all = Object.FindObjectsByType<Waypoint>(FindObjectsSortMode.None);
        if (all.Length == 0) { Debug.Log("[WaypointEditor] 씬에 Waypoint 없음."); return; }

        Undo.SetCurrentGroupName("Auto Orient All Waypoints");
        int group = Undo.GetCurrentGroup();
        foreach (var wp in all)
        {
            Undo.RecordObject(wp.transform, "Auto Orient");
            wp.AutoOrient();
        }
        Undo.CollapseUndoOperations(group);
        Debug.Log($"[WaypointEditor] {all.Length}개 웨이포인트 방향 설정 완료.");
    }
}
