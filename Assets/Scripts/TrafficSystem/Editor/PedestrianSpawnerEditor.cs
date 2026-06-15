using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using TrafficSystem;

[CustomEditor(typeof(PedestrianSpawner))]
public class PedestrianSpawnerEditor : Editor
{
    SerializedProperty pedPrefabProp;
    SerializedProperty pedCountProp;
    SerializedProperty routesProp;

    string      validationResult = "";
    MessageType validationType   = MessageType.None;

    static readonly Color[] routeColors =
    {
        new Color(0.2f, 0.8f, 1f,   0.9f),
        new Color(1f,   0.5f, 0.2f, 0.9f),
        new Color(0.4f, 1f,   0.4f, 0.9f),
        new Color(1f,   0.3f, 0.6f, 0.9f),
        new Color(0.9f, 1f,   0.2f, 0.9f),
        new Color(0.6f, 0.3f, 1f,   0.9f),
    };

    void OnEnable()
    {
        pedPrefabProp = serializedObject.FindProperty("pedestrianPrefab");
        pedCountProp  = serializedObject.FindProperty("pedestrianCount");
        routesProp    = serializedObject.FindProperty("routes");
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        EditorGUILayout.PropertyField(pedPrefabProp);
        EditorGUILayout.PropertyField(pedCountProp);
        EditorGUILayout.PropertyField(routesProp, true);

        EditorGUILayout.Space(6);
        EditorGUILayout.LabelField("── Validation ──────────────────────", EditorStyles.boldLabel);

        if (GUILayout.Button("✓ Validate Setup"))
            Validate();

        if (!string.IsNullOrEmpty(validationResult))
            EditorGUILayout.HelpBox(validationResult, validationType);

        // Route summary table
        if (routesProp.arraySize > 0)
        {
            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("Route Summary:", EditorStyles.miniBoldLabel);

            for (int i = 0; i < routesProp.arraySize; i++)
            {
                var    routeProp  = routesProp.GetArrayElementAtIndex(i);
                var    wpsProp    = routeProp.FindPropertyRelative("waypoints");
                var    lightProp  = routeProp.FindPropertyRelative("crosswalkLight");
                string fallback   = lightProp.objectReferenceValue != null
                                  ? lightProp.objectReferenceValue.name : "none";

                // CrosswalkWaypoint 개수 카운트
                int cwCount = CountCrosswalkWaypoints(wpsProp);

                Rect r = GUILayoutUtility.GetRect(0, 18, GUILayout.ExpandWidth(true));
                EditorGUI.DrawRect(r, routeColors[i % routeColors.Length] * 0.4f);
                EditorGUI.LabelField(r,
                    $"  Route [{i}]  WPs: {wpsProp.arraySize}  Crosswalks: {cwCount}  Fallback: {fallback}",
                    EditorStyles.miniLabel);
            }
        }

        serializedObject.ApplyModifiedProperties();
    }

    // ── Scene View ──────────────────────────────────────────────────────────────

    void OnSceneGUI()
    {
        for (int ri = 0; ri < routesProp.arraySize; ri++)
        {
            var routeProp = routesProp.GetArrayElementAtIndex(ri);
            var wpsProp   = routeProp.FindPropertyRelative("waypoints");

            Color c = routeColors[ri % routeColors.Length];
            Handles.color = c;

            Vector3? prev = null;
            for (int wi = 0; wi < wpsProp.arraySize; wi++)
            {
                var wpRef = wpsProp.GetArrayElementAtIndex(wi).objectReferenceValue as Transform;
                if (wpRef == null) continue;

                Vector3 pos    = wpRef.position;
                var     marker = wpRef.GetComponent<CrosswalkWaypoint>();

                if (marker != null)
                {
                    // 전용 신호등 이름 표시
                    string lightLabel = marker.tLight != null ? marker.tLight.name : "fallback";
                    Handles.color = new Color(1f, 0.2f, 0.2f, 1f);
                    DrawDiamond(pos, 0.4f);
                    Handles.Label(pos + Vector3.up * 0.7f, $"  ✖ Crosswalk [{lightLabel}]");
                    Handles.color = c;
                }
                else
                {
                    Handles.DotHandleCap(0, pos, Quaternion.identity, 0.2f, EventType.Repaint);
                }

                if (prev.HasValue)
                    Handles.DrawLine(prev.Value, pos, 2f);

                prev = pos;
            }

            if (wpsProp.arraySize > 0)
            {
                var first = wpsProp.GetArrayElementAtIndex(0).objectReferenceValue as Transform;
                if (first != null)
                    Handles.Label(first.position + Vector3.up * 1.2f, $"  Route [{ri}]");
            }
        }
    }

