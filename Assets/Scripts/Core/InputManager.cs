using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Ports;
using System.Linq;
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
    Walk = 5
}

[DefaultExecutionOrder(-100)]
public class InputManager : Singleton<InputManager>
{
    [Header("Serial Port")]
    [SerializeField] string portName = "COM8";
    [SerializeField] int baudRate = 115200;
    [SerializeField] bool autoConnect = true;

    [Header("Config")]
    public float BaseSpeedKph { get; private set; } = 15f;
    public bool ShowLogo { get; private set; } = true;
    public float SteeringRange { get; private set; } = 45f;
    public float YellowThreshold { get; private set; } = 20f;
    public float RedThreshold { get; private set; } = 30f;

    [Header("Keyboard")]
    [SerializeField] bool keyboardEnabled = true;
    [Tooltip("Space 키를 누를 때 적용되는 가상 속도 (km/h)")]
    [SerializeField] float keyboardSpeedKph = 20f;

    public float CadenceRPM { get; private set; }
    public float SpeedKph { get; private set; }
    public float SteeringAngle { get; private set; }
    public bool BrakeLeft { get; private set; }
    public bool BrakeRight { get; private set; }
    public bool BrakeAny => BrakeLeft || BrakeRight;
    public bool BtnOHeld { get; private set; }
    public bool BtnXHeld { get; private set; }
    public bool BtnODown { get; private set; }
    public bool BtnXDown { get; private set; }
    public bool BrkLDown { get; private set; }
    public bool BrkRDown { get; private set; }

    public event Action OnBtnO, OnBtnX, OnBrakeAny;
    public event Action OnCalibrated;
    public event Action<string> OnMagCalMessage;
    public event Action<string> OnErrorMessage;
    /// <summary>방향키 O/X 선택 변경. true=O, false=X, null=선택 없음 (모든 상태에서 동작)</summary>
    public event Action<bool?> OnSelectionChanged;

    public bool IsConnected => _serial?.IsOpen ?? false;

