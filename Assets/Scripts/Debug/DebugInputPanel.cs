using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using TMPro;

/// <summary>DebugInputPanel v4 — 케이던스 RPM 슬라이더 (0~120 RPM)
/// 단축키: [0]=O버튼  [`]=X버튼  [B]=브레이크
/// (Space/1/Esc/방향키는 InputManager 키보드 모드가 처리)</summary>
public class DebugInputPanel : MonoBehaviour
{
    [Header("Sliders")]
    [Tooltip("케이던스 RPM (0~120). 기준: 60=15km/h  80=20km/h  120=30km/h")]
    [SerializeField] Slider cadenceSlider;
    [SerializeField] Slider steerSlider;

    [Header("Labels")]
    [SerializeField] TextMeshProUGUI cadenceLabel;
    [SerializeField] TextMeshProUGUI speedLabel;
    [SerializeField] TextMeshProUGUI steerLabel;
    [SerializeField] TextMeshProUGUI statusText;

    [Header("Buttons")]
    [SerializeField] Button btnO, btnX, btnBrake;

    [Header("Calibration")]
    [Tooltip("케이던스→가상속도 계수 (기본 0.25)")]
    [SerializeField] float cadenceToKph = 0.25f;

    bool _brk, _o, _x;

    // 이전 프레임 값 캐시 — 값이 바뀔 때만 문자열 생성
    float      _prevRpm, _prevSpd, _prevStr;
    GameState  _prevState;
    int        _prevScore  = -1;
    bool       _prevConnected;

    void Start()
    {
        if (cadenceSlider) { cadenceSlider.minValue = 0; cadenceSlider.maxValue = 120; }
        if (steerSlider)   { steerSlider.minValue = -45; steerSlider.maxValue = 45; }
        btnO?.onClick.AddListener(()     => _o   = true);
        btnX?.onClick.AddListener(()     => _x   = true);
        btnBrake?.onClick.AddListener(() => _brk = !_brk);
    }

    void Update()
    {
        if (InputManager.Instance == null) return;

        float rpm = cadenceSlider ? cadenceSlider.value : 0f;
        float str = steerSlider   ? steerSlider.value   : 0f;

        var kb = Keyboard.current;
        if (kb != null)
        {
            if (kb.digit0Key.wasPressedThisFrame) _o    = true;  // 0키 → O버튼 (1은 게임시작 예약)
            if (kb.backquoteKey.wasPressedThisFrame) _x = true;  // ` 키 → X버튼
            _brk = kb.bKey.isPressed;
        }

        float spd = rpm * cadenceToKph;
        InputManager.Instance.Simulate(rpm, str, spd, _brk, _o, _x);
        _o = false; _x = false;

        // 값이 바뀔 때만 문자열 재생성
        if (!Mathf.Approximately(rpm, _prevRpm))
        {
            if (cadenceLabel) cadenceLabel.text = $"케이던스: {rpm:F0} RPM";
            _prevRpm = rpm;
        }
        if (!Mathf.Approximately(spd, _prevSpd))
        {
            if (speedLabel) speedLabel.text = $"가상 속도: {spd:F1} km/h";
            _prevSpd = spd;
        }
        if (!Mathf.Approximately(str, _prevStr))
        {
            if (steerLabel) steerLabel.text = $"조향: {str:F1}°";
            _prevStr = str;
        }

        var gm        = GameManager.Instance;
        var state     = gm?.CurrentState ?? GameState.Boot;
        int score     = gm?.TotalScore   ?? 0;
        bool connected = InputManager.Instance.IsConnected;
        if (state != _prevState || score != _prevScore || connected != _prevConnected)
        {
            if (statusText)
                statusText.text =
                    $"State: {state}\n" +
                    $"Score: {score}/80\n" +
                    $"Serial: {(connected ? "연결됨" : "시뮬레이션 모드")}\n\n" +
                    $"[0]=O  [`]=X  [B]=Brk\n" +
                    $"[RPM 참고] 60={60*cadenceToKph:F0}  80={80*cadenceToKph:F0}  " +
                    $"120={120*cadenceToKph:F0} km/h";
            _prevState     = state;
            _prevScore     = score;
            _prevConnected = connected;
        }
    }
}