    static void DrawDiamond(Vector3 center, float size)
    {
        Vector3[] verts =
        {
            center + Vector3.forward * size,
            center + Vector3.right   * size,
            center - Vector3.forward * size,
            center - Vector3.right   * size,
        };
        for (int i = 0; i < 4; i++)
            Handles.DrawLine(verts[i], verts[(i + 1) % 4], 2f);
    }

    // ── Validation ──────────────────────────────────────────────────────────────

    void Validate()
    {
        var sb = new System.Text.StringBuilder();

        var prefab = pedPrefabProp.objectReferenceValue as GameObject;
        if (prefab == null)
        {
            sb.AppendLine("✗ Pedestrian Prefab 미할당.");
        }
        else
        {
            sb.AppendLine(prefab.GetComponent<PedestrianController>() != null
                ? "✓ PedestrianController OK"
                : "✗ Prefab에 PedestrianController 없음.");
            sb.AppendLine(prefab.GetComponent<NavMeshAgent>() != null
                ? "✓ NavMeshAgent OK"
                : "✗ Prefab에 NavMeshAgent 없음.");
        }

        if (routesProp.arraySize == 0)
        {
            sb.AppendLine("✗ Route가 없습니다.");
        }
        else
        {
            for (int i = 0; i < routesProp.arraySize; i++)
            {
                var routeProp = routesProp.GetArrayElementAtIndex(i);
                var wpsProp   = routeProp.FindPropertyRelative("waypoints");
                var lightProp = routeProp.FindPropertyRelative("crosswalkLight");
                bool hasFallback = lightProp.objectReferenceValue != null;

                if (wpsProp.arraySize == 0)
                {
                    sb.AppendLine($"✗ Route [{i}] 웨이포인트 없음.");
                    continue;
                }

                sb.AppendLine($"✓ Route [{i}] — {wpsProp.arraySize} waypoints");

                // CrosswalkWaypoint 개별 검사
                bool hasCrosswalk = false;
                for (int wi = 0; wi < wpsProp.arraySize; wi++)
                {
                    var wpRef = wpsProp.GetArrayElementAtIndex(wi).objectReferenceValue as Transform;
                    if (wpRef == null) continue;
                    var marker = wpRef.GetComponent<CrosswalkWaypoint>();
                    if (marker == null) continue;

                    hasCrosswalk = true;
                    if (marker.tLight != null)
                        sb.AppendLine($"  ✓ CrosswalkWaypoint '{wpRef.name}' → {marker.tLight.name}");
                    else if (hasFallback)
                        sb.AppendLine($"  ⚠ CrosswalkWaypoint '{wpRef.name}' — 전용 신호등 없음 (fallback 사용)");
                    else
                        sb.AppendLine($"  ✗ CrosswalkWaypoint '{wpRef.name}' — 신호등 미지정 (대기 안 함)");
                }

                if (!hasCrosswalk)
                    sb.AppendLine($"  ⚠ Route [{i}] CrosswalkWaypoint 없음 (신호 대기 없이 통과)");
            }
        }

        validationResult = sb.ToString().TrimEnd();
        validationType   = validationResult.Contains("✗") ? MessageType.Error : MessageType.Info;
    }

    static int CountCrosswalkWaypoints(SerializedProperty wpsProp)
    {
        int count = 0;
        for (int i = 0; i < wpsProp.arraySize; i++)
        {
            var t = wpsProp.GetArrayElementAtIndex(i).objectReferenceValue as Transform;
            if (t != null && t.GetComponent<CrosswalkWaypoint>() != null) count++;
        }
        return count;
    }
}
