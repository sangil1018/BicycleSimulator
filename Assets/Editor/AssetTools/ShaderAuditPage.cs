using UnityEditor;
using UnityEngine;

namespace AssetTools.Editor
{
    /// <summary>Always Included Shaders 와 Resources 머티리얼 사용 현황을 진단하는 탭.</summary>
    public class ShaderAuditPage : AssetToolPage
    {
        public override string Title => "셰이더 감사";
        public override int Order => 40;

        string _report;
        Vector2 _reportScroll;
        bool _compileLog;

        public override void OnEnable() => _compileLog = ShaderAudit.IsCompileLogEnabled();

        public override void OnGUI()
        {
            EditorGUILayout.HelpBox(
                "빌드 용량과 첫 프레임 히칭을 만드는 것은 셰이더 '개수'가 아니라 컴파일된 '변형(variant)' 수입니다.\n" +
                "변형이 실제로 생기는 곳은 셋뿐입니다 — 머티리얼이 참조하는 셰이더, Always Included Shaders, " +
                "Resources 폴더의 머티리얼. 이 탭은 뒤의 둘을 눈으로 확인하게 해 줍니다.",
                MessageType.Info);

            EditorGUILayout.Space(6f);

            if (GUILayout.Button("셰이더 감사 실행", GUILayout.Height(30f))) _report = ShaderAudit.BuildReport();

            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField("셰이더 컴파일 로깅", EditorStyles.boldLabel);
            EditorGUILayout.LabelField(
                "켜두면 셰이더가 컴파일될 때마다 콘솔에 찍힙니다. 히칭이 나는 순간 로그가 쏟아지면 원인은 셰이더 컴파일입니다.",
                EditorStyles.wordWrappedMiniLabel);

            using (var check = new EditorGUI.ChangeCheckScope())
            {
                _compileLog = EditorGUILayout.ToggleLeft("변형 로깅 켜기", _compileLog);
                if (check.changed) ShaderAudit.SetCompileLog(_compileLog);
            }

            if (string.IsNullOrEmpty(_report)) return;

            EditorGUILayout.Space(8f);

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("감사 결과", EditorStyles.boldLabel);
                if (GUILayout.Button("콘솔로 보내기", EditorStyles.miniButton, GUILayout.Width(100f)))
                {
                    Debug.Log(_report);
                }

                if (GUILayout.Button("복사", EditorStyles.miniButton, GUILayout.Width(50f)))
                {
                    EditorGUIUtility.systemCopyBuffer = _report;
                }
            }

            using (var scroll = new EditorGUILayout.ScrollViewScope(_reportScroll, GUILayout.MinHeight(200f)))
            {
                _reportScroll = scroll.scrollPosition;
                EditorGUILayout.TextArea(_report, EditorStyles.label);
            }
        }
    }
}
