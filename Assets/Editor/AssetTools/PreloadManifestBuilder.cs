using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace AssetTools.Editor
{
    /// <summary>
    /// 레벨 씬의 에셋 의존성 "전체"를 수집해 프리로드 매니페스트 프리팹을 생성한다.
    /// (Assets/Resources/Preload/LevelN_Preload_XXX.prefab — PreloadManager가 Home에서 로드)
    ///
    /// 크기/유형 필터 없이 전부 상주시키는 이유: 빌드에서는 GoHome의 LoadScene(Single)이
    /// 미사용 에셋을 언로드해 매 진입마다 재로드가 반복된다. 셰이더 변형 역직렬화처럼
    /// 파일은 작아도 재로드 비용이 큰 에셋이 있어(빌드 실측), 선별하느니 전부 잡아두는 것이
    /// 단순하고 확실하다. 총량은 레벨당 수백 MB 수준으로 키오스크 PC 메모리에서 감당 가능.
    ///
    /// 레벨 씬의 에셋 구성이 바뀌면 다시 실행해 목록을 갱신한다.
    /// </summary>
    public static class PreloadManifestBuilder
    {
        const string OutputFolder = "Assets/Resources/Preload";

        // 청크당 에셋 수 — Unity 비동기 로딩 큐는 순차 처리라, 레벨 씬 로드가 진행 중인
        // 매니페스트 로드 뒤에 줄을 선다. 청크 단위로 발행을 멈출 수 있게 나누되,
        // 너무 잘게 나누면 청크당 오버헤드로 프리로드 총 시간이 늘어난다 (150개 ≈ 수백 ms 단위).
        const int ChunkSize = 150;

        public static readonly (string scenePath, string outputName)[] Levels =
        {
            ("Assets/Scenes/Level1.unity", "Level1_Preload"),
            ("Assets/Scenes/Level2.unity", "Level2_Preload"),
        };

        /// <summary>
        /// 모든 레벨의 매니페스트를 만들고 결과 요약 줄을 돌려준다.
        /// 진입점은 Tools > Asset Tools 창의 「프리로드 매니페스트」 탭 하나뿐이다.
        /// (예전에는 Tools/Build Preload Manifests 메뉴에도 같은 기능이 있었으나 중복이라 정리했다)
        /// </summary>
        public static List<string> BuildAll()
        {
            var report = new List<string>();

            if (!AssetDatabase.IsValidFolder("Assets/Resources"))
                AssetDatabase.CreateFolder("Assets", "Resources");
            if (!AssetDatabase.IsValidFolder(OutputFolder))
                AssetDatabase.CreateFolder("Assets/Resources", "Preload");

            try
            {
                for (int i = 0; i < Levels.Length; i++)
                {
                    var (scenePath, outputName) = Levels[i];
                    EditorUtility.DisplayProgressBar(
                        "프리로드 매니페스트 생성", $"{outputName} ({i + 1}/{Levels.Length})", (float)i / Levels.Length);

                    report.Add(BuildManifest(scenePath, outputName));
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            AssetDatabase.SaveAssets();
            return report;
        }

        static string BuildManifest(string scenePath, string outputName)
        {
            if (!File.Exists(scenePath))
            {
                string missing = $"[PreloadManifestBuilder] 씬을 찾을 수 없음: {scenePath}";
                Debug.LogError(missing);
                return missing;
            }

            var assets = new List<Object>();
            var seen = new HashSet<string>();
            long totalBytes = 0;

            // 씬 의존성 전체 — 스크립트/씬 파일과 "빌드에 넣으면 안 되는 것"만 제외하고 모두 포함
            // (애니메이션 클립을 통한 네비게이션 스프라이트, 셰이더/머티리얼, 프리팹까지 커버됨)
            foreach (string dep in AssetDatabase.GetDependencies(scenePath, true))
            {
                if (!seen.Add(dep) || dep == scenePath) continue;

                var type = AssetDatabase.GetMainAssetTypeAtPath(dep);
                if (type == null || type == typeof(MonoScript) || type == typeof(SceneAsset)) continue;
                if (IsProbeVolumeData(dep) || IsEditorOnly(dep)) continue;

                var asset = AssetDatabase.LoadMainAssetAtPath(dep);
                if (asset == null) continue;
                if ((asset.hideFlags & HideFlags.DontSaveInBuild) != 0) continue; // IsEditorOnly 주석 참고
                assets.Add(asset);

                var info = new FileInfo(dep);
                if (info.Exists) totalBytes += info.Length;
            }

            // 기존 매니페스트 제거 — 청크 수가 줄었을 때의 잔재와 구버전 파일 정리
            foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { OutputFolder }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (Path.GetFileNameWithoutExtension(path).StartsWith(outputName))
                    AssetDatabase.DeleteAsset(path);
            }

            int chunkCount = (assets.Count + ChunkSize - 1) / ChunkSize;
            for (int c = 0; c < chunkCount; c++)
            {
                string chunkName = $"{outputName}_{c:000}";
                var go = new GameObject(chunkName);
                try
                {
                    go.SetActive(false);
                    go.AddComponent<PreloadManifest>().assets = assets
                        .GetRange(c * ChunkSize, Mathf.Min(ChunkSize, assets.Count - c * ChunkSize))
                        .ToArray();
                    PrefabUtility.SaveAsPrefabAsset(go, $"{OutputFolder}/{chunkName}.prefab");
                }
                finally
                {
                    Object.DestroyImmediate(go);
                }
            }

            return $"[PreloadManifestBuilder] {outputName}: 에셋 {assets.Count}개 → 청크 {chunkCount}개, 원본 {totalBytes / (1024f * 1024f):F1} MB";
        }

        /// <summary>
        /// Adaptive Probe Volume 베이크 데이터(&lt;베이킹 세트&gt;.Cell*Data.bytes)인지.
        ///
        /// 이것만은 매니페스트에서 빼야 한다. APV는 빌드 시 이 데이터를 StreamingAssets로 복사해
        /// 자체 스트리밍으로 읽고, 디버그용 CellSupportData는 아예 스트립한다
        /// (ProbeVolumeBuildProcessor.PrepareForBuild). Resources 안의 매니페스트가 TextAsset을
        /// 직접 참조하면 그 처리가 전부 무효화되고 원본이 통째로 resources.assets에 들어간다.
        /// 레벨1+2 합 약 1.6 GB로, 단일 직렬화 파일 한계를 넘겨 빌드가 실패했다
        /// ("Building - Failed to write file: resources.assets").
        /// </summary>
        static bool IsProbeVolumeData(string path) =>
            path.EndsWith(".bytes", System.StringComparison.OrdinalIgnoreCase) &&
            Path.GetFileNameWithoutExtension(path).Contains(".Cell");

        /// <summary>
        /// 에디터 전용 에셋 경로인지 (…/Editor/…, …/Editor Resources/…).
        ///
        /// GetDependencies(recursive: true)는 에디터 의존성까지 돌려주기 때문에, TMP 컴포넌트의
        /// 기즈모 아이콘(Packages/com.unity.ugui/Editor Resources/Gizmos/TMP - Text Component Icon.psd)
        /// 처럼 빌드에 들어가면 안 되는 에셋이 섞여 온다. 매니페스트는 Resources 안에 있어 그 참조가
        /// 곧 빌드 포함을 의미하는데, 이런 에셋은 HideFlags.DontSave라 직렬화가 거부되고
        /// resources.assets 쓰기 전체가 실패한다:
        ///   "An asset is marked with HideFlags.DontSave but is included in the build"
        ///   → "Building - Failed to write file: resources.assets"
        /// 씬은 이 아이콘을 참조하지 않는다 — 오직 매니페스트만 끌어온다.
        /// </summary>
        static bool IsEditorOnly(string path) =>
            path.Contains("/Editor/") || path.Contains("/Editor Resources/");
    }
}
