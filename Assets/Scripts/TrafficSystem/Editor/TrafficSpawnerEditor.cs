using UnityEditor;
using UnityEngine;
using TrafficSystem;

[CustomEditor(typeof(TrafficSpawner))]
public class TrafficSpawnerEditor : Editor
{
    SerializedProperty carPrefabProp;
    SerializedProperty carCountProp;
    SerializedProperty spawnPointsProp;
    SerializedProperty heightOffsetProp;
    SerializedProperty spawnPerFrameProp;

    string      validationResult = "";
    MessageType validationType   = MessageType.None;

    static readonly Color[] spawnColors =
    {
        new Color(1f,  0.4f, 0.4f, 0.8f),
        new Color(0.4f,1f,  0.4f, 0.8f),
        new Color(0.4f,0.6f,1f,  0.8f),
        new Color(1f,  0.9f,0.2f,0.8f),
        new Color(0.9f,0.4f,1f,  0.8f),
        new Color(0.3f,1f,  0.9f,0.8f),
    };

    void OnEnable()
    {
        carPrefabProp    = serializedObject.FindProperty("carPrefab");
        carCountProp     = serializedObject.FindProperty("carCount");
        spawnPointsProp  = serializedObject.FindProperty("spawnPoints");
        heightOffsetProp = serializedObject.FindProperty("heightOffset");
        spawnPerFrameProp = serializedObject.FindProperty("spawnPerFrame");
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        EditorGUILayout.PropertyField(carPrefabProp);
        EditorGUILayout.PropertyField(carCountProp);
        EditorGUILayout.PropertyField(spawnPointsProp, new GUIContent("Spawn Points (시작 웨이포인트)"), true);
        EditorGUILayout.PropertyField(heightOffsetProp);
        EditorGUILayout.PropertyField(spawnPerFrameProp);

        EditorGUILayout.Space(6);
        EditorGUILayout.LabelField("── Validation ──────────────────────", EditorStyles.boldLabel);

        if (GUILayout.Button("✓ Validate Setup"))
            Validate();

        if (!string.IsNullOrEmpty(validationResult))
            EditorGUILayout.HelpBox(validationResult, validationType);

        // 배분 표
        if (spawnPointsProp.arraySize > 0 && carCountProp.intValue > 0)
        {
            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("Car Distribution per Spawn Point:", EditorStyles.miniBoldLabel);

            int carCount   = carCountProp.intValue;
            int pointCount = spawnPointsProp.arraySize;
            for (int i = 0; i < pointCount; i++)
            {
                var wpRef  = spawnPointsProp.GetArrayElementAtIndex(i).objectReferenceValue;
                string name = wpRef != null ? wpRef.name : "(none)";
                int assigned = 0;
                for (int c = 0; c < carCount; c++)
                    if (c % pointCount == i) assigned++;

                Rect r = GUILayoutUtility.GetRect(0, 18, GUILayout.ExpandWidth(true));
                EditorGUI.DrawRect(r, spawnColors[i % spawnColors.Length] * 0.35f);
                EditorGUI.LabelField(r, $"  [{i}] {name}", $"{assigned} cars", EditorStyles.miniLabel);
            }
        }

        serializedObject.ApplyModifiedProperties();
    }

    void OnSceneGUI()
    {
        float height = heightOffsetProp.floatValue;

        for (int i = 0; i < spawnPointsProp.arraySize; i++)
        {
            var wp = spawnPointsProp.GetArrayElementAtIndex(i).objectReferenceValue as Waypoint;
            if (wp == null) continue;

            Color c = spawnColors[i % spawnColors.Length];
            Handles.color = c;

            Vector3 spawnPos = wp.transform.position + Vector3.up * height;
            Handles.SphereHandleCap(0, spawnPos, Quaternion.identity, 0.7f, EventType.Repaint);
            Handles.Label(spawnPos + Vector3.up * 0.9f, $"Spawn [{i}]");

            // 방향 화살표
            Handles.ArrowHandleCap(0, spawnPos, wp.transform.rotation, 1.5f, EventType.Repaint);
        }
    }

    void Validate()
    {
        var sb = new System.Text.StringBuilder();

        var prefab = carPrefabProp.objectReferenceValue as GameObject;
        if (prefab == null)
            sb.AppendLine("✗ Car Prefab 미할당.");
        else if (prefab.GetComponent<CarController>() == null)
            sb.AppendLine("✗ Car Prefab에 CarController 없음.");
        else
            sb.AppendLine("✓ Car Prefab OK");

        if (spawnPointsProp.arraySize == 0)
        {
            sb.AppendLine("✗ Spawn Points 미설정.");
        }
        else
        {
            for (int i = 0; i < spawnPointsProp.arraySize; i++)
            {
                var wp = spawnPointsProp.GetArrayElementAtIndex(i).objectReferenceValue as Waypoint;
                if (wp == null)
                {
                    sb.AppendLine($"✗ Spawn Point [{i}] null.");
                }
                else
                {
                    int nextCount = wp.NextWaypoints != null ? wp.NextWaypoints.Length : 0;
                    string routing = nextCount == 0 ? "❌ 다음 노드 없음" :
                                     nextCount == 1 ? "→ 직진" :
                                     $"→ 직진 {100f - wp.RightTurnChance:F0}% / 우회전 {wp.RightTurnChance:F0}%";
                    sb.AppendLine($"✓ Spawn [{i}] '{wp.name}'  {routing}");
                }
            }
        }

        validationResult = sb.ToString().TrimEnd();
        validationType   = validationResult.Contains("✗") ? MessageType.Error : MessageType.Info;
    }
}
