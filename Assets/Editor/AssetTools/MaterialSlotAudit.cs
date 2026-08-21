using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace AssetTools.Editor
{
    /// <summary>
    /// 렌더러의 머티리얼 슬롯 수가 메시의 서브메시 수보다 많은 곳을 찾아내는 진단.
    ///
    /// GPU Resident Drawer(PC_RPAsset 의 m_GPUResidentDrawerMode)가 켜져 있으면
    /// 이런 렌더러마다 "Material count in the shared material list is higher than
    /// sub mesh count for the mesh. Object may be corrupted." 경고를 찍는다.
    /// 남는 슬롯은 원래부터 렌더링에 쓰이지 않는 죽은 데이터라 잘라내도 화면은 그대로다.
    /// </summary>
    public static class MaterialSlotAudit
    {
        public struct Options
        {
            public bool ScanProjectPrefabs;
            public bool ScanOpenScenes;
            public bool IncludeSkinnedMeshes;

            public static Options Default => new Options
            {
                ScanProjectPrefabs = true,
                ScanOpenScenes = true,
                IncludeSkinnedMeshes = true
            };
        }

        /// <summary>슬롯이 초과된 렌더러 하나.</summary>
        public class Entry
        {
            /// <summary>프리팹 에셋 경로. 씬 오브젝트면 null.</summary>
            public string AssetPath;

            /// <summary>씬 오브젝트일 때의 씬 이름.</summary>
            public string SceneName;

            public string HierarchyPath;
            public string MeshName;
            public string RendererType;

            public int Slots;
            public int SubMeshes;

            /// <summary>비어 있는(None) 머티리얼 슬롯 개수. 초과와는 별개의 문제.</summary>
            public int EmptySlots;

            /// <summary>모델(FBX) 등 수정할 수 없는 에셋에 속해 임포터에서 고쳐야 하는 경우.</summary>
            public bool ReadOnly;

            /// <summary>Ping 용. 씬 엔트리면 실제 렌더러, 프리팹 엔트리면 에셋 안의 렌더러.</summary>
            public Renderer Reference;

            public bool IsPrefabAsset => AssetPath != null;
            public int Excess => Slots - SubMeshes;
            public string Location => IsPrefabAsset ? AssetPath : $"[{SceneName}]";
        }

        public class ScanResult
        {
            public readonly List<Entry> Entries = new List<Entry>();

            public int ScannedPrefabs;
            public int ScannedScenes;
            public int ScannedRenderers;

            /// <summary>메시가 비어 있어 비교할 수 없었던 렌더러 개수.</summary>
            public int MissingMesh;

            public bool HasAny => Entries.Count > 0;
        }

        public class FixReport
        {
            public int FixedRenderers;
            public int RemovedSlots;
            public int ModifiedPrefabs;
            public int ModifiedScenes;
            public int SkippedReadOnly;

            public readonly List<string> Warnings = new List<string>();
        }

        // ---------------------------------------------------------------- 스캔

        public static ScanResult Scan(Options options)
        {
            var result = new ScanResult();

            try
            {
                if (options.ScanOpenScenes) ScanOpenScenes(options, result);
                if (options.ScanProjectPrefabs) ScanProjectPrefabs(options, result);
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            // 초과가 큰 순 → 위치 순으로 정렬해 심한 것부터 보이게 한다.
            result.Entries.Sort((a, b) =>
            {
                int byExcess = b.Excess.CompareTo(a.Excess);
                if (byExcess != 0) return byExcess;
                return string.CompareOrdinal(a.Location + a.HierarchyPath, b.Location + b.HierarchyPath);
            });

            return result;
        }

        static void ScanOpenScenes(Options options, ScanResult result)
        {
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (!scene.isLoaded) continue;

                result.ScannedScenes++;

                foreach (var root in scene.GetRootGameObjects())
                {
                    CollectFrom(root, options, result, assetPath: null, sceneName: scene.name, readOnly: false);
                }
            }
        }

        static void ScanProjectPrefabs(Options options, ScanResult result)
        {
            var guids = AssetDatabase.FindAssets("t:Prefab");

            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);

                // FindAssets 는 FBX 등 모델 에셋도 t:Prefab 으로 잡는다. 실제 프리팹 파일만 본다.
                if (!path.EndsWith(".prefab", System.StringComparison.OrdinalIgnoreCase)) continue;

                if (EditorUtility.DisplayCancelableProgressBar(
                        "머티리얼 슬롯 검사", $"{path} ({i + 1}/{guids.Length})", (float)i / guids.Length))
                {
                    break;
                }

                var root = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (root == null) continue;

                result.ScannedPrefabs++;

                bool readOnly = PrefabUtility.IsPartOfImmutablePrefab(root);
                CollectFrom(root, options, result, assetPath: path, sceneName: null, readOnly: readOnly);
            }
        }

        static void CollectFrom(GameObject root, Options options, ScanResult result,
                                string assetPath, string sceneName, bool readOnly)
        {
            foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer == null) continue;
                if (!IsAuditable(renderer, options)) continue;

                result.ScannedRenderers++;

                var mesh = GetMesh(renderer);
                if (mesh == null)
                {
                    result.MissingMesh++;
                    continue;
                }

                var materials = renderer.sharedMaterials;
                if (materials.Length <= mesh.subMeshCount) continue;

                int empty = 0;
                foreach (var material in materials)
                {
                    if (material == null) empty++;
                }

                result.Entries.Add(new Entry
                {
                    AssetPath = assetPath,
                    SceneName = sceneName,
                    HierarchyPath = HierarchyPathOf(renderer.transform),
                    MeshName = mesh.name,
                    RendererType = renderer.GetType().Name,
                    Slots = materials.Length,
                    SubMeshes = mesh.subMeshCount,
                    EmptySlots = empty,
                    ReadOnly = readOnly,
                    Reference = renderer
                });
            }
        }

        // ---------------------------------------------------------------- 수정

        /// <summary>
        /// 스캔 결과의 렌더러들에서 초과 슬롯을 잘라낸다.
        /// 씬 오브젝트는 Undo 가 기록되고, 프리팹 에셋은 파일이 직접 수정되어 Undo 가 되지 않는다.
        /// </summary>
        public static FixReport Trim(ScanResult scan)
        {
            var report = new FixReport();

            var prefabPaths = new List<string>();
            var sceneEntries = new List<Entry>();

            foreach (var entry in scan.Entries)
            {
                if (entry.ReadOnly)
                {
                    report.SkippedReadOnly++;
                    continue;
                }

                if (entry.IsPrefabAsset)
                {
                    if (!prefabPaths.Contains(entry.AssetPath)) prefabPaths.Add(entry.AssetPath);
                }
                else
                {
                    sceneEntries.Add(entry);
                }
            }

            try
            {
                TrimSceneObjects(sceneEntries, report);
                TrimPrefabAssets(prefabPaths, report);
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            if (report.ModifiedPrefabs > 0) AssetDatabase.SaveAssets();

            return report;
        }

        static void TrimSceneObjects(List<Entry> entries, FixReport report)
        {
            if (entries.Count == 0) return;

            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName("머티리얼 슬롯 정리");
            int group = Undo.GetCurrentGroup();

            var dirtyScenes = new List<Scene>();

            foreach (var entry in entries)
            {
                var renderer = entry.Reference;
                if (renderer == null)
                {
                    report.Warnings.Add($"{entry.Location} / {entry.HierarchyPath} : 렌더러가 사라져 건너뜁니다. 다시 검사하세요.");
                    continue;
                }

                var mesh = GetMesh(renderer);
                if (mesh == null) continue;

                Undo.RecordObject(renderer, "머티리얼 슬롯 정리");

                int removed = TrimTo(renderer, mesh.subMeshCount);
                if (removed <= 0) continue;

                report.FixedRenderers++;
                report.RemovedSlots += removed;

                var scene = renderer.gameObject.scene;
                if (scene.IsValid() && !dirtyScenes.Contains(scene)) dirtyScenes.Add(scene);
            }

            foreach (var scene in dirtyScenes) EditorSceneManager.MarkSceneDirty(scene);
            report.ModifiedScenes = dirtyScenes.Count;

            Undo.CollapseUndoOperations(group);
        }

        static void TrimPrefabAssets(List<string> paths, FixReport report)
        {
            for (int i = 0; i < paths.Count; i++)
            {
                string path = paths[i];

                EditorUtility.DisplayProgressBar(
                    "머티리얼 슬롯 정리", $"{path} ({i + 1}/{paths.Count})", (float)i / paths.Count);

                var contents = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    int fixedRenderers = 0;
                    int removedSlots = 0;

                    foreach (var renderer in contents.GetComponentsInChildren<Renderer>(true))
                    {
                        if (renderer == null) continue;

                        var mesh = GetMesh(renderer);
                        if (mesh == null) continue;
                        if (renderer.sharedMaterials.Length <= mesh.subMeshCount) continue;

                        int removed = TrimTo(renderer, mesh.subMeshCount);
                        if (removed <= 0) continue;

                        fixedRenderers++;
                        removedSlots += removed;
                    }

                    if (fixedRenderers == 0) continue;

                    PrefabUtility.SaveAsPrefabAsset(contents, path, out bool success);
                    if (success)
                    {
                        report.FixedRenderers += fixedRenderers;
                        report.RemovedSlots += removedSlots;
                        report.ModifiedPrefabs++;
                    }
                    else
                    {
                        report.Warnings.Add($"{path} : 프리팹 저장에 실패했습니다.");
                    }
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(contents);
                }
            }
        }

        /// <summary>머티리얼 배열을 subMeshCount 길이로 잘라낸다. 잘라낸 개수를 반환.</summary>
        static int TrimTo(Renderer renderer, int subMeshCount)
        {
            var current = renderer.sharedMaterials;
            if (current.Length <= subMeshCount) return 0;

            var trimmed = new Material[subMeshCount];
            for (int i = 0; i < subMeshCount; i++) trimmed[i] = current[i];

            renderer.sharedMaterials = trimmed;
            return current.Length - subMeshCount;
        }

        // ---------------------------------------------------------------- 공통

        /// <summary>메시 기반 렌더러만 대상. 파티클·라인·트레일 등은 서브메시 개념이 다르다.</summary>
        static bool IsAuditable(Renderer renderer, Options options)
        {
            if (renderer is MeshRenderer) return true;
            if (renderer is SkinnedMeshRenderer) return options.IncludeSkinnedMeshes;
            return false;
        }

        static Mesh GetMesh(Renderer renderer)
        {
            if (renderer is SkinnedMeshRenderer skinned) return skinned.sharedMesh;

            var filter = renderer.GetComponent<MeshFilter>();
            return filter != null ? filter.sharedMesh : null;
        }

        static string HierarchyPathOf(Transform transform)
        {
            string path = transform.name;
            for (var parent = transform.parent; parent != null; parent = parent.parent)
            {
                path = parent.name + "/" + path;
            }

            return path;
        }

        /// <summary>결과를 콘솔/클립보드용 텍스트로 만든다.</summary>
        public static string BuildReportText(ScanResult scan)
        {
            var builder = new System.Text.StringBuilder();

            builder.AppendLine("=== 머티리얼 슬롯 초과 검사 ===");
            builder.AppendLine($"프리팹 {scan.ScannedPrefabs}개 / 씬 {scan.ScannedScenes}개 / 렌더러 {scan.ScannedRenderers}개 검사");
            if (scan.MissingMesh > 0) builder.AppendLine($"메시가 비어 비교하지 못한 렌더러: {scan.MissingMesh}개");
            builder.AppendLine($"초과 렌더러: {scan.Entries.Count}개");
            builder.AppendLine();

            foreach (var entry in scan.Entries)
            {
                builder.Append($"[{entry.Slots} / 서브메시 {entry.SubMeshes}] {entry.Location} : {entry.HierarchyPath}");
                builder.Append($"  (mesh: {entry.MeshName}, {entry.RendererType})");
                if (entry.EmptySlots > 0) builder.Append($"  빈 슬롯 {entry.EmptySlots}개");
                if (entry.ReadOnly) builder.Append("  [읽기 전용]");
                builder.AppendLine();
            }

            return builder.ToString();
        }
    }
}
