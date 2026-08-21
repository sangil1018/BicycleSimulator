using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace AssetTools.Editor
{
    /// <summary>레벨 씬의 프리로드 매니페스트 프리팹을 다시 생성하는 탭.</summary>
    public class PreloadManifestPage : AssetToolPage
    {
        public override string Title => "프리로드 매니페스트";
        public override int Order => 50;

        List<string> _report;

        public override void OnGUI()
        {
            EditorGUILayout.HelpBox(
                "레벨 씬의 에셋 의존성 전체를 수집해 Assets/Resources/Preload 아래에 매니페스트 프리팹을 만듭니다.\n" +
                "레벨 씬의 에셋 구성이 바뀌면 반드시 다시 실행해야 프리로드 목록이 최신이 됩니다.\n" +
                "(매니페스트를 만드는 유일한 진입점입니다)",
                MessageType.Info);

            EditorGUILayout.Space(6f);
            EditorGUILayout.LabelField("대상 씬", EditorStyles.boldLabel);

            foreach (var (scenePath, outputName) in PreloadManifestBuilder.Levels)
            {
                EditorGUILayout.LabelField($"  • {scenePath}  →  {outputName}_XXX.prefab", EditorStyles.miniLabel);
            }

            EditorGUILayout.Space(8f);

            if (GUILayout.Button("매니페스트 다시 생성", GUILayout.Height(30f)))
            {
                if (EditorUtility.DisplayDialog(
                        "프리로드 매니페스트 생성",
                        "기존 매니페스트 프리팹을 지우고 다시 만듭니다.\n씬이 큰 경우 시간이 걸릴 수 있습니다.",
                        "생성", "취소"))
                {
                    _report = PreloadManifestBuilder.BuildAll();
                    foreach (string line in _report) Debug.Log(line);
                }
            }

            if (_report == null || _report.Count == 0) return;

            EditorGUILayout.Space(6f);
            EditorGUILayout.HelpBox(string.Join("\n", _report), MessageType.Info);
        }
    }
}
