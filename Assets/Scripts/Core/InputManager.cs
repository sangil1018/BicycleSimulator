using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Ports;
using System.Threading;
using UnityEngine;
using UnityEngine.InputSystem;

public enum VibeState
{
    Stop = 0,
    Danger = 1,
    Success = 2,
    Correct = 3,
    Wrong = 4,
    Walk = 5,
    Ready = 6
}

[DefaultExecutionOrder(-100)]
public class InputManager : Singleton<InputManager>
{
    [Header("Serial Port — ESP32-S3")]
    [SerializeField] string portName = "COM8";
    [SerializeField] int baudRate = 115200;
    [SerializeField] bool autoConnect = true;

    [Header("Config")]
    public float BaseSpeedKph { get; private set; } = 15f;
    public bool ShowLogo { get; private set; } = true;
    public float SteeringRange { get; private set; } = 45f;
    public float YellowThreshold { get; private set; } = 20f;
    public float RedThreshold { get; private set; } = 30f;
    public float PlaybackMultiplier { get; private set; } = 1f;
    public float CameraSteerSmoothTime { get; private set; } = 0.12f;

    [Header("Keyboard")]
    [SerializeField] bool keyboardEnabled = true;
    [Tooltip("Space 키를 누를 때 적용되는 가상 속도 (km/h)")]
    [SerializeField] float keyboardSpeedKph = 15f;

    public float CadenceRPM { get; private set; }
    public float SpeedKph { get; private set; }
    public float SteeringAngle { get; private set; }
    public bool Brake { get; private set; }
    public bool BrakeAny => Brake;
    public bool BtnOHeld { get; private set; }
    public bool BtnXHeld { get; private set; }
    public bool BtnODown { get; private set; }
    public bool BtnXDown { get; private set; }
    public bool BrkDown { get; private set; }

    public event Action OnBtnO, OnBtnX, OnBrakeAny;
    public event Action OnCalibrated;
    public event Action<string> OnMagCalMessage;
    public event Action<string> OnErrorMessage;
    public event Action<bool?> OnSelectionChanged;

    public bool IsConnected => _serial?.IsOpen ?? false;

    readonly object _lock = new();
    readonly Queue<string> _specialQueue = new();
    readonly List<string> _specialDrain = new(4);
    BikeInputData _pending;
    bool _hasNew;
    bool _prevO, _prevX, _prevBrk;
    SerialPort _serial;
    Thread _thread;
    bool _running;
    bool _dmpReady = false;
    float _dmpReadyFallbackTime = -1f;
    const float DMP_STABLE_TIMEOUT = 3f;
    float _yawOffset = 0f;
    bool _yawCalibrated = false;
    int _expectedStationID = 1;

    float _vibeMultiplier = 1.0f;
    bool? _pendingAnswer;
    InputSystem_Actions _actions;

    protected override void Awake()
    {
        base.Awake();
        LoadConfig();
        _actions = new InputSystem_Actions();
        _actions.BikeGame.Enable();
        if (autoConnect) Connect();
    }

    void LoadConfig()
    {
        string path = Path.Combine(Application.dataPath, "../config.ini");
        if (!File.Exists(path))
        {
            Debug.Log($"[Input] 설정 파일 없음: {path}. 기본값을 사용합니다.");
            return;
        }

        try
        {
            var lines = File.ReadAllLines(path);
            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line) || line.TrimStart().StartsWith(";")) continue;
                var parts = line.Split('=');
                if (parts.Length != 2) continue;

                string key = parts[0].Trim();
                string val = parts[1].Trim();

