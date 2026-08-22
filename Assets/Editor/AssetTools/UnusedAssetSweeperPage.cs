using UnityEditor;
using UnityEngine;

namespace AssetTools.Editor
{
    /// <summary>어디에서도 참조되지 않는 미디어 에셋을 찾아 프로젝트 밖으로 걷어내는 탭.</summary>
    public class UnusedAssetSweeperPage : AssetToolPage
    {
        public override string Title => "미참조 에셋 정리";
        public override int Order => 50;

        UnusedAssetSweeper.Result _result;
        string _report;
        string _quarantineFolder;
        Vector2 _reportScroll;

        public override void OnEnable() => _quarantineFolder = UnusedAssetSweeper.DefaultQuarantineFolder();

        public override void OnGUI()
        {
            EditorGUILayout.HelpBox(
                "빌드 세팅의 씬 · Resources 폴더 · Preloaded Assets 에서 출발한 의존성 폐포에 들어오지 않는 " +
                "미디어 에셋(이미지/오디오/비디오/모델)을 찾습니다. 여기 걸린 것은 빌드에도 들어가지 않으므로, " +
                "임포트 시간과 빌드 시간만 축내고 있는 파일들입니다.\n\n" +
                "머티리얼·프로파일·세팅 종류와 라이트맵이 있는 Assets/Scenes 는 오판 위험 때문에 아예 검사하지 않습니다.",
                MessageType.Info);

            EditorGUILayout.Space(6f);

            if (GUILayout.Button("미참조 에셋 검사", GUILayout.Height(30f)))
            {
                _result = UnusedAssetSweeper.Scan(UnusedAssetSweeper.DefaultSearchFolders);
                _report = UnusedAssetSweeper.BuildReport(_result);
            }

            if (_result != null && _result.candidates.Count > 0) DrawQuarantineSection();

            DrawReport();
        }

        void DrawQuarantineSection()
        {
            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField(
                $"미참조 {_result.candidates.Count}개 / {_result.TotalBytes / (1024f * 1024f):F1} MB",
                EditorStyles.boldLabel);

            EditorGUILayout.Space(4f);
            EditorGUILayout.LabelField("격리 폴더 (Assets 밖이라 Unity가 임포트하지 않습니다)", EditorStyles.miniBoldLabel);

            using (new EditorGUILayout.HorizontalScope())
            {
                _quarantineFolder = EditorGUILayout.TextField(_quarantineFolder);
                if (GUILayout.Button("…", EditorStyles.miniButton, GUILayout.Width(28f)))
                {
                    string picked = EditorUtility.SaveFolderPanel("격리 폴더 선택", _quarantineFolder, string.Empty);
                    if (!string.IsNullOrEmpty(picked)) _quarantineFolder = picked;
                }
            }

            EditorGUILayout.HelpBox(
                "삭제가 아니라 이동입니다. 원본 비디오처럼 git에 올라가 있지 않은 파일이 섞여 있어, " +
                "지워 버리면 되돌릴 방법이 없기 때문입니다. 게임을 한 바퀴 돌려 보고 이상이 없으면 격리 폴더를 지우세요.",
                MessageType.Warning);

            using (new EditorGUI.DisabledScope(string.IsNullOrWhiteSpace(_quarantineFolder)))
            {
                if (GUILayout.Button("격리 폴더로 이동", GUILayout.Height(30f)) && ConfirmQuarantine())
                {
                    _report = UnusedAssetSweeper.Quarantine(_result.candidates, _quarantineFolder);
                    _result = null;
                }
            }
        }

        void DrawReport()
        {
            if (string.IsNullOrEmpty(_report)) return;

            EditorGUILayout.Space(8f);

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("결과", EditorStyles.boldLabel);
                if (GUILayout.Button("콘솔로 보내기", EditorStyles.miniButton, GUILayout.Width(100f))) Debug.Log(_report);
                if (GUILayout.Button("복사", EditorStyles.miniButton, GUILayout.Width(50f)))
                    EditorGUIUtility.systemCopyBuffer = _report;
            }

            using (var scroll = new EditorGUILayout.ScrollViewScope(_reportScroll, GUILayout.MinHeight(240f)))
            {
                _reportScroll = scroll.scrollPosition;
                EditorGUILayout.TextArea(_report, EditorStyles.label);
            }
        }

        bool ConfirmQuarantine() =>
            EditorUtility.DisplayDialog(
                "미참조 에셋 격리",
                $"{_result.candidates.Count}개 ({_result.TotalBytes / (1024f * 1024f):F1} MB) 를 아래로 옮깁니다.\n\n"
                + $"{_quarantineFolder}\n\n"
                + "Assets 기준 상대 경로를 그대로 유지하므로, 문제가 생기면 폴더째 되돌려 놓으면 됩니다.",
                "이동", "취소");
    }
}
