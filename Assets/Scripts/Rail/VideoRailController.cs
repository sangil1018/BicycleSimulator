using UnityEngine;
using UnityEngine.Video;

/// <summary>
/// 레일 슈터 방식 영상 컨트롤러 (펌웨어 bicycle_sim_v4.1).
///
/// 속도 소스: ESP32 PAS 12자석 홀 센서 기반 CadenceRPM / SpeedKph
/// SpeedKph = CadenceRPM × 0.25 (펌웨어 계산)
///
/// [배속 매핑]
/// spd < minSpeedKph → 정지 (0x)
/// 15 km/h (baseSpeedKph 기본) → 1.0x
/// 30 km/h → 2.0x (maxRate로 제한)
/// </summary>
[RequireComponent(typeof(VideoPlayer))]
public class VideoRailController : MonoBehaviour
{
    [Header("Video Player")]
    [SerializeField] VideoPlayer videoPlayer;

    [Tooltip("StreamingAssets 기준 경로 (0=미사용, 1=초급, 2=고급)")]
    [SerializeField] string[] videoPaths =
    {
        "",
        "Videos/level1.mp4",
        "Videos/level2.mp4",
    };

    [Header("Speed Mapping")]
    [Tooltip("이 속도(km/h)에서 1.0× 재생. 체험 현장에서 캘리브레이션.")]
    [SerializeField] float baseSpeedKph = 15f;

    [Tooltip("이 속도(km/h) 미만이면 영상 정지")]
    [SerializeField] float minSpeedKph  = 1f;

    [Tooltip("최대 재생 배속")]
    [SerializeField] float maxRate      = 1.5f;

    [Tooltip("페달 정지 후 영상이 완전히 멈추는 데 걸리는 시간(초)")]
    [SerializeField] float decelTime    = 0.5f;

    [Header("Camera Tilt (Cosmetic)")]
    [Tooltip("핸들 틸트를 적용할 카메라 루트 Transform")]
    [SerializeField] Transform cameraRoot;
    [SerializeField] float tiltStrength = 0.15f;
    [SerializeField] float tiltSmooth   = 5f;

    // ── 상태 ──────────────────────────────────────────────────────
    bool  _canMove = false;
    float _curRate = 0f;

    public double CurrentTime => videoPlayer != null ? videoPlayer.time : 0.0;
    public bool   IsPlaying   => videoPlayer != null && videoPlayer.isPlaying;

    // ── 초기화 ────────────────────────────────────────────────────
    void Awake()
    {
        if (videoPlayer == null) videoPlayer = GetComponent<VideoPlayer>();
        videoPlayer.playOnAwake = false;
        videoPlayer.renderMode  = VideoRenderMode.CameraFarPlane;
        videoPlayer.loopPointReached += _ => GameManager.Instance.OnVideoComplete();
    }

    void Start()
    {
        // config.ini에서 로드된 값이 있다면 적용
        if (InputManager.Instance != null)
        {
            baseSpeedKph = InputManager.Instance.BaseSpeedKph;
        }
    }

    // ── 메인 루프 ─────────────────────────────────────────────────
    void Update()
    {
        if (!_canMove) return;
        UpdatePlaybackSpeed();
        UpdateCameraTilt();
    }

    // ── 재생 속도 제어 ────────────────────────────────────────────
    void UpdatePlaybackSpeed()
    {
        float spd    = InputManager.Instance.SpeedKph;
        float target = spd < minSpeedKph
            ? 0f
            : Mathf.Clamp(spd / baseSpeedKph, 0.05f, maxRate);

        _curRate = Mathf.MoveTowards(_curRate, target,
                                     maxRate / decelTime * Time.deltaTime);

        videoPlayer.playbackSpeed = _curRate;

        bool playing = videoPlayer.isPlaying;
        if      (_curRate < 0.01f &&  playing)  videoPlayer.Pause();
        else if (_curRate >= 0.01f && !playing)  videoPlayer.Play();
    }

    // ── 핸들 틸트 ─────────────────────────────────────────────────
    void UpdateCameraTilt()
    {
        if (cameraRoot == null || tiltStrength <= 0f) return;
        float tilt   = -InputManager.Instance.SteeringAngle * tiltStrength;
        var   target = Quaternion.Euler(0f, 0f, tilt);
        cameraRoot.localRotation = Quaternion.Lerp(
            cameraRoot.localRotation, target, Time.deltaTime * tiltSmooth);
    }

    // ── 외부 API ──────────────────────────────────────────────────
    public void LoadVideo(int level)
    {
        if (level < 0 || level >= videoPaths.Length)
        {
            Debug.LogError($"[Rail] Level {level} 영상 경로 없음.");
            return;
        }
        string path = System.IO.Path.Combine(
            Application.streamingAssetsPath, videoPaths[level]);
        videoPlayer.url = path;
        videoPlayer.Prepare();
        Debug.Log($"[Rail] 영상 로드: {path}");
    }

    public void Play()    { _canMove = true;  videoPlayer.Play(); }
    public void Freeze()  { _canMove = false; _curRate = 0f;
                            videoPlayer.playbackSpeed = 0f; videoPlayer.Pause(); }
    public void Resume()  { _canMove = true;  videoPlayer.Play(); }
    public void Stop()    { _canMove = false; _curRate = 0f; videoPlayer.Stop(); }

#if UNITY_EDITOR
    void OnGUI()
    {
        if (!Application.isPlaying) return;
        var im = InputManager.Instance;
        if (im == null) return;
        GUI.Label(new Rect(10, 10, 340, 20),
            $"CadenceRPM: {im.CadenceRPM:F0}  SpeedKph: {im.SpeedKph:F1}  Rate: {_curRate:F2}x");
    }
#endif
}
