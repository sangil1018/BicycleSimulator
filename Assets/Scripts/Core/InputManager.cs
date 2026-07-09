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
    Ready = 6,
    Click = 7
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
    public float MetersPerRevolution { get; private set; } = 1.5f;
    public bool ShowLogo { get; private set; } = true;
    public float SteeringRange { get; private set; } = 45f;
    public float YellowThreshold { get; private set; } = 20f;
    public float RedThreshold { get; private set; } = 30f;
    public float PlaybackMultiplier { get; private set; } = 1f;
    public float MaxRate { get; private set; } = 1.5f;
    public float CameraSteerSmoothTime { get; private set; } = 0.12f;
    public float BrakeStopDuration { get; private set; } = 1.0f;
    public int TargetFps { get; private set; } = 60;
    public bool DebugMode { get; private set; } = false;

    [Header("Config — Vibration Relay")]
    public bool VibrationActive { get; private set; } = true;
    public string RelayPortName { get; private set; } = "COM3";
    public int RelayBaudRate { get; private set; } = 9600;
    public float VibeShortDuration { get; private set; } = 0.2f;
    public float VibeMediumDuration { get; private set; } = 0.5f;
    public float VibeLongDuration { get; private set; } = 1.5f;
    /// <summary>진동 지속시간 배율. VibrationRelay가 프리셋 길이에 곱해 사용한다.</summary>
    public float VibeMultiplier { get; private set; } = 1.0f;

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
    /// <summary>조향 센서(ICM-20948) 인식 여부. 미인식 시 펌웨어가 str=0 고정 송신.</summary>
    public bool SteerSensorOk { get; private set; } = true;

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
    float _lastDataTime = -1f;
    bool _dataStale = false;
    const float DATA_TIMEOUT = 0.5f; // 펌웨어 50Hz 송신 기준 — 이 시간 무수신 시 입력값 0 처리
    float _lastConnectAttempt = -999f; // 마지막 연결 시도 시각
    int _connectFailCount = 0;         // 연속 연결 실패 횟수 (재시도마다 로그 표기)
    string _lastConnectError = "";     // 마지막 연결 실패 예외 메시지
    const float RECONNECT_INTERVAL = 5f; // 미연결 시 재연결 시도 주기 (초)
    bool _wasBraking = false;
    float _brakeDecelRate = 0f; // 브레이크 시작 시점 속도 / BrakeStopDuration (km/h per sec)
    float _yawOffset = 0f;
    bool _yawCalibrated = false;
    int _expectedStationID = 1;

    bool? _pendingAnswer;
    InputSystem_Actions _actions;

#if DEBUG_GUI
    float _dbgRawStr;
    GUIStyle _debugStyle;
