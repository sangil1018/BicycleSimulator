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
    Click = 7,
    Brake = 8
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

    // 워커가 갱신하는 미러 값. 메인 스레드는 SerialPort를 직접 조회하지 않는다.
    public bool IsConnected => _connected;
    /// <summary>조향 센서(ICM-20948) 인식 여부. 미인식 시 펌웨어가 str=0 고정 송신.</summary>
    public bool SteerSensorOk { get; private set; } = true;
    /// <summary>PAS 누적 펄스 수 (펌웨어 pc). 페달을 돌리는데 늘지 않으면 PAS 이상.</summary>
    public int PasPulseCount { get; private set; }
    /// <summary>PAS 핀 레벨 (펌웨어 pl). 페달을 돌리는데 토글하지 않으면 센서 전원/배선 문제.</summary>
    public int PasPinLevel { get; private set; }

    readonly object _lock = new();
    readonly Queue<string> _specialQueue = new();
    readonly List<string> _specialDrain = new(4);
    BikeInputData _pending;
    bool _hasNew;
    bool _prevO, _prevX, _prevBrk;

    // ── 시리얼 포트 소유권 ────────────────────────────────────────────
    // 포트의 열기·읽기·쓰기·닫기를 전부 워커 스레드가 소유한다. 메인 스레드는 _serial을
    // 절대 만지지 않는다. SerialPort.Open()/Close()는 장치가 제거된 상태에서 수 초간
    // 멈추거나 아예 걸릴 수 있어서(.NET의 알려진 이슈), 메인 스레드에서 호출하면 그대로
    // 게임이 멈춘다. 메인 스레드는 플래그를 세우고 큐에 명령을 넣는 것만 한다 — 전부 논블로킹.
    SerialPort _serial;                            // 워커 전용
    Thread _worker;
    volatile bool _running;                        // 워커 수명
    volatile bool _connected;                      // 메인 스레드가 보는 연결 상태 미러
    volatile bool _forceReconnect;                 // 메인 → 워커: 지금 끊고 다시 열어라
    volatile int _connectGeneration;               // 연결 성공마다 증가 — 메인이 "새 연결"을 감지
    int _seenGeneration;                           // 메인 스레드가 마지막으로 처리한 세대
    float _connectedSinceTime = -1f;               // 현재 연결이 수립된 시각 (메인 스레드 기준)
    readonly System.Collections.Concurrent.ConcurrentQueue<string> _txQueue = new();
    readonly AutoResetEvent _wake = new(false);    // 재시도 대기 중 종료·강제요청 시 즉시 깨움

    bool _dmpReady = false;
    float _dmpReadyFallbackTime = -1f;
    const float DMP_STABLE_TIMEOUT = 3f;
    float _lastDataTime = -1f;
    bool _dataStale = false;
    const float DATA_TIMEOUT = 0.5f; // 펌웨어 50Hz 송신 기준 — 이 시간 무수신 시 입력값 0 처리
    volatile int _connectFailCount = 0; // 연속 연결 실패 횟수 (워커가 갱신, GUI가 읽음)
    volatile string _lastConnectError = ""; // 마지막 연결 실패 예외 메시지
    const float RECONNECT_INTERVAL = 5f; // 연결 실패 시 재시도 주기 (초)
    // SerialPort.IsOpen은 장치가 죽어도 true를 유지하므로(좀비 포트),
    // 무수신 지속 시간을 기준으로 워커에 강제 재연결을 요청한다.
    const float STALE_RECONNECT_SEC = 5f;  // 수신 끊김이 이 시간 지속되면 강제 재연결
    const float FIRST_DATA_TIMEOUT = 10f;  // 연결 후 첫 데이터가 이 시간 안에 없으면 재연결 (보드 부팅 3초 고려)
    const float HEARTBEAT_INTERVAL = 10f;  // Unity→ESP32 keep-alive 주기 — USB OUT 트래픽 유지 (H 명령)
    float _lastHeartbeatTime = -999f;
    float _lastHbAckTime = -1f;            // 펌웨어 하트비트 응답 수신 시각 (진단용)
    // 수신 오류 백오프 — Time.time은 메인 스레드 전용이라 워커에서는 Environment.TickCount를 쓴다.
    int _readErrorCount = 0;
    int _lastReadErrorLogTick = 0;
    const int READ_ERROR_BACKOFF_MS = 100;       // ReadTimeout과 동일한 페이스로 재시도
    const int READ_ERROR_LOG_INTERVAL_MS = 2000; // 오류 지속 시 로그 폭주 방지
    const int READ_ERROR_LIMIT = 20;             // 연속 오류가 이만큼(약 2초) 쌓이면 포트를 닫고 재연결
    bool _wasBraking = false;
    float _brakeDecelRate = 0f; // 브레이크 시작 시점 속도 / BrakeStopDuration (km/h per sec)
    float _yawOffset = 0f;
    bool _yawCalibrated = false;
    int _expectedStationID = 1;

    bool? _pendingAnswer;
    InputSystem_Actions _actions;

    // ── PAS 워치독 ────────────────────────────────────────────────────
    // 펄스가 "늘고 있는지"(pc)와 핀이 "토글하는지"(pl)를 따로 추적한다. 두 신호가 갈리는
    // 지점이 곧 원인이다: 핀은 토글하는데 펄스가 안 늘면 신호는 오는데 못 세는 것 —
    // 즉 PAS 인터럽트가 죽은 것이고, 이건 R 명령(인터럽트 재등록)으로 복구된다.
    // FALLING 엣지가 실제로 발생하는데 카운터가 멈춰 있는 건 인터럽트가 살아 있는 한
    // 불가능한 조합이라, 오탐 걱정 없이 상시 감시할 수 있다.
    int _pasPrevCount;
    int _pasPrevLevel = -1;
    float _pasLastPulseTime = -1f;    // pc가 마지막으로 증가한 시각 (미수신이면 -1)
    float _pasLastToggleTime = -1f;   // pl이 마지막으로 바뀐 시각
    float _pasBaselineTime = -1f;     // 펄스 정지 시간의 기준점 (연결/재초기화 시각)
    float _lastPasReinitTime = -999f;
    int _pasReinitCount = 0;
    const float PAS_IDLE = 1.5f;             // 이 시간 이상 변화 없으면 "멈춤"으로 간주 (표시용)
    const float PAS_TOGGLE_WINDOW = 1f;      // 이 시간 안에 pl이 바뀌었으면 "지금 페달을 돌리는 중"
    const float PAS_STUCK_SEC = 2f;          // 핀은 토글하는데 펄스가 이 시간 멈춰 있으면 인터럽트 사망
    const float PAS_REINIT_COOLDOWN = 10f;   // 자동 재초기화 최소 간격 (복구 실패 시 폭주 방지)

