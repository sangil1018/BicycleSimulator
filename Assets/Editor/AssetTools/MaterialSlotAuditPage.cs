using UnityEditor;
using UnityEngine;

namespace AssetTools.Editor
{
    /// <summary>
    /// GPU Resident Drawer 가 찍는 "Material count ... higher than sub mesh count" 경고의
    /// 원인 렌더러를 찾아 초과 슬롯을 잘라내는 탭.
    /// </summary>
    public class MaterialSlotAuditPage : AssetToolPage
    {
        public override string Title => "머티리얼 슬롯";
        public override int Order => 60;

        const string PrefPrefabs = "AssetTools.MaterialSlotAudit.Prefabs";
        const string PrefScenes = "AssetTools.MaterialSlotAudit.Scenes";
        const string PrefSkinned = "AssetTools.MaterialSlotAudit.Skinned";

        MaterialSlotAudit.Options _options = MaterialSlotAudit.Options.Default;
        MaterialSlotAudit.ScanResult _scan;
        MaterialSlotAudit.FixReport _fix;

        Vector2 _listScroll;

        public override void OnEnable()
        {
            _options.ScanProjectPrefabs = EditorPrefs.GetBool(PrefPrefabs, true);
            _options.ScanOpenScenes = EditorPrefs.GetBool(PrefScenes, true);
            _options.IncludeSkinnedMeshes = EditorPrefs.GetBool(PrefSkinned, true);
        }

        public override void OnGUI()
        {
            EditorGUILayout.HelpBox(
                "메시의 서브메시 수보다 머티리얼 슬롯이 많은 렌더러를 찾습니다.\n" +
                "GPU Resident Drawer 가 켜져 있으면 이런 렌더러마다 " +
                "\"Material count in the shared material list is higher than sub mesh count\" 경고가 찍히고, " +
                "스택 수집 비용 때문에 에디터가 버벅입니다.\n" +
                "남는 슬롯은 원래부터 렌더링에 쓰이지 않는 죽은 데이터라 잘라내도 화면 결과는 바뀌지 않습니다.",
                MessageType.Info);

            EditorGUILayout.Space(6f);
            DrawOptions();

            EditorGUILayout.Space(6f);
            if (GUILayout.Button("검사", GUILayout.Height(30f)))
            {
                _fix = null;
                _scan = MaterialSlotAudit.Scan(_options);
            }

            if (_scan != null)
            {
                EditorGUILayout.Space(8f);
                DrawScanResult();
            }

            if (_fix != null)
            {
                EditorGUILayout.Space(8f);
                DrawFixReport();
            }
        }

        void DrawOptions()
        {
            using (var check = new EditorGUI.ChangeCheckScope())
            {
                _options.ScanProjectPrefabs = EditorGUILayout.ToggleLeft(
                    "프로젝트 프리팹 전체", _options.ScanProjectPrefabs);
                _options.ScanOpenScenes = EditorGUILayout.ToggleLeft(
                    "열려 있는 씬", _options.ScanOpenScenes);
                _options.IncludeSkinnedMeshes = EditorGUILayout.ToggleLeft(
                    "SkinnedMeshRenderer 포함", _options.IncludeSkinnedMeshes);

                if (check.changed)
                {
                    EditorPrefs.SetBool(PrefPrefabs, _options.ScanProjectPrefabs);
                    EditorPrefs.SetBool(PrefScenes, _options.ScanOpenScenes);
                    EditorPrefs.SetBool(PrefSkinned, _options.IncludeSkinnedMeshes);
                }
            }
        }

