using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;
using UnityEngine.Playables;
using UnityEngine.Timeline;
using UnityEngine.UI;

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
    [SerializeField] Slider navSlider;

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

    [Header("Checkpoint")]
    [Tooltip("CheckpointMarker의 Index에 대응. 마커 통과 시 해당 슬롯의 이벤트가 1회 발동")]
    [SerializeField] UnityEvent[] onCheckpoint = new UnityEvent[k_CheckpointCount];

    const int k_CheckpointCount = 8;
    const float k_TotalWeight = 10f;  // 슬라이더 전체를 10으로 환산
    const float k_EdgeWeight = 0.5f;  // 양 끝(시작~cp0, cp마지막~끝) 구간 가중치

    bool _canMove = false;
    bool _autoPlay = false;
    bool _completed = false;
    bool _navLocked = false;   // true면 슬라이더 자동 갱신 중단 (결과 창에서 1로 고정)
    float _currentRate = 0f;   // 마지막으로 적용한 재생 배속 — 체크포인트 선행 판정에 사용

    // 체크포인트 (시간 오름차순 정렬)
    double[] _cpTimes;      // 마커 시각
    int[] _cpEventIndex;    // 마커의 Index → onCheckpoint 슬롯
    bool[] _cpFired;

    // 슬라이더 리매핑 구간 — 0, cp0..cpN-1, duration 을 knot 으로 하는 조각별 선형 보간
    double[] _knotTimes;
    float[] _knotValues;

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

        CollectCheckpoints();
    }

    // ── 체크포인트 수집 / 리매핑 ──────────────────────────────────

    /// <summary>타임라인 에셋에서 CheckpointMarker를 모아 시간 오름차순으로 정렬하고 리매핑 knot을 만든다.</summary>
    void CollectCheckpoints()
    {
        _cpTimes = null;
        _cpEventIndex = null;
        _cpFired = null;
        _knotTimes = null;
        _knotValues = null;

        var asset = director != null ? director.playableAsset as TimelineAsset : null;
        if (asset == null) return;

        var markers = new List<CheckpointMarker>();
        foreach (var track in asset.GetRootTracks()) Collect(track, markers);
        Collect(asset.markerTrack, markers);

        if (markers.Count == 0) return;

        markers.Sort((a, b) => a.time.CompareTo(b.time));

        int n = markers.Count;
        if (n != k_CheckpointCount)
            Debug.LogWarning($"[TL] CheckpointMarker {n}개 — {k_CheckpointCount}개 기준이 아니라 사이 구간 {n - 1}등분으로 동작합니다.");

        _cpTimes = new double[n];
        _cpEventIndex = new int[n];
        _cpFired = new bool[n];

        // knot: 0 → 0, cp_i → (0.5 + i×step) / 10, duration → 1
        // 전체를 10으로 두고 양 끝 구간에 0.5씩, 남은 9를 체크포인트 사이 (n-1)개 구간으로 등분
        float step = n > 1 ? (k_TotalWeight - k_EdgeWeight * 2f) / (n - 1) : 0f;

        _knotTimes = new double[n + 2];
        _knotValues = new float[n + 2];
        _knotTimes[0] = 0.0;
        _knotValues[0] = 0f;

        for (int i = 0; i < n; i++)
        {
            _cpTimes[i] = markers[i].time;
            _cpEventIndex[i] = markers[i].Index;
            _knotTimes[i + 1] = markers[i].time;
            _knotValues[i + 1] = (k_EdgeWeight + i * step) / k_TotalWeight;

            if (markers[i].Index != i)
                Debug.LogWarning($"[TL] Checkpoint 순서 불일치 — 시간순 {i}번째 마커의 Index가 {markers[i].Index}. 슬라이더는 시간순으로 배치되고 이벤트는 Index 슬롯이 발동합니다.");
        }

        _knotTimes[n + 1] = director.duration;
        _knotValues[n + 1] = 1f;

        if (_knotTimes[n + 1] <= _knotTimes[n])
        {
            Debug.LogWarning($"[TL] duration({director.duration:F2})이 마지막 Checkpoint({_knotTimes[n]:F2}) 이하 — 마지막 구간이 없습니다.");
            _knotTimes[n + 1] = _knotTimes[n];
        }

        Debug.Log($"[TL] Checkpoint {n}개 수집 — 사이 {n - 1}등분(step {step / k_TotalWeight:F4}), 첫 지점 {_knotValues[1]:F4} / 마지막 지점 {_knotValues[n]:F4}");
    }

    /// <summary>그룹 트랙 하위까지 재귀 순회하며 CheckpointMarker를 수집.</summary>
    static void Collect(TrackAsset track, List<CheckpointMarker> into)
    {
        if (track == null) return;

        foreach (var m in track.GetMarkers())
            if (m is CheckpointMarker cp && !into.Contains(cp)) into.Add(cp);

        foreach (var child in track.GetChildTracks()) Collect(child, into);
    }

    /// <summary>실제 재생 시간을 체크포인트 등분 기준 슬라이더 값으로 변환.</summary>
    float RemapProgress(double time)
    {
        if (_knotTimes == null || _knotTimes.Length < 2)
            return director.duration > 0.0 ? (float)(time / director.duration) : 0f;

        if (time <= _knotTimes[0]) return _knotValues[0];

        for (int i = 1; i < _knotTimes.Length; i++)
        {
            if (time >= _knotTimes[i]) continue;

            double span = _knotTimes[i] - _knotTimes[i - 1];
            float t = span > 0.0 ? (float)((time - _knotTimes[i - 1]) / span) : 1f;
            return Mathf.Lerp(_knotValues[i - 1], _knotValues[i], t);
        }

        return _knotValues[_knotValues.Length - 1];
    }

    /// <summary>통과한 체크포인트의 이벤트를 1회씩 발동.</summary>
    void UpdateCheckpoints(double time)
    {
        if (_cpTimes == null) return;

        for (int i = 0; i < _cpTimes.Length; i++)
        {
            if (_cpFired[i] || time < _cpTimes[i]) continue;
            _cpFired[i] = true;

            int slot = _cpEventIndex[i];
            Debug.Log($"[TL] Checkpoint[{slot}] @ {_cpTimes[i]:F2}s");

            if (onCheckpoint != null && slot >= 0 && slot < onCheckpoint.Length)
                onCheckpoint[slot]?.Invoke();
            else
                Debug.LogWarning($"[TL] Checkpoint Index {slot} — 대응하는 이벤트 슬롯이 없습니다.");
        }
    }

    void Update()
    {
        if (director == null) return;

        if (Keyboard.current != null && Keyboard.current.spaceKey.wasPressedThisFrame)
            Debug.Log($"[TL] Space down — canMove:{_canMove}  graphValid:{director.playableGraph.IsValid()}  SpeedKph:{(InputManager.Instance != null ? InputManager.Instance.SpeedKph : -1f):F1}");

        if (!director.playableGraph.IsValid()) return;

        // 체크포인트/슬라이더는 재생 게이트보다 먼저 처리한다.
        //  - Freeze(퀴즈 등) 중에도 밀리지 않도록 조기 리턴 위로 올림
        //  - 플레이어 루프가 ScriptRunBehaviourUpdate → DirectorUpdate 순이라
        //    director.time은 이번 프레임 평가 이전 값이다. 이번 프레임에 진행할 만큼
        //    선행 보정해야 같은 시각의 마커 노티피케이션(퀴즈)보다 먼저 발동한다.
        if (director.state == PlayState.Playing)
        {
            UpdateCheckpoints(director.time + Time.deltaTime * _currentRate);

            if (navSlider != null && !_navLocked)
                navSlider.value = RemapProgress(director.time);
        }

        if (!_canMove) return;

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
        _currentRate = speed;
    }

    // ── 외부 API ──────────────────────────────────────────────────

    public void Play()
    {
        if (director == null) return;
        _canMove = true;
        _autoPlay = false;
        _completed = false;
        _navLocked = false;
        if (navSlider != null) navSlider.value = 0f;
        if (_cpFired != null) System.Array.Clear(_cpFired, 0, _cpFired.Length);
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

    public void End()
    {
        _canMove = false;
        if (director == null) return;
        SetRootSpeed(0f);
        director.time = director.duration;
        director.Evaluate();
    }

    /// <summary>
    /// 진행도 슬라이더를 끝(1)까지 채우고 자동 갱신을 잠근다.
    /// 결과 창이 열릴 때 호출 — 타임라인이 duration에 도달하기 전에 종료되거나
    /// 마지막 프레임 갱신이 누락돼 슬라이더가 1 미만으로 남는 것을 방지한다.
    /// 잠금은 다음 Play()에서 해제된다.
    /// </summary>
    public void FillNavSlider()
    {
        _navLocked = true;
        if (navSlider != null) navSlider.value = 1f;
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