#if DEBUG_GUI
    float _dbgRawStr;
    GUIStyle _debugStyle, _dbgStatusStyle;
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
        if (autoConnect) StartWorker();
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

    // ── ESP32-S3 연결 (전부 워커 스레드 소유) ─────────────────────────
    //
    // 메인 스레드가 하는 일: 플래그 세우기(_forceReconnect), 큐에 명령 넣기(_txQueue),
    // 세대 번호 확인(_connectGeneration). 전부 논블로킹이다.
    // 워커가 하는 일: 열기 → 읽기/쓰기 → 닫기 → 재시도. 여기서 몇 초를 멈추든 게임은 안 멈춘다.

    void StartWorker()
    {
        if (_worker != null) return;
        _running = true;
        _worker = new Thread(ConnectionLoop) { IsBackground = true, Name = "BikeSerial" };
        _worker.Start();
    }

    // 워커를 정지시킨다. Close()가 걸릴 수 있으므로 오래 기다리지 않는다 —
    // IsBackground 스레드라 끝까지 안 끝나도 프로세스 종료를 막지 않는다.
    void StopWorker()
    {
        _running = false;
        _wake.Set();
        _worker?.Join(300);
        _worker = null;
        _connected = false;
    }

    /// <summary>워커에 강제 재연결을 요청한다. 논블로킹 — 실제 닫기/열기는 워커가 수행한다.</summary>
    public void RequestReconnect(string reason)
    {
        if (_forceReconnect) return; // 이미 요청됨 — 중복 로그·중복 처리 방지
        UnityEngine.Debug.LogWarning($"[Input] 강제 재연결 요청 — {reason}");
        _forceReconnect = true;
        _wake.Set();
    }

    void ConnectionLoop()
    {
        try
        {
            while (_running)
            {
                if (!TryOpenPort())
                {
                    // 실패 — 재시도까지 대기하되, 종료·강제요청 시 즉시 깨어난다.
                    _wake.WaitOne((int)(RECONNECT_INTERVAL * 1000f));
                    continue;
                }

                PumpUntilBroken();
                ClosePort();
            }
        }
        catch (Exception e)
        {
            // 워커에서 예외가 새어나가면 스레드가 조용히 죽어 입력이 영구히 멈춘다.
            // 여기서 잡아 로그로 남기고, 아래 finally에서 포트를 반드시 정리한다.
            UnityEngine.Debug.LogError($"[Input] 시리얼 워커 비정상 종료: {e}");
        }
        finally
        {
            ClosePort();
        }
    }

    bool TryOpenPort()
    {
        try
        {
            var sp = new SerialPort(portName, baudRate)
            {
                ReadTimeout = 100,
                WriteTimeout = 100,
                DtrEnable = true,
                NewLine = "\n"
            };
            sp.Open();

            _serial = sp;
            _forceReconnect = false;
            _readErrorCount = 0;
            while (_txQueue.TryDequeue(out _)) { } // 이전 연결에서 남은 명령은 버린다
            _connected = true;
            _connectGeneration++;                  // 메인 스레드가 새 연결을 감지해 상태를 초기화한다

            // 연결 수명주기 로그는 현장(빌드) Player.log 진단용 — 전역 Debug 래퍼가 빌드에서
            // 로그를 제거하므로 UnityEngine.Debug를 직접 호출한다.
            UnityEngine.Debug.Log(_connectFailCount > 0
                ? $"[Input] ESP32 {portName} 연결됨 ({_connectFailCount}회 실패 후 복구)"
                : $"[Input] ESP32 {portName} 연결됨");
            _connectFailCount = 0;
            return true;
        }
        catch (Exception e)
        {
            _lastConnectError = e.Message;
            _connectFailCount++;
            _connected = false;
            _serial = null;

            // 장치를 안 꽂아둔 채로 며칠 돌 수도 있으므로, 실패 로그는 첫 회와 1분마다만 남긴다.
            if (_connectFailCount == 1 || _connectFailCount % 12 == 0)
                UnityEngine.Debug.LogWarning(
                    $"[Input] ESP32 연결 실패 ({_connectFailCount}회): {_lastConnectError} — " +
                    $"{RECONNECT_INTERVAL:F0}초마다 재시도 중 (포트 {portName} 확인)");
            return false;
        }
    }

    // 포트가 살아 있는 동안 송신 큐를 비우고 수신 라인을 처리한다. 끊기면 빠져나온다.
    void PumpUntilBroken()
    {
        while (_running && !_forceReconnect)
        {
            try
            {
                DrainTxQueue();

                string line = _serial.ReadLine();
                _readErrorCount = 0;
                HandleLine(line);
            }
            catch (TimeoutException) { } // 정상 — 잠시 데이터가 없을 뿐
            catch (Exception e)
            {
                if (!_running) break;

                // 장치가 제거되면 ReadLine()이 지연 없이 예외를 즉시 던진다. 그대로 재시도하면
                // busy-spin이 되어 코어 하나를 태우고 로그를 폭주시키므로, 정상 시의 ReadTimeout과
                // 같은 페이스로 늦춘다. 오류가 계속 쌓이면 포트를 닫고 재연결 사이클로 넘어간다.
                _readErrorCount++;
                int tick = Environment.TickCount;
                if (_readErrorCount == 1 || tick - _lastReadErrorLogTick >= READ_ERROR_LOG_INTERVAL_MS)
                {
                    _lastReadErrorLogTick = tick;
                    UnityEngine.Debug.LogWarning($"[Input] 시리얼 수신 오류 ({_readErrorCount}회): {e.Message}");
                }
                Thread.Sleep(READ_ERROR_BACKOFF_MS);
                if (_readErrorCount >= READ_ERROR_LIMIT) break;
            }
        }
    }

    // 송신도 워커가 한다 — WriteLine은 WriteTimeout(100ms)만큼 블로킹될 수 있어서
    // 메인 스레드에서 호출하면 하트비트 한 번에 프레임이 튄다.
    void DrainTxQueue()
    {
        while (_txQueue.TryDequeue(out string cmd))
        {
            try { _serial.WriteLine(cmd); }
            catch (Exception e) { UnityEngine.Debug.LogWarning($"[Input] ESP32 송신 실패({cmd}): {e.Message}"); }
        }
    }

    void HandleLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line)) return;

        // 특수 메시지(하트비트·보정·디버그)는 드물게 오므로 여기서만 Trim 할당을 허용한다.
        if (line.Contains("\"debug\"") ||
            line.Contains("\"calibrated\"") ||
            line.Contains("\"magcal\""))
        {
            lock (_lock) { _specialQueue.Enqueue(line.Trim()); }
            return;
        }

        // 텔레메트리는 초당 50개가 들어온다. JsonUtility.FromJson은 호출마다 내부 할당이
        // 있고 Trim()도 문자열을 새로 만들어, 이 경로만으로 초당 수십 KB의 가비지가 쌓인다.
        // 펌웨어가 보내는 형식이 고정(숫자만, 지수 표기 없음)이라 직접 훑는 편이 빠르고
        // 할당이 없다. 덤으로 로케일 의존(소수점 구분자)도 사라진다.
        BikeInputData d = default;
        if (!TryParseTelemetry(line, ref d)) return;
        if (d.id != _expectedStationID) return;
        lock (_lock) { _pending = d; _hasNew = true; }
    }

    // ── 무할당 텔레메트리 파서 ────────────────────────────────────────
    // {"id":1,"rpm":42.0,...} 형태를 키 기준으로 훑는다. 필드 순서가 바뀌거나 새 필드가
    // 늘어나도 견디고, 모르는 키는 그냥 건너뛴다. id가 없으면 유효한 패킷으로 보지 않는다.
    static bool TryParseTelemetry(string s, ref BikeInputData d)
    {
        int n = s.Length;
        int i = 0;
        while (i < n && s[i] != '{') i++;
        if (i >= n) return false;
        i++;

        bool hasId = false;
        while (i < n)
        {
            while (i < n && s[i] != '"' && s[i] != '}') i++;
            if (i >= n || s[i] == '}') break;

            int keyStart = ++i;
            while (i < n && s[i] != '"') i++;
            if (i >= n) break;
            int keyEnd = i++; // 키: [keyStart, keyEnd)

            while (i < n && s[i] != ':') i++;
            if (i >= n) break;
            i++;
            while (i < n && s[i] == ' ') i++;

            int valStart = i;
            while (i < n && s[i] != ',' && s[i] != '}') i++;
            int valEnd = i; // 값: [valStart, valEnd)
            if (i < n && s[i] == ',') i++;

            if (valEnd <= valStart || s[valStart] == '"') continue; // 빈 값·문자열 값은 건너뜀

            if (KeyIs(s, keyStart, keyEnd, "id")) { d.id = ParseInt(s, valStart, valEnd); hasId = true; }
            else if (KeyIs(s, keyStart, keyEnd, "rpm")) d.rpm = ParseFloat(s, valStart, valEnd);
            else if (KeyIs(s, keyStart, keyEnd, "spd")) d.spd = ParseFloat(s, valStart, valEnd);
            else if (KeyIs(s, keyStart, keyEnd, "str")) d.str = ParseFloat(s, valStart, valEnd);
            else if (KeyIs(s, keyStart, keyEnd, "brk")) d.brk = ParseInt(s, valStart, valEnd);
            else if (KeyIs(s, keyStart, keyEnd, "o")) d.o = ParseInt(s, valStart, valEnd);
            else if (KeyIs(s, keyStart, keyEnd, "x")) d.x = ParseInt(s, valStart, valEnd);
            else if (KeyIs(s, keyStart, keyEnd, "pc")) d.pc = ParseInt(s, valStart, valEnd);
            else if (KeyIs(s, keyStart, keyEnd, "pl")) d.pl = ParseInt(s, valStart, valEnd);
        }
        return hasId;
    }

    static bool KeyIs(string s, int start, int end, string key)
    {
        if (end - start != key.Length) return false;
        for (int k = 0; k < key.Length; k++)
            if (s[start + k] != key[k]) return false;
        return true;
    }

    static int ParseInt(string s, int i, int end)
    {
        bool neg = false;
        if (i < end && (s[i] == '-' || s[i] == '+')) { neg = s[i] == '-'; i++; }
        int v = 0;
        for (; i < end; i++)
        {
            char c = s[i];
            if (c < '0' || c > '9') break;
            v = v * 10 + (c - '0');
        }
        return neg ? -v : v;
    }

    // 펌웨어는 %.1f 형식만 보낸다 — 지수 표기(1e3)는 오지 않으므로 다루지 않는다.
    static float ParseFloat(string s, int i, int end)
    {
        bool neg = false;
        if (i < end && (s[i] == '-' || s[i] == '+')) { neg = s[i] == '-'; i++; }

        float v = 0f;
        for (; i < end; i++)
        {
            char c = s[i];
            if (c < '0' || c > '9') break;
            v = v * 10f + (c - '0');
        }
        if (i < end && s[i] == '.')
        {
            i++;
            float scale = 0.1f;
            for (; i < end; i++)
            {
                char c = s[i];
                if (c < '0' || c > '9') break;
                v += (c - '0') * scale;
                scale *= 0.1f;
            }
        }
        return neg ? -v : v;
    }

    // 워커 전용. 여기서 Close()가 몇 초 걸리거나 걸려도 게임 프레임에는 영향이 없다.
    void ClosePort()
    {
        _connected = false;
        var sp = _serial;
        _serial = null;
        if (sp == null) return;

        try { if (sp.IsOpen) sp.Close(); }
        catch (Exception e) { UnityEngine.Debug.LogWarning($"[Input] 포트 닫기 실패 — 무시하고 정리 진행: {e.Message}"); }
        try { sp.Dispose(); } catch { }
    }

    // 새 연결이 수립됐을 때 메인 스레드에서 해야 하는 초기화.
    // (Time.time 접근과 SendRaw 순서 때문에 워커가 아니라 여기서 한다.)
    void OnNewConnection()
    {
        _dmpReady = false;
        _dmpReadyFallbackTime = Time.time + DMP_STABLE_TIMEOUT;
        _yawCalibrated = false;
        _yawOffset = 0f;
        SteerSensorOk = true;
        _lastDataTime = -1f;
        _dataStale = false;
        _connectedSinceTime = Time.time;
        PasPulseCount = 0;
        ResetPasDiagnostics();

        // 펌웨어 상태 조회. boot/wdt_armed 메시지는 setup()에서 한 번만 나가는데 이 보드는
        // 포트를 열어도 리셋되지 않으므로, 나중에 붙는 Unity는 그걸 영영 못 본다.
        // I 명령으로 물어보면 펌웨어 버전·워치독 등록 여부·마지막 리셋 원인을 지금 받을 수 있다.
        SendRaw("I");

        // VibeMultiplier는 VibrationRelay가 프리셋 길이에 곱해 쓴다. 예전에는 여기서 ESP32에
        // P 명령으로도 보냈지만, 그건 펌웨어의 IRF520 시퀀서용이라 릴레이 단일 경로에서는 무의미하다.
        UnityEngine.Debug.Log("[Input] 연결 초기화 완료");
    }

    void Update()
    {
        BtnODown = BtnXDown = BrkDown = false;

        // 워커가 예기치 못하게 죽으면 입력이 영구히 멈춘다 — 살아 있는지 확인하고 되살린다.
        if (_running && _worker != null && !_worker.IsAlive)
        {
            UnityEngine.Debug.LogError("[Input] 시리얼 워커가 죽어 있음 — 재시작");
            _worker = null;
            StartWorker();
        }

        // 워커가 새 연결을 수립하면 세대 번호가 올라간다. 연결 직후 초기화해야 하는 상태는
        // 메인 스레드 전용(Time.time 등)이라 워커가 직접 만질 수 없으므로 여기서 처리한다.
        if (_connectGeneration != _seenGeneration)
        {
            _seenGeneration = _connectGeneration;
            OnNewConnection();
        }

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
                UnityEngine.Debug.Log("[Input] ESP32 데이터 수신 재개");
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
            UnityEngine.Debug.LogWarning($"[Input] ESP32 데이터 {DATA_TIMEOUT:F1}s 무수신 — 입력값 0으로 리셋");
        }
        else if (_dataStale && Time.time - _lastDataTime >= STALE_RECONNECT_SEC)
        {
            // 포트는 열려 있는데 데이터가 오지 않는 좀비 포트 (USB 절전/재열거/보드 리셋)
            // — 워커에 닫고 다시 열라고 요청한다. 실패하면 워커의 재시도 루프가 이어받는다.
            RequestReconnect($"{STALE_RECONNECT_SEC:F0}s 무수신 지속 — 좀비 포트로 판단");
        }
        else if (_lastDataTime < 0f && _connectedSinceTime > 0f &&
                 Time.time - _connectedSinceTime >= FIRST_DATA_TIMEOUT)
        {
            // 포트는 열렸지만 첫 데이터가 오지 않음 (다른 장치가 같은 포트로 재열거된 경우 등)
            RequestReconnect($"연결 후 {FIRST_DATA_TIMEOUT:F0}s 동안 데이터 없음");
        }

        // Unity→ESP32 keep-alive — USB 링크의 OUT 방향 트래픽을 유지해 절전 진입을 막고,
        // 펌웨어 에코({"debug":"hb"})로 왕복 상태를 확인한다.
        if (IsConnected && Time.time - _lastHeartbeatTime >= HEARTBEAT_INTERVAL)
        {
            _lastHeartbeatTime = Time.time;
            SendRaw("H");
        }

        // PAS 워치독 — 주행 중 인터럽트가 죽으면 레벨이 끝날 때까지 페달이 안 먹으므로,
        // 레벨 진입 시 재설정과 별개로 상시 감시해 즉시 되살린다.
        if (PasInterruptDead && Time.time - _lastPasReinitTime >= PAS_REINIT_COOLDOWN)
        {
            _lastPasReinitTime = Time.time;
            _pasReinitCount++;
            UnityEngine.Debug.LogWarning(
                $"[Input] PAS 워치독 발동 ({_pasReinitCount}회) — 핀은 토글하는데 펄스가 " +
                $"{PasFrozenFor:F1}s 멈춤(pc={PasPulseCount}). 인터럽트 재등록(R) 전송");
            SendRaw("R");
            PasPulseCount = 0;
            ResetPasDiagnostics();
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

    // 펌웨어 리셋 원인 (esp_reset_reason_t). 1이 정상, 6(TASK_WDT)이 반복되면 loop가
    // 굳어 워치독이 물고 있다는 뜻 — I2C 락업을 의심해야 한다.
    static string ResetReasonName(int rr) => rr switch
    {
        1 => "POWERON (정상 전원 인가)",
        2 => "EXT (외부 리셋 핀)",
        3 => "SW (소프트웨어 리셋)",
        4 => "PANIC (예외로 죽음)",
        5 => "INT_WDT (인터럽트 워치독)",
        6 => "TASK_WDT (loop 정지 — 워치독이 물었음)",
        7 => "WDT (기타 워치독)",
        9 => "BROWNOUT (전원 전압 불안정)",
        _ => $"코드 {rr}",
    };

    void HandleFirmwareInfo(string line)
    {
        int rr = ExtractInt(line, "reset_reason");
        int wdt = ExtractInt(line, "wdt");
        int i2c = ExtractInt(line, "i2c");
        int steer = ExtractInt(line, "steer");
        string fw = ExtractString(line, "fw");

        UnityEngine.Debug.Log(
            $"[Input] 펌웨어 정보 — v{fw}, I2C {i2c / 1000}kHz, "
            + $"워치독 {(wdt == 1 ? "활성" : "미등록")}, 조향센서 {(steer == 1 ? "정상" : "미인식")}, "
            + $"마지막 리셋: {ResetReasonName(rr)}");

        if (wdt != 1)
            UnityEngine.Debug.LogWarning(
                "[Input] 워치독이 등록되지 않았습니다 — loop가 굳어도 자동 복구되지 않습니다. "
                + "펌웨어 v6.2 이상인지 확인하세요.");

        if (rr == 6)
            UnityEngine.Debug.LogWarning(
                "[Input] 직전 리셋이 TASK_WDT입니다 — loop가 멈춰 워치독이 물었습니다. "
                + "반복되면 I2C 락업을 의심하세요 (I2C_CLOCK_HZ를 100000으로 낮춰볼 것).");
    }

    // info 응답 전용의 아주 단순한 추출기. 텔레메트리 파서를 쓰기엔 필드 구성이 달라
    // 여기서만 쓰는 최소 구현으로 둔다. 값이 없으면 -1.
    static int ExtractInt(string s, string key)
    {
        int i = s.IndexOf($"\"{key}\":", System.StringComparison.Ordinal);
        if (i < 0) return -1;
        i += key.Length + 3;
        while (i < s.Length && s[i] == ' ') i++;
        int start = i;
        if (i < s.Length && s[i] == '-') i++;
        while (i < s.Length && s[i] >= '0' && s[i] <= '9') i++;
        return i > start ? ParseInt(s, start, i) : -1;
    }

    static string ExtractString(string s, string key)
    {
        int i = s.IndexOf($"\"{key}\":\"", System.StringComparison.Ordinal);
        if (i < 0) return "?";
        i += key.Length + 4;
        int end = s.IndexOf('"', i);
        return end > i ? s.Substring(i, end - i) : "?";
    }

    void ProcessSpecialMessage(string line)
    {
        // 하트비트 에코 — 10초마다 오므로 로그 없이 수신 시각만 기록
        if (line.Contains("\"hb\""))
        {
            _lastHbAckTime = Time.time;
            return;
        }

        // PAS 재초기화 확인 에코 — prev(재초기화 직전 누적 펄스)가 진단의 핵심이므로
        // 빌드 로그 스트리핑을 우회해 Player.log에 남긴다. prev=0이면 부팅 후 펄스가
        // 한 번도 안 들어온 것 → 인터럽트가 아니라 센서 전원/배선을 봐야 한다.
        if (line.Contains("\"pas_reinit\""))
        {
            UnityEngine.Debug.Log($"[Input] PAS 재초기화 완료 — {line}");
            return;
        }

        // I 명령 응답 — 지금 보드에 올라가 있는 펌웨어의 실체.
        // 재플래싱이 실제로 됐는지, 워치독이 걸렸는지, 마지막에 왜 리셋됐는지를 여기서 확인한다.
        if (line.Contains("\"info\""))
        {
            HandleFirmwareInfo(line);
            return;
        }

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
                    SendVibrate(VibeState.Ready);
                Debug.LogWarning("[Input] 조향 센서 미인식 — 조향각 0 고정으로 진행 (페달/브레이크/버튼은 정상)");
            }
            OnErrorMessage?.Invoke(line);
        }
    }

    void ApplyData(BikeInputData d)
    {
        CadenceRPM = Mathf.Max(0f, d.rpm);

        // PAS 진단은 실제 하드웨어 패킷에서만 갱신한다. Simulate()(디버그 패널·키보드)는
        // pc/pl을 0으로 채워 넣으므로, 그대로 반영하면 워치독이 오작동한다.
        if (IsConnected)
        {
            PasPulseCount = d.pc;
            PasPinLevel = d.pl;

            if (_pasPrevLevel < 0)
            {
                // 첫 패킷은 기준값만 잡는다. 두 가지를 동시에 막기 위해서다.
                //  1) _pasPrevLevel의 초기값(-1)과 실제 pl(0/1)이 다르다는 이유로 이걸 '토글'로
                //     세면 안 된다. 그건 관측의 시작이지 핀이 움직인 게 아니다.
                //  2) 정지 시간의 기준점도 여기서 다시 잡는다. ApplyData는 DMP 준비(최대 3초)
                //     전에는 호출되지 않으므로, 연결 시각을 기준으로 두면 첫 데이터가 오는
                //     순간 이미 3초간 멈춰 있던 것으로 계산된다.
                // 이 둘이 겹치면 하드웨어가 멀쩡해도 시작할 때마다 워치독이 오발동한다.
                _pasPrevLevel = d.pl;
                _pasPrevCount = d.pc;
                _pasBaselineTime = Time.time;
            }
            else
            {
                if (d.pc != _pasPrevCount) { _pasPrevCount = d.pc; _pasLastPulseTime = Time.time; }
                if (d.pl != _pasPrevLevel) { _pasPrevLevel = d.pl; _pasLastToggleTime = Time.time; }
            }
        }
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

        if (BtnODown) OnBtnO?.Invoke();
        if (BtnXDown) OnBtnX?.Invoke();

        // 브레이크 상승 엣지 — USB 릴레이로 짧은 햅틱 피드백.
        // 진동은 릴레이 단일 경로다. 예전에는 여기서 ESP32에 B1/B0을 보내 펌웨어가
        // IRF520 모터를 브레이크 내내 켰지만, 그 경로는 더 이상 쓰지 않으므로 보내지 않는다.
        // 위험 이벤트 중 제동이면 GameManager가 Success(중간 길이)를 함께 요청하는데,
        // VibrationRelay가 겹친 요청 중 긴 쪽을 남기므로 두 번 울리지 않는다.
        if (BrkDown && BrakeAny)
        {
            SendVibrate(VibeState.Brake);
            OnBrakeAny?.Invoke();
        }

        _prevO = o; _prevX = x;
        _prevBrk = brk;
    }

    void OnDestroy()
    {
        // _wake는 Dispose하지 않는다 — Join(300)이 타임아웃되면 워커가 아직 WaitOne 중일 수
        // 있고, 그 상태에서 핸들을 버리면 종료 직전에 ObjectDisposedException이 튄다.
        // 프로세스가 곧 끝나므로 핸들 하나는 그대로 둔다.
        StopWorker();
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
            case VibeState.Brake:
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

    // 메인 스레드는 큐에 넣기만 한다 — 논블로킹. 실제 WriteLine은 워커가 수행한다.
    // 워커는 매 ReadLine 직전에 큐를 비우므로, 데이터가 흐르는 동안(50Hz) 지연은 약 20ms다.
    void SendRaw(string cmd)
    {
        if (!_connected) return; // 미연결 시 명령은 버린다 (연결되면 OnNewConnection이 다시 세팅)
        _txQueue.Enqueue(cmd);
    }

    public void SendCalibrate() => SendRaw("C");
    public void SendMagCal() => SendRaw("M");

    // 펌웨어가 펄스 카운터를 0으로 되돌리는 시점(재연결·PAS 재초기화)에 추적 상태도 함께
    // 초기화한다. 안 하면 pc가 0으로 "변한 것"을 펄스 수신으로 오인한다.
    // _pasBaselineTime은 "펄스가 얼마나 멈춰 있었나"를 재는 기준점 — 아직 한 번도 펄스가
    // 안 온 경우(부팅 직후 인터럽트 사망)에도 워치독이 정지 시간을 셀 수 있게 한다.
    void ResetPasDiagnostics()
    {
        _pasPrevCount = 0;
        _pasPrevLevel = -1;
        _pasLastPulseTime = -1f;
        _pasLastToggleTime = -1f;
        _pasBaselineTime = Time.time;
    }

    // 펄스가 멈춰 있던 시간. 아직 펄스를 한 번도 못 받았으면 기준점(연결/재초기화 시각)부터 잰다.
    float PasFrozenFor => Time.time - Mathf.Max(_pasLastPulseTime, _pasBaselineTime);

    // pl은 토글하는데 pc가 멈춘 상태 = FALLING 엣지는 오는데 못 세는 중 = 인터럽트 사망.
    bool PasInterruptDead =>
        IsConnected && _dmpReady && !_dataStale &&
        _pasLastToggleTime > 0f && Time.time - _pasLastToggleTime < PAS_TOGGLE_WINDOW &&
        PasFrozenFor >= PAS_STUCK_SEC;

    /// <summary>
    /// 컨트롤러 입력단 재설정 — PAS 인터럽트 재등록(R) + 조향 재보정(C).
    /// 장시간 가동 중 "버튼·브레이크는 정상인데 페달(PAS)만 죽는" 현상의 예방/복구용으로
    /// 레벨 진입 시 호출한다. 포트를 닫지 않고 보드도 리셋하지 않으므로 입력 공백이 없다.
    /// 연결이 끊긴 상태라면 워커에 재연결을 요청한다(논블로킹).
    /// </summary>
    public void ResetControllerInput()
    {
        if (!IsConnected)
        {
            // 워커가 이미 RECONNECT_INTERVAL 주기로 재시도 중이다. 즉시 한 번 더 깨워 앞당긴다.
            UnityEngine.Debug.LogWarning("[Input] 컨트롤러 재설정 요청 — 미연결 상태, 재연결 시도를 앞당김");
            _wake.Set();
            return;
        }

        UnityEngine.Debug.Log($"[Input] 컨트롤러 입력단 재설정 (PAS 재초기화 + 조향 재보정), 직전 누적 펄스={PasPulseCount}");
        SendRaw("R");

        // 펌웨어가 펄스 카운터를 0으로 되돌리므로 Unity 쪽 표시값도 맞춘다.
        // 방금 재초기화했으므로 워치독 쿨다운도 여기서부터 다시 센다.
        PasPulseCount = 0;
        _lastPasReinitTime = Time.time;
        ResetPasDiagnostics();

        // 핸들이 중립인 상태(주행 시작 전)에서 조향 원점을 다시 잡는다.
        _yawCalibrated = false;
        _yawOffset = 0f;
        SendCalibrate();
    }

#if DEBUG_GUI
    // 현재 상태를 한 줄 진단 + 조치 안내로 요약한다. 현장에서 화면만 보고 "지금 정상인지,
    // 아니면 무엇을 확인해야 하는지"를 바로 알 수 있게 하는 것이 목적.
    // 반환: (안내 문구, 표시 색)
    (string, Color) EvaluateStatus()
    {
        if (!IsConnected)
            return ("■ 연결 끊김 — ESP32 응답 없음\n" +
                    $"   · {RECONNECT_INTERVAL:F0}초마다 자동 재연결 시도 중 (실패 {_connectFailCount}회)\n" +
                    $"   · config.ini의 PortName({portName})과 실제 COM 번호가 같은지,\n" +
                    "     USB 케이블이 꽂혀 있는지 확인", Color.red);

        if (_dataStale)
            return ($"■ 수신 끊김 — 포트는 열려 있으나 데이터가 오지 않음\n" +
                    $"   · {STALE_RECONNECT_SEC:F0}초 지속되면 좀비 포트로 보고 강제 재연결\n" +
                    "   · 반복되면 USB 절전 해제 확인 (kiosk_power_setup.bat)", new Color(1f, 0.5f, 0f));

        if (!_dmpReady)
            return ("■ 준비 중 — DMP 안정화 대기\n" +
                    "   · 최대 3초 후 자동 진행. 이 동안 입력은 무시됨", Color.yellow);

        // ── PAS 판정 ──
        // pc(누적 펄스)와 pl(핀 레벨)이 갈리는 지점으로 원인을 좁힌다.
        bool pulsing = _pasLastPulseTime > 0f && Time.time - _pasLastPulseTime < PAS_IDLE;
        bool toggling = _pasLastToggleTime > 0f && Time.time - _pasLastToggleTime < PAS_IDLE;

        if (pulsing)
            return ($"● 정상 — PAS 펄스 수신 중 (페달 입력 정상)\n" +
                    $"   · 자동 복구 누적 {_pasReinitCount}회", Color.green);

        if (toggling)
            return ($"■ 이상 — PAS 인터럽트 사망 의심 ({PasFrozenFor:F0}s째 펄스 정지)\n" +
                    "   · 핀(pl)은 토글하는데 펄스(pc)가 늘지 않음 = 신호는 오는데 못 세는 중\n" +
                    $"   · {PAS_STUCK_SEC:F0}초 지속되면 워치독이 자동 복구(R 명령)합니다\n" +
                    $"   · 자동 복구 누적 {_pasReinitCount}회 — 계속 늘어나면 하드웨어 점검 필요", Color.red);

        // 펄스도 없고 핀도 안 움직임 — 페달을 안 돌리는 중일 수도 있으므로 단정하지 않는다.
        return ("○ 대기 — PAS 신호 없음 (페달을 돌려 확인하세요)\n" +
                "   · 돌렸는데 pc가 늘면 → 정상\n" +
                "   · 돌렸는데 pl이 고정이면 → 센서 전원/배선 문제 (소프트 복구 불가)\n" +
                "   · 돌렸는데 pl만 바뀌고 pc가 그대로면 → 인터럽트 사망", Color.white);
    }

    void OnGUI()
    {
        if (!DebugMode) return;
        if (_debugStyle == null)
        {
            _debugStyle = new GUIStyle(GUI.skin.box) { fontSize = 32, alignment = TextAnchor.UpperLeft };
            _debugStyle.normal.textColor = Color.white;
            _dbgStatusStyle = new GUIStyle(_debugStyle) { fontSize = 26, wordWrap = true };
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
            $"PAS       : pc={PasPulseCount} pl={PasPinLevel}\n" +
            $"Brake     : {Brake}  O:{BtnOHeld}  X:{BtnXHeld}\n" +
            $"YawCalib  : {(_yawCalibrated ? "완료" : "대기중")}\n" +
            $"HB Ack    : {(_lastHbAckTime < 0f ? "없음" : $"{Time.time - _lastHbAckTime:F0}s 전")}";
        GUI.Box(new Rect(10, 160, 900, 520), text, _debugStyle);

        var (status, color) = EvaluateStatus();
        _dbgStatusStyle.normal.textColor = color;
        GUI.Box(new Rect(10, 690, 900, 200), status, _dbgStatusStyle);
    }
#endif
}
