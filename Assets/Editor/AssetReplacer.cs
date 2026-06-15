using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public class AssetReplacer : EditorWindow
{
    GameObject prefab;
    Vector2 scrollPos;

    [MenuItem("Tools/에셋 리플레이서")]
    static void Open() => GetWindow<AssetReplacer>("에셋 리플레이서");

    void OnGUI()
    {
        EditorGUILayout.Space(6);

        // Prefab field
        EditorGUI.BeginChangeCheck();
        prefab = (GameObject)EditorGUILayout.ObjectField("교체할 프리팹", prefab, typeof(GameObject), false);

        EditorGUILayout.Space(4);

        // Selected objects list
        var selection = Selection.gameObjects;
        EditorGUILayout.LabelField($"선택된 오브젝트: {selection.Length}개", EditorStyles.boldLabel);

        scrollPos = EditorGUILayout.BeginScrollView(scrollPos, GUILayout.MaxHeight(160));
        foreach (var go in selection)
            EditorGUILayout.LabelField("  • " + go.name);
        EditorGUILayout.EndScrollView();

        EditorGUILayout.Space(8);

        // Validate
        bool canApply = prefab != null && selection.Length > 0;
        if (!canApply)
        {
            EditorGUILayout.HelpBox(
                prefab == null ? "교체할 프리팹을 선택하세요." : "씬에서 오브젝트를 선택하세요.",
                MessageType.Info);
        }

        GUI.enabled = canApply;
        if (GUILayout.Button("적용", GUILayout.Height(32)))
            Replace(selection);
        GUI.enabled = true;

        EditorGUILayout.Space(12);
        EditorGUILayout.LabelField("센터 빈 오브젝트 생성", EditorStyles.boldLabel);

        GUI.enabled = selection.Length > 0;
        if (GUILayout.Button("선택 오브젝트 센터에 빈 오브젝트 생성", GUILayout.Height(28)))
            CreateCenterEmpty(selection);
        GUI.enabled = true;
    }

    void OnSelectionChange() => Repaint();

    void CreateCenterEmpty(GameObject[] targets)
    {
        var center = Vector3.zero;
        foreach (var go in targets)
            center += go.transform.position;
        center /= targets.Length;

        var empty = new GameObject("Center");
        Undo.RegisterCreatedObjectUndo(empty, "센터 빈 오브젝트 생성");
        empty.transform.position = center;

        Selection.activeGameObject = empty;
        EditorGUIUtility.PingObject(empty);
    }

    void Replace(GameObject[] targets)
    {
        // Snapshot transforms before any destruction
        var snapshots = new List<(Transform parent, string name, Vector3 pos, Quaternion rot, Vector3 scale)>();
        foreach (var go in targets)
        {
            var t = go.transform;
            snapshots.Add((t.parent, go.name, t.position, t.rotation, t.lossyScale));
        }

        Undo.SetCurrentGroupName("에셋 리플레이서 적용");
        int undoGroup = Undo.GetCurrentGroup();

        var newObjects = new List<GameObject>();

        for (int i = 0; i < targets.Length; i++)
        {
            var (parent, origName, pos, rot, worldScale) = snapshots[i];

            // Destroy original
            Undo.DestroyObjectImmediate(targets[i]);

            // Instantiate prefab
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            Undo.RegisterCreatedObjectUndo(instance, "");

            instance.name = origName;
            instance.transform.position = pos;
            instance.transform.rotation = rot;

            // Apply world scale: convert worldScale to local scale relative to parent
            if (parent != null)
            {
                var parentLossyScale = parent.lossyScale;
                instance.transform.localScale = new Vector3(
                    parentLossyScale.x != 0 ? worldScale.x / parentLossyScale.x : worldScale.x,
                    parentLossyScale.y != 0 ? worldScale.y / parentLossyScale.y : worldScale.y,
                    parentLossyScale.z != 0 ? worldScale.z / parentLossyScale.z : worldScale.z
                );
            }
            else
            {
                instance.transform.localScale = worldScale;
            }

            newObjects.Add(instance);
        }

        Undo.CollapseUndoOperations(undoGroup);

        Selection.objects = newObjects.ToArray();
        EditorGUIUtility.PingObject(newObjects[0]);
    }
}
