using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace AssetTools.Editor
{
    /// <summary>
    /// 밉맵은 켜져 있는데 Streaming Mipmaps 는 꺼져 있는 텍스처를 찾아 스트리밍에 편입시킨다.
    ///
    /// QualitySettings 에서 텍스처 스트리밍은 이미 전역으로 켜져 있다(예산 512MB). 그런데 3D 에셋
    /// 텍스처 대부분이 개별 설정에서 빠져 있어, 밉 체인 전체가 최대 해상도로 상주한다 — 스트리밍이
    /// 관여하지 못하니 예산도 의미가 없다. 켜 주면 레벨 진입 시 업로드해야 할 양이 실제로 필요한
    /// 밉만큼으로 줄어든다.
    ///
    /// 스프라이트/UI 계열은 대상이 아니다. 1:1로 그려지는 데다 밉맵 자체가 필요 없어서, 스트리밍을
    /// 켜 봐야 이득이 없고 첫 프레임에 흐리게 나오는 부작용만 생긴다.
    /// </summary>
    public static class TextureStreamingOps
    {
        public static readonly string[] DefaultFolders = { "Assets/3DAssets" };

        /// <summary>스트리밍을 적용하지 않을 텍스처 유형 — 화면에 1:1로 그려지는 것들.</summary>
        static readonly HashSet<TextureImporterType> ExcludedTypes = new HashSet<TextureImporterType>
        {
            TextureImporterType.Sprite,
            TextureImporterType.GUI,
            TextureImporterType.Cursor,
            TextureImporterType.Cookie,
        };

        public class Result
        {
            public List<string> targets = new List<string>();
            public int scanned;
            public int alreadyStreaming;
            public int noMipmaps;
            public int excludedType;
        }

        /// <summary>대상을 찾는다. 임포트 설정은 건드리지 않는다.</summary>
        public static Result Scan(IEnumerable<string> folders)
        {
            var result = new Result();
            var valid = folders.Where(AssetDatabase.IsValidFolder).ToArray();
            if (valid.Length == 0) return result;

            string[] guids = AssetDatabase.FindAssets("t:Texture2D", valid);
            result.scanned = guids.Length;

            try
            {
                for (int i = 0; i < guids.Length; i++)
                {
                    if (i % 100 == 0)
                    {
                        EditorUtility.DisplayProgressBar(
                            "텍스처 스트리밍 검사", $"{i}/{guids.Length}", (float)i / guids.Length);
                    }

                    string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                    if (AssetImporter.GetAtPath(path) is not TextureImporter importer) continue;

                    if (ExcludedTypes.Contains(importer.textureType)) { result.excludedType++; continue; }
                    if (!importer.mipmapEnabled) { result.noMipmaps++; continue; }
                    if (importer.streamingMipmaps) { result.alreadyStreaming++; continue; }

                    result.targets.Add(path);
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            return result;
        }

        /// <summary>대상에 Streaming Mipmaps 를 켜고 재임포트한다.</summary>
        public static string Apply(IReadOnlyList<string> targets)
        {
            int applied = 0, failed = 0;

            try
            {
                AssetDatabase.StartAssetEditing();

                for (int i = 0; i < targets.Count; i++)
                {
                    if (i % 25 == 0)
                    {
                        EditorUtility.DisplayProgressBar(
                            "텍스처 스트리밍 적용", $"{i}/{targets.Count} — 재임포트는 시간이 걸립니다",
                            (float)i / targets.Count);
                    }

                    if (AssetImporter.GetAtPath(targets[i]) is not TextureImporter importer) { failed++; continue; }

                    importer.streamingMipmaps = true;
                    EditorUtility.SetDirty(importer);
                    importer.SaveAndReimport();
                    applied++;
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                AssetDatabase.StopAssetEditing();
                AssetDatabase.Refresh();
            }

            string summary = $"[TextureStreamingOps] Streaming Mipmaps 적용 {applied}개"
                             + (failed > 0 ? $", 실패 {failed}개" : string.Empty);
            Debug.Log(summary);
            return summary;
        }

        public static string BuildReport(Result result)
        {
            var lines = new List<string>
            {
                $"검사 {result.scanned}개 — 적용 대상 {result.targets.Count}개",
                $"이미 스트리밍 {result.alreadyStreaming}개 / 밉맵 없음 {result.noMipmaps}개 / 제외 유형 {result.excludedType}개",
                string.Empty,
                "── 폴더별 대상 수 ──",
            };

            var byFolder = result.targets
                .GroupBy(p => System.IO.Path.GetDirectoryName(p).Replace('\\', '/'))
                .Select(g => (folder: g.Key, count: g.Count()))
                .OrderByDescending(g => g.count)
                .Take(25);

            foreach (var g in byFolder) lines.Add($"{g.count,5}개  {g.folder}");
            return string.Join("\n", lines);
        }
    }
}