                switch (key)
                {
                    case "PortName": portName = val; break;
                    case "BaudRate": int.TryParse(val, out baudRate); break;
                    case "BaseSpeedKph": float.TryParse(val, out float s); BaseSpeedKph = s; break;
                    case "logo": ShowLogo = !int.TryParse(val, out int logo) || logo != 0; break;
                    case "SteeringRange": if (float.TryParse(val, out float sr)) SteeringRange = Mathf.Clamp(sr, 1f, 45f); break;
                    case "YellowThreshold": if (float.TryParse(val, out float yt)) YellowThreshold = yt; break;
                    case "RedThreshold": if (float.TryParse(val, out float rt)) RedThreshold = rt; break;
                    case "PlaybackMultiplier": if (float.TryParse(val, out float pm)) PlaybackMultiplier = Mathf.Max(0.01f, pm); break;
                    case "CameraSteerSmoothTime": if (float.TryParse(val, out float ct)) CameraSteerSmoothTime = Mathf.Max(0f, ct); break;
                    case "StationID": int.TryParse(val, out _expectedStationID); break;
                    case "VibeMultiplier": if (float.TryParse(val, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float vm)) _vibeMultiplier = Mathf.Clamp(vm, 0.5f, 3.0f); break;
                }
            }
            Debug.Log($"[Input] 설정 로드: ESP32={portName}@{baudRate}");
        }
        catch (Exception e)
        {
            Debug.LogError($"[Input] 설정 파일 읽기 실패: {e.Message}");
        }
    }

    // ── ESP32-S3 연결 ────────────────────────────────────────────────
    public void Connect()
    {
        Disconnect();
        try
        {
            _serial = new SerialPort(portName, baudRate)
            {
                ReadTimeout = 100,
                WriteTimeout = 100,
                DtrEnable = true,
                NewLine = "\n"
            };
            _serial.Open();
            _running = true;
            _dmpReady = false;
            _dmpReadyFallbackTime = Time.time + DMP_STABLE_TIMEOUT;
            _yawCalibrated = false;
            _yawOffset = 0f;
            _thread = new Thread(ReadLoop) { IsBackground = true, Name = "BikeSerial" };
            _thread.Start();
            SendRaw($"P{Mathf.RoundToInt(_vibeMultiplier * 100)}");
            Debug.Log($"[Input] ESP32 {portName} 연결됨 (VibeScale={_vibeMultiplier:F1}x)");
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[Input] ESP32 연결 실패: {e.Message}");
        }
    }

    public void Disconnect()
    {
        _running = false;
        _thread?.Join(500);
        _thread = null;
        if (_serial?.IsOpen == true) _serial.Close();
        _serial = null;
    }

    void ReadLoop()
    {
        while (_running)
        {
            try
            {
                string line = _serial.ReadLine();
                if (string.IsNullOrWhiteSpace(line)) continue;
                line = line.Trim();

                if (line.Contains("\"debug\"") ||
                    line.Contains("\"calibrated\"") ||
                    line.Contains("\"magcal\""))
                {
                    lock (_lock) { _specialQueue.Enqueue(line); }
                    continue;
                }

                if (!line.StartsWith("{")) continue;
                var d = JsonUtility.FromJson<BikeInputData>(line);
                if (d.id != _expectedStationID) continue;
                lock (_lock) { _pending = d; _hasNew = true; }
            }
            catch (TimeoutException) { }
            catch (Exception e) { if (_running) Debug.LogWarning($"[Input] {e.Message}"); }
        }
    }

    void Update()
    {
        BtnODown = BtnXDown = BrkDown = false;

        BikeInputData snap = default;
        bool hasSnap = false;
        lock (_lock)
        {
            while (_specialQueue.Count > 0)
                _specialDrain.Add(_specialQueue.Dequeue());
            if (_hasNew) { snap = _pending; hasSnap = true; _hasNew = false; }
        }

        foreach (var s in _specialDrain) ProcessSpecialMessage(s);
        _specialDrain.Clear();

        if (!_dmpReady && _dmpReadyFallbackTime > 0f && Time.time >= _dmpReadyFallbackTime)
        {
            _dmpReady = true;
            Debug.Log("[Input] DMP 안정화 메시지 미수신 — 타임아웃으로 자동 활성화");
            SendCalibrate();
            SendVibrate(VibeState.Ready);
        }

        if (hasSnap && _dmpReady)
            ApplyData(snap);
        else if (!IsConnected)
            CadenceRPM = SpeedKph = SteeringAngle = 0f;

        if (keyboardEnabled) UpdateKeyboard();
    }

    void UpdateKeyboard()
    {
        var bike = _actions.BikeGame;

        if (bike.Forward.IsPressed())
        {
            CadenceRPM = Mathf.Max(CadenceRPM, keyboardSpeedKph / 0.25f);
            SpeedKph = Mathf.Max(SpeedKph, keyboardSpeedKph);
        }

        if (bike.SelectO.WasPressedThisFrame()) SetPendingAnswer(true);
        if (bike.SelectX.WasPressedThisFrame()) SetPendingAnswer(false);

        if (bike.Confirm.WasPressedThisFrame())
        {
            if (_pendingAnswer.HasValue)
            {
                bool ans = _pendingAnswer.Value;
                SetPendingAnswer(null);
                if (ans) OnBtnO?.Invoke();
                else OnBtnX?.Invoke();
            }
            else
            {
                OnBtnO?.Invoke();
            }
        }

        if (bike.Exit.WasPressedThisFrame()) OnBtnX?.Invoke();
    }

    void SetPendingAnswer(bool? value)
    {
        if (_pendingAnswer == value) return;
        _pendingAnswer = value;
        OnSelectionChanged?.Invoke(value);
    }

    void ProcessSpecialMessage(string line)
    {
        Debug.Log($"[Input] {line}");
        if (line.Contains("\"calibrated\"")) OnCalibrated?.Invoke();
        else if (line.Contains("\"magcal\"")) OnMagCalMessage?.Invoke(line);
        else if (line.Contains("\"debug\""))
        {
            if (line.Contains("DMP Stabilized"))
            {
                _dmpReady = true;
                SendRaw($"P{Mathf.RoundToInt(_vibeMultiplier * 100)}");
                SendCalibrate();
                SendVibrate(VibeState.Ready);
            }
            OnErrorMessage?.Invoke(line);
        }
    }

    void ApplyData(BikeInputData d)
    {
        CadenceRPM = Mathf.Max(0f, d.rpm);
        SpeedKph = Mathf.Max(0f, d.spd);

        const float YawCalibThreshold = 5f;
        if (!_yawCalibrated)
        {
            if (Mathf.Abs(d.str) <= YawCalibThreshold)
            {
                _yawOffset = d.str;
                _yawCalibrated = true;
                Debug.Log($"[Input] Yaw 보정 완료: offset={_yawOffset:F2}°");
            }
            else
            {
                Debug.Log($"[Input] Yaw 보정 대기 중 (str={d.str:F2}° > ±{YawCalibThreshold}°, 핸들을 센터로)");
            }
        }
        SteeringAngle = (d.str - _yawOffset) / 45f * SteeringRange;
        Debug.Log($"[Input] raw str={d.str:F2}  offset={_yawOffset:F2}  SteeringAngle={SteeringAngle:F2}");

        bool brk = d.brk == 1, o = d.o == 1, x = d.x == 1;
        Brake = brk;
        BtnOHeld = o; BtnXHeld = x;

        BtnODown = o && !_prevO;
        BtnXDown = x && !_prevX;
        BrkDown = brk && !_prevBrk;

        if (brk != _prevBrk)
            SendRaw(brk ? "B1" : "B0");

        if (BtnODown) OnBtnO?.Invoke();
        if (BtnXDown) OnBtnX?.Invoke();
        if (BrkDown && BrakeAny) OnBrakeAny?.Invoke();

        _prevO = o; _prevX = x;
        _prevBrk = brk;
    }

    void OnDestroy()
    {
        Disconnect();
        _actions?.BikeGame.Disable();
        _actions?.Dispose();
    }

    public void Simulate(float cadenceRpm, float steering, float speedKph = -1f,
                         bool brk = false, bool o = false, bool x = false)
    {
        float spd = speedKph >= 0f ? speedKph : cadenceRpm * 0.25f;
        ApplyData(new BikeInputData
        {
            id = 1,
            rpm = cadenceRpm,
            spd = spd,
            str = steering,
            brk = brk ? 1 : 0,
            o = o ? 1 : 0,
            x = x ? 1 : 0
        });
    }

    // ── Unity → ESP32-S3 송신 ────────────────────────────────────────
    public void SendVibrate(VibeState state)
    {
        Debug.Log($"[Input] SendVibrate → {state} (V{(int)state}), connected={IsConnected}");
        SendRaw($"V{(int)state}");
    }

    void SendRaw(string cmd)
    {
        if (_serial?.IsOpen != true) return;
        try { _serial.WriteLine(cmd); }
        catch (Exception e) { Debug.LogWarning($"[Input] ESP32 송신 실패: {e.Message}"); }
    }

    public void SendCalibrate() => SendRaw("C");
    public void SendMagCal()    => SendRaw("M");
}
