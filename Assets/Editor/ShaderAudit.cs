using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 셰이더 정리를 "감으로" 하지 않기 위한 진단 도구.
///
/// 셰이더 정리에서 흔한 오해: 안 쓰는 .shader 파일을 지우면 빌드가 가벼워진다 — 아니다.
/// 머티리얼이 참조하지 않는 셰이더는 애초에 빌드에 들어가지 않는다. 빌드 용량과
/// (특히) 첫 프레임 히칭을 만드는 것은 셰이더 '개수'가 아니라 컴파일된 '변형(variant)' 수다.
///
/// 변형이 실제로 생기는 곳은 셋뿐이다:
///   1. 머티리얼이 참조하는 셰이더  — 정상 비용. 줄이려면 머티리얼을 줄여야 한다.
///   2. Always Included Shaders     — 참조 여부와 무관하게 '모든 변형'이 컴파일된다. 가장 큰 낭비원.
///   3. Resources 폴더의 머티리얼    — 전부 포함된다.
/// 이 도구는 2번과 3번을 눈으로 확인할 수 있게 출력한다.
/// </summary>
public static class ShaderAudit
{
    [MenuItem("Tools/Shaders/셰이더 감사 — Always Included + Resources 사용 현황")]
    public static void Audit()
    {
        var sb = new StringBuilder();
        sb.AppendLine("═══ 셰이더 감사 ═══");

        AppendAlwaysIncluded(sb);
        AppendResourcesMaterials(sb);
        AppendSceneShaders(sb);

        sb.AppendLine();
        sb.AppendLine("── 다음 할 일 ──");
        sb.AppendLine("· Always Included에 있는데 아래 '실제 사용' 목록에 없는 셰이더 = 순수 낭비.");
        sb.AppendLine("  Project Settings > Graphics 에서 빼면 된다. 단, 코드가 Shader.Find로");
        sb.AppendLine("  런타임에 찾아 쓰는 셰이더는 빼면 빌드에서만 분홍색으로 깨진다.");
        sb.AppendLine("  (이 프로젝트 스크립트에는 Shader.Find가 없지만 패키지는 쓸 수 있으니 빌드로 확인할 것)");
        sb.AppendLine("· 첫 진입 히칭은 셰이더를 '지워서'가 아니라 '미리 컴파일해서' 없앤다 —");
        sb.AppendLine("  Tools/Shaders/변형 로깅 켜기 로 무엇이 언제 컴파일되는지부터 확인할 것.");

        Debug.Log(sb.ToString());
    }

    static void AppendAlwaysIncluded(StringBuilder sb)
    {
        sb.AppendLine();
        sb.AppendLine("── Always Included Shaders (모든 변형이 무조건 컴파일된다) ──");

        var so = new SerializedObject(
            AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/GraphicsSettings.asset")[0]);
        var list = so.FindProperty("m_AlwaysIncludedShaders");
        if (list == null || list.arraySize == 0)
        {
            sb.AppendLine("  (없음 — 이상적인 상태)");
            return;
        }

        for (int i = 0; i < list.arraySize; i++)
        {
            var shader = list.GetArrayElementAtIndex(i).objectReferenceValue as Shader;
            if (shader == null)
            {
                sb.AppendLine($"  [{i}] (빈 슬롯 — 지워도 된다)");
                continue;
            }
            // 변형 수는 내부 API라 정확히 못 세지만, 이름만 봐도 URP 프로젝트에 필요 없는
            // Built-in/Legacy 셰이더는 바로 가려낸다.
            sb.AppendLine($"  [{i}] {shader.name}");
        }
    }

    static void AppendResourcesMaterials(StringBuilder sb)
    {
        sb.AppendLine();
        sb.AppendLine("── Resources 안의 머티리얼이 쓰는 셰이더 (빌드에 무조건 포함) ──");

        var guids = AssetDatabase.FindAssets("t:Material", new[] { "Assets" });
        var byShader = new Dictionary<string, int>();
        int inResources = 0;

        foreach (var guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (path.IndexOf("/Resources/", System.StringComparison.OrdinalIgnoreCase) < 0) continue;

            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null || mat.shader == null) continue;

            inResources++;
            byShader.TryGetValue(mat.shader.name, out int n);
            byShader[mat.shader.name] = n + 1;
        }

        if (inResources == 0)
        {
            sb.AppendLine("  (없음)");
            return;
        }
        sb.AppendLine($"  머티리얼 {inResources}개 → 셰이더 {byShader.Count}종");
        foreach (var kv in byShader.OrderByDescending(k => k.Value))
            sb.AppendLine($"    {kv.Value,4}개  {kv.Key}");
    }

    static void AppendSceneShaders(StringBuilder sb)
    {
        sb.AppendLine();
        sb.AppendLine("── Assets/ 안의 머티리얼이 실제로 쓰는 셰이더 ──");

        // Packages/는 제외한다. AssetDatabase.FindAssets는 기본적으로 패키지까지 훑어서
        // VFX Graph·HDRP·SpeedTree 같은 '패키지 내장 머티리얼'이 섞여 들어오는데,
        // 그것들은 우리 씬이 참조하지 않으면 빌드에도 안 들어간다. 목록만 어지럽힌다.
        var byShader = new Dictionary<string, int>();
        foreach (var guid in AssetDatabase.FindAssets("t:Material", new[] { "Assets" }))
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guid));
            if (mat == null || mat.shader == null) continue;
            byShader.TryGetValue(mat.shader.name, out int n);
            byShader[mat.shader.name] = n + 1;
        }

        sb.AppendLine($"  머티리얼 {byShader.Values.Sum()}개 → 셰이더 {byShader.Count}종");
        foreach (var kv in byShader.OrderByDescending(k => k.Value))
            sb.AppendLine($"    {kv.Value,4}개  {kv.Key}");
    }

    // ── 셰이더 컴파일 로깅 ────────────────────────────────────────────
    // 에디터 첫 플레이 히칭의 정체를 확인하는 가장 직접적인 방법.
    // 켜두면 셰이더가 컴파일될 때마다 콘솔에 어떤 셰이더의 어떤 변형인지 찍힌다.
    // 히칭이 나는 순간에 이 로그가 쏟아지면 원인은 셰이더 컴파일이 맞다.

    const string LogPref = "ShaderAudit.CompileLogging";

    [MenuItem("Tools/Shaders/변형 로깅 켜기 (컴파일되는 셰이더를 콘솔에 출력)")]
    public static void EnableCompileLog() => SetCompileLog(true);

    [MenuItem("Tools/Shaders/변형 로깅 끄기")]
    public static void DisableCompileLog() => SetCompileLog(false);

    static void SetCompileLog(bool on)
    {
        var so = new SerializedObject(
            AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/GraphicsSettings.asset")[0]);
        var prop = so.FindProperty("m_LogWhenShaderIsCompiled");
        if (prop == null)
        {
            Debug.LogWarning("[ShaderAudit] m_LogWhenShaderIsCompiled 프로퍼티를 찾지 못했습니다.");
            return;
        }
        prop.boolValue = on;
        so.ApplyModifiedProperties();
        AssetDatabase.SaveAssets();
        EditorPrefs.SetBool(LogPref, on);
        Debug.Log($"[ShaderAudit] 셰이더 컴파일 로깅 {(on ? "켜짐" : "꺼짐")} — "
                  + (on ? "이제 플레이하면 컴파일되는 셰이더가 콘솔에 찍힙니다." : ""));
    }
}
