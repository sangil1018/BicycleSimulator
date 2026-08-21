using UnityEditor;
using UnityEngine;

namespace AssetTools.Editor
{
    /// <summary>선택한 씬 오브젝트를 프리팹으로 교체하고, 트랜스폼에 랜덤 변형을 주는 탭.</summary>
    public class AssetReplacerPage : AssetToolPage
    {
        /// <summary>선택 목록에 한 번에 그리는 최대 줄 수.</summary>
        const int MaxListedTargets = 200;

        const string SettingsPref = "AssetTools.AssetReplacer.Settings";

        static readonly string[] BaseModeNames = { "현재값", "기준값 직접입력" };
        static readonly string[] UnitNames = { "퍼센트 (%)", "절대값" };

        public override string Title => "에셋 리플레이서";
        public override int Order => 30;

        AssetReplacerOps.Settings _settings = new AssetReplacerOps.Settings();
        GameObject _prefab;
        GameObject[] _selection = new GameObject[0];
        Vector2 _selectionScroll;
        string _result;

        public override void OnEnable()
        {
            LoadSettings();
            RefreshSelection();
        }

        public override void OnSelectionChanged() => RefreshSelection();

        void RefreshSelection() => _selection = Selection.gameObjects;

        // ── 설정 저장 / 복원 ──────────────────────────────────────

        void LoadSettings()
        {
            var json = EditorPrefs.GetString(SettingsPref, "");
            if (!string.IsNullOrEmpty(json)) EditorJsonUtility.FromJsonOverwrite(json, _settings);

            // 오브젝트 참조는 EditorPrefs 에 담을 수 없어 GUID 로 저장해 두고 여기서 되살린다.
            if (string.IsNullOrEmpty(_settings.prefabGuid)) return;

            var path = AssetDatabase.GUIDToAssetPath(_settings.prefabGuid);
            if (!string.IsNullOrEmpty(path)) _prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        }

        void SaveSettings()
        {
            _settings.prefabGuid = _prefab == null
                ? ""
                : AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(_prefab));

            EditorPrefs.SetString(SettingsPref, EditorJsonUtility.ToJson(_settings));
        }

        // ── UI ────────────────────────────────────────────────────

        public override void OnGUI()
        {
            EditorGUILayout.HelpBox(
                "Hierarchy 에서 선택한 씬 오브젝트에만 동작합니다. (프리팹 에셋·폴더 선택은 대상이 아닙니다)\n" +
                "모든 동작은 Ctrl+Z 로 되돌릴 수 있습니다.",
                MessageType.Info);

            EditorGUILayout.Space(6f);

            using (var check = new EditorGUI.ChangeCheckScope())
            {
                _prefab = (GameObject)EditorGUILayout.ObjectField("교체할 프리팹", _prefab, typeof(GameObject), false);
                if (check.changed) SaveSettings();
            }

            EditorGUILayout.Space(4f);
            DrawSelectionList();
            EditorGUILayout.Space(8f);

            bool canReplace = _prefab != null && _selection.Length > 0;
            if (!canReplace)
            {
                EditorGUILayout.HelpBox(
                    _prefab == null ? "교체할 프리팹을 선택하세요." : "씬에서 오브젝트를 선택하세요.",
                    MessageType.Info);
            }

            using (new EditorGUI.DisabledScope(!canReplace))
            {
                if (GUILayout.Button($"선택 {_selection.Length}개를 프리팹으로 교체", GUILayout.Height(30f))) ExecuteReplace();
            }

            EditorGUILayout.Space(12f);
            EditorGUILayout.LabelField("센터 빈 오브젝트 생성", EditorStyles.boldLabel);

            using (new EditorGUI.DisabledScope(_selection.Length == 0))
            {
                if (GUILayout.Button("선택 오브젝트 센터에 빈 오브젝트 생성", GUILayout.Height(26f)))
                {
                    AssetReplacerOps.CreateCenterEmpty(_selection);
                    _result = "센터 위치에 빈 오브젝트를 만들었습니다.";
                }
            }

            DrawRandomSection();

            if (!string.IsNullOrEmpty(_result))
            {
                EditorGUILayout.Space(6f);
                EditorGUILayout.HelpBox(_result, MessageType.Info);
            }
        }

        void DrawSelectionList()
        {
            EditorGUILayout.LabelField($"선택된 오브젝트: {_selection.Length}개", EditorStyles.miniBoldLabel);

            if (_selection.Length == 0) return;

            using (var scroll = new EditorGUILayout.ScrollViewScope(_selectionScroll, GUILayout.MaxHeight(120f)))
            {
                _selectionScroll = scroll.scrollPosition;

                int listed = Mathf.Min(_selection.Length, MaxListedTargets);
                for (int i = 0; i < listed; i++)
                {
                    if (_selection[i] != null) EditorGUILayout.LabelField("  • " + _selection[i].name, EditorStyles.miniLabel);
                }

                if (_selection.Length > listed)
                {
                    EditorGUILayout.LabelField($"… 외 {_selection.Length - listed}개", EditorStyles.miniLabel);
                }
            }
        }

