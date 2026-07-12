using System;
using System.IO.Ports;
using System.Threading;
using UnityEngine;

/// <summary>
/// USB 릴레이로 진동 모터를 독립 제어. ESP32(InputManager)와는 별도의 시리얼 포트를 사용하며,
/// ESP32 펌웨어는 그대로 두고 더 이상 진동 트리거 신호(V{state})만 보내지 않는 방식으로 전환한다.
///
/// 포트 I/O(연결·상태확인·진동 ON/OFF)는 모두 전용 백그라운드 스레드에서 처리한다.
/// 메인 스레드는 진동 명령만 큐로 전달하므로, 릴레이 무응답 시에도 프레임이 멈추지 않는다.
/// </summary>
public class VibrationRelay : Singleton<VibrationRelay>
{
    [Header("활성화 여부")]
    [SerializeField] bool isActive = true;

    [Header("포트 설정 (USB 릴레이 — ESP32와 별도)")]
    [SerializeField] string portName = "COM3";
    [SerializeField] int baudRate = 9600;
    [SerializeField] bool autoConnect = true;

    [Header("진동 지속시간 프리셋 (초)")]
    [SerializeField] float shortDuration = 0.2f;
    [SerializeField] float mediumDuration = 0.5f;
    [SerializeField] float longDuration = 1.5f;

    [Header("연결 확인 / 재연결")]
    [SerializeField] float reconnectInterval = 5f;
    [SerializeField] int statusTimeoutMs = 200;

    // 릴레이 보드 스펙: 9600bps, N/8/1, 릴레이1 ON/OFF 명령 (체크섬 = 바이트 합산)
    readonly byte[] onCmd = { 0xA0, 0x01, 0x01, 0xA2 };
    readonly byte[] offCmd = { 0xA0, 0x01, 0x00, 0xA1 };
    // 상태확인 명령: 헤더 0xFF만 전송, 릴레이 상태값은 ASCII로 응답
    readonly byte[] statusCmd = { 0xFF };

    const int PollMs = 20;           // 스레드 폴링 주기 — 진동 명령 반영 지연 상한
    const int ShutdownJoinMs = 1500; // 종료 시 스레드 정리(OFF 전송 + 포트 닫기) 대기 상한
    const float MinDuration = 0.05f; // 진동 1회 지속시간 하한
    const float MaxDuration = 5f;    // 진동 1회 지속시간 상한 (config 오타 방어)

    static float ClampDuration(float seconds) => Mathf.Clamp(seconds, MinDuration, MaxDuration);

    SerialPort port;                 // 백그라운드 스레드에서만 접근
    Thread _thread;
    volatile bool _threadRunning;
    volatile bool _connected;        // 스레드가 게시, 메인이 읽음
    volatile bool _forceReconnect;   // 메인 → 스레드 재연결 요청

    readonly object _cmdLock = new();
    float? _pendingVibe;             // 메인 → 스레드 진동 요청 (지속시간 초)

    /// <summary>릴레이 포트가 열려 있고 상태확인에 응답한 상태인지 여부 (메인 스레드용).</summary>
    public bool IsConnected => isActive && _connected;

    protected override void Awake()
    {
        base.Awake();
        if (Instance != this) return; // 중복 인스턴스 — Destroy 예정이므로 연결 시도 등 부수효과 생략
        ApplyConfig();
        if (!isActive)
        {
            Debug.Log("[VibrationRelay] 진동 비활성화 설정(isActive=0) — 연결하지 않음");
            return;
        }
        StartThread();
    }

    // config.ini는 InputManager가 읽어들이므로, 여기서는 그 값을 그대로 가져다 쓴다.
    void ApplyConfig()
    {
        var im = InputManager.Instance;
        if (im == null)
        {
            Debug.LogWarning("[VibrationRelay] InputManager 없음 — 인스펙터 기본값 사용");
            return;
        }

        isActive = im.VibrationActive;
        portName = im.RelayPortName;
        baudRate = im.RelayBaudRate;

        // 프리셋 길이에 VibeMultiplier(0.5~3.0)를 곱해 전체 진동 시간을 일괄 조절한다.
        // config 오타로 과도한 값이 들어와도 진동이 오래 켜지지 않도록 최종값에 상한을 둔다.
        float mul = im.VibeMultiplier;
        shortDuration = ClampDuration(im.VibeShortDuration * mul);
        mediumDuration = ClampDuration(im.VibeMediumDuration * mul);
        longDuration = ClampDuration(im.VibeLongDuration * mul);
        Debug.Log($"[VibrationRelay] 설정 로드: {portName}@{baudRate}, x{mul:F2} → S={shortDuration:F2} M={mediumDuration:F2} L={longDuration:F2}");
    }

