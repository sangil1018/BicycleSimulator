using UnityEditor;
using UnityEngine;

namespace AssetTools.Editor
{
    /// <summary>밉맵은 켜져 있는데 스트리밍에서 빠져 있는 텍스처를 찾아 편입시키는 탭.</summary>
    public class TextureStreamingPage : AssetToolPage
    {
        public override string Title => "텍스처 스트리밍";
        public override int Order => 55;

        TextureStreamingOps.Result _result;
        string _report;
        Vector2 _reportScroll;

        public override void OnGUI()
        {
            EditorGUILayout.HelpBox(
                "텍스처 스트리밍은 QualitySettings 에서 이미 전역으로 켜져 있습니다(예산 512MB). " +
                "그런데 3D 에셋 텍스처 대부분이 개별 설정에서 빠져 있어 밉 체인 전체가 최대 해상도로 상주하고, " +
                "그만큼 레벨 진입에서 업로드해야 할 양이 늘어납니다.\n\n" +
                "스프라이트·GUI·커서·쿠키는 대상이 아닙니다. 1:1로 그려져서 이득이 없고 첫 프레임만 흐려집니다.",
                MessageType.Info);

            EditorGUILayout.Space(6f);

            if (GUILayout.Button("대상 검사", GUILayout.Height(30f)))
            {
                _result = TextureStreamingOps.Scan(TextureStreamingOps.DefaultFolders);
                _report = TextureStreamingOps.BuildReport(_result);
            }

            if (_result != null && _result.targets.Count > 0)
            {
                EditorGUILayout.Space(8f);
                EditorGUILayout.LabelField($"적용 대상 {_result.targets.Count}개", EditorStyles.boldLabel);

                EditorGUILayout.HelpBox(
                    "적용하면 대상 텍스처가 전부 재임포트됩니다. 개수가 많으면 수 분 걸릴 수 있습니다.",
                    MessageType.Warning);

                if (GUILayout.Button("Streaming Mipmaps 켜기", GUILayout.Height(30f)) && Confirm())
                {
                    _report = TextureStreamingOps.Apply(_result.targets);
                    _result = null;
                    return;
                }
            }

            if (string.IsNullOrEmpty(_report)) return;

            EditorGUILayout.Space(8f);

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("검사 결과", EditorStyles.boldLabel);
                if (GUILayout.Button("콘솔로 보내기", EditorStyles.miniButton, GUILayout.Width(100f))) Debug.Log(_report);
                if (GUILayout.Button("복사", EditorStyles.miniButton, GUILayout.Width(50f)))
                    EditorGUIUtility.systemCopyBuffer = _report;
            }

            using (var scroll = new EditorGUILayout.ScrollViewScope(_reportScroll, GUILayout.MinHeight(200f)))
            {
                _reportScroll = scroll.scrollPosition;
                EditorGUILayout.TextArea(_report, EditorStyles.label);
            }
        }

        bool Confirm() =>
            EditorUtility.DisplayDialog(
                "텍스처 스트리밍 적용",
                $"{_result.targets.Count}개 텍스처에 Streaming Mipmaps 를 켜고 재임포트합니다.\n\n"
                + "임포트 설정만 바뀌므로 되돌리기는 안전합니다.",
                "적용", "취소");
    }
}
