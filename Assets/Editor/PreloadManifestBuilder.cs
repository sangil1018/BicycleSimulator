using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 레벨 씬의 대형 에셋 의존성을 수집해 프리로드 매니페스트 프리팹을 생성한다.
/// (Assets/Resources/Preload/LevelN_Preload.prefab — PreloadManager가 Home에서 로드)
/// 레벨 씬의 에셋 구성이 바뀌면 다시 실행해 목록을 갱신한다.
/// </summary>
public static class PreloadManifestBuilder
{
    // 이 크기 이상인 씬 의존성 에셋만 매니페스트에 포함 (원본 파일 크기 기준)
    const long SizeThresholdBytes = 512 * 1024;
    const string OutputFolder = "Assets/Resources/Preload";

    static readonly (string scenePath, string animPath, string outputName)[] Levels =
    {
        ("Assets/Scenes/Level1.unity", "Assets/Animation/level1/lvl1.anim", "Level1_Preload"),
        ("Assets/Scenes/Level2.unity", "Assets/Animation/level2/lvl2.anim", "Level2_Preload"),
    };

    [MenuItem("Tools/Build Preload Manifests")]
    public static void Build()
    {
        if (!AssetDatabase.IsValidFolder("Assets/Resources"))
            AssetDatabase.CreateFolder("Assets", "Resources");
        if (!AssetDatabase.IsValidFolder(OutputFolder))
            AssetDatabase.CreateFolder("Assets/Resources", "Preload");

        foreach (var (scenePath, animPath, outputName) in Levels)
            BuildManifest(scenePath, animPath, outputName);

        AssetDatabase.SaveAssets();
    }

    static void BuildManifest(string scenePath, string animPath, string outputName)
    {
        if (!File.Exists(scenePath))
        {
            Debug.LogError($"[PreloadManifestBuilder] 씬을 찾을 수 없음: {scenePath}");
            return;
        }

        var assets = new List<Object>();
        var seen = new HashSet<string>();
        long totalBytes = 0;

        // 씬 의존성 중 대형 에셋 (텍스처/오디오/모델/폰트)
        foreach (string dep in AssetDatabase.GetDependencies(scenePath, true))
        {
            if (!seen.Add(dep) || dep == scenePath) continue;
            var info = new FileInfo(dep);
            if (!info.Exists || info.Length < SizeThresholdBytes) continue;
            if (!IsPreloadableType(dep)) continue;

            var asset = AssetDatabase.LoadMainAssetAtPath(dep);
            if (asset == null) continue;
            assets.Add(asset);
            totalBytes += info.Length;
        }

        // 네비게이션 애니메이션 스프라이트 — 개별 크기는 작지만 수백 장이라 전부 포함
        if (File.Exists(animPath))
        {
            foreach (string dep in AssetDatabase.GetDependencies(animPath, true))
            {
                if (!seen.Add(dep)) continue;
                if (!typeof(Texture).IsAssignableFrom(AssetDatabase.GetMainAssetTypeAtPath(dep))) continue;

                var asset = AssetDatabase.LoadMainAssetAtPath(dep);
                if (asset == null) continue;
                assets.Add(asset);
                var info = new FileInfo(dep);
                if (info.Exists) totalBytes += info.Length;
            }
        }
        else
        {
            Debug.LogWarning($"[PreloadManifestBuilder] 애니메이션 클립 없음: {animPath}");
        }

        var go = new GameObject(outputName);
        try
        {
            go.SetActive(false);
            go.AddComponent<PreloadManifest>().assets = assets.ToArray();
            PrefabUtility.SaveAsPrefabAsset(go, $"{OutputFolder}/{outputName}.prefab");
        }
        finally
        {
            Object.DestroyImmediate(go);
        }

        Debug.Log($"[PreloadManifestBuilder] {outputName}: 에셋 {assets.Count}개, 원본 {totalBytes / (1024f * 1024f):F1} MB");
    }

    static bool IsPreloadableType(string path)
    {
        var type = AssetDatabase.GetMainAssetTypeAtPath(path);
        if (type == null) return false;

        if (typeof(Texture).IsAssignableFrom(type)) return true;
        if (type == typeof(AudioClip)) return true;
        if (type == typeof(Font)) return true;

        // 모델 파일(.fbx 등)의 메인 에셋은 GameObject — 메시/아바타가 함께 로드됨.
        // 씬/프리팹의 GameObject는 제외하고 모델 파일만 포함한다.
        if (type == typeof(GameObject))
        {
            string ext = Path.GetExtension(path).ToLowerInvariant();
            return ext == ".fbx" || ext == ".obj" || ext == ".blend" || ext == ".dae";
        }

        return false;
    }
}
