using UnityEditor;
using UnityEngine;
using TrafficSystem;

[CustomEditor(typeof(WaypointPath))]
public class WaypointPathEditor : Editor
{
    SerializedProperty waypointsProp;
    SerializedProperty loopProp;
    SerializedProperty branchesProp;

    static readonly Color pathColor   = new Color(1f, 0.9f, 0f, 1f);
    static readonly Color arrowColor  = new Color(1f, 0.6f, 0f, 1f);
    static readonly Color branchColor = new Color(0f, 1f, 0.9f, 1f);
    static readonly GUIStyle indexStyle = null; // initialized lazily

    void OnEnable()
    {
        waypointsProp = serializedObject.FindProperty("waypoints");
        loopProp      = serializedObject.FindProperty("loop");
        branchesProp  = serializedObject.FindProperty("branches");
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        var path = (WaypointPath)target;

        EditorGUILayout.PropertyField(loopProp);
        EditorGUILayout.PropertyField(waypointsProp, true);

        EditorGUILayout.Space(4);

        // Path stats
        float totalLength = CalculateTotalLength(path);
        EditorGUILayout.LabelField($"Waypoints: {path.Count}   Total Length: {totalLength:F1} m",
                                   EditorStyles.miniLabel);

        EditorGUILayout.Space(4);

        // Action buttons
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("＋ Add Waypoint"))
                AddWaypoint(path);

