using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Home 씬 대기 중 레벨 대형 에셋을 백그라운드로 미리 로드해 상주시킨다.
/// HomeGameManager.Awake에서 코드로 부착되므로 씬 파일 수정이 필요 없다.
///
/// 매니페스트는 청크 프리팹(Level1_Preload_000, _001, ...) 단위로 나뉘어 있다.
/// Unity 비동기 로딩 큐는 순차 처리라, 빌드에서 거대한 단일 매니페스트가 로딩 중이면
/// 레벨 씬 로드가 그 뒤에 줄을 서게 된다 — 청크 단위로 발행하고 레벨 로드가 시작되면
/// 다음 청크 발행을 멈춰, 씬 로드의 큐 대기 상한을 "청크 하나"로 제한한다.
///
/// 로드된 프리팹 참조를 static으로 유지해, 레벨 종료 후 LoadScene("Home")(single)의
/// 미사용 에셋 언로드에도 에셋이 해제되지 않게 한다.
/// </summary>
public class PreloadManager : MonoBehaviour
{
    static readonly string[] ManifestPrefixes = { "Preload/Level1_Preload", "Preload/Level2_Preload" };

    // 프리로드된 에셋이 Resources.UnloadUnusedAssets에 회수되지 않도록 참조를 붙잡아 두는 목록
    static readonly List<Object> LoadedChunks = new List<Object>();
    static bool _started;
    static bool _levelLoading;         // 레벨 씬 로드 진행 중 — 새 청크 발행 중단
    static int _prevUploadSlice = -1;  // 가속 전 asyncUploadTimeSlice (원복용)
    static int _prevUploadBuffer = -1; // 가속 전 asyncUploadBufferSize (원복용)

    /// <summary>
    /// 레벨 씬 로드 시작 시 호출. 백그라운드 로딩 우선순위와 GPU 비동기 업로드
    /// 타임슬라이스(프로젝트 기본 2ms/frame — 빌드에서 텍스처 업로드 병목)를 끌어올려
    /// 씬 로드를 가속한다. 홈 화면/트랜지션이 덮고 있는 동안이라 히칭은 체감되지 않는다.
    /// </summary>
    public static void BoostLoadingPriority()
    {
        _levelLoading = true;
        Application.backgroundLoadingPriority = ThreadPriority.High;
        if (_prevUploadSlice < 0) _prevUploadSlice = QualitySettings.asyncUploadTimeSlice;
        if (_prevUploadBuffer < 0) _prevUploadBuffer = QualitySettings.asyncUploadBufferSize;
        // 활성화 구간(빌드 실측 ~9.4s)이 평균 33ms 프레임 ≈ 업로드 16ms + 프레임 16ms로
        // 업로드 스로틀에 지배됨 — 화면이 트랜지션으로 덮여 있으므로 사실상 무제한으로 연다.
        QualitySettings.asyncUploadTimeSlice = 128;
        QualitySettings.asyncUploadBufferSize = 64; // MB — 대형 프레임 시퀀스 업로드 버퍼
    }

    /// <summary>레벨 씬 활성화 완료 후 호출 — 가속을 원복하고 남은 프리로드를 재개한다.</summary>
    public static void OnLevelLoadFinished()
    {
        _levelLoading = false;
        if (_prevUploadSlice >= 0)
        {
            QualitySettings.asyncUploadTimeSlice = _prevUploadSlice;
            _prevUploadSlice = -1;
        }
        if (_prevUploadBuffer >= 0)
        {
            QualitySettings.asyncUploadBufferSize = _prevUploadBuffer;
            _prevUploadBuffer = -1;
        }
        // 남은 프리로드 청크가 게임플레이 프레임을 방해하지 않도록 낮은 우선순위로 재개
        Application.backgroundLoadingPriority = ThreadPriority.BelowNormal;
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

        foreach (string prefix in ManifestPrefixes)
        {
            var timer = System.Diagnostics.Stopwatch.StartNew();
            int chunk = 0;
            for (; ; chunk++)
            {
                // 레벨 로드 중에는 새 청크를 발행하지 않는다 (씬 로드가 큐에서 기다리지 않도록)
                while (_levelLoading) yield return null;

                var request = Resources.LoadAsync<GameObject>($"{prefix}_{chunk:000}");
                yield return request;
                if (request.asset == null) break;
                LoadedChunks.Add(request.asset);
            }

            // UnityEngine.Debug 정규화 — 전역 Debug 래퍼가 빌드에서 로그를 제거하므로 원본 API 직접 호출
            if (chunk == 0)
                UnityEngine.Debug.LogWarning($"[PreloadManager] 매니페스트 없음: Resources/{prefix}_000 — 에디터 메뉴 Tools/Build Preload Manifests를 실행하세요.");
            else
                UnityEngine.Debug.Log($"[PreloadManager] 프리로드 완료: {prefix} 청크 {chunk}개 ({timer.Elapsed.TotalSeconds:F2}s)");
        }

        if (!_levelLoading)
            Application.backgroundLoadingPriority = prevPriority;
    }
}
