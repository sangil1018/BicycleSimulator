using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

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

    static readonly (string scenePath, string outputName)[] Levels =
    {
        ("Assets/Scenes/Level1.unity", "Level1_Preload"),
        ("Assets/Scenes/Level2.unity", "Level2_Preload"),
    };

    [MenuItem("Tools/Build Preload Manifests")]
    public static void Build()
    {
        if (!AssetDatabase.IsValidFolder("Assets/Resources"))
            AssetDatabase.CreateFolder("Assets", "Resources");
        if (!AssetDatabase.IsValidFolder(OutputFolder))
            AssetDatabase.CreateFolder("Assets/Resources", "Preload");

        foreach (var (scenePath, outputName) in Levels)
            BuildManifest(scenePath, outputName);

        AssetDatabase.SaveAssets();
    }

    static void BuildManifest(string scenePath, string outputName)
    {
        if (!File.Exists(scenePath))
        {
            Debug.LogError($"[PreloadManifestBuilder] 씬을 찾을 수 없음: {scenePath}");
            return;
        }

        var assets = new List<Object>();
        var seen = new HashSet<string>();
        long totalBytes = 0;

        // 씬 의존성 전체 — 스크립트/씬 파일만 제외하고 모두 포함
        // (애니메이션 클립을 통한 네비게이션 스프라이트, 셰이더/머티리얼, 프리팹까지 커버됨)
        foreach (string dep in AssetDatabase.GetDependencies(scenePath, true))
        {
            if (!seen.Add(dep) || dep == scenePath) continue;

            var type = AssetDatabase.GetMainAssetTypeAtPath(dep);
            if (type == null || type == typeof(MonoScript) || type == typeof(SceneAsset)) continue;

            var asset = AssetDatabase.LoadMainAssetAtPath(dep);
            if (asset == null) continue;
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

        Debug.Log($"[PreloadManifestBuilder] {outputName}: 에셋 {assets.Count}개 → 청크 {chunkCount}개, 원본 {totalBytes / (1024f * 1024f):F1} MB");
    }
}
