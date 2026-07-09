using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Playables;

/// <summary>
/// Timeline 기반 카메라 애니메이션 컨트롤러.
/// PlayableDirector를 GameTime 모드로 구동하며,
/// 루트 플레이어블의 Speed를 조절해 자전거 속도에 비례한 재생 속도를 구현한다.
/// (Manual + Evaluate() 방식은 Signal Track 시그널이 발화되지 않음)
/// </summary>
public class TimelineGameController : MonoBehaviour
{
    [Header("Timeline")]
    [SerializeField] PlayableDirector director;

    [Header("Speed Mapping")]
    [Tooltip("이 속도(km/h)에서 1.0× 재생")]
    [SerializeField] float baseSpeedKph = 15f;
    [Tooltip("최대 재생 배속. config.ini의 MaxRate 값으로 덮어씌워짐")]
    [SerializeField] float maxRate = 1.5f;
    [Tooltip("자동진행 구간 재생 배속 (CrosswalkWalk 등)")]
    [SerializeField] float fixedAutoSpeed = 1.0f;

    [Header("Playback Control")]
    [Tooltip("전체 재생 배속 승수. config.ini의 PlaybackMultiplier 값으로 덮어씌워짐")]
    [SerializeField] float playbackMultiplier = 1f;

    bool _canMove = false;
    bool _autoPlay = false;
    bool _completed = false;

    public bool IsPlaying => _canMove;
    public double CurrentTime => director != null ? director.time : 0.0;

    void Awake()
    {
#if DEBUG_GUI
        useGUILayout = false; // GUILayout 미사용 — Layout 이벤트 패스 생략으로 OnGUI 상주 비용 절감
#endif
        if (director == null) director = GetComponent<PlayableDirector>();
        if (director != null)
        {
            director.playOnAwake = false;
            director.timeUpdateMode = DirectorUpdateMode.GameTime;
        }
    }

    void Start()
    {
        if (InputManager.Instance != null)
        {
            baseSpeedKph = InputManager.Instance.BaseSpeedKph;
            playbackMultiplier = InputManager.Instance.PlaybackMultiplier;
            maxRate = InputManager.Instance.MaxRate;
        }
        bool hasAsset = director != null && director.playableAsset != null;
        Debug.Log($"[TimelineGameController] Start — director:{director != null}  asset:{hasAsset}  duration:{(director != null ? director.duration : 0):F2}");
    }

    void Update()
    {
        if (director == null) return;

        if (Keyboard.current != null && Keyboard.current.spaceKey.wasPressedThisFrame)
            Debug.Log($"[TL] Space down — canMove:{_canMove}  graphValid:{director.playableGraph.IsValid()}  SpeedKph:{(InputManager.Instance != null ? InputManager.Instance.SpeedKph : -1f):F1}");

        if (!_canMove || !director.playableGraph.IsValid()) return;

        var gm = GameManager.Instance;
        if (gm != null && !gm.CanMove && !_autoPlay) return;

        SetPlaybackSpeed();

        if (!_completed && director.time >= director.duration)
        {
            _completed = true;
            _canMove = false;
            SetRootSpeed(0f);
            // if (GameManager.Instance != null)
            //     GameManager.Instance.OnTimelineComplete();
        }
    }

    void SetPlaybackSpeed()
    {
        float rate;

        if (_autoPlay)
        {
            rate = fixedAutoSpeed;
        }
        else
        {
            float spd = InputManager.Instance != null ? InputManager.Instance.SpeedKph : 0f;
            rate = Mathf.Clamp(spd / baseSpeedKph * playbackMultiplier, 0f, maxRate);
        }

        SetRootSpeed(rate);
    }

    void SetRootSpeed(float speed)
    {
        if (!director.playableGraph.IsValid()) return;
        director.playableGraph.GetRootPlayable(0).SetSpeed(speed);
    }

    // ── 외부 API ──────────────────────────────────────────────────

    public void Play()
    {
        if (director == null) return;
        _canMove = true;
        _autoPlay = false;
        _completed = false;
        director.time = 0;
        director.Play();
        Debug.Log($"[TL] Play() called — graphValid:{director.playableGraph.IsValid()}  duration:{director.duration:F2}");
    }

    public void Freeze()
    {
        _canMove = false;
        SetRootSpeed(0f);
    }

    public void Resume()
    {
        _canMove = true;
    }

    public void Stop()
    {
        _canMove = false;
        if (director == null) return;
        SetRootSpeed(0f);
        director.Stop();
    }

    /// <summary>자동진행 구간 전환. true이면 입력 무시하고 fixedAutoSpeed로 재생.</summary>
    public void SetAutoPlay(bool auto)
    {
        _autoPlay = auto;
        if (auto) _canMove = true;
    }

#if DEBUG_GUI
    [Header("Debug GUI")]
    [SerializeField] int debugFontSize = 50;
    [SerializeField] Color debugFontColor = Color.white;
    GUIStyle _debugStyle;

    void OnGUI()
    {
        if (!Application.isPlaying) return;
        var im = InputManager.Instance;
        if (im == null || !im.DebugMode) return;
        float spd = im.SpeedKph;
        bool valid = director != null && director.playableGraph.IsValid();
        float rate = _autoPlay ? 0f : Mathf.Clamp(spd / baseSpeedKph, 0f, maxRate);

        _debugStyle ??= new GUIStyle(GUI.skin.label)
        {
            fontSize = debugFontSize,
            normal = { textColor = debugFontColor }
        };
        GUI.Label(new Rect(10, 10, 1000, 400),
            $"[TL] canMove:{_canMove}  graphValid:{valid}  SpeedKph:{spd:F1}  rate:{rate:F2}  time:{(director != null ? director.time : 0):F2}/{(director != null ? director.duration : 0):F2}",
            _debugStyle);
    }
#endif
}