            GUI.enabled = path.Count > 0;
            if (GUILayout.Button("－ Remove Last"))
                RemoveLast(path);
            GUI.enabled = true;
        }

        if (GUILayout.Button("↺ Collect Children as Waypoints"))
            CollectChildren(path);

        // Branches
        EditorGUILayout.Space(6);
        EditorGUILayout.LabelField("── 분기 설정 (우회전 등) ────────────", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(branchesProp, new GUIContent("Branches"), true);

        if (branchesProp.arraySize > 0)
        {
            EditorGUILayout.HelpBox(
                "분기점은 Scene View에서 청록색 선으로 표시됩니다.\n" +
                "At Waypoint Index: 해당 웨이포인트 도달 시 확률 판정 후 Target Path로 전환.",
                MessageType.Info);
        }

        serializedObject.ApplyModifiedProperties();
    }

    // ── Scene View ─────────────────────────────────────────────────────────────

    void OnSceneGUI()
    {
        var path = (WaypointPath)target;
        if (path.Count == 0) return;

        serializedObject.Update();

        Event e = Event.current;

        // Draw path lines and arrows
        Handles.color = pathColor;
        for (int i = 0; i < path.Count; i++)
        {
            var wp = path.GetWaypoint(i);
            if (wp == null) continue;

            int next = i + 1;
            if (next < path.Count || path.Loop)
            {
                var wpNext = path.GetWaypoint(next);
                if (wpNext != null)
                {
                    Handles.DrawLine(wp.position, wpNext.position, 2f);

                    // Direction arrow at midpoint
                    Vector3 mid = (wp.position + wpNext.position) * 0.5f;
                    Vector3 dir = (wpNext.position - wp.position).normalized;
                    if (dir != Vector3.zero)
                    {
                        Handles.color = arrowColor;
                        Handles.ArrowHandleCap(0, mid, Quaternion.LookRotation(dir), 0.8f, EventType.Repaint);
                        Handles.color = pathColor;
                    }
                }
            }

            // Index label
            Handles.Label(wp.position + Vector3.up * 0.5f, $"  [{i}]");

            // Position handle — allows dragging waypoint in Scene View
            EditorGUI.BeginChangeCheck();
            Vector3 newPos = Handles.PositionHandle(wp.position, Quaternion.identity);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(wp, "Move Waypoint");
                wp.position = newPos;
            }
        }

        // Shift+click to insert waypoint near a path segment
        if (e.shift && e.type == EventType.MouseDown && e.button == 0)
        {
            InsertNearestWaypoint(path, e);
            e.Use();
        }

        serializedObject.ApplyModifiedProperties();
    }

    // ── Helpers ─────────────────────────────────────────────────────────────────

    void AddWaypoint(WaypointPath path)
    {
        Undo.RegisterCompleteObjectUndo(path, "Add Waypoint");

        Vector3 newPos = path.Count > 0
            ? path.GetWaypoint(path.Count - 1).position + path.transform.forward * 3f
            : path.transform.position;

        var go = new GameObject($"WP_{path.Count:D2}");
        Undo.RegisterCreatedObjectUndo(go, "Add Waypoint");
        go.transform.SetParent(path.transform);
        go.transform.position = newPos;

        // Add to array via SerializedProperty
        waypointsProp.arraySize++;
        waypointsProp.GetArrayElementAtIndex(waypointsProp.arraySize - 1)
                     .objectReferenceValue = go.transform;

        serializedObject.ApplyModifiedProperties();
        EditorUtility.SetDirty(path);
    }

    void RemoveLast(WaypointPath path)
    {
        if (path.Count == 0) return;

        int last = waypointsProp.arraySize - 1;
        var prop = waypointsProp.GetArrayElementAtIndex(last);
        var wp   = prop.objectReferenceValue as Transform;

        Undo.RegisterCompleteObjectUndo(path, "Remove Waypoint");
        prop.objectReferenceValue = null;
        waypointsProp.DeleteArrayElementAtIndex(last);

        if (wp != null)
            Undo.DestroyObjectImmediate(wp.gameObject);

        serializedObject.ApplyModifiedProperties();
    }

    void CollectChildren(WaypointPath path)
    {
        Undo.RecordObject(path, "Collect Children as Waypoints");
        waypointsProp.arraySize = path.transform.childCount;
        for (int i = 0; i < path.transform.childCount; i++)
            waypointsProp.GetArrayElementAtIndex(i).objectReferenceValue = path.transform.GetChild(i);
        serializedObject.ApplyModifiedProperties();
        EditorUtility.SetDirty(path);
    }

    void InsertNearestWaypoint(WaypointPath path, Event e)
    {
        // Find closest segment to mouse cursor
        Ray ray = HandleUtility.GUIPointToWorldRay(e.mousePosition);
        float bestT = 0;
        int   bestSeg = 0;
        float bestDist = float.MaxValue;

        int count = path.Loop ? path.Count : path.Count - 1;
        for (int i = 0; i < count; i++)
        {
            var a = path.GetWaypoint(i);
            var b = path.GetWaypoint((i + 1) % path.Count);
            if (a == null || b == null) continue;

            float t;
            float d = HandleUtility.DistancePointToLineSegment(
                          new Vector3(e.mousePosition.x, Screen.height - e.mousePosition.y, 0),
                          HandleUtility.WorldToGUIPoint(a.position),
                          HandleUtility.WorldToGUIPoint(b.position));

            if (d < bestDist)
            {
                bestDist = d;
                bestSeg  = i;
            }
        }

        if (bestDist > 30f) return; // too far from any segment

        // Insert position = midpoint of segment
        var segA  = path.GetWaypoint(bestSeg);
        var segB  = path.GetWaypoint((bestSeg + 1) % path.Count);
        Vector3 insertPos = segA != null && segB != null
            ? (segA.position + segB.position) * 0.5f
            : path.transform.position;

        var go = new GameObject($"WP_{bestSeg + 1:D2}_ins");
        Undo.RegisterCreatedObjectUndo(go, "Insert Waypoint");
        go.transform.SetParent(path.transform);
        go.transform.position = insertPos;

        waypointsProp.InsertArrayElementAtIndex(bestSeg + 1);
        waypointsProp.GetArrayElementAtIndex(bestSeg + 1).objectReferenceValue = go.transform;
        serializedObject.ApplyModifiedProperties();
        EditorUtility.SetDirty(path);
    }

    static float CalculateTotalLength(WaypointPath path)
    {
        float total = 0f;
        int count = path.Loop ? path.Count : path.Count - 1;
        for (int i = 0; i < count; i++)
        {
            var a = path.GetWaypoint(i);
            var b = path.GetWaypoint((i + 1) % path.Count);
            if (a != null && b != null)
                total += Vector3.Distance(a.position, b.position);
        }
        return total;
    }
}