#endif

    protected override void Awake()
    {
        base.Awake();
        if (Instance != this) return; // 중복 인스턴스 — Destroy 예정이므로 연결 시도 등 부수효과 생략
#if DEBUG_GUI
        useGUILayout = false; // GUILayout 미사용 — Layout 이벤트 패스 생략으로 OnGUI 상주 비용 절감
#endif
        LoadConfig();
        ApplyFrameRate();
        _actions = new InputSystem_Actions();
        _actions.BikeGame.Enable();
        if (autoConnect) AttemptConnect();
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
                    case "MetersPerRevolution": if (float.TryParse(val, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float mpr)) MetersPerRevolution = Mathf.Max(0.01f, mpr); break;
                    case "logo": ShowLogo = !int.TryParse(val, out int logo) || logo != 0; break;
                    case "SteeringRange": if (float.TryParse(val, out float sr)) SteeringRange = Mathf.Clamp(sr, 1f, 45f); break;
                    case "YellowThreshold": if (float.TryParse(val, out float yt)) YellowThreshold = yt; break;
                    case "RedThreshold": if (float.TryParse(val, out float rt)) RedThreshold = rt; break;
                    case "PlaybackMultiplier": if (float.TryParse(val, out float pm)) PlaybackMultiplier = Mathf.Max(0.01f, pm); break;
                    case "MaxRate": if (float.TryParse(val, out float mr)) MaxRate = Mathf.Max(0.01f, mr); break;
                    case "CameraSteerSmoothTime": if (float.TryParse(val, out float ct)) CameraSteerSmoothTime = Mathf.Max(0f, ct); break;
                    case "BrakeStopDuration": if (float.TryParse(val, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float bsd)) BrakeStopDuration = Mathf.Clamp(bsd, 0.05f, 10f); break;
                    case "fps": if (int.TryParse(val, out int fps)) TargetFps = fps <= 0 ? 0 : Mathf.Clamp(fps, 15, 240); break;
                    case "debugMode": DebugMode = int.TryParse(val, out int dm) && dm != 0; break;
                    case "StationID": int.TryParse(val, out _expectedStationID); break;
                    case "VibeMultiplier": if (float.TryParse(val, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float vm)) VibeMultiplier = Mathf.Clamp(vm, 0.5f, 3.0f); break;
                    case "isActive": VibrationActive = !int.TryParse(val, out int va) || va != 0; break;
                    case "RelayPortName": RelayPortName = val; break;
                    case "RelayBaudRate": if (int.TryParse(val, out int rb)) RelayBaudRate = rb; break;
                    case "VibeShortDuration": if (float.TryParse(val, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float vsd)) VibeShortDuration = vsd; break;
                    case "VibeMediumDuration": if (float.TryParse(val, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float vmd)) VibeMediumDuration = vmd; break;
                    case "VibeLongDuration": if (float.TryParse(val, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float vld)) VibeLongDuration = vld; break;
                }
            }
            Debug.Log($"[Input] 설정 로드: ESP32={portName}@{baudRate}");
        }
        catch (Exception e)
        {
            Debug.LogError($"[Input] 설정 파일 읽기 실패: {e.Message}");
        }
    }

    void ApplyFrameRate()
    {
        // VSync가 켜져 있으면 targetFrameRate가 무시되므로 반드시 비활성화
        QualitySettings.vSyncCount = 0;
        Application.targetFrameRate = TargetFps > 0 ? TargetFps : -1;
        Debug.Log($"[Input] 목표 프레임레이트: {(TargetFps > 0 ? $"{TargetFps}fps" : "제한 없음")}");
    }

    // ── ESP32-S3 연결 ────────────────────────────────────────────────

    // 미연결 시 RECONNECT_INTERVAL 주기로 재시도. 재시도마다 결과를 로그로 남긴다.
    void AttemptConnect()
    {
        _lastConnectAttempt = Time.time;
        if (Connect())
        {
            Debug.Log(_connectFailCount > 0
                ? $"[Input] ESP32 {portName} 연결됨 (VibeScale={VibeMultiplier:F1}x, {_connectFailCount}회 실패 후 복구)"
                : $"[Input] ESP32 {portName} 연결됨 (VibeScale={VibeMultiplier:F1}x)");
            _connectFailCount = 0;
        }
        else
        {
            _connectFailCount++;
            Debug.LogWarning($"[Input] ESP32 연결 실패 ({_connectFailCount}회): {_lastConnectError} — {RECONNECT_INTERVAL:F0}초 후 재시도 (포트 {portName} 확인)");
        }
    }

    // 포트 열고 수신 스레드 시작까지 성공하면 true. 로그는 호출부(AttemptConnect)에서 처리.
    public bool Connect()
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
            SteerSensorOk = true;
            _lastDataTime = -1f;
            _dataStale = false;
            _thread = new Thread(ReadLoop) { IsBackground = true, Name = "BikeSerial" };
            _thread.Start();
            SendRaw($"P{Mathf.RoundToInt(VibeMultiplier * 100)}");
            return true;
        }
        catch (Exception e)
        {
            _lastConnectError = e.Message;
            try { _serial?.Dispose(); } catch { }
            _serial = null;
            return false;
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

        // 미연결 시 주기적으로 자동 재연결 시도 (ESP32를 나중에 꽂거나 재부팅해도 복구)
        if (autoConnect && !IsConnected && Time.time - _lastConnectAttempt >= RECONNECT_INTERVAL)
            AttemptConnect();

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
        {
            ApplyData(snap);
            _lastDataTime = Time.time;
            if (_dataStale)
            {
                _dataStale = false;
                Debug.Log("[Input] ESP32 데이터 수신 재개");
            }
        }
        else if (!IsConnected)
        {
            CadenceRPM = SteeringAngle = 0f;
        }
        else if (_lastDataTime > 0f && !_dataStale && Time.time - _lastDataTime > DATA_TIMEOUT)
        {
            // 연결은 살아있지만 데이터가 끊김 — 이전 입력값이 남지 않게 0으로 리셋
            _dataStale = true;
            CadenceRPM = SteeringAngle = 0f;
            Brake = BtnOHeld = BtnXHeld = false;
            _prevO = _prevX = _prevBrk = false;
            Debug.LogWarning($"[Input] ESP32 데이터 {DATA_TIMEOUT:F1}s 무수신 — 입력값 0으로 리셋");
        }

        if (keyboardEnabled) UpdateKeyboard();

        // 케이던스(RPM) × 1회전당 이동거리(m) 기준으로 매 프레임 속도 재계산 (RPM × m/rev × 60/1000)
        float rawSpeed = CadenceRPM * MetersPerRevolution * 0.06f;

        if (BrakeAny)
        {
            if (!_wasBraking)
            {
                _wasBraking = true;
                _brakeDecelRate = Mathf.Max(SpeedKph, rawSpeed) / BrakeStopDuration;
            }
            // 브레이크 유지 중에는 페달 입력을 무시하고 BrakeStopDuration 안에 0까지 선형 감속
            SpeedKph = Mathf.MoveTowards(SpeedKph, 0f, _brakeDecelRate * Time.deltaTime);
        }
        else
        {
            _wasBraking = false;
            SpeedKph = rawSpeed;
        }
    }

    void UpdateKeyboard()
    {
        var bike = _actions.BikeGame;

        if (bike.Forward.IsPressed())
        {
            float kphPerRpm = MetersPerRevolution * 0.06f;
            CadenceRPM = Mathf.Max(CadenceRPM, keyboardSpeedKph / kphPerRpm);
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
                // 타임아웃 폴백이 이미 활성화했다면 캘리브레이션·진동을 다시 실행하지 않는다.
                bool alreadyReady = _dmpReady;
                _dmpReady = true;
                SteerSensorOk = true;
                if (!alreadyReady)
                {
                    SendRaw($"P{Mathf.RoundToInt(VibeMultiplier * 100)}");
                    SendCalibrate();
                    SendVibrate(VibeState.Ready);
                }
            }
            else if (line.Contains("Steer sensor NOT found"))
            {
                // 펌웨어가 조향 센서 인식 실패 — str=0 고정 송신 모드. 대기 없이 바로 활성화
                bool alreadyReady = _dmpReady;
                SteerSensorOk = false;
                _dmpReady = true;
                _yawCalibrated = true;
                _yawOffset = 0f;
                if (!alreadyReady)
                {
                    SendRaw($"P{Mathf.RoundToInt(VibeMultiplier * 100)}");
                    SendVibrate(VibeState.Ready);
                }
                Debug.LogWarning("[Input] 조향 센서 미인식 — 조향각 0 고정으로 진행 (페달/브레이크/버튼은 정상)");
            }
            OnErrorMessage?.Invoke(line);
        }
    }

    void ApplyData(BikeInputData d)
    {
        CadenceRPM = Mathf.Max(0f, d.rpm);
#if DEBUG_GUI
        _dbgRawStr = d.str;
#endif

        const float YawCalibThreshold = 5f;
        if (!_yawCalibrated)
        {
            if (Mathf.Abs(d.str) <= YawCalibThreshold)
            {
                _yawOffset = d.str;
                _yawCalibrated = true;
                Debug.Log($"[Input] Yaw 보정 완료: offset={_yawOffset:F2}°");
            }
        }
        SteeringAngle = (d.str - _yawOffset) / 45f * SteeringRange;

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
        float spd = speedKph >= 0f ? speedKph : cadenceRpm * MetersPerRevolution * 0.06f;
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

    // ── 진동 제어 (USB 릴레이 — ESP32와 별도 포트) ─────────────────────
    public void SendVibrate(VibeState state)
    {
        if (VibrationRelay.Instance == null)
        {
            Debug.LogWarning($"[Input] SendVibrate → {state} 무시 (VibrationRelay 없음)");
            return;
        }

        Debug.Log($"[Input] SendVibrate → {state}, relayConnected={VibrationRelay.Instance.IsConnected}");

        switch (state)
        {
            case VibeState.Stop:
                break;
            case VibeState.Ready:
            case VibeState.Walk:
            case VibeState.Correct:
            case VibeState.Click:
                VibrationRelay.Instance.VibrateShort();
                break;
            case VibeState.Success:
                VibrationRelay.Instance.VibrateMedium();
                break;
            case VibeState.Danger:
            case VibeState.Wrong:
                VibrationRelay.Instance.VibrateLong();
                break;
        }
    }

    // ── Unity → ESP32-S3 송신 ────────────────────────────────────────

    void SendRaw(string cmd)
    {
        if (_serial?.IsOpen != true) return;
        try { _serial.WriteLine(cmd); }
        catch (Exception e) { Debug.LogWarning($"[Input] ESP32 송신 실패: {e.Message}"); }
    }

    public void SendCalibrate() => SendRaw("C");
    public void SendMagCal() => SendRaw("M");

#if DEBUG_GUI
    void OnGUI()
    {
        if (!DebugMode) return;
        if (_debugStyle == null)
        {
            _debugStyle = new GUIStyle(GUI.skin.box) { fontSize = 34, alignment = TextAnchor.UpperLeft };
            _debugStyle.normal.textColor = Color.white;
        }
        string text =
            $"[InputManager Debug]\n" +
            $"Connected : {IsConnected}{(_dataStale ? " (수신끊김)" : "")}\n" +
            $"SteerSens : {(SteerSensorOk ? "OK" : "미인식(0고정)")}\n" +
            $"raw str   : {_dbgRawStr:F2}°\n" +
            $"yawOffset : {_yawOffset:F2}°\n" +
            $"Steering  : {SteeringAngle:F2}\n" +
            $"Speed     : {SpeedKph:F1} km/h\n" +
            $"Cadence   : {CadenceRPM:F0} RPM\n" +
            $"Brake     : {Brake}  O:{BtnOHeld}  X:{BtnXHeld}\n" +
            $"YawCalib  : {(_yawCalibrated ? "완료" : "대기중")}";
        GUI.Box(new Rect(10, 160, 560, 420), text, _debugStyle);
    }
#endif
}