    readonly object _lock = new();
    readonly Queue<string> _specialQueue = new();
    readonly List<string> _specialDrain = new(4);   // Update()에서 재사용, 매 이벤트 배열 할당 방지
    BikeInputData _pending;
    bool _hasNew;
    bool _prevO, _prevX, _prevBrkL, _prevBrkR;
    SerialPort _serial;
    Thread _thread;
    bool _running;
    bool _dmpReady = false;
    float _dmpReadyFallbackTime = -1f;
    const float DMP_STABLE_TIMEOUT = 3f;

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
                }
            }
            Debug.Log($"[Input] 설정 로드 완료: Port={portName}, Baud={baudRate}, BaseSpeed={BaseSpeedKph}, ShowLogo={ShowLogo}, YellowThreshold={YellowThreshold}, RedThreshold={RedThreshold}");
        }
        catch (Exception e)
        {
            Debug.LogError($"[Input] 설정 파일 읽기 실패: {e.Message}");
        }
    }

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
                NewLine = "\n"   // ESP32는 LF 한 문자만 전송
            };
            _serial.Open();
            _running = true;
            _dmpReady = false;
            _dmpReadyFallbackTime = Time.time + DMP_STABLE_TIMEOUT;
            _thread = new Thread(ReadLoop) { IsBackground = true, Name = "BikeSerial" };
            _thread.Start();
            Debug.Log($"[Input] {portName} 연결됨");
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[Input] 연결실패: {e.Message}");
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

                // 이벤트성 특수 메시지 — 메인 스레드에서 처리
                if (line.Contains("\"debug\"") ||
                    line.Contains("\"calibrated\"") ||
                    line.Contains("\"magcal\""))
                {
                    lock (_lock) { _specialQueue.Enqueue(line); }
                    continue;
                }

                if (!line.StartsWith("{")) continue;
                var d = JsonUtility.FromJson<BikeInputData>(line);
                // 부팅 직후 깨진 첫 줄 방지: id 필드로 유효성 검사
                if (d.id != 1) continue;
                lock (_lock) { _pending = d; _hasNew = true; }
            }
            catch (TimeoutException) { }
            catch (Exception e) { if (_running) Debug.LogWarning($"[Input] {e.Message}"); }
        }
    }

    void Update()
    {
        BtnODown = BtnXDown = BrkLDown = BrkRDown = false;

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
        }

        if (hasSnap && _dmpReady)
            ApplyData(snap);
        else if (!IsConnected)
            CadenceRPM = SpeedKph = SteeringAngle = 0f;  // 연결 없을 때 잔류값 초기화

        if (keyboardEnabled) UpdateKeyboard();
    }

    // ── 키보드 입력 (New Input System — InputSystem_Actions.BikeGame 맵) ──
    void UpdateKeyboard()
    {
        var bike = _actions.BikeGame;

        // Space — 앞으로 가기 (시리얼 속도보다 낮지 않을 때만 반영)
        if (bike.Forward.IsPressed())
        {
            CadenceRPM = Mathf.Max(CadenceRPM, keyboardSpeedKph / 0.25f);
            SpeedKph = Mathf.Max(SpeedKph, keyboardSpeedKph);
        }

        // 방향키 — O/X 선택 (모든 상태에서 동작)
        if (bike.SelectO.WasPressedThisFrame()) SetPendingAnswer(true);   // ←  O (사용자 요청: 왼쪽이 O)
        if (bike.SelectX.WasPressedThisFrame()) SetPendingAnswer(false);  // →  X (사용자 요청: 오른쪽이 X)

        // 1 — 시작 / 확인: 선택된 답 확정, 미선택이면 O 버튼
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

        // Esc — 종료
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
            if (line.Contains("DMP Stabilized")) _dmpReady = true;
            OnErrorMessage?.Invoke(line);
        }
    }

    void ApplyData(BikeInputData d)
    {
        CadenceRPM = Mathf.Max(0f, d.rpm);
        SpeedKph = Mathf.Max(0f, d.spd);
        SteeringAngle = d.str / 45f * SteeringRange;

        bool bL = d.brkL == 1, bR = d.brkR == 1, o = d.o == 1, x = d.x == 1;
        BrakeLeft = bL; BrakeRight = bR;
        BtnOHeld = o; BtnXHeld = x;

        BtnODown = o && !_prevO;
        BtnXDown = x && !_prevX;
        BrkLDown = bL && !_prevBrkL;
        BrkRDown = bR && !_prevBrkR;

        if (BtnODown) OnBtnO?.Invoke();
        if (BtnXDown) OnBtnX?.Invoke();
        if ((BrkLDown || BrkRDown) && BrakeAny) OnBrakeAny?.Invoke();

        _prevO = o; _prevX = x;
        _prevBrkL = bL; _prevBrkR = bR;
    }

    void OnDestroy()
    {
        Disconnect();
        _actions?.BikeGame.Disable();
        _actions?.Dispose();
    }

    public void Simulate(float cadenceRpm, float steering, float speedKph = -1f,
                         bool brkL = false, bool brkR = false, bool o = false, bool x = false)
    {
        float spd = speedKph >= 0f ? speedKph : cadenceRpm * 0.25f;
        ApplyData(new BikeInputData
        {
            id = 1,
            rpm = cadenceRpm,
            spd = spd,
            str = steering,
            brkL = brkL ? 1 : 0,
            brkR = brkR ? 1 : 0,
            o = o ? 1 : 0,
            x = x ? 1 : 0
        });
    }

    // ── Unity → ESP32 송신 ────────────────────────────────────────────

    public void SendVibrate(VibeState state) { if (_serial?.IsOpen == true) _serial.WriteLine($"V{(int)state}"); }
    public void SendCalibrate() { if (_serial?.IsOpen == true) _serial.WriteLine("C"); }
    public void SendMagCal() { if (_serial?.IsOpen == true) _serial.WriteLine("M"); }
}
