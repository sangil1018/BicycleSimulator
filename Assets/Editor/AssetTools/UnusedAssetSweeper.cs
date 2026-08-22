using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace AssetTools.Editor
{
    /// <summary>
    /// 어디에서도 참조되지 않는 소스 미디어 에셋(이미지/오디오/비디오/모델)을 찾아 프로젝트 밖으로 격리한다.
    ///
    /// 판정 기준은 "빌드에 들어가는 루트에서 출발한 의존성 폐포에 없으면 미참조"다. 루트는 셋이다 —
    /// 빌드 세팅의 씬, Resources 폴더 전체(문자열 경로 로드라 정적 참조가 없다), PlayerSettings의
    /// Preloaded Assets. 프리로드 매니페스트는 Resources 안에 있으므로 두 번째 루트에 자동으로 포함된다.
    ///
    /// 후보를 미디어 확장자 + 콘텐츠 폴더로 좁히는 이유: 머티리얼·프로파일·세팅 에셋 종류는
    /// ProjectSettings나 GraphicsSettings에서 GUID로만 물려 있어 의존성 폐포로는 안전하게 판정할 수
    /// 없다. 라이트맵이 사는 Assets/Scenes 도 같은 이유로 통째 제외한다 (LightingData 경유 참조가
    /// 버전에 따라 폐포에 안 잡히는 경우가 있고, 오판의 대가가 재베이크라 위험 대비 이득이 없다).
    ///
    /// 삭제가 아니라 이동인 이유: 대상 중 일부는 git 미추적(용량 때문에 커밋되지 않은 원본 비디오 등)이라
    /// 삭제하면 복구 경로가 없다. 프로젝트 밖으로 옮기면 Unity는 더 이상 임포트하지 않으므로 효과는
    /// 삭제와 같고, 확인 후 폴더째 지우면 된다.
    /// </summary>
    public static class UnusedAssetSweeper
    {
        /// <summary>후보를 찾을 폴더. 여기 없는 곳은 아예 건드리지 않는다.</summary>
        public static readonly string[] DefaultSearchFolders =
        {
            "Assets/Sources",
            "Assets/Videos",
            "Assets/3DAssets",
            "Assets/Prefabs",
            "Assets/Animation",
            "Assets/Materials",
        };

        /// <summary>미참조 판정을 적용할 확장자. 미디어 원본만 다룬다.</summary>
        static readonly HashSet<string> MediaExtensions = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase)
        {
            ".png", ".jpg", ".jpeg", ".tga", ".tif", ".tiff", ".psd", ".bmp", ".gif", ".exr", ".hdr",
            ".wav", ".mp3", ".ogg", ".aiff", ".aif",
            ".mp4", ".mov", ".webm", ".avi",
            ".fbx", ".obj", ".blend", ".dae",
        };

        /// <summary>확장자가 맞아도 이 경로에 있으면 후보에서 뺀다.</summary>
        static readonly string[] ExcludedPathFragments =
        {
            "/Editor/", "/Editor Resources/", "/Resources/", "/Plugins/", "/StreamingAssets/",
        };

        public struct Candidate
        {
            public string path;
            public long bytes;
        }

        public class Result
        {
            public List<Candidate> candidates = new List<Candidate>();
            public int rootCount;
            public int closureCount;
            public int scannedCount;
            public long TotalBytes => candidates.Sum(c => c.bytes);
        }

        // ── 스캔 ──────────────────────────────────────────────────────

        /// <summary>미참조 후보를 찾는다. 파일은 건드리지 않는다.</summary>
        public static Result Scan(IEnumerable<string> searchFolders)
        {
            var result = new Result();

            try
            {
                EditorUtility.DisplayProgressBar("미참조 에셋 검사", "루트 수집 중…", 0.05f);
                var roots = CollectRoots();
                result.rootCount = roots.Count;

                EditorUtility.DisplayProgressBar("미참조 에셋 검사", $"의존성 폐포 계산 중… (루트 {roots.Count}개)", 0.2f);
                var closure = new HashSet<string>(
                    AssetDatabase.GetDependencies(roots.ToArray(), true),
                    System.StringComparer.OrdinalIgnoreCase);
                result.closureCount = closure.Count;

                var folders = searchFolders.Where(AssetDatabase.IsValidFolder).ToArray();
                if (folders.Length == 0) return result;

                string[] guids = AssetDatabase.FindAssets(string.Empty, folders);
                result.scannedCount = guids.Length;

                for (int i = 0; i < guids.Length; i++)
                {
                    if (i % 200 == 0)
                    {
                        EditorUtility.DisplayProgressBar(
                            "미참조 에셋 검사", $"대조 중… ({i}/{guids.Length})", 0.3f + 0.7f * i / guids.Length);
                    }

                    string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                    if (!IsSweepCandidate(path) || closure.Contains(path)) continue;

                    var info = new FileInfo(path);
                    result.candidates.Add(new Candidate { path = path, bytes = info.Exists ? info.Length : 0L });
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            result.candidates.Sort((a, b) => b.bytes.CompareTo(a.bytes));
            return result;
        }

        /// <summary>빌드에 들어가는 진입점 전체 — 여기서 닿지 않으면 빌드에도 들어가지 않는다.</summary>
        static List<string> CollectRoots()
        {
            var roots = new List<string>();

            foreach (var scene in EditorBuildSettings.scenes)
                if (scene.enabled && File.Exists(scene.path)) roots.Add(scene.path);

            // Resources 는 경로 문자열로 로드되므로 정적 참조가 없다 — 폴더 전체를 루트로 친다.
            foreach (string guid in AssetDatabase.FindAssets(string.Empty, new[] { "Assets" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (path.Contains("/Resources/") && !AssetDatabase.IsValidFolder(path)) roots.Add(path);
            }

            foreach (var preloaded in PlayerSettings.GetPreloadedAssets())
            {
                if (preloaded == null) continue;
                string path = AssetDatabase.GetAssetPath(preloaded);
                if (!string.IsNullOrEmpty(path)) roots.Add(path);
            }

            return roots.Distinct().ToList();
        }

        static bool IsSweepCandidate(string path)
        {
            if (string.IsNullOrEmpty(path) || AssetDatabase.IsValidFolder(path)) return false;
            if (!MediaExtensions.Contains(Path.GetExtension(path))) return false;

            foreach (string fragment in ExcludedPathFragments)
                if (path.Contains(fragment)) return false;

            return true;
        }

        // ── 격리 ──────────────────────────────────────────────────────

        /// <summary>프로젝트와 같은 위치에 만드는 기본 격리 폴더 (Assets 밖이라 Unity가 임포트하지 않는다).</summary>
        public static string DefaultQuarantineFolder()
        {
            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            string parent = Directory.GetParent(projectRoot).FullName;
            string name = "_" + Path.GetFileName(projectRoot) + "_Quarantine";
            return Path.Combine(parent, name);
        }

        /// <summary>
        /// 후보를 격리 폴더로 옮긴다. Assets 기준 상대 경로를 그대로 유지하므로 되돌리기 쉽다.
        /// .meta 도 함께 옮겨야 Unity가 새 GUID를 발급하지 않는다.
        /// </summary>
        public static string Quarantine(IReadOnlyList<Candidate> candidates, string quarantineFolder)
        {
            int moved = 0, failed = 0;
            long bytes = 0;
            string projectRoot = Directory.GetParent(Application.dataPath).FullName;

            try
            {
                AssetDatabase.StartAssetEditing();

                for (int i = 0; i < candidates.Count; i++)
                {
                    if (i % 100 == 0)
                    {
                        EditorUtility.DisplayProgressBar(
                            "미참조 에셋 격리", $"{i}/{candidates.Count}", (float)i / candidates.Count);
                    }

                    string relative = candidates[i].path;
                    string source = Path.Combine(projectRoot, relative);
                    string destination = Path.Combine(quarantineFolder, relative.Replace('/', Path.DirectorySeparatorChar));

                    try
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(destination));

                        if (!File.Exists(source)) { failed++; continue; }
                        if (File.Exists(destination)) File.Delete(destination);
                        File.Move(source, destination);

                        if (File.Exists(source + ".meta"))
                        {
                            if (File.Exists(destination + ".meta")) File.Delete(destination + ".meta");
                            File.Move(source + ".meta", destination + ".meta");
                        }

                        moved++;
                        bytes += candidates[i].bytes;
                    }
                    catch (System.Exception e)
                    {
                        failed++;
                        Debug.LogWarning($"[UnusedAssetSweeper] 이동 실패: {relative} — {e.Message}");
                    }
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                AssetDatabase.StopAssetEditing();
                AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
            }

            string summary =
                $"[UnusedAssetSweeper] 격리 완료 — {moved}개 / {bytes / (1024f * 1024f):F1} MB"
                + (failed > 0 ? $", 실패 {failed}개" : string.Empty)
                + $"\n위치: {quarantineFolder}";
            Debug.Log(summary);
            return summary;
        }

        /// <summary>보고용 텍스트. 실행 전에 눈으로 확인하는 용도.</summary>
        public static string BuildReport(Result result)
        {
            var lines = new List<string>
            {
                $"루트 {result.rootCount}개 → 의존성 폐포 {result.closureCount}개",
                $"검사 대상 {result.scannedCount}개 중 미참조 {result.candidates.Count}개 / {result.TotalBytes / (1024f * 1024f):F1} MB",
                string.Empty,
                "── 용량 상위 30개 ──",
            };

            foreach (var c in result.candidates.Take(30))
                lines.Add($"{c.bytes / (1024f * 1024f),8:F1} MB  {c.path}");

            var byFolder = result.candidates
                .GroupBy(c => Path.GetDirectoryName(c.path).Replace('\\', '/'))
                .Select(g => (folder: g.Key, count: g.Count(), bytes: g.Sum(x => x.bytes)))
                .OrderByDescending(g => g.bytes)
                .Take(25);

            lines.Add(string.Empty);
            lines.Add("── 폴더별 합계 상위 25개 ──");
            foreach (var g in byFolder)
                lines.Add($"{g.bytes / (1024f * 1024f),8:F1} MB  ({g.count,4}개)  {g.folder}");

            return string.Join("\n", lines);
        }
    }
}