    // ── 스레드 수명 관리 ──────────────────────────────────────────────
    void StartThread()
    {
        if (_thread != null) return;
        _threadRunning = true;
        _forceReconnect = autoConnect;
        _thread = new Thread(RelayLoop) { IsBackground = true, Name = "VibrationRelay" };
        _thread.Start();
    }

    void StopThread()
    {
        _threadRunning = false;
        if (_thread != null)
        {
            // 최악의 경우 폴링(20ms) + 상태확인 블로킹(ReadTimeout) + 종료 정리 쓰기(WriteTimeout)가
            // 겹칠 수 있어 넉넉히 기다린다. 여기서 못 빠져나오면 OFF를 못 보내고 진동이 켜진 채 남는다.
            if (!_thread.Join(ShutdownJoinMs))
                Debug.LogWarning($"[VibrationRelay] 스레드가 {ShutdownJoinMs}ms 내에 종료되지 않음 — 진동이 켜진 채 남을 수 있음");
            _thread = null;
        }
    }

    /// <summary>수동 재연결 요청 (DebugInputPanel 버튼 등). 실제 연결은 백그라운드 스레드가 처리.</summary>
    public void Connect()
    {
        if (!isActive) { Debug.LogWarning("[VibrationRelay] 진동 비활성화(isActive=0) — 연결 요청 무시"); return; }
        _forceReconnect = true;
        if (_thread == null) StartThread();
    }

    /// <summary>스레드 정지 및 포트 종료 (앱 종료/파괴 시).</summary>
    public void Disconnect() => StopThread();

    // ── 백그라운드 스레드 루프 ────────────────────────────────────────
    void RelayLoop()
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        long lastAttemptMs = long.MinValue; // 마지막 연결시도/상태확인 시각
        bool vibeActive = false;
        long vibeEndMs = 0;
        int failCount = 0;                  // 연속 실패 횟수 (재시도마다 로그에 표기)
        long reconnectMs = (long)(reconnectInterval * 1000f);

        while (_threadRunning)
        {
            long now = sw.ElapsedMilliseconds;

            // 1) 진동 명령 우선 처리 (블로킹 상태확인보다 먼저 반영해 지연 최소화)
            float? cmd = null;
            lock (_cmdLock) { if (_pendingVibe.HasValue) { cmd = _pendingVibe; _pendingVibe = null; } }
            if (cmd.HasValue && ThreadWrite(onCmd))
            {
                // 진동 중에 새 요청이 오면 남은 시간이 더 길 때 그대로 유지한다.
                // (예: Danger 1.5초 진동이 뒤이은 Click 0.2초 요청에 잘리지 않도록)
                long endMs = now + (long)(cmd.Value * 1000f);
                vibeEndMs = vibeActive ? Math.Max(vibeEndMs, endMs) : endMs;
                vibeActive = true;
            }
            // OFF 전송이 실패하면 vibeActive를 유지해 다음 폴링에서 다시 시도한다.
            // (여기서 무조건 false로 내리면 릴레이가 켜진 채 방치된다)
            if (vibeActive && now >= vibeEndMs)
                vibeActive = !ThreadWrite(offCmd);

            // 2) 연결 관리 / 주기적 상태확인
            //    미연결 시에도 reconnectInterval 주기로만 재시도해 매 폴링 스팸을 막는다.
            bool portUp = port != null && port.IsOpen;
            bool due = now - lastAttemptMs >= reconnectMs;

            if (_forceReconnect)
            {
                _forceReconnect = false;
                lastAttemptMs = now;
                AttemptConnect(ref failCount);
            }
            else if (!portUp)
            {
                if (due)
                {
                    lastAttemptMs = now;
                    AttemptConnect(ref failCount);
                }
            }
            else if (due)
            {
                lastAttemptMs = now;
                if (ThreadCheckStatus())
                {
                    failCount = 0;
                }
                else
                {
                    _connected = false;
                    Debug.LogWarning("[VibrationRelay] 릴레이 응답 없음 — 재연결 시도");
                    AttemptConnect(ref failCount);
                }
            }

            Thread.Sleep(PollMs);
        }