        void DrawScanResult()
        {
            EditorGUILayout.LabelField(
                $"프리팹 {_scan.ScannedPrefabs}개 · 씬 {_scan.ScannedScenes}개 · 렌더러 {_scan.ScannedRenderers}개 검사",
                EditorStyles.miniLabel);

            if (_scan.MissingMesh > 0)
            {
                EditorGUILayout.LabelField(
                    $"메시가 비어 비교하지 못한 렌더러 {_scan.MissingMesh}개 (별개의 문제일 수 있습니다)",
                    EditorStyles.miniLabel);
            }

            if (!_scan.HasAny)
            {
                EditorGUILayout.HelpBox("초과된 슬롯이 없습니다.", MessageType.Info);
                return;
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField($"초과 렌더러 {_scan.Entries.Count}개", EditorStyles.boldLabel);

                if (GUILayout.Button("콘솔로 보내기", EditorStyles.miniButton, GUILayout.Width(100f)))
                {
                    Debug.Log(MaterialSlotAudit.BuildReportText(_scan));
                }

                if (GUILayout.Button("복사", EditorStyles.miniButton, GUILayout.Width(50f)))
                {
                    EditorGUIUtility.systemCopyBuffer = MaterialSlotAudit.BuildReportText(_scan);
                }
            }

            DrawEntryList();

            int readOnly = CountReadOnly();
            if (readOnly > 0)
            {
                EditorGUILayout.HelpBox(
                    $"{readOnly}개는 모델(FBX 등) 안에 있어 수정할 수 없습니다. " +
                    "해당 모델의 Import Settings > Materials 에서 고치거나, 프리팹으로 풀어 쓰세요.",
                    MessageType.Warning);
            }

            EditorGUILayout.Space(4f);

            using (new EditorGUI.DisabledScope(_scan.Entries.Count - readOnly <= 0))
            {
                if (GUILayout.Button("초과 슬롯 일괄 정리", GUILayout.Height(30f))) Trim();
            }

            EditorGUILayout.LabelField(
                "프리팹 에셋 수정은 Undo 되지 않습니다. 씬 오브젝트만 Ctrl+Z 로 되돌릴 수 있습니다.",
                EditorStyles.wordWrappedMiniLabel);
        }

        void DrawEntryList()
        {
            using (var scroll = new EditorGUILayout.ScrollViewScope(_listScroll, GUILayout.MinHeight(160f), GUILayout.MaxHeight(320f)))
            {
                _listScroll = scroll.scrollPosition;

                foreach (var entry in _scan.Entries)
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        EditorGUILayout.LabelField(
                            $"{entry.Slots} → {entry.SubMeshes}", GUILayout.Width(60f));

                        string label = $"{entry.Location} : {entry.HierarchyPath}";
                        if (entry.EmptySlots > 0) label += $"  (빈 슬롯 {entry.EmptySlots})";
                        if (entry.ReadOnly) label += "  [읽기 전용]";

                        EditorGUILayout.LabelField(new GUIContent(label, $"mesh: {entry.MeshName} / {entry.RendererType}"));

                        using (new EditorGUI.DisabledScope(entry.Reference == null))
                        {
                            if (GUILayout.Button("선택", EditorStyles.miniButton, GUILayout.Width(40f)))
                            {
                                Selection.activeObject = entry.Reference.gameObject;
                                EditorGUIUtility.PingObject(entry.Reference.gameObject);
                            }
                        }
                    }
                }
            }
        }

        void DrawFixReport()
        {
            EditorGUILayout.LabelField("정리 결과", EditorStyles.boldLabel);
            EditorGUILayout.LabelField(
                $"렌더러 {_fix.FixedRenderers}개에서 슬롯 {_fix.RemovedSlots}개 제거 " +
                $"(프리팹 {_fix.ModifiedPrefabs}개 · 씬 {_fix.ModifiedScenes}개)");

            if (_fix.SkippedReadOnly > 0)
            {
                EditorGUILayout.LabelField($"수정할 수 없어 건너뜀: {_fix.SkippedReadOnly}개", EditorStyles.miniLabel);
            }

            foreach (var warning in _fix.Warnings)
            {
                EditorGUILayout.HelpBox(warning, MessageType.Warning);
            }
        }

        void Trim()
        {
            int fixable = _scan.Entries.Count - CountReadOnly();

            bool proceed = EditorUtility.DisplayDialog(
                "초과 슬롯 정리",
                $"렌더러 {fixable}개의 남는 머티리얼 슬롯을 잘라냅니다.\n\n" +
                "프리팹 에셋 수정은 Undo 되지 않습니다. 버전 관리에 커밋되지 않은 변경이 있다면 먼저 커밋하세요.",
                "정리", "취소");

            if (!proceed) return;

            _fix = MaterialSlotAudit.Trim(_scan);

            // 수정 후 프리팹 참조가 무효해지므로 결과를 다시 만든다.
            _scan = MaterialSlotAudit.Scan(_options);
        }

        int CountReadOnly()
        {
            int count = 0;
            foreach (var entry in _scan.Entries)
            {
                if (entry.ReadOnly) count++;
            }

            return count;
        }
    }
}
