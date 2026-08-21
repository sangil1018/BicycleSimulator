using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace AssetTools.Editor
{
    /// <summary>선택한 오브젝트/프리팹의 하위 Renderer 에 라이팅 설정 프리셋을 일괄 적용하는 탭.</summary>
    public class LightingPresetPage : AssetToolPage
    {
        /// <summary>목록에 한 번에 그리는 최대 줄 수. 폴더를 고르면 수백 개가 될 수 있어 제한한다.</summary>
        const int MaxListedTargets = 200;

        const string IncludeFoldersPref = "AssetTools.LightingPreset.IncludeFolders";

        public override string Title => "라이팅 프리셋";
        public override int Order => 20;

        bool _includeFolders = true;

        SelectionResult _selection = new SelectionResult();
        List<ToolTarget> _targets = new List<ToolTarget>();
        int[] _rendererCounts = new int[0];
        int _rendererTotal;

        string _result;
        MessageType _resultType = MessageType.Info;
        Vector2 _targetScroll;

        public override void OnEnable()
        {
            _includeFolders = EditorPrefs.GetBool(IncludeFoldersPref, true);
            RefreshTargets();
        }

        public override void OnSelectionChanged() => RefreshTargets();

        void RefreshTargets()
        {
            _selection = AssetToolsSelection.Resolve(_includeFolders);
            _targets = _selection.Targets;
            _rendererCounts = LightingPresetApplier.CountEach(_targets);

            _rendererTotal = 0;
            foreach (int count in _rendererCounts) _rendererTotal += count;
        }

        public override void OnGUI()
        {
            EditorGUILayout.HelpBox(
                "선택한 폴더·프리팹 에셋·씬 오브젝트의 하위 Renderer 전체에 라이팅 설정을 적용합니다.\n" +
                "· 비활성 오브젝트도 함께 처리합니다.\n" +
                "· 씬 오브젝트: Ctrl+Z 로 되돌릴 수 있습니다.\n" +
                "· 프리팹 에셋: 파일에 바로 저장되며 되돌릴 수 없습니다.",
                MessageType.Info);

            EditorGUILayout.Space(4f);

            using (var check = new EditorGUI.ChangeCheckScope())
            {
                _includeFolders = EditorGUILayout.ToggleLeft("폴더 선택 시 하위 프리팹 전체 처리", _includeFolders);

                if (check.changed)
                {
                    EditorPrefs.SetBool(IncludeFoldersPref, _includeFolders);
                    RefreshTargets();
                }
            }

            if (_selection.HasIgnoredFolders)
            {
                EditorGUILayout.HelpBox(
                    $"폴더 {_selection.SelectedFolders}개를 선택했지만 위 옵션이 꺼져 있어 무시했습니다.",
                    MessageType.Warning);
            }

            EditorGUILayout.Space(6f);
            DrawTargetList();
            EditorGUILayout.Space(6f);

            EditorGUILayout.HelpBox(
                "베이크가 끝난 뒤에 이 설정을 바꾸면 구워둔 라이트맵과 어긋나 검은 면이 생깁니다. " +
                "설정을 모두 확정한 다음 Generate Lighting 을 실행하세요.",
                MessageType.Warning);

            EditorGUILayout.Space(4f);

            using (new EditorGUI.DisabledScope(_targets.Count == 0))
            {
                foreach (var preset in LightingPresetApplier.Presets) DrawPresetBlock(preset);
            }

            if (GUILayout.Button("선택 항목 다시 검사", EditorStyles.miniButton)) RefreshTargets();

            if (!string.IsNullOrEmpty(_result))
            {
                EditorGUILayout.Space(6f);
                EditorGUILayout.HelpBox(_result, _resultType);
            }
        }

        void DrawTargetList()
        {
            if (_targets.Count == 0)
            {
                EditorGUILayout.HelpBox(
                    "Hierarchy 나 Project 창에서 오브젝트·프리팹·폴더를 선택하세요.", MessageType.None);
                return;
            }

            var header = $"대상 {_targets.Count}개 · Renderer {_rendererTotal}개";
            if (_selection.PrefabsFromFolders > 0)
            {
                header += $"  (폴더 {_selection.SelectedFolders}개에서 프리팹 {_selection.PrefabsFromFolders}개)";
            }

            EditorGUILayout.LabelField(header, EditorStyles.miniBoldLabel);

            using (var scroll = new EditorGUILayout.ScrollViewScope(_targetScroll, GUILayout.MaxHeight(120f)))
            {
                _targetScroll = scroll.scrollPosition;

                int listed = Mathf.Min(_targets.Count, MaxListedTargets);
                for (int i = 0; i < listed; i++)
                {
                    var target = _targets[i];
                    var icon = target.IsPrefabAsset ? "Prefab Icon" : "GameObject Icon";
                    var content = new GUIContent(
                        $"{target.DisplayName}  ({_rendererCounts[i]})", EditorGUIUtility.IconContent(icon).image);

                    EditorGUILayout.LabelField(content, EditorStyles.miniLabel);
                }

                if (_targets.Count > listed)
                {
                    EditorGUILayout.LabelField($"… 외 {_targets.Count - listed}개", EditorStyles.miniLabel);
                }
            }
        }

        void DrawPresetBlock(LightingPresetApplier.Preset preset)
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField(preset.Title, EditorStyles.boldLabel);
                EditorGUILayout.LabelField(preset.Description, EditorStyles.wordWrappedMiniLabel);

                if (GUILayout.Button("적용", GUILayout.Height(26f))) Execute(preset);
            }

            EditorGUILayout.Space(2f);
        }

        void Execute(LightingPresetApplier.Preset preset)
        {
            int prefabCount = 0;
            foreach (var target in _targets)
            {
                if (target.IsPrefabAsset) prefabCount++;
            }

            var message = $"대상 {_targets.Count}개 · Renderer {_rendererTotal}개에 [{preset.Title}] 설정을 적용합니다.";
            if (_selection.PrefabsFromFolders > 0)
            {
                message += $"\n\n선택한 폴더 {_selection.SelectedFolders}개에서 찾은 프리팹 {_selection.PrefabsFromFolders}개가 포함되어 있습니다.";
            }

            if (prefabCount > 0) message += $"\n\n이 중 프리팹 에셋 {prefabCount}개는 파일에 직접 저장되며 되돌릴 수 없습니다.";

            if (!EditorUtility.DisplayDialog("라이팅 프리셋 적용", message, "적용", "취소")) return;

            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName($"Set Lighting Preset ({preset.Title})");
            int undoGroup = Undo.GetCurrentGroup();

            var report = LightingPresetApplier.Apply(_targets, preset);

            Undo.CollapseUndoOperations(undoGroup);

            _result =
                $"[{preset.Title}] Renderer {report.Renderers}개 중\n" +
                $"Contribute GI {report.ChangedFlags}개, Receive GI({preset.ReceiveGI}) {report.ChangedGI}개 변경";

            if (preset.LightProbes.HasValue) _result += $", Light Probes({preset.LightProbes.Value}) {report.ChangedProbes}개 변경";
            if (preset.CastShadows.HasValue) _result += $", Cast Shadows {report.ChangedShadows}개 변경";
            if (report.ModifiedPrefabs > 0) _result += $"\n프리팹 {report.ModifiedPrefabs}개 저장됨";
            if (report.SkippedPrefabs > 0) _result += $"\n수정할 수 없는 프리팹 {report.SkippedPrefabs}개는 건너뛰었습니다.";

            _resultType = MessageType.Info;

            if (report.Warnings.Count > 0)
            {
                _result += "\n\n" + string.Join("\n", report.Warnings);
                _resultType = MessageType.Warning;
                foreach (var warning in report.Warnings) Debug.LogWarning($"[라이팅 프리셋] {warning}");
            }

            Debug.Log($"[라이팅 프리셋] {_result.Replace("\n", " ")}");

            RefreshTargets();
        }
    }
}
