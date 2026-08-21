using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace AssetTools.Editor
{
    /// <summary>오브젝트/프리팹의 콜라이더를 하위 계층까지 모두 제거하는 기능.</summary>
    public static class ColliderRemover
    {
        public struct Options
        {
            public bool Include2D;
            public bool IncludeInactive;

            public static Options Default => new Options { Include2D = true, IncludeInactive = true };
        }

        public class Report
        {
            public int Removed;
            public int Targets;
            public int ModifiedPrefabs;

            /// <summary>폴더 검사로 찾았지만 수정할 수 없어 조용히 건너뛴 프리팹 개수.</summary>
            public int SkippedPrefabs;

            public readonly List<string> Warnings = new List<string>();
        }

        /// <summary>루트와 그 하위의 콜라이더 개수를 센다. (삭제하지 않음)</summary>
        public static int Count(GameObject root, Options options)
        {
            if (root == null) return 0;

            int count = root.GetComponentsInChildren<Collider>(options.IncludeInactive).Length;
            if (options.Include2D) count += root.GetComponentsInChildren<Collider2D>(options.IncludeInactive).Length;
            return count;
        }

        public static int Count(IEnumerable<ToolTarget> targets, Options options)
        {
            int total = 0;
            foreach (var target in targets) total += Count(target.Root, options);
            return total;
        }

        /// <summary>대상별 콜라이더 개수를 한 번에 계산한다. (매 프레임 다시 세지 않도록 캐싱용)</summary>
        public static int[] CountEach(IReadOnlyList<ToolTarget> targets, Options options)
        {
            var counts = new int[targets.Count];
            for (int i = 0; i < targets.Count; i++) counts[i] = Count(targets[i].Root, options);
            return counts;
        }

        /// <summary>선택 대상 전체에서 콜라이더를 제거한다. 씬 오브젝트는 Undo 가 기록된다.</summary>
        public static Report RemoveFrom(IReadOnlyList<ToolTarget> targets, Options options)
        {
            var report = new Report();
            bool showProgress = targets.Count > 8;

            try
            {
                for (int i = 0; i < targets.Count; i++)
                {
                    var target = targets[i];
                    report.Targets++;

                    if (showProgress)
                    {
                        EditorUtility.DisplayProgressBar(
                            "콜라이더 삭제", $"{target.DisplayName} ({i + 1}/{targets.Count})", (float)i / targets.Count);
                    }

                    if (target.IsPrefabAsset) RemoveFromPrefabAsset(target, options, report);
                    else RemoveFromSceneObject(target, options, report);
                }
            }
            finally
            {
                if (showProgress) EditorUtility.ClearProgressBar();
            }

            if (report.ModifiedPrefabs > 0) AssetDatabase.SaveAssets();

            return report;
        }

        static void RemoveFromSceneObject(ToolTarget target, Options options, Report report)
        {
            if (target.Root == null) return;

            foreach (var collider in Collect(target.Root, options))
            {
                if (collider == null) continue;

                if (IsRequiredByOther(collider, out string blocker))
                {
                    report.Warnings.Add($"{Path(collider)} : {blocker} 가 요구하는 컴포넌트라 삭제하지 못했습니다.");
                    continue;
                }

                Undo.DestroyObjectImmediate(collider);
                report.Removed++;
            }
        }

        static void RemoveFromPrefabAsset(ToolTarget target, Options options, Report report)
        {
            var assetType = PrefabUtility.GetPrefabAssetType(target.Root);
            if (assetType == PrefabAssetType.Model)
            {
                // 폴더를 통째로 훑은 경우엔 대상이 많아 경고가 쏟아지므로 조용히 넘어간다.
                if (target.FromFolder) report.SkippedPrefabs++;
                else report.Warnings.Add($"{target.AssetPath} : 모델(FBX 등) 에셋은 수정할 수 없어 건너뜁니다.");
                return;
            }

            if (PrefabUtility.IsPartOfImmutablePrefab(target.Root))
            {
                if (target.FromFolder) report.SkippedPrefabs++;
                else report.Warnings.Add($"{target.AssetPath} : 수정할 수 없는 프리팹이라 건너뜁니다.");
                return;
            }

            // 콜라이더가 없는 프리팹은 굳이 열지 않는다. (폴더 전체 처리 시 대부분이 여기서 걸러진다)
            if (Count(target.Root, options) == 0) return;

            var contents = PrefabUtility.LoadPrefabContents(target.AssetPath);
            try
            {
                int removed = 0;

                foreach (var collider in Collect(contents, options))
                {
                    if (collider == null) continue;

                    if (IsRequiredByOther(collider, out string blocker))
                    {
                        report.Warnings.Add($"{target.AssetPath} / {Path(collider)} : {blocker} 가 요구하는 컴포넌트라 삭제하지 못했습니다.");
                        continue;
                    }

                    Object.DestroyImmediate(collider);
                    removed++;
                }

                if (removed > 0)
                {
                    PrefabUtility.SaveAsPrefabAsset(contents, target.AssetPath, out bool success);
                    if (success)
                    {
                        report.Removed += removed;
                        report.ModifiedPrefabs++;
                    }
                    else
                    {
                        report.Warnings.Add($"{target.AssetPath} : 프리팹 저장에 실패했습니다.");
                    }
                }
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(contents);
            }
        }

        static List<Component> Collect(GameObject root, Options options)
        {
            var buffer = new List<Component>();
            buffer.AddRange(root.GetComponentsInChildren<Collider>(options.IncludeInactive));
            if (options.Include2D) buffer.AddRange(root.GetComponentsInChildren<Collider2D>(options.IncludeInactive));
            return buffer;
        }

        /// <summary>같은 게임오브젝트의 다른 컴포넌트가 [RequireComponent] 로 이 콜라이더를 요구하는지 검사.</summary>
        static bool IsRequiredByOther(Component target, out string blocker)
        {
            var components = target.gameObject.GetComponents<Component>();

            foreach (var other in components)
            {
                if (other == null || other == target) continue;

                foreach (var attribute in other.GetType().GetCustomAttributes(typeof(RequireComponent), true))
                {
                    var require = (RequireComponent)attribute;
                    if (Blocks(require.m_Type0, target, components) ||
                        Blocks(require.m_Type1, target, components) ||
                        Blocks(require.m_Type2, target, components))
                    {
                        blocker = other.GetType().Name;
                        return true;
                    }
                }
            }

            blocker = null;
            return false;
        }

        static bool Blocks(System.Type required, Component target, Component[] components)
        {
            if (required == null || !required.IsInstanceOfType(target)) return false;

            // 요구 조건을 만족하는 컴포넌트가 하나 더 남아 있다면 이건 삭제해도 된다.
            int remaining = 0;
            foreach (var component in components)
            {
                if (component != null && required.IsInstanceOfType(component)) remaining++;
            }

            return remaining <= 1;
        }

        static string Path(Component component)
        {
            var path = component.gameObject.name;
            for (var parent = component.transform.parent; parent != null; parent = parent.parent)
            {
                path = parent.name + "/" + path;
            }

            return $"{path} ({component.GetType().Name})";
        }
    }
}
