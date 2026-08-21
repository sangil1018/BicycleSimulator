using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace AssetTools.Editor
{
    /// <summary>
    /// Contribute GI(Static Flag) / Receive GI / Light Probes / Cast Shadows 조합을
    /// 대상의 하위 Renderer 전체에 일괄 적용하는 기능.
    /// </summary>
    public static class LightingPresetApplier
    {
        /// <summary>버튼 하나에 대응하는 프리셋 정의.</summary>
        public struct Preset
        {
            public string Title;
            public string Description;
            public bool ContributeGI;
            public ReceiveGI ReceiveGI;

            /// <summary>
            /// null 이면 Light Probes 모드를 건드리지 않는다.
            /// Receive GI 가 Light Probes 여도 이 값이 Off 면 프로브(APV)를 아예 샘플링하지 않아
            /// 오브젝트가 새까맣게 나오므로, 프로브 프리셋에서는 반드시 Blend Probes 로 맞춰준다.
            /// </summary>
            public LightProbeUsage? LightProbes;

            /// <summary>null 이면 Cast Shadows 를 건드리지 않는다.</summary>
            public bool? CastShadows;
        }

        public static readonly Preset[] Presets =
        {
            new Preset
            {
                Title = "1. 대형 정적 구조물",
                Description = "지형, 건물, 도로 등 두께가 얇거나 넓은 면\nCast Shadows: 체크 / Contribute GI: 체크 / Receive GI: Lightmaps",
                ContributeGI = true,
                ReceiveGI = ReceiveGI.Lightmaps,
                CastShadows = true,
            },
            new Preset
            {
                Title = "2. 중소형 프롭 및 소품",
                Description = "나무, 작은 프롭 등 부피가 있는 것\n" +
                              "Cast Shadows: 체크 / Contribute GI: 체크 / Receive GI: Light Probes / Light Probes: Blend Probes",
                ContributeGI = true,
                ReceiveGI = ReceiveGI.LightProbes,
                LightProbes = LightProbeUsage.BlendProbes,
                CastShadows = true,
            },
            new Preset
            {
                Title = "3. 동적 오브젝트",
                Description = "플레이어, 움직이는 것들\n" +
                              "Cast Shadows: 체크 / Contribute GI: 해제 / Receive GI: Light Probes / Light Probes: Blend Probes",
                ContributeGI = false,
                ReceiveGI = ReceiveGI.LightProbes,
                LightProbes = LightProbeUsage.BlendProbes,
                CastShadows = true,
            },
            new Preset
            {
                Title = "4. 그림자 제거 구조물",
                Description = "구조물, 큰 오브젝트 등\nCast Shadows: 해제 / Contribute GI: 체크 / Receive GI: Lightmaps",
                ContributeGI = true,
                ReceiveGI = ReceiveGI.Lightmaps,
                CastShadows = false,
            },
        };

        public class Report
        {
            public int Targets;
            public int Renderers;
            public int ChangedFlags;
            public int ChangedGI;
            public int ChangedProbes;
            public int ChangedShadows;
            public int ModifiedPrefabs;

            /// <summary>폴더 검사로 찾았지만 수정할 수 없어 조용히 건너뛴 프리팹 개수.</summary>
            public int SkippedPrefabs;

            public readonly List<string> Warnings = new List<string>();

            public int TotalChanges => ChangedFlags + ChangedGI + ChangedProbes + ChangedShadows;
        }

        /// <summary>루트와 그 하위의 Renderer 개수를 센다. (비활성 포함)</summary>
        public static int CountRenderers(GameObject root) =>
            root == null ? 0 : root.GetComponentsInChildren<Renderer>(true).Length;

        /// <summary>대상별 Renderer 개수를 한 번에 계산한다. (매 프레임 다시 세지 않도록 캐싱용)</summary>
        public static int[] CountEach(IReadOnlyList<ToolTarget> targets)
        {
            var counts = new int[targets.Count];
            for (int i = 0; i < targets.Count; i++) counts[i] = CountRenderers(targets[i].Root);
            return counts;
        }

        /// <summary>선택 대상 전체에 프리셋을 적용한다. 씬 오브젝트는 Undo 가 기록된다.</summary>
        public static Report Apply(IReadOnlyList<ToolTarget> targets, Preset preset)
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
                            "라이팅 프리셋 적용",
                            $"{target.DisplayName} ({i + 1}/{targets.Count})",
                            (float)i / targets.Count);
                    }

                    if (target.IsPrefabAsset) ApplyToPrefabAsset(target, preset, report);
                    else ApplyToSceneObject(target, preset, report);
                }
            }
            finally
            {
                if (showProgress) EditorUtility.ClearProgressBar();
            }

            if (report.ModifiedPrefabs > 0) AssetDatabase.SaveAssets();

            return report;
        }

        static void ApplyToSceneObject(ToolTarget target, Preset preset, Report report)
        {
            if (target.Root == null) return;

            // 비활성 오브젝트도 포함해서 하위 전체를 순회한다.
            foreach (var renderer in target.Root.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer == null) continue;
                report.Renderers++;

                var go = renderer.gameObject;
                var flags = GameObjectUtility.GetStaticEditorFlags(go);
                var newFlags = WithContributeGI(flags, preset.ContributeGI);

                if (newFlags != flags)
                {
                    Undo.RecordObject(go, "Set Lighting Preset");
                    GameObjectUtility.SetStaticEditorFlags(go, newFlags);
                    EditorUtility.SetDirty(go);
                    report.ChangedFlags++;
                }

                // receiveGI 는 MeshRenderer 에만 있다. SkinnedMeshRenderer 등은 나머지 항목만 적용된다.
                if (renderer is MeshRenderer meshRenderer && meshRenderer.receiveGI != preset.ReceiveGI)
                {
                    Undo.RecordObject(meshRenderer, "Set Lighting Preset");
                    meshRenderer.receiveGI = preset.ReceiveGI;
                    EditorUtility.SetDirty(meshRenderer);
                    report.ChangedGI++;
                }

                // lightProbeUsage 는 모든 Renderer 에 있다. (SkinnedMeshRenderer 포함)
                if (preset.LightProbes.HasValue && renderer.lightProbeUsage != preset.LightProbes.Value)
                {
                    Undo.RecordObject(renderer, "Set Lighting Preset");
                    renderer.lightProbeUsage = preset.LightProbes.Value;
                    EditorUtility.SetDirty(renderer);
                    report.ChangedProbes++;
                }

                if (preset.CastShadows.HasValue)
                {
                    var mode = preset.CastShadows.Value ? ShadowCastingMode.On : ShadowCastingMode.Off;
                    if (renderer.shadowCastingMode != mode)
                    {
                        Undo.RecordObject(renderer, "Set Lighting Preset");
                        renderer.shadowCastingMode = mode;
                        EditorUtility.SetDirty(renderer);
                        report.ChangedShadows++;
                    }
                }
            }
        }

        static void ApplyToPrefabAsset(ToolTarget target, Preset preset, Report report)
        {
            if (PrefabUtility.GetPrefabAssetType(target.Root) == PrefabAssetType.Model)
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

            // Renderer 가 없는 프리팹은 굳이 열지 않는다. (폴더 전체 처리 시 대부분이 여기서 걸러진다)
            if (CountRenderers(target.Root) == 0) return;

            var contents = PrefabUtility.LoadPrefabContents(target.AssetPath);
            try
            {
                int changed = 0;

                foreach (var renderer in contents.GetComponentsInChildren<Renderer>(true))
                {
                    if (renderer == null) continue;
                    report.Renderers++;

                    var go = renderer.gameObject;
                    var flags = GameObjectUtility.GetStaticEditorFlags(go);
                    var newFlags = WithContributeGI(flags, preset.ContributeGI);

                    if (newFlags != flags)
                    {
                        GameObjectUtility.SetStaticEditorFlags(go, newFlags);
                        report.ChangedFlags++;
                        changed++;
                    }

                    if (renderer is MeshRenderer meshRenderer && meshRenderer.receiveGI != preset.ReceiveGI)
                    {
                        meshRenderer.receiveGI = preset.ReceiveGI;
                        report.ChangedGI++;
                        changed++;
                    }

                    if (preset.LightProbes.HasValue && renderer.lightProbeUsage != preset.LightProbes.Value)
                    {
                        renderer.lightProbeUsage = preset.LightProbes.Value;
                        report.ChangedProbes++;
                        changed++;
                    }

                    if (preset.CastShadows.HasValue)
                    {
                        var mode = preset.CastShadows.Value ? ShadowCastingMode.On : ShadowCastingMode.Off;
                        if (renderer.shadowCastingMode != mode)
                        {
                            renderer.shadowCastingMode = mode;
                            report.ChangedShadows++;
                            changed++;
                        }
                    }
                }

                if (changed > 0)
                {
                    PrefabUtility.SaveAsPrefabAsset(contents, target.AssetPath, out bool success);
                    if (success) report.ModifiedPrefabs++;
                    else report.Warnings.Add($"{target.AssetPath} : 프리팹 저장에 실패했습니다.");
                }
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(contents);
            }
        }

        static StaticEditorFlags WithContributeGI(StaticEditorFlags flags, bool contribute) =>
            contribute ? flags | StaticEditorFlags.ContributeGI : flags & ~StaticEditorFlags.ContributeGI;
    }
}
