using UnityEditor;
using UnityEngine;
using TrafficSystem;

[CustomEditor(typeof(TrafficSpawner))]
public class TrafficSpawnerEditor : Editor
{
    SerializedProperty carPrefabsProp;
    SerializedProperty carCountProp;
    SerializedProperty spawnPointsProp;
    SerializedProperty heightOffsetProp;
    SerializedProperty spawnPerFrameProp;
    SerializedProperty maxPathStepsProp;

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
        carPrefabsProp    = serializedObject.FindProperty("carPrefabs");
        carCountProp      = serializedObject.FindProperty("carCount");
        spawnPointsProp   = serializedObject.FindProperty("spawnPoints");
        heightOffsetProp  = serializedObject.FindProperty("heightOffset");
        spawnPerFrameProp = serializedObject.FindProperty("spawnPerFrame");
        maxPathStepsProp  = serializedObject.FindProperty("maxPathSteps");
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        EditorGUILayout.PropertyField(carPrefabsProp, new GUIContent("Car Prefabs (랜덤 스폰)"), true);
        EditorGUILayout.PropertyField(carCountProp);
        EditorGUILayout.PropertyField(spawnPointsProp, new GUIContent("Spawn Points (시작 웨이포인트)"), true);
        EditorGUILayout.PropertyField(heightOffsetProp);
        EditorGUILayout.PropertyField(spawnPerFrameProp);
        EditorGUILayout.PropertyField(maxPathStepsProp, new GUIContent("Max Path Steps", "경로 탐색 최대 웨이포인트 수. 루프 도로가 길면 늘려주세요."));

        EditorGUILayout.Space(6);
        EditorGUILayout.LabelField("── Validation ──────────────────────", EditorStyles.boldLabel);

        if (GUILayout.Button("✓ Validate Setup"))
            Validate();

        if (!string.IsNullOrEmpty(validationResult))
            EditorGUILayout.HelpBox(validationResult, validationType);

        // 배분 표
        if (carPrefabsProp.arraySize > 0 && spawnPointsProp.arraySize > 0 && carCountProp.intValue > 0)
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

        if (carPrefabsProp.arraySize == 0)
        {
            sb.AppendLine("✗ Car Prefabs 미할당.");
        }
        else
        {
            bool allOk = true;
            for (int i = 0; i < carPrefabsProp.arraySize; i++)
            {
                var prefab = carPrefabsProp.GetArrayElementAtIndex(i).objectReferenceValue as GameObject;
                if (prefab == null)
                { sb.AppendLine($"✗ carPrefabs[{i}] null."); allOk = false; continue; }
                if (prefab.GetComponent<CarController>() == null)
                { sb.AppendLine($"✗ carPrefabs[{i}] '{prefab.name}' — CarController 없음."); allOk = false; }
            }
            if (allOk) sb.AppendLine($"✓ Car Prefabs OK ({carPrefabsProp.arraySize}개)");
        }

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
                    var nextWps = wp.NextWaypoints;
                    bool hasS2  = nextWps != null && nextWps.Length > 0 && nextWps[0] != null;
                    bool hasR2  = nextWps != null && nextWps.Length > 1 && nextWps[1] != null;
                    bool hasL2  = nextWps != null && nextWps.Length > 2 && nextWps[2] != null;
                    int  connectedCount = (hasS2 ? 1 : 0) + (hasR2 ? 1 : 0) + (hasL2 ? 1 : 0);

                    string routing;
                    if (connectedCount == 0)
                    {
                        routing = "❌ 다음 노드 없음";
                    }
                    else
                    {
                        float wS = hasS2 ? wp.StraightWeight : 0f;
                        float wR = hasR2 ? wp.RightWeight    : 0f;
                        float wL = hasL2 ? wp.LeftWeight     : 0f;
                        float tot = wS + wR + wL;
                        float nS  = tot > 0f ? wS / tot * 100f : 0f;
                        float nR  = tot > 0f ? wR / tot * 100f : 0f;
                        float nL  = tot > 0f ? wL / tot * 100f : 0f;

                        var parts = new System.Collections.Generic.List<string>(3);
                        if (hasS2) parts.Add($"직진 {nS:F0}%");
                        if (hasR2) parts.Add($"우회전 {nR:F0}%");
                        if (hasL2) parts.Add($"좌회전 {nL:F0}%");
                        routing = "→ " + string.Join(" / ", parts);
                    }
                    sb.AppendLine($"✓ Spawn [{i}] '{wp.name}'  {routing}");
                }
            }
        }

        validationResult = sb.ToString().TrimEnd();
        validationType   = validationResult.Contains("✗") ? MessageType.Error : MessageType.Info;
    }
}
