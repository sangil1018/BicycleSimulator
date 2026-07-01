using System;
using System.Collections;
using System.IO.Ports;
using UnityEngine;

/// <summary>
/// USB 릴레이로 진동 모터를 독립 제어. ESP32(InputManager)와는 별도의 시리얼 포트를 사용하며,
/// ESP32 펌웨어는 그대로 두고 더 이상 진동 트리거 신호(V{state})만 보내지 않는 방식으로 전환한다.
/// </summary>
public class VibrationRelay : Singleton<VibrationRelay>
{
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

    SerialPort port;
    Coroutine currentVibration;
    bool _verified;

    public bool IsConnected => port != null && port.IsOpen && _verified;

    protected override void Awake()
    {
        base.Awake();
        ApplyConfig();
        if (autoConnect) Connect();
        StartCoroutine(ConnectionWatchdog());
    }

    IEnumerator ConnectionWatchdog()
    {
        var wait = new WaitForSeconds(reconnectInterval);
        while (true)
        {
            yield return wait;

            if (port == null || !port.IsOpen)
            {
                Connect();
                continue;
            }

            _verified = CheckStatus();
            if (!_verified)
            {
                Debug.LogWarning("[VibrationRelay] 릴레이 응답 없음 — 재연결 시도");
                Connect();
            }
        }
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

        portName = im.RelayPortName;
        baudRate = im.RelayBaudRate;
        shortDuration = im.VibeShortDuration;
        mediumDuration = im.VibeMediumDuration;
        longDuration = im.VibeLongDuration;
        Debug.Log($"[VibrationRelay] 설정 로드: {portName}@{baudRate}, S={shortDuration:F2} M={mediumDuration:F2} L={longDuration:F2}");
    }

    public void Connect()
    {
        Disconnect();
        try
        {
            port = new SerialPort(portName, baudRate) { WriteTimeout = 200, ReadTimeout = statusTimeoutMs };
            port.Open();
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[VibrationRelay] 연결 실패: {e.Message}");
            return;
        }

        _verified = CheckStatus();
        Debug.Log(_verified
            ? $"[VibrationRelay] {portName} 연결됨 (상태확인 성공)"
            : $"[VibrationRelay] {portName} 포트는 열렸지만 릴레이 응답 없음");
    }

    public void Disconnect()
    {
        if (currentVibration != null)
        {
            StopCoroutine(currentVibration);
            currentVibration = null;
        }
        if (port != null && port.IsOpen)
        {
            try { port.Write(offCmd, 0, offCmd.Length); }
            catch (Exception e) { Debug.LogWarning($"[VibrationRelay] OFF 전송 실패: {e.Message}"); }
            port.Close();
        }
        port = null;
        _verified = false;
    }

    // 헤더 0xFF만 전송해 릴레이 보드가 살아있는지 확인 (응답 수신 여부만 체크)
    bool CheckStatus()
    {
        if (port == null || !port.IsOpen) return false;
        try
        {
            port.DiscardInBuffer();
            port.Write(statusCmd, 0, statusCmd.Length);
            port.ReadByte();
            return true;
        }
        catch (TimeoutException)
        {
            return false;
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[VibrationRelay] 상태확인 오류: {e.Message}");
            return false;
        }
    }

    // ===== 프리셋 호출용 =====
    public void VibrateShort() => Vibrate(shortDuration);
    public void VibrateMedium() => Vibrate(mediumDuration);
    public void VibrateLong() => Vibrate(longDuration);

    // 커스텀 길이가 필요하면 이것도 그대로 사용 가능
    public void Vibrate(float duration)
    {
        if (!IsConnected)
        {
            Debug.LogWarning("[VibrationRelay] 포트 미연결 — 진동 무시");
            return;
        }

        if (currentVibration != null)
            StopCoroutine(currentVibration);

        currentVibration = StartCoroutine(VibrateRoutine(duration));
    }

    IEnumerator VibrateRoutine(float duration)
    {
        port.Write(onCmd, 0, onCmd.Length);
        yield return new WaitForSeconds(duration);
        port.Write(offCmd, 0, offCmd.Length);
        currentVibration = null;
    }

    void OnApplicationQuit() => Disconnect();

    void OnDestroy() => Disconnect();
}
