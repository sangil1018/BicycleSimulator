using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace AssetTools.Editor
{
    /// <summary>처리 대상 하나. 씬 오브젝트이거나 프로젝트의 프리팹 에셋이다.</summary>
    public readonly struct ToolTarget
    {
        /// <summary>씬 오브젝트, 또는 프리팹 에셋의 루트(읽기 전용 인스턴스).</summary>
        public readonly GameObject Root;

        /// <summary>프리팹 에셋일 때의 경로. 씬 오브젝트면 null.</summary>
        public readonly string AssetPath;

        /// <summary>직접 고른 게 아니라 선택한 폴더를 훑어서 찾아낸 대상인지 여부.</summary>
        public readonly bool FromFolder;

        public bool IsPrefabAsset => !string.IsNullOrEmpty(AssetPath);
        public string DisplayName => IsPrefabAsset ? AssetPath : Root.name;

        public ToolTarget(GameObject root, string assetPath, bool fromFolder = false)
        {
            Root = root;
            AssetPath = assetPath;
            FromFolder = fromFolder;
        }
    }

    /// <summary>선택 해석 결과. 대상 목록과 폴더 관련 부가 정보를 함께 담는다.</summary>
    public class SelectionResult
    {
        public readonly List<ToolTarget> Targets = new List<ToolTarget>();

        /// <summary>선택된 폴더 개수. (옵션이 꺼져 있어도 집계한다)</summary>
        public int SelectedFolders;

        /// <summary>폴더를 훑어서 찾아낸 프리팹 개수.</summary>
        public int PrefabsFromFolders;

        /// <summary>폴더를 선택했지만 옵션이 꺼져 있어 무시된 상태.</summary>
        public bool HasIgnoredFolders;

        public int Count => Targets.Count;
    }

    /// <summary>선택 항목을 처리 대상 목록으로 변환하는 공용 헬퍼.</summary>
    public static class AssetToolsSelection
    {
        /// <param name="includeFoldersRecursively">폴더를 선택한 경우 하위 폴더까지 훑어 프리팹을 모두 포함할지 여부.</param>
        public static SelectionResult Resolve(bool includeFoldersRecursively)
        {
            var result = new SelectionResult();
            var seenPaths = new HashSet<string>();
            var sceneRoots = new List<GameObject>();
            var folders = new List<string>();

            foreach (var obj in Selection.objects)
            {
                if (obj == null) continue;

                if (obj is GameObject go)
                {
                    if (PrefabUtility.IsPartOfPrefabAsset(go))
                    {
                        // 프리팹 에셋의 하위 오브젝트를 골랐어도 루트 프리팹 전체를 대상으로 삼는다.
                        string path = AssetDatabase.GetAssetPath(go);
                        if (!string.IsNullOrEmpty(path) && seenPaths.Add(path))
                        {
                            var root = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                            if (root != null) result.Targets.Add(new ToolTarget(root, path));
                        }
                    }
                    else
                    {
                        sceneRoots.Add(go);
                    }

                    continue;
                }

                string assetPath = AssetDatabase.GetAssetPath(obj);
                if (!string.IsNullOrEmpty(assetPath) && AssetDatabase.IsValidFolder(assetPath))
                {
                    result.SelectedFolders++;
                    folders.Add(assetPath);
                }
            }

            if (folders.Count > 0)
            {
                if (includeFoldersRecursively) CollectFromFolders(folders, seenPaths, result);
                else result.HasIgnoredFolders = true;
            }

            // 부모가 함께 선택된 씬 오브젝트는 중복 처리되므로 제외한다.
            foreach (var go in sceneRoots)
            {
                if (HasSelectedAncestor(go, sceneRoots)) continue;
                result.Targets.Add(new ToolTarget(go, null));
            }

            return result;
        }

        /// <summary>선택한 폴더(하위 폴더 포함)의 프리팹을 모두 대상에 추가한다.</summary>
        static void CollectFromFolders(List<string> folders, HashSet<string> seenPaths, SelectionResult result)
        {
            // 폴더를 중첩해서 골랐을 때 같은 프리팹을 두 번 훑지 않도록 상위 폴더만 남긴다.
            var roots = TopLevelFolders(folders);
            var guids = AssetDatabase.FindAssets("t:Prefab", roots.ToArray());

            try
            {
                for (int i = 0; i < guids.Length; i++)
                {
                    if (guids.Length > 32 && (i & 15) == 0)
                    {
                        EditorUtility.DisplayProgressBar(
                            "폴더 검사", $"프리팹을 읽는 중… ({i + 1}/{guids.Length})", (float)i / guids.Length);
                    }

                    string path = AssetDatabase.GUIDToAssetPath(guids[i]);

                    // t:Prefab 은 FBX 같은 모델 에셋도 함께 잡아내므로 실제 프리팹 파일만 남긴다.
                    if (!path.EndsWith(".prefab", System.StringComparison.OrdinalIgnoreCase)) continue;
                    if (!seenPaths.Add(path)) continue;

                    var root = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                    if (root == null) continue;

                    result.Targets.Add(new ToolTarget(root, path, true));
                    result.PrefabsFromFolders++;
                }
            }
            finally
            {
                if (guids.Length > 32) EditorUtility.ClearProgressBar();
            }
        }

        /// <summary>서로 포함 관계인 폴더 중 최상위만 남긴다.</summary>
        static List<string> TopLevelFolders(List<string> folders)
        {
            var roots = new List<string>();

            foreach (var folder in folders)
            {
                bool nested = false;
                foreach (var other in folders)
                {
                    if (other == folder) continue;
                    if (folder.StartsWith(other + "/", System.StringComparison.Ordinal))
                    {
                        nested = true;
                        break;
                    }
                }

                if (!nested && !roots.Contains(folder)) roots.Add(folder);
            }

            return roots;
        }

        static bool HasSelectedAncestor(GameObject go, List<GameObject> all)
        {
            for (var parent = go.transform.parent; parent != null; parent = parent.parent)
            {
                if (all.Contains(parent.gameObject)) return true;
            }

            return false;
        }
    }
}
