using UnityEngine;
using UnityEngine.Playables;

/// <summary>
/// Timeline 기반 카메라 애니메이션 컨트롤러.
/// VideoRailController를 대체하며, PlayableDirector의 재생 속도를
/// 자전거 입력(SpeedKph)에 따라 제어한다.
/// </summary>
public class TimelineGameController : MonoBehaviour
{
    [Header("Timeline")]
    [SerializeField] PlayableDirector director;

    [Header("Speed Mapping")]
    [Tooltip("이 속도(km/h)에서 1.0× 재생")]
    [SerializeField] float baseSpeedKph = 15f;
    [Tooltip("이 속도(km/h) 미만이면 타임라인 정지")]
    [SerializeField] float minSpeedKph = 1f;
    [Tooltip("최대 재생 배속")]
    [SerializeField] float maxRate = 1.5f;
    [Tooltip("자동진행 구간 재생 배속 (CrosswalkWalk 등)")]
    [SerializeField] float fixedAutoSpeed = 1.0f;

    bool _canMove = false;
    bool _autoPlay = false;

    public bool IsPlaying => director != null && director.state == PlayState.Playing;
    public double CurrentTime => director != null ? director.time : 0.0;

    void Awake()
    {
        if (director == null) director = GetComponent<PlayableDirector>();
        if (director != null)
        {
            director.playOnAwake = false;
            director.stopped += OnDirectorStopped;
        }
    }

    void Start()
    {
        if (InputManager.Instance != null)
            baseSpeedKph = InputManager.Instance.BaseSpeedKph;
    }

    void Update()
    {
        if (!_canMove) return;
        UpdatePlaybackSpeed();
    }

    void UpdatePlaybackSpeed()
    {
        float rate;

        if (_autoPlay)
        {
            rate = fixedAutoSpeed;
        }
        else
        {
            float spd = InputManager.Instance != null ? InputManager.Instance.SpeedKph : 0f;
            rate = spd < minSpeedKph
                ? 0f
                : Mathf.Clamp(spd / baseSpeedKph, 0.05f, maxRate);
        }

        SetDirectorSpeed(rate);
    }

    void SetDirectorSpeed(float rate)
    {
        if (director == null || !director.playableGraph.IsValid()) return;

        var root = director.playableGraph.GetRootPlayable(0);
        root.SetSpeed(rate);

        if (rate < 0.01f && director.state == PlayState.Playing)
            director.playableGraph.GetRootPlayable(0).Pause();
        else if (rate >= 0.01f && director.state != PlayState.Playing)
            director.Resume();
    }

    void OnDirectorStopped(PlayableDirector _)
    {
        GameManager.Instance?.OnTimelineComplete();
    }

    // ── 외부 API ──────────────────────────────────────────────────

    public void Play()
    {
        _canMove = true;
        _autoPlay = false;
        if (director != null) director.Play();
    }

    public void Freeze()
    {
        _canMove = false;
        if (director != null && director.playableGraph.IsValid())
        {
            director.playableGraph.GetRootPlayable(0).SetSpeed(0f);
            director.Pause();
        }
    }

    public void Resume()
    {
        _canMove = true;
        if (director != null) director.Resume();
    }

    public void Stop()
    {
        _canMove = false;
        if (director != null) director.Stop();
    }

    /// <summary>자동진행 구간 전환. true이면 입력 무시하고 fixedAutoSpeed로 재생.</summary>
    public void SetAutoPlay(bool auto)
    {
        _autoPlay = auto;
        if (auto && director != null && director.state != PlayState.Playing)
            director.Resume();
    }

#if UNITY_EDITOR
    void OnGUI()
    {
        if (!Application.isPlaying) return;
        var im = InputManager.Instance;
        if (im == null) return;
        GUI.Label(new Rect(10, 10, 360, 20),
            $"SpeedKph: {im.SpeedKph:F1}  Timeline: {(director != null ? director.time:0):F2}s  Auto:{_autoPlay}");
    }
#endif
}
