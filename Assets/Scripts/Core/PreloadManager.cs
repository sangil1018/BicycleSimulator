using System.Collections;
using UnityEngine;

/// <summary>
/// Home 씬 대기 중 레벨 대형 에셋을 백그라운드로 미리 로드해 상주시킨다.
/// HomeGameManager.Awake에서 코드로 부착되므로 씬 파일 수정이 필요 없다.
/// 로드된 매니페스트 프리팹 참조를 static으로 유지해, 레벨 종료 후
/// LoadScene("Home")(single)의 미사용 에셋 언로드에도 에셋이 해제되지 않게 한다.
/// </summary>
public class PreloadManager : MonoBehaviour
{
    static readonly string[] ManifestPaths = { "Preload/Level1_Preload", "Preload/Level2_Preload" };

    // 프리로드된 에셋이 Resources.UnloadUnusedAssets에 회수되지 않도록 참조를 붙잡아 두는 배열
    static readonly Object[] LoadedManifests = new Object[ManifestPaths.Length];
    static bool _started;
    static bool _priorityBoosted;

    /// <summary>
    /// 레벨 씬 로드 시작 시 호출 — 프리로드가 점유 중일 수 있는 백그라운드 로딩
    /// 우선순위를 끌어올려 씬 로드를 가속한다. (트랜지션 오버레이가 화면을 덮고 있어
    /// 로딩 중 프레임 저하는 체감되지 않음)
    /// </summary>
    public static void BoostLoadingPriority()
    {
        _priorityBoosted = true;
        Application.backgroundLoadingPriority = ThreadPriority.High;
    }

    void Start()
    {
        // 홈 재방문 시 이미 상주 중이면 다시 로드하지 않는다.
        if (_started)
        {
            enabled = false;
            return;
        }
        _started = true;
        StartCoroutine(PreloadRoutine());
    }

    IEnumerator PreloadRoutine()
    {
        // Home UI와 배경 비디오가 먼저 자리 잡도록 한 프레임 양보
        yield return null;

        var prevPriority = Application.backgroundLoadingPriority;
        Application.backgroundLoadingPriority = ThreadPriority.BelowNormal;

        for (int i = 0; i < ManifestPaths.Length; i++)
        {
            var timer = System.Diagnostics.Stopwatch.StartNew();
            var request = Resources.LoadAsync<GameObject>(ManifestPaths[i]);
            yield return request;

            if (request.asset != null)
            {
                LoadedManifests[i] = request.asset;
                Debug.Log($"[PreloadManager] 프리로드 완료: {ManifestPaths[i]} ({timer.Elapsed.TotalSeconds:F2}s)");
            }
            else
            {
                Debug.LogWarning($"[PreloadManager] 매니페스트 없음: Resources/{ManifestPaths[i]} — 에디터 메뉴 Tools/Build Preload Manifests를 실행하세요.");
            }
        }

        // 레벨 로드가 이미 시작됐다면 High 우선순위를 유지한다.
        if (!_priorityBoosted)
            Application.backgroundLoadingPriority = prevPriority;
    }
}
