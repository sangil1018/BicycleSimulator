// =============================================================================
//  URPRealismSetupWindow.cs
//  Unity 6.3 (6000.3) / URP 17.x 전용
//  "극사실주의 그래픽 세팅 & 최적화 가이드" 에디터 도구
//
//  설치: 이 파일을 프로젝트의  Assets/Editor/  폴더 안에 두세요.
//  실행: 상단 메뉴  Tools > URP 극사실주의 가이드
//
//  각 항목은 다음을 표시합니다:
//    · 중요도 뱃지 (필수 / 권장 / 선택)
//    · 유니티 6.3 기준 정확한 메뉴 경로
//    · 추천 설정값 (Recommended)
//    · [열기] 버튼 → 해당 설정/에셋을 인스펙터·설정창에 즉시 표시
//    · [문서] 버튼 → 공식 매뉴얼
// =============================================================================

using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace FluxionTools
{
    public class URPRealismSetupWindow : EditorWindow
    {
        // ------------------------------------------------------------------
        //  모델
        // ------------------------------------------------------------------
        private enum Prio { Must, Rec, Opt }

        private class Item
        {
            public string Title;       // 항목명
            public string Version;     // 버전 뱃지
            public Prio Priority;      // 중요도
            public string MenuPath;    // 유니티 6.3 정확한 메뉴 경로
            public string Recommend;   // 추천 설정값
            public string Desc;        // 설명 / 주의사항
            public string DocUrl;      // 공식 문서
            public Action OnOpen;      // [열기] 동작
            public bool Expanded;      // 접기/펼치기
        }

        private class Category { public string Name; public List<Item> Items = new List<Item>(); }
        private class Tab { public string Name; public List<Category> Categories = new List<Category>(); }

        private readonly List<Tab> _tabs = new List<Tab>();
        private int _tabIndex;
        private Vector2 _scroll;
        private string _search = "";
        private bool _hideOptional;

        // 스타일
        private GUIStyle _titleStyle, _descStyle, _tagStyle, _catStyle, _pathStyle, _recStyle;
        private bool _stylesReady;

        private const string DocRoot = "https://docs.unity3d.com/6000.3/Documentation/Manual/";

        [MenuItem("Tools/URP 극사실주의 가이드")]
        public static void Open()
        {
            var w = GetWindow<URPRealismSetupWindow>("URP 극사실주의 가이드");
            w.minSize = new Vector2(520, 560);
        }

        private void OnEnable() => BuildData();

        // ==================================================================
        //  데이터: 가이드 5개 탭
        // ==================================================================
        private void BuildData()
        {
            _tabs.Clear();

            // =============== TAB 1. 전역 설정 ===============
            var t1 = new Tab { Name = "1. 전역" };

            var c1a = new Category { Name = "프로젝트 전역" };
            c1a.Items.Add(new Item
            {
                Title = "Color Space = Linear",
                Priority = Prio.Must,
                MenuPath = "Edit > Project Settings > Player > Other Settings > Rendering",
                Recommend = "Color Space: Linear   (Auto Graphics API 해제 후 DX12 우선 권장)",
                Desc = "감마 공간에서는 빛의 감쇠·혼합이 물리적으로 틀리게 계산됩니다. PBR의 대전제라 이것부터 확인. " +
                       "이미 진행 중인 프로젝트라면 변경 시 전체 룩이 바뀌므로 조명 재조정이 필요합니다.",
                DocUrl = DocRoot + "linear-rendering-overview.html",
                OnOpen = () => OpenSettings("Project/Player")
            });
            c1a.Items.Add(new Item
            {
                Title = "Render Graph (Compatibility Mode 해제)",
                Version = "6.3 기본 활성",
                Priority = Prio.Must,
                MenuPath = "Edit > Project Settings > Graphics > Pipeline Specific Settings > URP",
                Recommend = "Compatibility Mode (Render Graph Disabled): 해제 상태 유지",
                Desc = "Unity 6.3부터 Compatibility Mode는 URP_COMPATIBILITY_MODE 컴파일 디파인 뒤로 숨겨져 사실상 Render Graph가 전제입니다. " +
                       "구버전에서 올라온 프로젝트나 서드파티 Renderer Feature가 회색 화면을 내면 해당 에셋의 Render Graph 대응 버전을 확인하세요.",
                DocUrl = DocRoot + "urp/render-graph.html",
                OnOpen = () => OpenSettings("Project/Graphics")
            });
            c1a.Items.Add(new Item
            {
                Title = "Default Render Pipeline 지정 확인",
                Priority = Prio.Must,
                MenuPath = "Edit > Project Settings > Graphics > Default Render Pipeline",
                Recommend = "고품질 URP Asset 지정. 품질 레벨별로 다르게 쓰려면 Quality 탭에서 레벨마다 override",
                Desc = "Quality 레벨에 URP Asset이 지정돼 있으면 그것이 우선하고, 비어 있으면 Graphics의 Default가 쓰입니다. " +
                       "설치물은 품질 레벨을 하나만 남겨두면 현장에서 엉뚱한 레벨로 실행되는 사고를 막을 수 있습니다.",
                DocUrl = DocRoot + "srp-setting-render-pipeline-asset.html",
                OnOpen = () => OpenSettings("Project/Graphics")
            });

            var c1b = new Category { Name = "URP Asset / Renderer" };
            c1b.Items.Add(new Item
            {
                Title = "URP Asset — Quality (HDR · Render Scale · Upscaling)",
                Priority = Prio.Must,
                MenuPath = "URP Asset 선택 > Inspector > Quality",
                Recommend = "HDR: 켬 / HDR Precision: 64 Bit / Render Scale: 1.0 (부하 시 0.8~0.9) / " +
                            "Upscaling Filter: Spatial-Temporal Post-Processing (STP) / Anti Aliasing(MSAA): Disabled",
                Desc = "HDR은 Bloom·ACES 톤매핑의 전제조건입니다. 64 Bit Precision은 밝은 하이라이트의 계단 현상을 줄여줍니다. " +
                       "STP·TAA 같은 시간 누적 AA를 쓸 때 MSAA는 비용만 늘고 이점이 적으니 끄는 편이 낫습니다.",
                DocUrl = DocRoot + "urp/universalrp-asset.html",
                OnOpen = SelectURPAsset
            });
            c1b.Items.Add(new Item
            {
                Title = "URP Asset — Lighting (Main/Additional Light · Probe)",
                Priority = Prio.Must,
                MenuPath = "URP Asset 선택 > Inspector > Lighting",
                Recommend = "Main Light: Per Pixel + Cast Shadows / Additional Lights: Per Pixel (Forward+에선 개수 제한 없음) / " +
                            "Light Probe System: Probe Volumes(APV) / Reflection Probe Blending·Box Projection: 켬",
                Desc = "Forward+에서는 Additional Lights의 개수 상한이 사라져 다중 광원이 자유롭습니다. " +
                       "Reflection Probe Blending을 켜야 프로브 경계에서 반사가 튀지 않습니다.",
                DocUrl = DocRoot + "urp/universalrp-asset.html",
                OnOpen = SelectURPAsset
            });
            c1b.Items.Add(new Item
            {
                Title = "URP Asset — Shadows",
                Priority = Prio.Rec,
                MenuPath = "URP Asset 선택 > Inspector > Shadows",
                Recommend = "Max Distance: 씬에 필요한 최소값(실내 30~50m) / Cascade Count: 4 / " +
                            "Depth Bias 1.0·Normal Bias 1.0에서 미세 조정 / Soft Shadows: 켬, Quality: High / Shadow Resolution: 4096",
                Desc = "Max Distance는 화질과 비용을 동시에 좌우하는 최대 변수입니다. 무작정 늘리지 말고 카메라가 실제로 닿는 범위로 조이세요. " +
                       "그림자 끝단이 끊겨 보이면 Distance를, 표면에 줄무늬(아크네)가 생기면 Bias를 조정합니다.",
                DocUrl = DocRoot + "urp/universalrp-asset.html",
                OnOpen = SelectURPAsset
            });
            c1b.Items.Add(new Item
            {
                Title = "Universal Renderer — Rendering Path = Forward+",
                Version = "2022.2+",
                Priority = Prio.Must,
                MenuPath = "URP Asset > Renderer List에서 Renderer 더블클릭 > Inspector > Rendering",
                Recommend = "Rendering Path: Forward+ / Depth Priming Mode: Auto / Intermediate Texture: Auto",
                Desc = "Forward+는 오브젝트당 조명 제한(4~8개)을 없애고, GPU Resident Drawer의 전제 조건이기도 합니다. " +
                       "Deferred+도 GPU Resident Drawer와 호환되지만 반투명·머티리얼 다양성에서 Forward+가 무난합니다.",
                DocUrl = DocRoot + "urp/urp-universal-renderer.html",
                OnOpen = SelectRendererData
            });
            c1b.Items.Add(new Item
            {
                Title = "Universal Renderer — Depth / Opaque Texture",
                Priority = Prio.Must,
                MenuPath = "URP Asset > Inspector > Rendering  (Depth Texture / Opaque Texture)",
                Recommend = "Depth Texture: 켬 / Opaque Texture: 켬 (굴절·반투명 효과 필요 시) / Opaque Downsampling: None",
                Desc = "SSAO·SSR·포그 등 화면 공간 효과가 깊이 버퍼를 요구합니다. Depth Texture가 꺼져 있으면 효과가 조용히 동작하지 않으니 먼저 확인하세요. " +
                       "SSAO의 Depth Normals 소스를 쓰면 노멀 프리패스가 추가로 필요합니다.",
                DocUrl = DocRoot + "urp/universalrp-asset.html",
                OnOpen = SelectURPAsset
            });

            t1.Categories.Add(c1a); t1.Categories.Add(c1b);
            _tabs.Add(t1);

            // =============== TAB 2. 라이팅 & GI ===============
            var t2 = new Tab { Name = "2. 라이팅·GI" };

            var c2a = new Category { Name = "전역 조명 (GI)" };
            c2a.Items.Add(new Item
            {
                Title = "APV (Adaptive Probe Volumes)",
                Version = "2022.2+",
                Priority = Prio.Must,
                MenuPath = "① Project Settings > Graphics > Pipeline Specific Settings > URP > Light Probe System = Probe Volumes\n" +
                           "② GameObject > Light > Adaptive Probe Volume\n" +
                           "③ Window > Rendering > Lighting > Adaptive Probe Volumes 탭 > Bake",
                Recommend = "Global 방식으로 씬 전체 감싸기 / Min Probe Spacing 0.5~1m, Max 8~16m / " +
                            "Normal Bias 0.33 / 실내외 혼합 씬이면 Sky Occlusion 켬",
                Desc = "간접광을 지오메트리 밀도에 맞춰 적응적으로 배치합니다. Probe Spacing을 너무 촘촘히 하면 베이크 시간과 메모리가 급증하니 " +
                       "빛 변화가 큰 구역만 Probe Adjustment Volume으로 국소 보강하는 편이 효율적입니다.",
                DocUrl = DocRoot + "urp/probevolumes.html",
                OnOpen = () => ExecuteMenu("Window/Rendering/Lighting")
            });
            c2a.Items.Add(new Item
            {
                Title = "베이크 GI (Progressive GPU Lightmapper)",
                Priority = Prio.Rec,
                MenuPath = "Window > Rendering > Lighting > Scene 탭 > Lightmapping Settings",
                Recommend = "Lightmapper: Progressive GPU / Lightmap Resolution 20~40 texels(히어로 60+) / " +
                            "Max Lightmap Size 2048~4096 / Direct·Indirect Samples 64 / 512 / Denoiser: Optix / Compress Lightmaps: 해제",
                Desc = "정적 설치물이라면 실시간 GI보다 베이크가 화질·프레임 모두 압도적으로 유리합니다. " +
                       "대상 오브젝트는 Inspector 우상단 Static 드롭다운에서 Contribute GI를 켜야 라이트맵에 포함됩니다. " +
                       "Compress를 끄면 용량은 늘지만 그라데이션 밴딩이 사라집니다.",
                DocUrl = DocRoot + "progressive-lightmapper.html",
                OnOpen = () => ExecuteMenu("Window/Rendering/Lighting")
            });
            c2a.Items.Add(new Item
            {
                Title = "Mixed Lighting 모드",
                Priority = Prio.Rec,
                MenuPath = "Window > Rendering > Lighting > Scene 탭 > Mixed Lighting\n(라이트별 모드는 Light 컴포넌트 > Mode)",
                Recommend = "Lighting Mode: Shadowmask / 배경 조명은 Light Mode를 Mixed, 인터랙션 반응광만 Realtime",
                Desc = "Shadowmask는 정적 그림자를 텍스처로 굽고 동적 오브젝트만 실시간 그림자를 받게 해 실시간 그림자 개수를 크게 줄입니다. " +
                       "Baked Indirect는 그림자가 전부 실시간이라 더 무겁지만 동적 변화에 자연스럽습니다. 설치물은 Shadowmask 권장.",
                DocUrl = DocRoot + "LightModes.html",
                OnOpen = () => ExecuteMenu("Window/Rendering/Lighting")
            });
            c2a.Items.Add(new Item
            {
                Title = "Area Light (베이크 전용)",
                Priority = Prio.Opt,
                MenuPath = "GameObject > Light > Area Light  →  Inspector > Mode = Baked",
                Recommend = "Mode: Baked 고정 / 창문·간접 조명·소프트박스 연출에 사용 / 실시간 필요 시 Spot+쿠키로 근사",
                Desc = "URP에서 Area Light는 실시간으로 동작하지 않습니다(베이크에만 기여). " +
                       "실시간처럼 켜두면 씬 뷰에서만 보이고 플레이 모드에서 사라져 혼란스러우니 Mode를 반드시 Baked로 두세요.",
                DocUrl = DocRoot + "urp/light-component.html",
                OnOpen = () => ExecuteMenu("Window/Rendering/Lighting")
            });

            var c2b = new Category { Name = "화면 공간 효과 (Renderer Feature)" };
            c2b.Items.Add(new Item
            {
                Title = "SSAO (Screen Space Ambient Occlusion)",
                Priority = Prio.Must,
                MenuPath = "Renderer(Universal Renderer Data) > Inspector > Add Renderer Feature > Screen Space Ambient Occlusion",
                Recommend = "Method: Interleaved Gradient / Source: Depth Normals / Intensity 0.5~1.0 / " +
                            "Radius 0.25~0.5 / Direct Lighting Strength 0.15~0.25 / Samples: Medium / Blur Quality: High",
                Desc = "접촉면 그림자로 물체가 바닥에 '앉아 있는' 느낌을 만듭니다. Direct Lighting Strength를 높이면 직사광까지 어두워져 " +
                       "때가 낀 듯 인위적인 느낌이 나므로 낮게 유지하는 것이 사실감의 핵심입니다.",
                DocUrl = DocRoot + "urp/post-processing-ssao.html",
                OnOpen = SelectRendererData
            });
            c2b.Items.Add(new Item
            {
                Title = "SSR (Screen Space Reflections)",
                Version = "2023.2+",
                Priority = Prio.Rec,
                MenuPath = "Renderer > Add Renderer Feature > Screen Space Reflections\n(세부값은 Volume에 Screen Space Reflection override 추가)",
                Recommend = "Resolution: Half / Max Steps 32~64 / Thickness 0.05~0.1 / " +
                            "젖은 바닥·유리·금속 등 Smoothness 높은 표면 위주",
                Desc = "화면 밖 정보는 반사할 수 없어 화면 경계에서 반사가 사라집니다. Reflection Probe를 밑에 깔고 SSR을 그 위에 얹는 " +
                       "조합이 안정적입니다. 프레임이 부족하면 가장 먼저 Half Resolution 또는 해제를 검토할 항목.",
                DocUrl = DocRoot + "urp/post-processing-ssr.html",
                OnOpen = SelectRendererData
            });
            c2b.Items.Add(new Item
            {
                Title = "Decal Renderer Feature",
                Priority = Prio.Opt,
                MenuPath = "Renderer > Add Renderer Feature > Decal",
                Recommend = "Technique: Automatic / 누수·얼룩·마모 표현으로 표면 반복감 제거",
                Desc = "같은 텍스처가 반복되는 바닥·벽에 데칼로 불규칙성을 주면 사실감이 크게 올라갑니다. " +
                       "극사실주의에서 가장 저렴하게 '실제로 쓰인 공간' 느낌을 내는 방법입니다.",
                DocUrl = DocRoot + "urp/renderer-feature-decal.html",
                OnOpen = SelectRendererData
            });

            var c2c = new Category { Name = "공기감 (볼류메트릭)" };
            c2c.Items.Add(new Item
            {
                Title = "볼류메트릭 포그 — URP 네이티브 미지원",
                Priority = Prio.Rec,
                MenuPath = "(내장 없음) 서드파티 / 오픈소스 도입 필요",
                Recommend = "HAZE (Alisavakis, 유료·아트컨트롤 풍부) / AERO (Mirza Beig, 무료·경량) / " +
                            "Unity-URP-Volumetric-Light (오픈소스, Render Graph·APV 호환)",
                Desc = "URP는 Unity 6 기준으로도 볼류메트릭 포그가 내장되어 있지 않습니다. 실내 빛줄기(God Ray)는 극사실 분위기의 핵심이라 " +
                       "SSR처럼 외부 솔루션으로 메워야 합니다. 도입 시 반드시 Render Graph 대응 버전인지 확인하세요.",
                DocUrl = "https://github.com/CristianQiu/Unity-URP-Volumetric-Light",
                OnOpen = () => Application.OpenURL("https://github.com/CristianQiu/Unity-URP-Volumetric-Light")
            });
            c2c.Items.Add(new Item
            {
                Title = "기본 Fog + Height Fog (경량 대안)",
                Priority = Prio.Opt,
                MenuPath = "Window > Rendering > Lighting > Environment 탭 > Fog",
                Recommend = "Mode: Exponential Squared / Density 0.005~0.02 / 색은 하늘·주광원 색과 맞추기",
                Desc = "볼류메트릭까지 갈 예산이 없을 때 원경 공기 원근만이라도 확보하는 방법입니다. " +
                       "포그 색을 스카이박스와 어긋나게 두면 원경이 붕 떠 보이니 색 매칭이 중요합니다.",
                DocUrl = DocRoot + "urp/lighting/environment-lighting.html",
                OnOpen = () => ExecuteMenu("Window/Rendering/Lighting")
            });

            t2.Categories.Add(c2a); t2.Categories.Add(c2b); t2.Categories.Add(c2c);
            _tabs.Add(t2);

            // =============== TAB 3. 머티리얼 & 텍스처 ===============
            var t3 = new Tab { Name = "3. 머티리얼" };

            var c3a = new Category { Name = "텍스처 메모리 / 품질" };
            c3a.Items.Add(new Item
            {
                Title = "Texture Streaming (Mipmap Streaming)",
                Priority = Prio.Must,
                MenuPath = "Edit > Project Settings > Quality > Textures > Texture Streaming",
                Recommend = "Texture Streaming: 켬 / Memory Budget: VRAM의 50~60% (12GB 기준 4096~6144MB) / " +
                            "Max Level Reduction 2 / 텍스처별 Import Settings에서 Streaming Mip Maps 체크",
                Desc = "4K 텍스처를 다수 쓰는 극사실 씬에서 VRAM 초과를 막는 안전장치입니다. Budget이 너무 낮으면 카메라 이동 시 " +
                       "텍스처가 흐릿하게 늦게 로드되는 팝핑이 보이니 현장 GPU 기준으로 여유 있게 잡으세요.",
                DocUrl = DocRoot + "TextureStreaming.html",
                OnOpen = () => OpenSettings("Project/Quality")
            });
            c3a.Items.Add(new Item
            {
                Title = "Anisotropic Filtering",
                Priority = Prio.Rec,
                MenuPath = "Edit > Project Settings > Quality > Rendering > Anisotropic Textures",
                Recommend = "Anisotropic Textures: Forced On / 개별 텍스처는 Aniso Level 4~8 (바닥·벽 등 비스듬한 표면)",
                Desc = "바닥처럼 시선과 각도가 큰 표면의 원경이 뭉개지는 것을 방지합니다. 비용 대비 체감 화질 개선이 커서 " +
                       "데스크톱 타깃이면 Forced On으로 두는 편이 편합니다.",
                DocUrl = DocRoot + "class-QualitySettings.html",
                OnOpen = () => OpenSettings("Project/Quality")
            });
            c3a.Items.Add(new Item
            {
                Title = "텍스처 압축 포맷 (BC7 / BC5)",
                Priority = Prio.Opt,
                MenuPath = "텍스처 선택 > Inspector(Import Settings) > Windows 플랫폼 탭 > Format",
                Recommend = "Albedo·Mask: BC7 / Normal Map: BC5 (Texture Type을 Normal map으로) / " +
                            "Max Size 2048~4096 / sRGB는 컬러만 켜고 Mask·Roughness는 해제",
                Desc = "노멀맵을 일반 컬러 압축(BC1)으로 두면 굴곡이 계단처럼 깨집니다. " +
                       "Metallic/Smoothness 같은 데이터 텍스처에 sRGB가 켜져 있으면 값이 왜곡되니 반드시 해제하세요.",
                DocUrl = DocRoot + "class-TextureImporterOverride.html",
                OnOpen = () => Info("텍스처 압축 설정",
                    "텍스처는 임포터 단위 설정이라 전역 창이 없습니다.\n\n" +
                    "1. Project 창에서 텍스처 선택\n" +
                    "2. Inspector 하단 플랫폼 탭에서 Windows 선택\n" +
                    "3. Override 체크 후 Format 지정\n" +
                    "   · 컬러/마스크 → BC7\n" +
                    "   · 노멀맵 → Texture Type을 Normal map으로 바꾸면 BC5 자동\n\n" +
                    "여러 텍스처를 한 번에 선택하면 일괄 적용됩니다.")
            });

            var c3b = new Category { Name = "PBR 머티리얼 (Lit 셰이더)" };
            c3b.Items.Add(new Item
            {
                Title = "Metallic / Smoothness 채널 정합",
                Priority = Prio.Must,
                MenuPath = "머티리얼 선택 > Inspector > Surface Inputs  (Shader: Universal Render Pipeline/Lit)",
                Recommend = "Workflow Mode: Metallic / 금속 1.0·비금속 0.0 (중간값은 혼합 표면만) / " +
                            "Smoothness Source: Metallic Alpha / 매트 0.1~0.3, 광택 목재 0.5~0.7, 젖은 표면·유리 0.85+",
                Desc = "알베도에 조명이나 AO가 이미 구워진 텍스처는 이중 음영을 만들어 CG 티의 주범이 됩니다. " +
                       "금속은 알베도가 반사색을 담당하고 확산광이 거의 없다는 PBR 규칙을 지켜야 조명 반응이 자연스럽습니다.",
                DocUrl = DocRoot + "urp/lit-shader.html",
                OnOpen = () => Info("머티리얼 PBR 설정",
                    "머티리얼별 설정이라 전역 창이 없습니다.\n\n" +
                    "Project 창에서 머티리얼 선택 → Inspector > Surface Inputs\n\n" +
                    "체크리스트\n" +
                    "· Workflow Mode: Metallic\n" +
                    "· 금속/비금속 값을 극단으로 (0 또는 1)\n" +
                    "· 알베도에 그림자·AO가 구워져 있지 않은지 확인\n" +
                    "· Smoothness 소스가 의도한 채널인지 확인")
            });
            c3b.Items.Add(new Item
            {
                Title = "Normal Map 강도",
                Priority = Prio.Rec,
                MenuPath = "머티리얼 > Inspector > Surface Inputs > Normal Map (우측 강도 필드)",
                Recommend = "Strength 0.8~1.0 유지 (1.5 이상은 실루엣과 어긋나 가짜 티) / 큰 요철은 지오메트리, 미세 굴곡만 노멀",
                Desc = "노멀맵을 과하게 올리면 그림자 방향과 실제 실루엣이 충돌해 오히려 평평해 보입니다. " +
                       "강도를 올리고 싶어지면 대개 텍스처 자체나 지오메트리 문제입니다.",
                DocUrl = DocRoot + "urp/lit-shader.html",
                OnOpen = () => Info("Normal Map 강도",
                    "머티리얼 선택 → Inspector > Surface Inputs > Normal Map\n\n" +
                    "맵을 할당하면 우측에 강도 필드가 나타납니다. 0.8~1.0 범위를 권장하며, " +
                    "그 이상이 필요하다고 느껴지면 지오메트리나 소스 텍스처를 먼저 점검하세요.")
            });
            c3b.Items.Add(new Item
            {
                Title = "Detail Map (근접 디테일)",
                Priority = Prio.Rec,
                MenuPath = "머티리얼 > Inspector > Detail Inputs (Mask · Detail Albedo · Detail Normal)",
                Recommend = "Detail Albedo/Normal 할당 후 Tiling 8~20배 / Detail Normal Scale 0.5~1.0 / " +
                            "관람객이 가까이 붙는 바닥·벽·손잡이에 우선 적용",
                Desc = "인터랙티브 설치물은 관람객이 표면에 얼굴을 들이대는 상황이 실제로 발생합니다. " +
                       "메인 텍스처 해상도를 올리는 것보다 디테일 맵을 겹치는 편이 메모리 대비 효율이 훨씬 좋습니다.",
                DocUrl = DocRoot + "urp/lit-shader.html",
                OnOpen = () => Info("Detail Map",
                    "머티리얼 선택 → Inspector > Detail Inputs 섹션을 펼치세요.\n\n" +
                    "· Detail Albedo x2 : 근접 시 색 변주\n" +
                    "· Detail Normal Map : 미세 표면 요철 (Scale 0.5~1.0)\n" +
                    "· Tiling을 메인의 8~20배로 설정\n" +
                    "· Mask로 적용 영역 제어 가능")
            });
            c3b.Items.Add(new Item
            {
                Title = "Parallax Occlusion (Height Map)",
                Priority = Prio.Opt,
                MenuPath = "머티리얼 > Inspector > Surface Inputs > Height Map",
                Recommend = "Height Scale 0.02~0.08 (과하면 가장자리 왜곡) / 벽돌·자갈·타일 등 깊이감 중요한 표면만",
                Desc = "픽셀 셰이더 부하가 있으니 카메라가 가까이 가는 핵심 표면에만 쓰세요. " +
                       "값을 크게 올리면 실루엣 경계에서 텍스처가 미끄러지는 현상이 보입니다.",
                DocUrl = DocRoot + "urp/lit-shader.html",
                OnOpen = () => Info("Parallax Occlusion",
                    "머티리얼 선택 → Inspector > Surface Inputs > Height Map에 하이트맵 할당\n\n" +
                    "Height Scale은 0.02~0.08부터 시작해 시선을 비스듬히 두고 확인하세요. " +
                    "가장자리가 흘러내리듯 왜곡되면 값을 낮춰야 합니다.")
            });

            t3.Categories.Add(c3a); t3.Categories.Add(c3b);
            _tabs.Add(t3);

            // =============== TAB 4. 포스트 프로세싱 ===============
            var t4 = new Tab { Name = "4. 포스트" };

            var c4a = new Category { Name = "안티앨리어싱 / 업스케일" };
            c4a.Items.Add(new Item
            {
                Title = "STP (Spatial-Temporal Post-Processing)",
                Version = "Unity 6",
                Priority = Prio.Rec,
                MenuPath = "URP Asset 선택 > Inspector > Quality > Upscaling Filter",
                Recommend = "Upscaling Filter: Spatial-Temporal Post-Processing / " +
                            "여유 있으면 Render Scale 1.0 유지, 프레임 부족하면 0.7~0.85로 낮춰 활용",
                Desc = "STP는 Render Scale 1.0에서도 계속 동작하며 최종 출력에 TAA를 적용합니다. 즉 업스케일과 AA를 겸합니다. " +
                       "저해상도 렌더에서 재구성 품질이 뛰어나 4K 디스플레이 설치물에서 프레임을 확보하는 가장 효과적인 수단입니다.",
                DocUrl = DocRoot + "urp/stp/stp-enable.html",
                OnOpen = SelectURPAsset
            });
            c4a.Items.Add(new Item
            {
                Title = "TAA (Temporal Anti-Aliasing)",
                Version = "2022.2+",
                Priority = Prio.Must,
                MenuPath = "Main Camera 선택 > Inspector > Rendering > Anti-aliasing",
                Recommend = "Anti-aliasing: Temporal Anti-aliasing / Quality: High / 잔상 거슬리면 샤프닝 소량 병행",
                Desc = "외곽선 계단은 물론 SSAO·SSR의 노이즈까지 시간축으로 뭉개줘 화질 향상 폭이 가장 큰 항목입니다. " +
                       "STP를 켠 경우 STP가 TAA 역할을 겸하므로 중복 설정하지 않아도 됩니다. 빠른 움직임에서 고스팅이 생길 수 있습니다.",
                DocUrl = DocRoot + "urp/anti-aliasing.html",
                OnOpen = SelectMainCamera
            });

            var c4b = new Category { Name = "톤 & 컬러 (Global Volume)" };
            c4b.Items.Add(new Item
            {
                Title = "Tonemapping = ACES",
                Priority = Prio.Must,
                MenuPath = "Global Volume 선택 > Inspector > Volume Profile > Add Override > Post-processing > Tonemapping",
                Recommend = "Mode: ACES / (HDR 디스플레이 출력 시 Neutral + HDR Output 조합 검토)",
                Desc = "HDR 광량을 필름 반응 곡선으로 압축해 하이라이트가 하얗게 타지 않게 합니다. " +
                       "ACES는 대비가 강하게 걸리므로 적용 후 조명 강도를 다시 조정하는 것이 보통입니다. URP Asset의 HDR이 켜져 있어야 의미가 있습니다.",
                DocUrl = DocRoot + "urp/post-processing-tonemapping.html",
                OnOpen = SelectOrCreateGlobalVolume
            });
            c4b.Items.Add(new Item
            {
                Title = "Color Adjustments / Curves / SMH",
                Priority = Prio.Rec,
                MenuPath = "Volume Profile > Add Override > Post-processing > Color Adjustments · Color Curves · Shadows Midtones Highlights",
                Recommend = "Post Exposure 0 기준 ±0.5 미세 조정 / Contrast 5~15 / Saturation -5~+10 / " +
                            "Shadows에 약한 청색, Highlights에 약한 난색 (시네마틱 스플릿 톤)",
                Desc = "채도를 과하게 올리면 즉시 게임 룩이 됩니다. 실사 레퍼런스는 대체로 채도가 낮고 대비가 정돈돼 있습니다. " +
                       "URP에는 자동 노출이 없으므로 Post Exposure가 수동 노출 조절 창구 역할을 합니다.",
                DocUrl = DocRoot + "urp/post-processing-color-adjustments.html",
                OnOpen = SelectOrCreateGlobalVolume
            });
            c4b.Items.Add(new Item
            {
                Title = "White Balance / Lift Gamma Gain",
                Priority = Prio.Opt,
                MenuPath = "Volume Profile > Add Override > Post-processing > White Balance / Lift Gamma Gain",
                Recommend = "Temperature -10~+10 범위로 조명 색온도와 맞추기 / Lift로 블랙 레벨을 살짝 띄우면 필름 느낌",
                Desc = "블랙을 완전한 0으로 두면 디지털 느낌이 강합니다. Lift를 아주 살짝 올려 어두운 부분에 정보를 남기는 것이 " +
                       "필름 룩의 흔한 트릭입니다. URP 볼륨에는 커스텀 LUT 슬롯이 없어 이 조합으로 대체합니다.",
                DocUrl = DocRoot + "urp/post-processing-white-balance.html",
                OnOpen = SelectOrCreateGlobalVolume
            });

            var c4c = new Category { Name = "렌즈 & 필름 특성 (Global Volume)" };
            c4c.Items.Add(new Item
            {
                Title = "Bloom",
                Priority = Prio.Must,
                MenuPath = "Volume Profile > Add Override > Post-processing > Bloom",
                Recommend = "Threshold 0.9~1.1 / Intensity 0.1~0.3 (과용 금물) / Scatter 0.6~0.7 / High Quality Filtering: 켬",
                Desc = "Intensity를 높이면 즉시 뿌옇고 값싼 느낌이 납니다. 실사에서 블룸은 아주 밝은 광원 주변에만 미세하게 보입니다. " +
                       "Threshold를 1.0 근처로 두어 진짜 HDR 하이라이트에만 반응하게 하는 것이 핵심입니다.",
                DocUrl = DocRoot + "urp/post-processing-bloom.html",
                OnOpen = SelectOrCreateGlobalVolume
            });
            c4c.Items.Add(new Item
            {
                Title = "Depth of Field (보케)",
                Priority = Prio.Rec,
                MenuPath = "Volume Profile > Add Override > Post-processing > Depth Of Field",
                Recommend = "Mode: Bokeh / Focal Length 50mm / Aperture(F-Stop) 2.8~5.6 / Blade Count 6~8 / " +
                            "시점 이동 많으면 Gaussian 또는 약한 설정",
                Desc = "실제 렌즈 파라미터를 쓰므로 사진 지식이 그대로 적용됩니다. F값이 낮을수록 심도가 얕아집니다. " +
                       "관람객이 카메라를 조작하는 콘텐츠라면 초점이 계속 어긋나 답답할 수 있으니 연출 샷 위주로 쓰세요.",
                DocUrl = DocRoot + "urp/post-processing-depth-of-field.html",
                OnOpen = SelectOrCreateGlobalVolume
            });
            c4c.Items.Add(new Item
            {
                Title = "Motion Blur",
                Priority = Prio.Opt,
                MenuPath = "Volume Profile > Add Override > Post-processing > Motion Blur",
                Recommend = "Mode: Camera Only / Quality: Medium / Intensity 0.05~0.15 (아주 약하게) / 체험형은 생략 검토",
                Desc = "실감형 콘텐츠에서 모션 블러는 멀미를 유발하는 흔한 원인입니다. 영상 녹화용 빌드에서는 켜고 " +
                       "현장 체험 빌드에서는 끄는 식으로 분리하는 것도 방법입니다.",
                DocUrl = DocRoot + "urp/post-processing-motion-blur.html",
                OnOpen = SelectOrCreateGlobalVolume
            });
            c4c.Items.Add(new Item
            {
                Title = "Vignette / Film Grain / Chromatic Aberration",
                Priority = Prio.Opt,
                MenuPath = "Volume Profile > Add Override > Post-processing > (각 항목)",
                Recommend = "Vignette Intensity 0.15~0.25, Smoothness 0.4 / Film Grain Type Thin1, Intensity 0.1~0.2 / " +
                            "Chromatic Aberration Intensity 0.03~0.08",
                Desc = "세 가지 모두 '있는 줄 모를 정도'가 정답입니다. 미세한 그레인은 그라데이션 밴딩을 가려주는 실용적 효과도 있습니다. " +
                       "과용하면 오히려 인디 게임 필터처럼 보이니 수치를 올리고 싶을 때 한 번 더 참으세요.",
                DocUrl = DocRoot + "urp/post-processing-vignette.html",
                OnOpen = SelectOrCreateGlobalVolume
            });

            t4.Categories.Add(c4a); t4.Categories.Add(c4b); t4.Categories.Add(c4c);
            _tabs.Add(t4);

            // =============== TAB 5. 최적화 ===============
            var t5 = new Tab { Name = "5. 최적화" };

            var c5a = new Category { Name = "CPU: 드로우콜 & 컬링" };
            c5a.Items.Add(new Item
            {
                Title = "GPU Resident Drawer (3단계 설정)",
                Version = "Unity 6",
                Priority = Prio.Rec,
                MenuPath = "① Project Settings > Graphics > Shader Stripping > BatchRendererGroup Variants = Keep All\n" +
                           "② URP Asset > Inspector > Rendering > SRP Batcher 켬 → GPU Resident Drawer = Instanced Drawing\n" +
                           "③ Renderer 더블클릭 > Rendering Path = Forward+ (또는 Deferred+)",
                Recommend = "GPU Resident Drawer: Instanced Drawing / Small-Mesh Screen Percentage 0 / " +
                            "활성화 직후 나타나는 GPU Occlusion Culling도 함께 켜기",
                Desc = "세 조건이 모두 맞아야 동작합니다. 제외 조건도 확인하세요 — MaterialPropertyBlock 사용, " +
                       "Light Probes가 Use Proxy Volume, LOD 크로스페이드 애니메이션, DOTS Instancing 미지원 커스텀 셰이더는 자동 제외됩니다. " +
                       "BRG 배리언트를 모두 컴파일하므로 빌드 시간이 늘어납니다.",
                DocUrl = DocRoot + "urp/gpu-resident-drawer.html",
                OnOpen = SelectURPAsset
            });
            c5a.Items.Add(new Item
            {
                Title = "GPU Occlusion Culling",
                Version = "Unity 6",
                Priority = Prio.Rec,
                MenuPath = "URP Asset > Inspector > Rendering  (GPU Resident Drawer 활성화 후 나타남)\n" +
                           "디버그: Window > Analysis > Rendering Debugger > GPU Resident Drawer > Occlusion Test Overlay",
                Recommend = "GPU Occlusion Culling: 켬 / 오버레이로 실제 컬링 범위 확인 후 판단",
                Desc = "이전 프레임 깊이 버퍼로 가려진 지오메트리를 GPU에서 컬링합니다. 기존 베이크 방식과 달리 " +
                       "동적 오브젝트에도 적용되고 베이크가 필요 없습니다. 실내 공간형 씬에서 효과가 큽니다.",
                DocUrl = DocRoot + "urp/gpu-culling.html",
                OnOpen = SelectURPAsset
            });
            c5a.Items.Add(new Item
            {
                Title = "Occlusion Culling (베이크 방식)",
                Priority = Prio.Must,
                MenuPath = "Window > Rendering > Occlusion Culling\n대상 오브젝트: Inspector 우상단 Static > Occluder/Occludee Static",
                Recommend = "Smallest Occluder 5 (실내는 2~3) / Smallest Hole 0.25 / Backface Threshold 100 / Bake 실행",
                Desc = "벽 뒤 지오메트리를 아예 렌더에서 제외합니다. 실내 공간형 설치물에서 가장 확실한 절감 수단입니다. " +
                       "GPU Occlusion Culling을 쓰는 경우 중복 투자일 수 있으니 둘 중 하나로 정리해도 됩니다.",
                DocUrl = DocRoot + "OcclusionCulling.html",
                OnOpen = () => ExecuteMenu("Window/Rendering/Occlusion Culling")
            });
            c5a.Items.Add(new Item
            {
                Title = "LOD Group",
                Priority = Prio.Must,
                MenuPath = "GameObject 선택 > Inspector > Add Component > LOD Group\n전역 배율: Project Settings > Quality > Rendering > LOD Bias",
                Recommend = "LOD0 100% → LOD1 50% → LOD2 25% 폴리곤 / 전환 60% / 30% / 12% / Culled 1~2% / " +
                            "LOD Bias 1.5~2.0 (품질 우선) / GRD 사용 시 Cross-fade 애니메이션 미지원",
                Desc = "원경 오브젝트의 정점 부하를 줄이는 기본기입니다. 자동 생성이 필요하면 Unity Mesh Simplifier나 " +
                       "DCC 툴에서 미리 LOD 메시를 만들어 임포트하세요.",
                DocUrl = DocRoot + "LevelOfDetail.html",
                OnOpen = () => Info("LOD Group 설정",
                    "LOD Group은 오브젝트 단위 컴포넌트입니다.\n\n" +
                    "1. Hierarchy에서 대상 오브젝트 선택\n" +
                    "2. Inspector > Add Component > LOD Group\n" +
                    "3. 각 LOD 슬롯에 폴리곤이 낮은 메시(자식) 할당\n" +
                    "4. 전환 지점: LOD0 60% / LOD1 30% / LOD2 12% / Culled 1~2%\n\n" +
                    "전역 배율은 Project Settings > Quality > LOD Bias (1.5~2.0 = 품질 우선)")
            });
            c5a.Items.Add(new Item
            {
                Title = "Batching (SRP Batcher / Static / Instancing)",
                Priority = Prio.Opt,
                MenuPath = "SRP Batcher: URP Asset > Rendering\n" +
                           "Static Batching: Project Settings > Player > Other Settings > Rendering\n" +
                           "GPU Instancing: 머티리얼 > Inspector > Advanced Options",
                Recommend = "SRP Batcher: 켬(기본) / GPU Resident Drawer 사용 시 Static Batching은 오히려 해제 권장 / " +
                            "동일 메시 반복 배치는 머티리얼의 Enable GPU Instancing 체크",
                Desc = "GPU Resident Drawer와 Static Batching은 함께 쓰면 서로의 이점을 깎아먹습니다. " +
                       "GRD를 쓰기로 했다면 Static Batching은 끄고 GRD에 맡기는 편이 낫습니다.",
                DocUrl = DocRoot + "urp/optimize-draw-calls.html",
                OnOpen = SelectURPAsset
            });

            var c5b = new Category { Name = "CPU: 외부 연산 (실감형 특화)" };
            c5b.Items.Add(new Item
            {
                Title = "C# Job System & Burst Compiler",
                Priority = Prio.Must,
                MenuPath = "Window > Package Manager에서 Burst 설치\n확인: Jobs > Burst > Enable Compilation / Jobs > Leak Detection",
                Recommend = "IJobParallelFor + [BurstCompile] / NativeArray로 데이터 전달 / " +
                            "Schedule 후 같은 프레임에 Complete() 호출하지 말고 다음 프레임에 회수",
                Desc = "뎁스 카메라 파싱, OpenCV 전처리 같은 대량 픽셀 연산을 Update()에서 돌리면 메인 스레드가 그대로 멈춥니다. " +
                       "Job으로 넘기고 Burst로 컴파일하면 수십 배 빨라지는 경우가 흔합니다. " +
                       "같은 프레임에 Complete()를 부르면 결국 대기하게 되므로 한 프레임 지연을 허용하는 설계가 핵심입니다.",
                DocUrl = DocRoot + "job-system.html",
                OnOpen = () => ExecuteMenuOrDoc("Window/Package Manager", DocRoot + "job-system.html")
            });
            c5b.Items.Add(new Item
            {
                Title = "AsyncGPUReadback",
                Priority = Prio.Rec,
                MenuPath = "(코드 레벨) UnityEngine.Rendering.AsyncGPUReadback.Request(...)",
                Recommend = "동기 Texture2D.ReadPixels 금지 / Request 후 콜백 또는 폴링으로 수신 / " +
                            "요청 큐를 2~3개로 제한해 지연 누적 방지",
                Desc = "카메라 렌더 결과를 CPU로 가져와 OpenCV에 넘길 때 동기 읽기는 GPU 파이프라인을 정지시켜 " +
                       "수 밀리초~수십 밀리초 스파이크를 만듭니다. 비동기 요청은 보통 1~3프레임 지연되지만 프레임은 유지됩니다.",
                DocUrl = "https://docs.unity3d.com/6000.3/Documentation/ScriptReference/Rendering.AsyncGPUReadback.html",
                OnOpen = () => Application.OpenURL("https://docs.unity3d.com/6000.3/Documentation/ScriptReference/Rendering.AsyncGPUReadback.html")
            });

            var c5c = new Category { Name = "GPU: 렌더링 다이어트" };
            c5c.Items.Add(new Item
            {
                Title = "Render Scale (1순위 조절 레버)",
                Priority = Prio.Rec,
                MenuPath = "URP Asset > Inspector > Quality > Render Scale",
                Recommend = "1.0 기준 → 부하 시 0.85 → 0.75 단계 하향 (STP 병행 시 0.7까지 실용) / " +
                            "런타임 조절은 UniversalRenderPipelineAsset.renderScale",
                Desc = "개별 이펙트를 끄면 룩이 무너지지만 Render Scale은 전 화면에 균일하게 작용해 룩 손상이 가장 적습니다. " +
                       "STP 업스케일러와 조합하면 0.75에서도 체감 화질 손실이 크지 않습니다. 프레임이 부족할 때 가장 먼저 손댈 값.",
                DocUrl = DocRoot + "urp/universalrp-asset.html",
                OnOpen = SelectURPAsset
            });
            c5c.Items.Add(new Item
            {
                Title = "Shadow Distance 조이기",
                Priority = Prio.Rec,
                MenuPath = "URP Asset > Inspector > Shadows > Max Distance / Cascade Count",
                Recommend = "실내 30~50m / 중형 공간 75m / Cascade 4단계, 분할을 앞쪽에 집중(0.05 / 0.15 / 0.35) / " +
                            "Last Border 0.8로 원경 그림자 페이드",
                Desc = "Max Distance를 절반으로 줄이면 같은 4096 해상도로 4배 촘촘한 그림자를 얻습니다. " +
                       "해상도를 올리기 전에 거리부터 조이는 것이 항상 효율적입니다. 원경 그림자는 라이트맵으로 대체하세요.",
                DocUrl = DocRoot + "urp/universalrp-asset.html",
                OnOpen = SelectURPAsset
            });
            c5c.Items.Add(new Item
            {
                Title = "Frame Debugger / Render Graph Viewer",
                Priority = Prio.Opt,
                MenuPath = "Window > Analysis > Frame Debugger\nWindow > Rendering > Render Graph Viewer",
                Recommend = "패스별 비용을 확인해 상위 3개부터 손대기 / 보통 SSR·볼류메트릭 포그·그림자 순으로 무거움",
                Desc = "추측으로 최적화하지 말고 실제 패스 비용을 보고 판단하세요. Render Graph Viewer는 " +
                       "패스 병합과 리소스 수명을 시각화해 불필요한 중간 텍스처를 찾아줍니다.",
                DocUrl = DocRoot + "urp/render-graph-view.html",
                OnOpen = () => ExecuteMenu("Window/Analysis/Frame Debugger")
            });
            c5c.Items.Add(new Item
            {
                Title = "Rendering Debugger",
                Priority = Prio.Opt,
                MenuPath = "Window > Analysis > Rendering Debugger",
                Recommend = "Lighting 탭에서 조명 기여도 분리 확인 / GPU Resident Drawer 탭에서 Occlusion Test Overlay",
                Desc = "라이팅 문제를 진단할 때 각 조명 성분(직사광·간접광·반사)을 분리해 볼 수 있어 " +
                       "'왜 이 부분만 어둡지' 같은 문제를 빠르게 좁힐 수 있습니다.",
                DocUrl = DocRoot + "urp/features/rendering-debugger.html",
                OnOpen = () => ExecuteMenu("Window/Analysis/Rendering Debugger")
            });
            c5c.Items.Add(new Item
            {
                Title = "Reflection Probe (정적 반사)",
                Priority = Prio.Opt,
                MenuPath = "GameObject > Light > Reflection Probe  →  Inspector > Type = Baked",
                Recommend = "Type: Baked / Resolution 256~512 / Box Projection: 켬 / 방마다 1~2개 / " +
                            "URP Asset의 Reflection Probe Blending도 함께 켜기",
                Desc = "SSR은 화면 밖을 반사하지 못하므로 프로브를 바탕에 깔아두는 것이 정석입니다. " +
                       "Box Projection을 켜야 실내에서 반사가 벽 위치에 맞게 투영됩니다.",
                DocUrl = DocRoot + "urp/lighting/reflection-probes.html",
                OnOpen = SelectOrHintReflectionProbe
            });

            var c5d = new Category { Name = "설치물 운영" };
            c5d.Items.Add(new Item
            {
                Title = "Target Frame Rate / VSync",
                Priority = Prio.Rec,
                MenuPath = "Edit > Project Settings > Quality > Rendering > VSync Count\n(코드) Application.targetFrameRate = 60;",
                Recommend = "VSync Count: Every V Blank (60Hz 기준) / targetFrameRate는 주사율과 일치 / " +
                            "VSync 사용 시 targetFrameRate는 -1로 두는 것이 충돌 적음",
                Desc = "전시장 디스플레이는 대부분 60Hz 고정입니다. 프레임을 그 이상 뽑아봐야 티어링과 발열만 늘어납니다. " +
                       "프레임 상한을 걸어 GPU 여유를 남겨두면 인터랙션 순간의 스파이크를 흡수할 수 있습니다.",
                DocUrl = DocRoot + "class-QualitySettings.html",
                OnOpen = () => OpenSettings("Project/Quality")
            });
            c5d.Items.Add(new Item
            {
                Title = "Player 빌드 / 해상도 설정",
                Priority = Prio.Rec,
                MenuPath = "Edit > Project Settings > Player > Resolution and Presentation",
                Recommend = "Fullscreen Mode: Exclusive Fullscreen (성능) 또는 Fullscreen Window (멀티 디스플레이) / " +
                            "Run In Background: 켬 / Visible In Background: 켬 / Resizable Window: 해제",
                Desc = "무인 운영 설치물은 Run In Background가 꺼져 있으면 포커스를 잃는 순간 멈춥니다. 반드시 확인하세요. " +
                       "다면 프로젝션이면 Exclusive Fullscreen이 오히려 방해가 되므로 Fullscreen Window를 씁니다.",
                DocUrl = DocRoot + "class-PlayerSettings.html",
                OnOpen = () => OpenSettings("Project/Player")
            });
            c5d.Items.Add(new Item
            {
                Title = "멀티 디스플레이 / 프로젝터",
                Priority = Prio.Opt,
                MenuPath = "카메라 선택 > Inspector > Output > Target Display\n(코드) Display.displays[i].Activate();",
                Recommend = "카메라별 Target Display 지정 / 시작 시 Display.displays 전체 Activate 호출 필수 / " +
                            "베젤 보정·엣지 블렌딩은 별도 셰이더나 프로젝터 자체 기능 활용",
                Desc = "빌드에서는 Display.displays[i].Activate()를 명시적으로 호출하지 않으면 2번째 이후 디스플레이가 검게 남습니다. " +
                       "현장 셋업 전에 반드시 실제 다중 출력 환경에서 검증하세요.",
                DocUrl = DocRoot + "MultiDisplay.html",
                OnOpen = SelectMainCamera
            });
            c5d.Items.Add(new Item
            {
                Title = "Profiler / Memory Profiler",
                Priority = Prio.Opt,
                MenuPath = "Window > Analysis > Profiler\nMemory Profiler는 Package Manager에서 별도 설치",
                Recommend = "빌드에 연결해 측정(에디터 수치는 부정확) / Deep Profile은 원인 좁힐 때만 / CPU·GPU·Memory 모듈 위주",
                Desc = "에디터에서의 프레임은 실제 빌드와 크게 다릅니다. Development Build를 만들어 " +
                       "Autoconnect Profiler로 실측하세요. GPU 내부까지 파야 하면 RenderDoc을 병행합니다.",
                DocUrl = DocRoot + "Profiler.html",
                OnOpen = () => ExecuteMenu("Window/Analysis/Profiler")
            });

            t5.Categories.Add(c5a); t5.Categories.Add(c5b); t5.Categories.Add(c5c); t5.Categories.Add(c5d);
            _tabs.Add(t5);
        }

        // ==================================================================
        //  GUI
        // ==================================================================
        private void OnGUI()
        {
            EnsureStyles();

            EditorGUILayout.Space(4);
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label("Unity 6.3 · URP 17 극사실주의 세팅 도구", EditorStyles.boldLabel);
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("전체 펼치기", EditorStyles.miniButtonLeft, GUILayout.Width(78))) SetExpandAll(true);
                if (GUILayout.Button("전체 접기", EditorStyles.miniButtonRight, GUILayout.Width(70))) SetExpandAll(false);
            }

            DrawLegend();

            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                GUILayout.Label("검색", EditorStyles.miniLabel, GUILayout.Width(30));
                _search = GUILayout.TextField(_search, EditorStyles.toolbarSearchField, GUILayout.MinWidth(120));
                GUILayout.FlexibleSpace();
                _hideOptional = GUILayout.Toggle(_hideOptional, "선택 항목 숨기기", EditorStyles.toolbarButton, GUILayout.Width(110));
            }

            EditorGUILayout.Space(4);

            var names = new string[_tabs.Count];
            for (int i = 0; i < _tabs.Count; i++) names[i] = _tabs[i].Name;
            _tabIndex = GUILayout.Toolbar(_tabIndex, names, EditorStyles.toolbarButton);

            EditorGUILayout.Space(6);

            _scroll = EditorGUILayout.BeginScrollView(_scroll);

            bool searching = !string.IsNullOrEmpty(_search);
            var tabsToDraw = searching ? _tabs : new List<Tab> { _tabs[_tabIndex] };
            int shown = 0;

            foreach (var tab in tabsToDraw)
            {
                bool tabHeaderDrawn = false;
                foreach (var cat in tab.Categories)
                {
                    var visible = new List<Item>();
                    foreach (var it in cat.Items)
                    {
                        if (_hideOptional && it.Priority == Prio.Opt) continue;
                        if (searching && !Matches(it, _search)) continue;
                        visible.Add(it);
                    }
                    if (visible.Count == 0) continue;

                    if (searching && !tabHeaderDrawn)
                    {
                        EditorGUILayout.Space(4);
                        GUILayout.Label("· " + tab.Name, EditorStyles.miniBoldLabel);
                        tabHeaderDrawn = true;
                    }

                    GUILayout.Label(cat.Name, _catStyle);
                    foreach (var it in visible) { DrawItem(it); shown++; }
                    EditorGUILayout.Space(6);
                }
            }

            if (shown == 0)
                EditorGUILayout.HelpBox("표시할 항목이 없습니다. 검색어나 필터를 확인하세요.", MessageType.Info);

            EditorGUILayout.EndScrollView();
        }

        private static bool Matches(Item it, string q)
        {
            q = q.ToLowerInvariant();
            return (it.Title != null && it.Title.ToLowerInvariant().Contains(q))
                || (it.MenuPath != null && it.MenuPath.ToLowerInvariant().Contains(q))
                || (it.Recommend != null && it.Recommend.ToLowerInvariant().Contains(q))
                || (it.Desc != null && it.Desc.ToLowerInvariant().Contains(q));
        }

        private void SetExpandAll(bool v)
        {
            foreach (var t in _tabs) foreach (var c in t.Categories) foreach (var i in c.Items) i.Expanded = v;
        }

        private void DrawLegend()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                DrawTag(Prio.Must); GUILayout.Label("반드시 적용", EditorStyles.miniLabel, GUILayout.Width(66));
                DrawTag(Prio.Rec);  GUILayout.Label("효과 큼", EditorStyles.miniLabel, GUILayout.Width(52));
                DrawTag(Prio.Opt);  GUILayout.Label("선별 적용", EditorStyles.miniLabel, GUILayout.Width(60));
                GUILayout.FlexibleSpace();
            }
            EditorGUILayout.Space(2);
        }

        private void DrawItem(Item item)
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    DrawTag(item.Priority);

                    string title = item.Title;
                    if (!string.IsNullOrEmpty(item.Version)) title += "   [" + item.Version + "]";

                    var content = new GUIContent(title);
                    var foldRect = GUILayoutUtility.GetRect(content, _titleStyle);
                    item.Expanded = EditorGUI.Foldout(foldRect, item.Expanded, title, true, _titleStyle);

                    GUILayout.FlexibleSpace();

                    if (!string.IsNullOrEmpty(item.DocUrl))
                        if (GUILayout.Button("문서", EditorStyles.miniButtonLeft, GUILayout.Width(38), GUILayout.Height(20)))
                            Application.OpenURL(item.DocUrl);

                    if (GUILayout.Button("열기 ▸", EditorStyles.miniButtonRight, GUILayout.Width(56), GUILayout.Height(20)))
                    {
                        try { item.OnOpen?.Invoke(); }
                        catch (Exception e) { Debug.LogError("[URP 가이드] 실행 오류: " + e.Message); }
                    }
                }

                if (!item.Expanded) return;

                EditorGUILayout.Space(2);

                if (!string.IsNullOrEmpty(item.MenuPath))
                {
                    GUILayout.Label("메뉴 경로", EditorStyles.miniBoldLabel);
                    GUILayout.Label(item.MenuPath, _pathStyle);
                }

                if (!string.IsNullOrEmpty(item.Recommend))
                {
                    EditorGUILayout.Space(2);
                    GUILayout.Label("추천 설정", EditorStyles.miniBoldLabel);
                    GUILayout.Label(item.Recommend, _recStyle);
                }

                if (!string.IsNullOrEmpty(item.Desc))
                {
                    EditorGUILayout.Space(2);
                    GUILayout.Label(item.Desc, _descStyle);
                }

                EditorGUILayout.Space(2);
            }
        }

        private void DrawTag(Prio p)
        {
            string label; Color col;
            switch (p)
            {
                case Prio.Must: label = "필수"; col = new Color(0.906f, 0.298f, 0.235f); break;
                case Prio.Rec:  label = "권장"; col = new Color(0.902f, 0.494f, 0.133f); break;
                default:        label = "선택"; col = new Color(0.584f, 0.647f, 0.651f); break;
            }
            var rect = GUILayoutUtility.GetRect(34, 18, GUILayout.Width(34), GUILayout.Height(18));
            EditorGUI.DrawRect(rect, col);
            GUI.Label(rect, label, _tagStyle);
        }

        private void EnsureStyles()
        {
            if (_stylesReady) return;

            _titleStyle = new GUIStyle(EditorStyles.foldout) { fontStyle = FontStyle.Bold, wordWrap = false };
            _descStyle  = new GUIStyle(EditorStyles.wordWrappedMiniLabel);
            _catStyle   = new GUIStyle(EditorStyles.boldLabel) { fontSize = 12 };

            _pathStyle = new GUIStyle(EditorStyles.wordWrappedMiniLabel);
            _pathStyle.normal.textColor = new Color(0.36f, 0.56f, 0.82f);

            _recStyle = new GUIStyle(EditorStyles.wordWrappedMiniLabel) { fontStyle = FontStyle.Bold };
            _recStyle.normal.textColor = new Color(0.24f, 0.62f, 0.36f);

            _tagStyle = new GUIStyle(EditorStyles.miniBoldLabel) { alignment = TextAnchor.MiddleCenter };
            _tagStyle.normal.textColor = Color.white;

            _stylesReady = true;
        }

        // ==================================================================
        //  액션 헬퍼
        // ==================================================================
        private static void OpenSettings(string path) => SettingsService.OpenProjectSettings(path);

        private static void ExecuteMenu(string menuPath)
        {
            if (!EditorApplication.ExecuteMenuItem(menuPath))
                EditorUtility.DisplayDialog("열 수 없음",
                    $"메뉴를 찾지 못했습니다:\n{menuPath}\n\n에디터 버전이나 패키지 설치 상태를 확인하세요.", "확인");
        }

        private static void ExecuteMenuOrDoc(string menuPath, string url)
        {
            if (!EditorApplication.ExecuteMenuItem(menuPath)) Application.OpenURL(url);
        }

        private static void Info(string title, string msg) => EditorUtility.DisplayDialog(title, msg, "확인");

        private static UniversalRenderPipelineAsset GetURPAsset()
        {
            var rp = GraphicsSettings.currentRenderPipeline ?? GraphicsSettings.defaultRenderPipeline;
            return rp as UniversalRenderPipelineAsset;
        }

        private static void SelectURPAsset()
        {
            var urp = GetURPAsset();
            if (urp == null)
            {
                EditorUtility.DisplayDialog("URP Asset 없음",
                    "활성 URP Asset을 찾지 못했습니다.\n\n" +
                    "Edit > Project Settings > Graphics > Default Render Pipeline에 URP Asset을 지정하거나,\n" +
                    "Quality 탭의 현재 레벨에 Render Pipeline Asset을 할당하세요.", "확인");
                return;
            }
            Focus(urp);
        }

        private static void SelectRendererData()
        {
            var urp = GetURPAsset();
            if (urp == null) { SelectURPAsset(); return; }

            var so = new SerializedObject(urp);
            var list = so.FindProperty("m_RendererDataList");
            int defIndex = 0;
            var idxProp = so.FindProperty("m_DefaultRendererIndex");
            if (idxProp != null) defIndex = idxProp.intValue;

            if (list != null && list.isArray && list.arraySize > 0)
            {
                if (defIndex < 0 || defIndex >= list.arraySize) defIndex = 0;
                var data = list.GetArrayElementAtIndex(defIndex).objectReferenceValue;
                if (data != null) { Focus(data); return; }
            }

            EditorUtility.DisplayDialog("Renderer Data 없음",
                "URP Asset에서 Renderer Data를 찾지 못했습니다.\n\n" +
                "URP Asset을 대신 선택했습니다. Inspector의 Renderer List에서 렌더러를 더블클릭해 여세요.", "확인");
            Focus(urp);
        }

        private static void SelectMainCamera()
        {
            var cam = Camera.main;
            if (cam == null)
            {
                var cams = UnityEngine.Object.FindObjectsByType<Camera>(FindObjectsSortMode.None);
                if (cams != null && cams.Length > 0) cam = cams[0];
            }
            if (cam == null)
            {
                EditorUtility.DisplayDialog("카메라 없음", "씬에서 카메라를 찾지 못했습니다.", "확인");
                return;
            }
            Focus(cam.gameObject);
        }

        private static void SelectOrCreateGlobalVolume()
        {
            var volumes = UnityEngine.Object.FindObjectsByType<Volume>(FindObjectsSortMode.None);
            Volume target = null;
            foreach (var v in volumes) { if (v.isGlobal) { target = v; break; } }
            if (target == null && volumes.Length > 0) target = volumes[0];

            if (target != null)
            {
                if (target.sharedProfile != null) Focus(target.sharedProfile);
                else Focus(target.gameObject);
                return;
            }

            if (!EditorUtility.DisplayDialog("Global Volume 없음",
                "씬에 Volume이 없습니다. 새 Global Volume과 프로파일을 만들까요?\n\n" +
                "생성 후 Inspector에서 Add Override로 Tonemapping·Bloom 등을 추가할 수 있습니다.",
                "생성", "취소"))
                return;

            var go = new GameObject("Global Volume");
            Undo.RegisterCreatedObjectUndo(go, "Create Global Volume");
            var vol = go.AddComponent<Volume>();
            vol.isGlobal = true;
            vol.priority = 0f;

            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            var unique = AssetDatabase.GenerateUniqueAssetPath("Assets/GlobalVolumeProfile.asset");
            AssetDatabase.CreateAsset(profile, unique);
            AssetDatabase.SaveAssets();
            vol.sharedProfile = profile;

            Selection.activeObject = profile;
            EditorGUIUtility.PingObject(go);
        }

        private static void SelectOrHintReflectionProbe()
        {
            var probes = UnityEngine.Object.FindObjectsByType<ReflectionProbe>(FindObjectsSortMode.None);
            if (probes != null && probes.Length > 0) { Focus(probes[0].gameObject); return; }

            Info("Reflection Probe",
                "씬에 Reflection Probe가 없습니다.\n\n" +
                "GameObject > Light > Reflection Probe로 생성 후:\n" +
                "· Type: Baked\n" +
                "· Resolution: 256~512\n" +
                "· Box Projection: 켬 (실내 필수)\n\n" +
                "URP Asset > Lighting의 Reflection Probe Blending도 함께 켜세요.");
        }

        private static void Focus(UnityEngine.Object obj)
        {
            if (obj == null) return;
            Selection.activeObject = obj;
            EditorGUIUtility.PingObject(obj);
            var insType = Type.GetType("UnityEditor.InspectorWindow,UnityEditor");
            if (insType != null) GetWindow(insType);
        }
    }
}
