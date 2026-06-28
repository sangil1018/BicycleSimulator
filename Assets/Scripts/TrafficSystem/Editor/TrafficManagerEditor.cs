using UnityEditor;
using UnityEngine;
using TrafficSystem;

[CustomEditor(typeof(TrafficManager))]
public class TrafficManagerEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        var mgr = (TrafficManager)target;
        var so  = new SerializedObject(mgr);

        var prefabsProp     = so.FindProperty("vehiclePrefabs");
        var spawnNodesProp  = so.FindProperty("spawnNodes");
        var vehicleCountProp = so.FindProperty("vehicleCount");

        EditorGUILayout.Space(6);

        // ── 유효성 검사 ──────────────────────────────────────────────────
        if (GUILayout.Button("✔ Validate"))
        {
            bool ok = true;

            if (prefabsProp.arraySize == 0)
            { Debug.LogError("[TrafficManager] vehiclePrefabs 미설정."); ok = false; }

            for (int i = 0; i < prefabsProp.arraySize; i++)
            {
                var go = prefabsProp.GetArrayElementAtIndex(i).objectReferenceValue as GameObject;
                if (go == null) { Debug.LogWarning($"[TrafficManager] vehiclePrefabs[{i}] null."); ok = false; continue; }
                if (!go.TryGetComponent<TrafficVehicle>(out _))
                    Debug.LogWarning($"[TrafficManager] '{go.name}'에 TrafficVehicle 없음.");
            }

            if (spawnNodesProp.arraySize == 0)
            { Debug.LogError("[TrafficManager] spawnNodes 미설정."); ok = false; }

            int nullNodes = 0;
            for (int i = 0; i < spawnNodesProp.arraySize; i++)
                if (spawnNodesProp.GetArrayElementAtIndex(i).objectReferenceValue == null) nullNodes++;
            if (nullNodes > 0)
                Debug.LogWarning($"[TrafficManager] spawnNodes 중 {nullNodes}개가 null입니다.");

            if (ok) Debug.Log("[TrafficManager] Validate 통과.");
        }

        // ── 분배 테이블 ──────────────────────────────────────────────────
        if (spawnNodesProp.arraySize > 0 && vehicleCountProp.intValue > 0)
        {
            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("── 스폰 분배 (노드당 차량 수) ─────────", EditorStyles.boldLabel);
            int nodeCount = spawnNodesProp.arraySize;
            int total     = vehicleCountProp.intValue;
            int perNode   = total / nodeCount;
            int remainder = total % nodeCount;
            EditorGUILayout.HelpBox(
                $"총 {total}대 / {nodeCount}개 노드 = 노드당 {perNode}대" +
                (remainder > 0 ? $" (처음 {remainder}개 노드에 +1대)" : ""),
                MessageType.None);
        }
    }

    [DrawGizmo(GizmoType.NonSelected | GizmoType.Selected | GizmoType.InSelectionHierarchy)]
    static void DrawGizmosAlways(TrafficManager mgr, GizmoType gizmoType)
    {
        if (!mgr) return;
        var so = new SerializedObject(mgr);
        var sp = so.FindProperty("spawnNodes");
        if (sp == null) return;

        bool isSelected = (gizmoType & GizmoType.Selected) != 0;

        for (int i = 0; i < sp.arraySize; i++)
        {
            var node = sp.GetArrayElementAtIndex(i).objectReferenceValue as TrafficNode;
            if (node == null) continue;

            Color col = new Color(0f, 0.8f, 1f, isSelected ? 0.9f : 0.5f);
            Handles.color = col;
            Handles.SphereHandleCap(0, node.transform.position + Vector3.up * 0.6f,
                Quaternion.identity, 0.5f, EventType.Repaint);
            Handles.ArrowHandleCap(0, node.transform.position + Vector3.up * 0.6f,
                node.transform.rotation, 2f, EventType.Repaint);

            if (isSelected)
                Handles.Label(node.transform.position + Vector3.up * 1.2f, $"  Spawn[{i}]");
        }
    }
}
