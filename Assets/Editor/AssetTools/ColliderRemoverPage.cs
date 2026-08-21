using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace AssetTools.Editor
{
    /// <summary>선택한 폴더/프리팹/오브젝트의 콜라이더를 하위까지 전부 삭제하는 탭.</summary>
    public class ColliderRemoverPage : AssetToolPage
    {
        /// <summary>목록에 한 번에 그리는 최대 줄 수. 폴더를 고르면 수백 개가 될 수 있어 제한한다.</summary>
        const int MaxListedTargets = 200;

        public override string Title => "콜라이더 삭제";
        public override int Order => 10;

        ColliderRemover.Options _options = ColliderRemover.Options.Default;
        bool _includeFolders = true;

        SelectionResult _selection = new SelectionResult();
        List<ToolTarget> _targets = new List<ToolTarget>();
        int[] _targetCounts = new int[0];
        int _colliderCount;
        string _result;
        MessageType _resultType = MessageType.Info;
        Vector2 _targetScroll;

        public override void OnEnable() => RefreshTargets();

        public override void OnSelectionChanged() => RefreshTargets();

        void RefreshTargets()
        {
            _selection = AssetToolsSelection.Resolve(_includeFolders);
            _targets = _selection.Targets;
            _targetCounts = ColliderRemover.CountEach(_targets, _options);

            _colliderCount = 0;
            foreach (int count in _targetCounts) _colliderCount += count;
        }

        public override void OnGUI()
        {
            EditorGUILayout.HelpBox(
                "선택한 폴더·프리팹 에셋·씬 오브젝트의 하위 계층 전체에서 콜라이더를 삭제합니다.\n" +
                "· 폴더: 하위 폴더까지 훑어서 안에 있는 프리팹을 모두 처리합니다.\n" +
                "· 씬 오브젝트: Ctrl+Z 로 되돌릴 수 있습니다.\n" +
                "· 프리팹 에셋: 파일에 바로 저장되며 되돌릴 수 없습니다.",
                MessageType.Info);

            EditorGUILayout.Space(4f);

            using (var check = new EditorGUI.ChangeCheckScope())
            {
                _options.IncludeInactive = EditorGUILayout.ToggleLeft("비활성 오브젝트 포함", _options.IncludeInactive);
                _options.Include2D = EditorGUILayout.ToggleLeft("2D 콜라이더도 삭제", _options.Include2D);
                _includeFolders = EditorGUILayout.ToggleLeft("폴더 선택 시 하위 프리팹 전체 처리", _includeFolders);

                if (check.changed) RefreshTargets();
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

            using (new EditorGUI.DisabledScope(_colliderCount == 0))
            {
                var label = _colliderCount > 0
                    ? $"콜라이더 {_colliderCount}개 삭제"
                    : "삭제할 콜라이더 없음";

                if (GUILayout.Button(label, GUILayout.Height(30f))) Execute();
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

            var header = $"대상 {_targets.Count}개 · 콜라이더 {_colliderCount}개";
            if (_selection.PrefabsFromFolders > 0)
            {
                header += $"  (폴더 {_selection.SelectedFolders}개에서 프리팹 {_selection.PrefabsFromFolders}개)";
            }

            EditorGUILayout.LabelField(header, EditorStyles.miniBoldLabel);

            using (var scroll = new EditorGUILayout.ScrollViewScope(_targetScroll, GUILayout.MaxHeight(140f)))
            {
                _targetScroll = scroll.scrollPosition;

                int listed = Mathf.Min(_targets.Count, MaxListedTargets);
                for (int i = 0; i < listed; i++)
                {
                    var target = _targets[i];
                    var icon = target.IsPrefabAsset ? "Prefab Icon" : "GameObject Icon";
                    var content = new GUIContent(
                        $"{target.DisplayName}  ({_targetCounts[i]})", EditorGUIUtility.IconContent(icon).image);

                    EditorGUILayout.LabelField(content, EditorStyles.miniLabel);
                }

                if (_targets.Count > listed)
                {
                    EditorGUILayout.LabelField($"… 외 {_targets.Count - listed}개", EditorStyles.miniLabel);
                }
            }
        }

        void Execute()
        {
            int prefabCount = 0;
            foreach (var target in _targets)
            {
                if (target.IsPrefabAsset) prefabCount++;
            }

            var message = $"대상 {_targets.Count}개에서 콜라이더 {_colliderCount}개를 삭제합니다.";
            if (_selection.PrefabsFromFolders > 0)
            {
                message += $"\n\n선택한 폴더 {_selection.SelectedFolders}개에서 찾은 프리팹 {_selection.PrefabsFromFolders}개가 포함되어 있습니다.";
            }

            if (prefabCount > 0) message += $"\n\n이 중 프리팹 에셋 {prefabCount}개는 파일에 직접 저장되며 되돌릴 수 없습니다.";

            if (!EditorUtility.DisplayDialog("콜라이더 삭제", message, "삭제", "취소")) return;

            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName("Remove Colliders");
            int undoGroup = Undo.GetCurrentGroup();

            var report = ColliderRemover.RemoveFrom(_targets, _options);

            Undo.CollapseUndoOperations(undoGroup);

            _result = $"콜라이더 {report.Removed}개 삭제 (대상 {report.Targets}개, 저장된 프리팹 {report.ModifiedPrefabs}개)";
            if (report.SkippedPrefabs > 0) _result += $"\n수정할 수 없는 프리팹 {report.SkippedPrefabs}개는 건너뛰었습니다.";
            _resultType = MessageType.Info;

            if (report.Warnings.Count > 0)
            {
                _result += "\n\n" + string.Join("\n", report.Warnings);
                _resultType = MessageType.Warning;
                foreach (var warning in report.Warnings) Debug.LogWarning($"[콜라이더 삭제] {warning}");
            }

            RefreshTargets();
        }
    }
}
