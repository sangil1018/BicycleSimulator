using UnityEditor;
using UnityEngine;
using TrafficSystem;

[CustomEditor(typeof(Waypoint))]
public class WaypointEditor : Editor
{
    SerializedProperty nextWaypointsProp;
    SerializedProperty straightWeightProp;
    SerializedProperty rightWeightProp;
    SerializedProperty leftWeightProp;

    static readonly Color colorStraight  = new Color(1f,  0.85f, 0f,  1f);
    static readonly Color colorRightTurn = new Color(0f,  0.9f,  1f,  1f);
    static readonly Color colorLeftTurn  = new Color(0.2f,1f,    0.4f,1f);
    static readonly Color colorNone      = new Color(1f,  0.3f,  0.3f,1f);

    void OnEnable()
    {
        nextWaypointsProp   = serializedObject.FindProperty("nextWaypoints");
        straightWeightProp  = serializedObject.FindProperty("straightWeight");
        rightWeightProp     = serializedObject.FindProperty("rightWeight");
        leftWeightProp      = serializedObject.FindProperty("leftWeight");
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
                "연결된 슬롯의 가중치를 정규화해 확률로 사용합니다.\n" +
                "1개만 연결되면 해당 방향 100%."), true);

        int size = nextWaypointsProp.arraySize;
        bool hasS = size > 0 && nextWaypointsProp.GetArrayElementAtIndex(0).objectReferenceValue != null;
        bool hasR = size > 1 && nextWaypointsProp.GetArrayElementAtIndex(1).objectReferenceValue != null;
        bool hasL = size > 2 && nextWaypointsProp.GetArrayElementAtIndex(2).objectReferenceValue != null;
        int connected = (hasS ? 1 : 0) + (hasR ? 1 : 0) + (hasL ? 1 : 0);

        EditorGUILayout.Space(4);

        if (connected == 0)
        {
            DrawStatusBar(colorNone, "연결 없음 — 차량이 이 지점에서 정지합니다");
        }
        else if (connected == 1)
        {
            string dir = hasS ? "직진 100%" : (hasR ? "우회전 100%" : "좌회전 100%");
            Color  col = hasS ? colorStraight : (hasR ? colorRightTurn : colorLeftTurn);
            DrawStatusBar(col, dir);
        }
        else
        {
            EditorGUILayout.LabelField("── 방향 가중치 (연결된 슬롯만 적용) ──", EditorStyles.boldLabel);
            if (hasS) EditorGUILayout.PropertyField(straightWeightProp, new GUIContent("[0] 직진 가중치 (%)"));
            if (hasR) EditorGUILayout.PropertyField(rightWeightProp,    new GUIContent("[1] 우회전 가중치 (%)"));
            if (hasL) EditorGUILayout.PropertyField(leftWeightProp,     new GUIContent("[2] 좌회전 가중치 (%)"));

            float wS = hasS ? straightWeightProp.floatValue : 0f;
            float wR = hasR ? rightWeightProp.floatValue    : 0f;
            float wL = hasL ? leftWeightProp.floatValue     : 0f;
            float total = wS + wR + wL;

            float nS = total > 0f ? wS / total : 0f;
            float nR = total > 0f ? wR / total : 0f;
            float nL = total > 0f ? wL / total : 0f;

            DrawChanceBar(nS, nR, nL);

            string info = "";
            if (hasS) info += $"[0] 직진: {nS * 100f:F1}%   ";
            if (hasR) info += $"[1] 우회전: {nR * 100f:F1}%   ";
            if (hasL) info += $"[2] 좌회전: {nL * 100f:F1}%";
            EditorGUILayout.HelpBox(info.TrimEnd(), MessageType.None);
        }

        // ── 도구 ────────────────────────────────────────────────────────
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

    // 선택 여부와 무관하게 모든 웨이포인트 연결선 항상 표시
    [DrawGizmo(GizmoType.NonSelected | GizmoType.Selected | GizmoType.InSelectionHierarchy)]
    static void DrawGizmosAlways(Waypoint wp, GizmoType gizmoType)
    {
        var nexts = wp.NextWaypoints;
        bool isSelected = (gizmoType & GizmoType.Selected) != 0;

        // 웨이포인트 노드 구
        Handles.color = new Color(1f, 0.85f, 0f, isSelected ? 0.5f : 0.2f);
        Handles.SphereHandleCap(0, wp.transform.position, Quaternion.identity,
            isSelected ? 0.6f : 0.35f, EventType.Repaint);

        // TrafficLightStop 마커 표시 (빨간 정지선)
        if (wp.TryGetComponent(out TrafficLightStop stopMarker))
        {
            bool red = stopMarker.trafficLight != null &&
                       stopMarker.trafficLight.VehicleState != TrafficLightState.Green;
            Handles.color = red ? new Color(1f, 0.1f, 0.1f, 0.9f) : new Color(0.2f, 1f, 0.3f, 0.7f);
            Vector3 pos = wp.transform.position;
            Handles.DrawLine(pos + wp.transform.right * 1.5f, pos - wp.transform.right * 1.5f, 3f);
            if (isSelected && stopMarker.trafficLight != null)
            {
                Handles.color = new Color(1f, 0.5f, 0f, 0.8f);
                Handles.DrawDottedLine(pos, stopMarker.trafficLight.transform.position, 4f);
                Handles.Label(pos + Vector3.up * 0.8f, $"  STOP → {stopMarker.trafficLight.name}");
            }
        }

        if (nexts == null || nexts.Length == 0) return;

        // 비선택 시 간략한 선만, 선택 시 OnSceneGUI가 상세 표시
        if (isSelected) return;

        for (int i = 0; i < nexts.Length; i++)
        {
            if (nexts[i] == null) continue;
            Color col = i == 0 ? colorStraight : (i == 1 ? colorRightTurn : colorLeftTurn);
            Handles.color = new Color(col.r, col.g, col.b, 0.5f);
            Handles.DrawLine(wp.transform.position, nexts[i].transform.position, 2f);
        }
    }

    void OnSceneGUI()
    {
        var wp    = (Waypoint)target;
        var nexts = wp.NextWaypoints;
        if (nexts == null || nexts.Length == 0) return;

        // 정규화된 실제 확률 계산
        bool hasS2 = nexts.Length > 0 && nexts[0] != null;
        bool hasR2 = nexts.Length > 1 && nexts[1] != null;
        bool hasL2 = nexts.Length > 2 && nexts[2] != null;

        float wS    = hasS2 ? wp.StraightWeight : 0f;
        float wR    = hasR2 ? wp.RightWeight    : 0f;
        float wL    = hasL2 ? wp.LeftWeight     : 0f;
        float total = wS + wR + wL;
        float nS    = total > 0f ? wS / total * 100f : 0f;
        float nR    = total > 0f ? wR / total * 100f : 0f;
        float nL    = total > 0f ? wL / total * 100f : 0f;

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

            float pct   = i == 0 ? nS : (i == 1 ? nR : nL);
            string label = i == 0 ? $"직진 {pct:F0}%"
                         : i == 1 ? $"우회전 {pct:F0}%"
                         :           $"좌회전 {pct:F0}%";

            if (i == 0)
            {
                Handles.DrawLine(from, to, 3f);
                Vector3 mid = (from + to) * 0.5f;
                Vector3 dir = to - from; dir.y = 0f;
                if (dir.sqrMagnitude > 0.001f)
                    Handles.ArrowHandleCap(0, mid, Quaternion.LookRotation(dir.normalized), 3f, EventType.Repaint);
                Handles.Label(mid + Vector3.up * 0.6f, label);
            }
            else
            {
                DrawArcHandle(from, forward, to, rightTurn: i == 1, col, label);
            }
        }

        Handles.color = new Color(1f, 0.85f, 0f, 0.4f);
        Handles.SphereHandleCap(0, from, Quaternion.identity, 0.6f, EventType.Repaint);
    }

    static void DrawArcHandle(Vector3 startPos, Vector3 startForward,
                               Vector3 endPos, bool rightTurn, Color col, string label)
    {
        Vector3 s   = startPos; s.y = 0f;
        Vector3 e   = endPos;   e.y = 0f;
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
        if ( rightTurn && totalDeg < 0f) totalDeg += 360f;
        if (!rightTurn && totalDeg > 0f) totalDeg -= 360f;
        if (totalDeg >  180f) totalDeg -= 360f;
        if (totalDeg < -180f) totalDeg += 360f;

        float   y     = startPos.y;
        int     steps = 32;
        Vector3 prev  = center + startVec * r; prev.y = y;

        Handles.color = col;
        for (int k = 1; k <= steps; k++)
        {
            float   angle  = totalDeg * (k / (float)steps);
            Vector3 radial = Quaternion.AngleAxis(angle, Vector3.up) * startVec;
            Vector3 p = center + radial * r; p.y = y;
            Handles.DrawLine(prev, p, 3f);
            prev = p;
        }

        {
            float   halfAngle = totalDeg * 0.5f;
            Vector3 midRadial = Quaternion.AngleAxis(halfAngle, Vector3.up) * startVec;
            Vector3 midPos    = center + midRadial * r; midPos.y = y;
            Vector3 tangent   = totalDeg > 0f
                ? Vector3.Cross(Vector3.up, midRadial).normalized
                : Vector3.Cross(midRadial, Vector3.up).normalized;
            if (tangent.sqrMagnitude > 0.001f)
                Handles.ArrowHandleCap(0, midPos, Quaternion.LookRotation(tangent), 3f, EventType.Repaint);
            Handles.Label(midPos + Vector3.up * 0.6f, label);
        }
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
            EditorGUI.DrawRect(new Rect(cursor, iY, w, iH), new Color(1f,  0.85f, 0f,  0.85f));
            cursor += w;
        }
        if (rightF > 0f)
        {
            float w = iW * rightF;
            EditorGUI.DrawRect(new Rect(cursor, iY, w, iH), new Color(0f,  0.9f,  1f,  0.85f));
            cursor += w;
        }
        if (leftF > 0f)
        {
            float w = iW * leftF;
            EditorGUI.DrawRect(new Rect(cursor, iY, w, iH), new Color(0.2f,1f,    0.4f,0.85f));
        }
    }

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