        // 종료 정리 — ThreadClosePort가 OFF 전송과 포트 닫기를 모두 처리한다.
        ThreadClosePort();
    }

    // 연결 1회 시도. 재시도마다 결과를 로그로 남긴다(실패 시 연속 실패 횟수 표기).
    void AttemptConnect(ref int failCount)
    {
        // 연결 수명주기 로그는 현장(빌드) Player.log 진단용 — 전역 Debug 래퍼가 빌드에서
        // 로그를 제거하므로 UnityEngine.Debug를 직접 호출한다.
        if (ThreadConnect())
        {
            UnityEngine.Debug.Log(failCount > 0
                ? $"[VibrationRelay] {portName} 연결됨 (상태확인 성공, {failCount}회 실패 후 복구)"
                : $"[VibrationRelay] {portName} 연결됨 (상태확인 성공)");
            failCount = 0;
        }
        else
        {
            failCount++;
            UnityEngine.Debug.LogWarning($"[VibrationRelay] {portName} 연결/응답 실패 ({failCount}회) — {reconnectInterval:F0}초 후 재시도 (릴레이 연결 확인)");
        }
    }

    // 포트 열고 상태확인까지 성공하면 true. 로그는 호출부(AttemptConnect)에서 처리.
    bool ThreadConnect()
    {
        ThreadClosePort();
        try
        {
            port = new SerialPort(portName, baudRate) { WriteTimeout = 200, ReadTimeout = statusTimeoutMs };
            port.Open();
        }
        catch (Exception)
        {
            try { port?.Dispose(); } catch { }
            port = null;
            _connected = false;
            return false;
        }

        // 이전 실행이 크래시 등으로 OFF를 못 보냈다면 릴레이가 켜진 채 래치되어 있다.
        // 연결 직후 OFF를 한 번 보내 항상 꺼진 상태에서 시작한다.
        ThreadWrite(offCmd);

        bool ok = ThreadCheckStatus();
        _connected = ok;
        return ok;
    }

    void ThreadClosePort()
    {
        if (port != null)
        {
            // OFF 전송이 실패해도 포트는 반드시 닫는다. 닫지 못하면 포트가 열린 채
            // 참조를 잃어 이후 재연결이 계속 접근 거부로 실패한다.
            try { if (port.IsOpen) port.Write(offCmd, 0, offCmd.Length); }
            catch (Exception e) { Debug.LogWarning($"[VibrationRelay] 종료 OFF 전송 실패: {e.Message}"); }

            try { port.Close(); }
            catch (Exception e) { Debug.LogWarning($"[VibrationRelay] 포트 종료 오류: {e.Message}"); }
            finally { try { port.Dispose(); } catch { } port = null; }
        }
        _connected = false;
    }

    // 헤더 0xFF만 전송해 릴레이 보드가 살아있는지 확인 (응답 수신 여부만 체크)
    bool ThreadCheckStatus()
    {
        if (port == null || !port.IsOpen) { _connected = false; return false; }
        try
        {
            port.DiscardInBuffer();
            port.Write(statusCmd, 0, statusCmd.Length);
            port.ReadByte();       // 백그라운드 스레드라 블로킹 허용
            _connected = true;
            return true;
        }
        catch (TimeoutException) { _connected = false; return false; }
        catch (Exception e)
        {
            Debug.LogWarning($"[VibrationRelay] 상태확인 오류: {e.Message}");
            _connected = false;
            return false;
        }
    }

    bool ThreadWrite(byte[] cmd)
    {
        if (port == null || !port.IsOpen) return false;
        try { port.Write(cmd, 0, cmd.Length); return true; }
        catch (Exception e) { Debug.LogWarning($"[VibrationRelay] 전송 실패: {e.Message}"); return false; }
    }

    // ── 프리셋 호출용 (메인 스레드) ───────────────────────────────────
    public void VibrateShort() => Vibrate(shortDuration);
    public void VibrateMedium() => Vibrate(mediumDuration);
    public void VibrateLong() => Vibrate(longDuration);

    // 커스텀 길이가 필요하면 이것도 그대로 사용 가능. 실제 ON/OFF는 백그라운드 스레드가 수행.
    public void Vibrate(float duration)
    {
        duration = ClampDuration(duration);
        if (!isActive) return;
        if (!_connected)
        {
            Debug.LogWarning("[VibrationRelay] 포트 미연결 — 진동 무시");
            return;
        }
        // 같은 프레임에 여러 요청이 겹치면 긴 쪽을 남긴다.
        // (예: 퀴즈 오답 1.5초 진동이 같은 입력의 클릭 0.2초 진동에 잘리지 않도록)
        lock (_cmdLock) { _pendingVibe = Mathf.Max(_pendingVibe ?? 0f, duration); }
    }

    void OnApplicationQuit() => StopThread();
    void OnDestroy() => StopThread();
}
