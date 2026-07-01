using System;
using System.Collections;
using System.Globalization;
using System.IO;
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
    [SerializeField] float shortDuration = 0.15f;
    [SerializeField] float mediumDuration = 0.5f;
    [SerializeField] float longDuration = 1.5f;

    // 보드에 맞는 명령어로 교체 필요
    readonly byte[] onCmd = { 0xA0, 0x01, 0x01, 0xA2 };
    readonly byte[] offCmd = { 0xA0, 0x01, 0x00, 0xA1 };

    SerialPort port;
    Coroutine currentVibration;

    public bool IsConnected => port?.IsOpen ?? false;

    protected override void Awake()
    {
        base.Awake();
        LoadConfig();
        if (autoConnect) Connect();
    }

    void LoadConfig()
    {
        string path = Path.Combine(Application.dataPath, "../config.ini");
        if (!File.Exists(path))
        {
            Debug.Log($"[VibrationRelay] 설정 파일 없음: {path}. 기본값을 사용합니다.");
            return;
        }

        try
        {
            foreach (var line in File.ReadAllLines(path))
            {
                if (string.IsNullOrWhiteSpace(line) || line.TrimStart().StartsWith(";")) continue;
                var parts = line.Split('=');
                if (parts.Length != 2) continue;

                string key = parts[0].Trim();
                string val = parts[1].Trim();

                switch (key)
                {
                    case "RelayPortName": portName = val; break;
                    case "RelayBaudRate": int.TryParse(val, out baudRate); break;
                    case "VibeShortDuration": if (float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out float sd)) shortDuration = sd; break;
                    case "VibeMediumDuration": if (float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out float md)) mediumDuration = md; break;
                    case "VibeLongDuration": if (float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out float ld)) longDuration = ld; break;
                }
            }
            Debug.Log($"[VibrationRelay] 설정 로드: {portName}@{baudRate}, S={shortDuration:F2} M={mediumDuration:F2} L={longDuration:F2}");
        }
        catch (Exception e)
        {
            Debug.LogError($"[VibrationRelay] 설정 파일 읽기 실패: {e.Message}");
        }
    }

    public void Connect()
    {
        Disconnect();
        try
        {
            port = new SerialPort(portName, baudRate) { WriteTimeout = 200 };
            port.Open();
            Debug.Log($"[VibrationRelay] {portName} 연결됨");
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[VibrationRelay] 연결 실패: {e.Message}");
        }
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
    }

    // ===== 프리셋 호출용 =====
    public void VibrateShort()  => Vibrate(shortDuration);
    public void VibrateMedium() => Vibrate(mediumDuration);
    public void VibrateLong()   => Vibrate(longDuration);

    // 커스텀 길이가 필요하면 이것도 그대로 사용 가능
    public void Vibrate(float duration)
    {
        if (port == null || !port.IsOpen)
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
