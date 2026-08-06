using UnityEditor;
using UnityEngine;

/// <summary>
/// Hierarchy에서 선택한 오브젝트의 하위 Renderer를 모두 찾아
/// Contribute GI(Static Flag) / Receive Global Illumination 조합을 버튼으로 일괄 설정하는 에디터 창.
///
/// 1. 대형 정적 구조물 (지형, 건물 등)    : Contribute GI 체크 + Receive GI Lightmaps
/// 2. 중소형 프롭 및 소품 (나무, 작은 프롭): Contribute GI 체크 + Receive GI Light Probes
/// 3. 동적 오브젝트 (플레이어, 움직이는 것): Contribute GI 해제 + Receive GI Light Probes
/// </summary>
public class LightingPresetTools : EditorWindow
{
    private const string MenuPath = "Tools/Lighting/Lighting Preset";

    /// <summary>버튼 하나에 대응하는 프리셋 정의.</summary>
    private struct Preset
    {
        public string Title;
        public string Description;
        public bool ContributeGI;
        public ReceiveGI ReceiveGI;
    }

    private static readonly Preset[] Presets =
    {
        new Preset
        {
            Title = "1. 대형 정적 구조물",
            Description = "지형, 건물 등\nContribute GI: 체크 / Receive GI: Lightmaps",
            ContributeGI = true,
            ReceiveGI = ReceiveGI.Lightmaps,
        },
        new Preset
        {
            Title = "2. 중소형 프롭 및 소품",
            Description = "나무, 작은 프롭 등\nContribute GI: 체크 / Receive GI: Light Probes",
            ContributeGI = true,
            ReceiveGI = ReceiveGI.LightProbes,
        },
        new Preset
        {
            Title = "3. 동적 오브젝트",
            Description = "플레이어, 움직이는 것들\nContribute GI: 해제 / Receive GI: Light Probes",
            ContributeGI = false,
            ReceiveGI = ReceiveGI.LightProbes,
        },
    };

    private int selectedRootCount;
    private int selectedRendererCount;
    private string lastResult = string.Empty;
    private Vector2 scroll;

    [MenuItem(MenuPath, false, 100)]
    private static void Open()
    {
        LightingPresetTools window = GetWindow<LightingPresetTools>("Lighting Preset");
        window.minSize = new Vector2(340f, 380f);
        window.RefreshSelectionInfo();
        window.Show();
    }

    private void OnEnable()
    {
        RefreshSelectionInfo();
    }

    private void OnSelectionChange()
    {
        RefreshSelectionInfo();
        Repaint();
    }

    private void RefreshSelectionInfo()
    {
        GameObject[] roots = Selection.gameObjects;
        selectedRootCount = roots != null ? roots.Length : 0;
        selectedRendererCount = 0;

        if (roots == null)
            return;

        foreach (GameObject root in roots)
        {
            // 비활성 오브젝트도 포함해서 하위 전체를 센다.
            selectedRendererCount += root.GetComponentsInChildren<Renderer>(true).Length;
        }
    }

    private void OnGUI()
    {
        scroll = EditorGUILayout.BeginScrollView(scroll);

        EditorGUILayout.HelpBox(
            "Hierarchy에서 오브젝트를 선택한 뒤 아래 버튼을 누르면\n" +
            "하위(비활성 포함) Renderer 전체에 Lighting 설정이 적용됩니다.",
            MessageType.Info);

        EditorGUILayout.Space();

        bool hasSelection = selectedRootCount > 0;
        EditorGUILayout.LabelField(
            "현재 선택",
            hasSelection
                ? $"오브젝트 {selectedRootCount}개 / Renderer {selectedRendererCount}개"
                : "없음");

        EditorGUILayout.Space();

        using (new EditorGUI.DisabledScope(!hasSelection))
        {
            foreach (Preset preset in Presets)
            {
                DrawPresetBlock(preset);
            }
        }

        if (!hasSelection)
        {
            EditorGUILayout.HelpBox("Hierarchy에서 오브젝트를 먼저 선택하세요.", MessageType.Warning);
        }

        if (!string.IsNullOrEmpty(lastResult))
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("최근 결과", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(lastResult, MessageType.None);
        }

        EditorGUILayout.EndScrollView();
    }

    private void DrawPresetBlock(Preset preset)
    {
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);

        EditorGUILayout.LabelField(preset.Title, EditorStyles.boldLabel);
        EditorGUILayout.LabelField(preset.Description, EditorStyles.wordWrappedMiniLabel);

        if (GUILayout.Button("적용", GUILayout.Height(28f)))
        {
            lastResult = Apply(preset);
            RefreshSelectionInfo();
        }

        EditorGUILayout.EndVertical();
        EditorGUILayout.Space(4f);
    }

    private static string Apply(Preset preset)
    {
        GameObject[] roots = Selection.gameObjects;
        if (roots == null || roots.Length == 0)
        {
            return "선택된 오브젝트가 없습니다.";
        }

        string undoName = $"Set Lighting Preset ({preset.Title})";
        Undo.IncrementCurrentGroup();
        int undoGroup = Undo.GetCurrentGroup();

        int changedFlags = 0;
        int changedGI = 0;
        int total = 0;

        foreach (GameObject root in roots)
        {
            // 비활성 오브젝트도 포함해서 하위 전체를 순회한다.
            Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
            foreach (Renderer renderer in renderers)
            {
                total++;
                GameObject go = renderer.gameObject;

                StaticEditorFlags flags = GameObjectUtility.GetStaticEditorFlags(go);
                StaticEditorFlags newFlags = preset.ContributeGI
                    ? flags | StaticEditorFlags.ContributeGI
                    : flags & ~StaticEditorFlags.ContributeGI;

                if (newFlags != flags)
                {
                    Undo.RecordObject(go, undoName);
                    GameObjectUtility.SetStaticEditorFlags(go, newFlags);
                    EditorUtility.SetDirty(go);
                    changedFlags++;
                }

                // receiveGI는 MeshRenderer에만 있다. SkinnedMeshRenderer 등은 Contribute GI 설정만 적용된다.
                if (renderer is MeshRenderer meshRenderer && meshRenderer.receiveGI != preset.ReceiveGI)
                {
                    Undo.RecordObject(meshRenderer, undoName);
                    meshRenderer.receiveGI = preset.ReceiveGI;
                    EditorUtility.SetDirty(meshRenderer);
                    changedGI++;
                }
            }
        }

        Undo.SetCurrentGroupName(undoName);
        Undo.CollapseUndoOperations(undoGroup);

        string result =
            $"[{preset.Title}] Renderer {total}개 중\n" +
            $"Contribute GI {changedFlags}개, Receive GI({preset.ReceiveGI}) {changedGI}개 변경";

        UnityEngine.Debug.Log($"[Lighting Preset] {result.Replace("\n", " ")}");
        return result;
    }
}
