using UnityEditor;
using UnityEngine;

/// <summary>
/// Hierarchy에서 선택한 오브젝트의 하위 MeshRenderer를 모두 찾아
/// Lighting > Receive Global Illumination 값을 Light Probes로 변경한다.
/// </summary>
public static class SetReceiveGILightProbes
{
    private const string MenuPath = "Tools/Lighting/Set Receive GI to Light Probes";

    [MenuItem(MenuPath, false, 100)]
    private static void Apply()
    {
        GameObject[] roots = Selection.gameObjects;
        if (roots == null || roots.Length == 0)
        {
            EditorUtility.DisplayDialog("Receive GI", "Hierarchy에서 오브젝트를 먼저 선택하세요.", "확인");
            return;
        }

        int changed = 0;
        int total = 0;

        foreach (GameObject root in roots)
        {
            // 비활성 오브젝트도 포함해서 하위 전체를 순회한다.
            MeshRenderer[] renderers = root.GetComponentsInChildren<MeshRenderer>(true);
            foreach (MeshRenderer renderer in renderers)
            {
                total++;
                if (renderer.receiveGI == ReceiveGI.LightProbes)
                    continue;

                Undo.RecordObject(renderer, "Set Receive GI to Light Probes");
                renderer.receiveGI = ReceiveGI.LightProbes;
                EditorUtility.SetDirty(renderer);
                changed++;
            }
        }

        UnityEngine.Debug.Log($"[SetReceiveGILightProbes] MeshRenderer {total}개 중 {changed}개를 Light Probes로 변경했습니다.");
    }

    [MenuItem(MenuPath, true)]
    private static bool ApplyValidate()
    {
        return Selection.gameObjects != null && Selection.gameObjects.Length > 0;
    }
}