        void DrawRandomSection()
        {
            EditorGUILayout.Space(14f);
            _settings.randomFoldout = EditorGUILayout.BeginFoldoutHeaderGroup(_settings.randomFoldout, "랜덤 변형");

            if (_settings.randomFoldout)
            {
                EditorGUILayout.HelpBox(
                    "선택한 오브젝트마다 최소~최대 사이의 값을 축별로 따로 뽑아 적용합니다.\n" +
                    "[퍼센트] 이동 · 회전 = 현재값 + 기준값 × % / 크기 = 기준값 × (1 + %)\n" +
                    "[절대값] 이동 = 현재값 + m / 회전 = 현재값 + ° / 크기 = 뽑은 값 그대로",
                    MessageType.None);

                using (var check = new EditorGUI.ChangeCheckScope())
                {
                    DrawRandomGroup("이동", _settings.move, false, "m");
                    DrawRandomGroup("회전", _settings.rotation, false, "°");
                    DrawRandomGroup("크기", _settings.scale, true, "배율");

                    EditorGUILayout.Space(6f);
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        _settings.useSeed = EditorGUILayout.ToggleLeft("시드 고정", _settings.useSeed, GUILayout.Width(90f));
                        using (new EditorGUI.DisabledScope(!_settings.useSeed))
                        {
                            _settings.seed = EditorGUILayout.IntField(_settings.seed);
                        }
                    }

                    if (check.changed) SaveSettings();
                }

                EditorGUILayout.Space(4f);

                bool anyGroup = _settings.AnyRandomGroup;
                using (new EditorGUI.DisabledScope(_selection.Length == 0 || !anyGroup))
                {
                    if (GUILayout.Button("랜덤 변형 적용", GUILayout.Height(30f)))
                    {
                        int count = AssetReplacerOps.ApplyRandom(_selection, _settings);
                        _result = $"오브젝트 {count}개에 랜덤 변형을 적용했습니다.";
                    }
                }

                if (_selection.Length == 0) EditorGUILayout.HelpBox("씬에서 오브젝트를 선택하세요.", MessageType.Info);
                else if (!anyGroup) EditorGUILayout.HelpBox("이동 · 회전 · 크기 중 하나 이상을 켜세요.", MessageType.Info);
            }

            EditorGUILayout.EndFoldoutHeaderGroup();
            EditorGUILayout.Space(8f);
        }

        void DrawRandomGroup(string title, AssetReplacerOps.RandomGroup g, bool isScale, string absUnit)
        {
            EditorGUILayout.Space(6f);
            g.enabled = EditorGUILayout.ToggleLeft(title, g.enabled, EditorStyles.boldLabel);
            if (!g.enabled) return;

            EditorGUI.indentLevel++;

            g.absolute = EditorGUILayout.Popup("단위", g.absolute ? 1 : 0, UnitNames) == 1;

            if (!g.absolute)
            {
                g.baseMode = (AssetReplacerOps.BaseMode)EditorGUILayout.Popup("기준", (int)g.baseMode, BaseModeNames);
                if (g.baseMode == AssetReplacerOps.BaseMode.Reference)
                {
                    g.reference = EditorGUILayout.Vector3Field("기준값", g.reference);
                }
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.PrefixLabel(g.absolute ? $"범위 ({absUnit})" : "범위 (%)");
                g.min = EditorGUILayout.FloatField(g.min);
                GUILayout.Label("~", GUILayout.Width(14f));
                g.max = EditorGUILayout.FloatField(g.max);
            }

            if (g.max < g.min) g.max = g.min;

            bool locked = isScale && _settings.linkXZ;

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.PrefixLabel("적용 축");
                g.x = GUILayout.Toggle(g.x, "X", EditorStyles.miniButtonLeft);
                g.y = GUILayout.Toggle(g.y, "Y", EditorStyles.miniButtonMid);

                using (new EditorGUI.DisabledScope(locked))
                {
                    bool zShown = GUILayout.Toggle(locked ? g.x : g.z, "Z", EditorStyles.miniButtonRight);
                    if (!locked) g.z = zShown;
                }
            }

            if (isScale)
            {
                _settings.linkXZ = EditorGUILayout.ToggleLeft("X · Z 연동 (같은 비율로 함께 변형)", _settings.linkXZ);
                if (_settings.linkXZ) g.z = g.x;
            }

            EditorGUI.indentLevel--;
        }

        void ExecuteReplace()
        {
            AssetReplacerOps.Replace(_selection, _prefab);
            _result = $"오브젝트를 '{_prefab.name}' 프리팹으로 교체했습니다.";
            RefreshSelection();
        }
    }
}
