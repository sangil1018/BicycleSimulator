using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public class AssetReplacer : EditorWindow
{
    enum BaseMode { Current = 0, Reference = 1 }

    static readonly string[] BaseModeNames = { "현재값", "기준값 직접입력" };
    static readonly string[] UnitNames = { "퍼센트 (%)", "절대값" };

    [System.Serializable]
    class RandomGroup
    {
        public bool enabled;
        public bool absolute;                       // true면 min/max를 퍼센트가 아닌 실제 단위로 사용
        public BaseMode baseMode = BaseMode.Reference;
        public Vector3 reference = Vector3.one;
        public float min = -10f;
        public float max = 10f;
        public bool x = true, y = true, z = true;
    }

    GameObject prefab;
    Vector2 scrollPos;
    Vector2 windowScroll;

    // 랜덤 변형
    [SerializeField] bool randomFoldout = true;
    [SerializeField] RandomGroup moveGroup = new RandomGroup { reference = Vector3.one };
    [SerializeField] RandomGroup rotGroup = new RandomGroup { absolute = true, min = 0f, max = 360f, x = false, z = false };
    [SerializeField] RandomGroup scaleGroup = new RandomGroup { baseMode = BaseMode.Current };
    [SerializeField] bool linkXZ = true;
    [SerializeField] bool useSeed;
    [SerializeField] int seed = 0;

    [MenuItem("Tools/에셋 리플레이서")]
    static void Open() => GetWindow<AssetReplacer>("에셋 리플레이서");

    void OnGUI()
    {
        windowScroll = EditorGUILayout.BeginScrollView(windowScroll);
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

        DrawRandomSection(selection);

        EditorGUILayout.EndScrollView();
    }

    void OnSelectionChange() => Repaint();

    // ── 랜덤 변형 ──────────────────────────────────────────────

    void DrawRandomSection(GameObject[] selection)
    {
        EditorGUILayout.Space(14);
        randomFoldout = EditorGUILayout.BeginFoldoutHeaderGroup(randomFoldout, "랜덤 변형");

        if (randomFoldout)
        {
            EditorGUILayout.HelpBox(
                "선택한 오브젝트마다 최소~최대 사이의 값을 축별로 따로 뽑아 적용합니다.\n" +
                "[퍼센트] 이동 · 회전 = 현재값 + 기준값 × % / 크기 = 기준값 × (1 + %)\n" +
                "[절대값] 이동 = 현재값 + m / 회전 = 현재값 + ° / 크기 = 뽑은 값 그대로",
                MessageType.None);

            DrawRandomGroup("이동", moveGroup, false, "m");
            DrawRandomGroup("회전", rotGroup, false, "°");
            DrawRandomGroup("크기", scaleGroup, true, "배율");

            EditorGUILayout.Space(6);
            EditorGUILayout.BeginHorizontal();
            useSeed = EditorGUILayout.ToggleLeft("시드 고정", useSeed, GUILayout.Width(90));
            using (new EditorGUI.DisabledScope(!useSeed))
                seed = EditorGUILayout.IntField(seed);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(4);
            bool anyGroup = moveGroup.enabled || rotGroup.enabled || scaleGroup.enabled;
            using (new EditorGUI.DisabledScope(selection.Length == 0 || !anyGroup))
            {
                if (GUILayout.Button("랜덤 변형 적용", GUILayout.Height(32)))
                    ApplyRandom(selection);
            }

            if (selection.Length == 0)
                EditorGUILayout.HelpBox("씬에서 오브젝트를 선택하세요.", MessageType.Info);
            else if (!anyGroup)
                EditorGUILayout.HelpBox("이동 · 회전 · 크기 중 하나 이상을 켜세요.", MessageType.Info);
        }

        EditorGUILayout.EndFoldoutHeaderGroup();
        EditorGUILayout.Space(8);
    }

    void DrawRandomGroup(string title, RandomGroup g, bool isScale, string absUnit)
    {
        EditorGUILayout.Space(6);
        g.enabled = EditorGUILayout.ToggleLeft(title, g.enabled, EditorStyles.boldLabel);
        if (!g.enabled) return;

        EditorGUI.indentLevel++;

        g.absolute = EditorGUILayout.Popup("단위", g.absolute ? 1 : 0, UnitNames) == 1;

        if (!g.absolute)
        {
            g.baseMode = (BaseMode)EditorGUILayout.Popup("기준", (int)g.baseMode, BaseModeNames);
            if (g.baseMode == BaseMode.Reference)
                g.reference = EditorGUILayout.Vector3Field("기준값", g.reference);
        }

        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.PrefixLabel(g.absolute ? $"범위 ({absUnit})" : "범위 (%)");
        g.min = EditorGUILayout.FloatField(g.min);
        GUILayout.Label("~", GUILayout.Width(14));
        g.max = EditorGUILayout.FloatField(g.max);
        EditorGUILayout.EndHorizontal();
        if (g.max < g.min) g.max = g.min;

        bool locked = isScale && linkXZ;

        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.PrefixLabel("적용 축");
        g.x = GUILayout.Toggle(g.x, "X", EditorStyles.miniButtonLeft);
        g.y = GUILayout.Toggle(g.y, "Y", EditorStyles.miniButtonMid);
        using (new EditorGUI.DisabledScope(locked))
        {
            bool zShown = GUILayout.Toggle(locked ? g.x : g.z, "Z", EditorStyles.miniButtonRight);
            if (!locked) g.z = zShown;
        }
        EditorGUILayout.EndHorizontal();

        if (isScale)
        {
            linkXZ = EditorGUILayout.ToggleLeft("X · Z 연동 (같은 비율로 함께 변형)", linkXZ);
            if (linkXZ) g.z = g.x;
        }

        EditorGUI.indentLevel--;
    }

    void ApplyRandom(GameObject[] targets)
    {
        if (useSeed) Random.InitState(seed);

        var transforms = new Transform[targets.Length];
        for (int i = 0; i < targets.Length; i++)
            transforms[i] = targets[i].transform;

        Undo.RecordObjects(transforms, "랜덤 변형 적용");

        foreach (var t in transforms)
        {
            if (moveGroup.enabled)
            {
                var r = RandomValue(moveGroup, false);
                if (moveGroup.absolute)
                {
                    t.localPosition += r;
                }
                else
                {
                    var b = moveGroup.baseMode == BaseMode.Current ? t.localPosition : moveGroup.reference;
                    t.localPosition += new Vector3(b.x * r.x, b.y * r.y, b.z * r.z);
                }
            }

            if (rotGroup.enabled)
            {
                var r = RandomValue(rotGroup, false);
                if (rotGroup.absolute)
                {
                    t.localEulerAngles += r;
                }
                else
                {
                    var b = rotGroup.baseMode == BaseMode.Current ? t.localEulerAngles : rotGroup.reference;
                    t.localEulerAngles += new Vector3(b.x * r.x, b.y * r.y, b.z * r.z);
                }
            }

            if (scaleGroup.enabled)
            {
                var r = RandomValue(scaleGroup, linkXZ);
                var s = t.localScale;
                if (scaleGroup.absolute)
                {
                    if (scaleGroup.x) s.x = r.x;
                    if (scaleGroup.y) s.y = r.y;
                    if (scaleGroup.z) s.z = r.z;
                }
                else
                {
                    var b = scaleGroup.baseMode == BaseMode.Current ? t.localScale : scaleGroup.reference;
                    if (scaleGroup.x) s.x = b.x * (1f + r.x);
                    if (scaleGroup.y) s.y = b.y * (1f + r.y);
                    if (scaleGroup.z) s.z = b.z * (1f + r.z);
                }
                t.localScale = s;
            }

            EditorUtility.SetDirty(t);
        }
    }

    // 축별 랜덤값. 퍼센트 모드면 비율(%/100), 절대값 모드면 입력값 그대로.
    // 꺼진 축은 0, X·Z 연동 시 Z는 X와 동일.
    Vector3 RandomValue(RandomGroup g, bool link)
    {
        float scale = g.absolute ? 1f : 0.01f;
        float rx = g.x ? Random.Range(g.min, g.max) * scale : 0f;
        float ry = g.y ? Random.Range(g.min, g.max) * scale : 0f;
        float rz = link ? rx : (g.z ? Random.Range(g.min, g.max) * scale : 0f);
        return new Vector3(rx, ry, rz);
    }

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
