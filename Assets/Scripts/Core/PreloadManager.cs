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

    // 홈 배경 비디오가 준비되기 전까지 청크 발행을 미룬다.
    // VideoPlayer.Prepare()는 클립을 열고 디코더를 세팅하는 동안 디스크와 메인 스레드를
    // 함께 쓴다. 여기에 청크 로딩까지 겹치면 캐시가 차가운 첫 진입에서 둘이 서로를 밀어내
    // 프레임이 튄다. 홈 화면은 사용자가 한동안 머무는 곳이라, 비디오를 먼저 띄우고
    // 프리로드를 뒤로 미뤄도 레벨 진입 시점까지 로딩을 마치는 데 아무 지장이 없다.
    static bool _homeReady;
    const float HOME_READY_TIMEOUT = 6f; // 비디오가 없거나 준비에 실패해도 프리로드는 반드시 시작한다

    /// <summary>홈 배경 비디오 준비가 끝났음을 알린다 (HomeGameManager가 호출).</summary>
    public static void MarkHomeReady() => _homeReady = true;

    // ── 계측 ──
    // 첫 진입 버벅임의 원인을 좁히기 위한 것. 어느 청크가 오래 걸리는지, 프리로드 중에
    // 긴 프레임이 실제로 발생하는지를 기록한다. 원인이 확정되면 걷어내도 된다.
    bool _measuring;
    float _lastFrameTime;
    int _spikeCount;
    const float SPIKE_MS = 100f;   // 이 이상 걸린 프레임은 눈에 띄는 버벅임
    const long SLOW_CHUNK_MS = 150; // 이 이상 걸린 청크는 이름을 남긴다

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

    // 프리로드가 도는 동안에만 프레임 간격을 잰다. 긴 프레임이 실제로 프리로드 구간에서
    // 나는지, 아니면 그 전(비디오 준비·셰이더 컴파일)에서 나는지를 가르는 것이 목적이다.
    void Update()
    {
        if (!_measuring) return;
        float now = Time.realtimeSinceStartup;
        float ms = (now - _lastFrameTime) * 1000f;
        _lastFrameTime = now;
        if (ms > SPIKE_MS)
        {
            _spikeCount++;
            UnityEngine.Debug.LogWarning(
                $"[PreloadManager] 긴 프레임 {ms:F0}ms (프리로드 중, 누적 {_spikeCount}회)");
        }
    }

    IEnumerator PreloadRoutine()
    {
        // Home UI와 배경 비디오가 먼저 자리 잡도록 한 프레임 양보
        yield return null;

        // 배경 비디오 준비가 끝날 때까지 대기 — 디스크·메인 스레드 경합을 피한다.
        var waitTimer = System.Diagnostics.Stopwatch.StartNew();
        while (!_homeReady && waitTimer.Elapsed.TotalSeconds < HOME_READY_TIMEOUT)
            yield return null;
        UnityEngine.Debug.Log(
            $"[PreloadManager] 프리로드 시작 (홈 준비 대기 {waitTimer.Elapsed.TotalSeconds:F2}s, "
            + $"{(_homeReady ? "비디오 준비됨" : "타임아웃 — 그대로 진행")})");

        var prevPriority = Application.backgroundLoadingPriority;
        Application.backgroundLoadingPriority = ThreadPriority.BelowNormal;

        _lastFrameTime = Time.realtimeSinceStartup;
        _measuring = true;

        foreach (string prefix in ManifestPrefixes)
        {
            var timer = System.Diagnostics.Stopwatch.StartNew();
            int chunk = 0;
            for (; ; chunk++)
            {
                // 레벨 로드 중에는 새 청크를 발행하지 않는다 (씬 로드가 큐에서 기다리지 않도록)
                while (_levelLoading) yield return null;

                var chunkTimer = System.Diagnostics.Stopwatch.StartNew();
                var request = Resources.LoadAsync<GameObject>($"{prefix}_{chunk:000}");
                yield return request;
                if (request.asset == null) break;
                LoadedChunks.Add(request.asset);

                if (chunkTimer.ElapsedMilliseconds >= SLOW_CHUNK_MS)
                    UnityEngine.Debug.Log(
                        $"[PreloadManager] 느린 청크: {prefix}_{chunk:000} — {chunkTimer.ElapsedMilliseconds}ms");
            }

            // UnityEngine.Debug 정규화 — 전역 Debug 래퍼가 빌드에서 로그를 제거하므로 원본 API 직접 호출
            if (chunk == 0)
                UnityEngine.Debug.LogWarning($"[PreloadManager] 매니페스트 없음: Resources/{prefix}_000 — 에디터 메뉴 Tools/Build Preload Manifests를 실행하세요.");
            else
                UnityEngine.Debug.Log($"[PreloadManager] 프리로드 완료: {prefix} 청크 {chunk}개 ({timer.Elapsed.TotalSeconds:F2}s)");
        }

        _measuring = false;
        UnityEngine.Debug.Log($"[PreloadManager] 프리로드 구간 긴 프레임(>{SPIKE_MS:F0}ms) 누적 {_spikeCount}회");

        if (!_levelLoading)
            Application.backgroundLoadingPriority = prevPriority;
    }
}
