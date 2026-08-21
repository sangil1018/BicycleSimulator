using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 기본 해상도 1080x1920(9:16)을 기준으로,
/// 실행 중인 디스플레이의 "세로 해상도"에 맞춰 가로 해상도를 9:16이 되도록 자동 설정한다.
/// 예) 세로 3840 디스플레이 → 2160x3840 으로 설정.
///
/// 디스플레이 자체가 9:16이 아니어서 창 비율이 어긋나는 경우에는
/// 카메라 Rect를 조정(필라박스/레터박스)해 화면이 늘어나 보이는 것을 막는다.
///
/// 주의: ScreenSpace-Overlay 캔버스는 카메라 Rect의 영향을 받지 않는다.
///       UI까지 함께 박스 처리하려면 캔버스를 ScreenSpace-Camera로 두고
///       해당 카메라를 targetCameras에 포함시킬 것.
/// </summary>
[DefaultExecutionOrder(-1000)]
public class ScreenAspectSetter : Singleton<ScreenAspectSetter>
{
    /// <summary>기준 해상도 (세로 모드).</summary>
    public const int BaseWidth = 1080;
    public const int BaseHeight = 1920;

    [Header("Target Aspect (기본 9:16)")]
    [SerializeField] private int aspectWidth = 9;
    [SerializeField] private int aspectHeight = 16;

    [Header("Resolution")]
    [Tooltip("체크 시 디스플레이의 세로 해상도를 사용. 해제 시 기본 해상도(1080x1920) 고정.")]
    [SerializeField] private bool useDisplayHeight = true;

    [Tooltip("세로 해상도 상한. 0이면 제한 없음. (과도한 해상도로 인한 성능 저하 방지용)")]
    [SerializeField] private int maxHeight = 0;

    [SerializeField] private FullScreenMode fullScreenMode = FullScreenMode.FullScreenWindow;

    [Tooltip("계산된 가로 해상도가 디스플레이 가로 해상도와 다르면 창 모드로 전환한다. " +
             "(전체화면에서 강제 스케일되어 화면이 늘어나는 것을 방지)")]
    [SerializeField] private bool windowedWhenAspectMismatch = true;

    [Header("Camera Letterbox")]
    [Tooltip("실제 창 비율이 목표 비율과 다를 때 카메라 Rect를 조정해 왜곡을 막는다.")]
    [SerializeField] private bool applyCameraBox = true;

    [Tooltip("비워두면 씬의 모든 활성 카메라에 적용한다.")]
    [SerializeField] private Camera[] targetCameras;

    [Header("Runtime")]
    [Tooltip("실행 중 디스플레이/창 크기 변경을 감시해 다시 적용한다.")]
    [SerializeField] private bool watchForChanges = true;
    [SerializeField] private float checkInterval = 1f;

    [Header("Debug")]
    [SerializeField] private bool logOnApply = true;

    private int _lastWindowWidth;
    private int _lastWindowHeight;

    /// <summary>목표 화면 비율 (가로/세로).</summary>
    public float TargetAspect => (float)aspectWidth / aspectHeight;

    protected override void Awake()
    {
        base.Awake();
        if (Instance != this) return;

        Apply();
    }

    private void OnEnable()
    {
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private void OnDisable()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    private void Start()
    {
        if (watchForChanges)
            StartCoroutine(WatchRoutine());
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        // 새 씬의 카메라에도 Rect를 다시 적용.
        ApplyCameraRects();
    }

    /// <summary>해상도와 카메라 Rect를 모두 다시 계산해 적용한다.</summary>
    [ContextMenu("Apply Now")]
    public void Apply()
    {
        ApplyResolution();
        ApplyCameraRects();

        _lastWindowWidth = Screen.width;
        _lastWindowHeight = Screen.height;
    }

    /// <summary>디스플레이 세로 해상도에 맞춘 9:16 해상도를 계산해 적용한다.</summary>
    public void ApplyResolution()
    {
        // 에디터에서는 Screen.SetResolution이 Game 뷰에 반영되지 않으므로 건너뛴다.
        if (Application.isEditor) return;

        GetTargetResolution(out int width, out int height, out FullScreenMode mode);

        if (Screen.width == width && Screen.height == height && Screen.fullScreenMode == mode)
            return;

        Screen.SetResolution(width, height, mode);

        if (logOnApply)
        {
            UnityEngine.Debug.Log(
                $"[ScreenAspectSetter] {width}x{height} ({aspectWidth}:{aspectHeight}, {mode}) " +
                $"/ display {Display.main.systemWidth}x{Display.main.systemHeight}");
        }
    }

    /// <summary>적용할 해상도와 전체화면 모드를 계산한다.</summary>
    public void GetTargetResolution(out int width, out int height, out FullScreenMode mode)
    {
        int displayWidth = Mathf.Max(Display.main.systemWidth, 1);
        int displayHeight = Mathf.Max(Display.main.systemHeight, 1);

        height = useDisplayHeight ? displayHeight : BaseHeight;
        if (maxHeight > 0)
            height = Mathf.Min(height, maxHeight);

        width = Mathf.RoundToInt(height * TargetAspect);
        width -= width & 1;                 // 짝수로 맞춰 하프픽셀 오차 방지
        width = Mathf.Max(width, 2);

        mode = fullScreenMode;

        // 디스플레이 가로보다 크면 가로 기준으로 다시 맞춘다. (가로 모니터 등)
        if (width > displayWidth)
        {
            width = displayWidth - (displayWidth & 1);
            height = Mathf.RoundToInt(width / TargetAspect);
        }

        // 디스플레이를 꽉 채우지 못하면 전체화면 스케일로 늘어나므로 창 모드로.
        if (windowedWhenAspectMismatch && (width != displayWidth || height != displayHeight))
            mode = FullScreenMode.Windowed;
    }

    /// <summary>창 비율이 목표 비율과 다를 때 카메라 Rect로 여백을 만든다.</summary>
    public void ApplyCameraRects()
    {
        if (!applyCameraBox) return;

        Camera[] cameras = (targetCameras != null && targetCameras.Length > 0)
            ? targetCameras
            : Camera.allCameras;

        Rect rect = CalculateViewportRect();

        for (int i = 0; i < cameras.Length; i++)
        {
            Camera cam = cameras[i];
            if (cam == null) continue;
            cam.rect = rect;
        }
    }

    /// <summary>현재 창 크기 기준으로 목표 비율을 유지하는 뷰포트 Rect를 반환한다.</summary>
    public Rect CalculateViewportRect()
    {
        float windowAspect = (float)Screen.width / Mathf.Max(Screen.height, 1);
        float scaleHeight = windowAspect / TargetAspect;

        if (Mathf.Abs(scaleHeight - 1f) < 0.001f)
            return new Rect(0f, 0f, 1f, 1f);

        if (scaleHeight < 1f)
        {
            // 창이 목표보다 좁다 → 위아래 여백 (레터박스)
            return new Rect(0f, (1f - scaleHeight) * 0.5f, 1f, scaleHeight);
        }

        // 창이 목표보다 넓다 → 좌우 여백 (필라박스)
        float scaleWidth = 1f / scaleHeight;
        return new Rect((1f - scaleWidth) * 0.5f, 0f, scaleWidth, 1f);
    }

    private IEnumerator WatchRoutine()
    {
        var wait = new WaitForSecondsRealtime(Mathf.Max(checkInterval, 0.1f));

        while (true)
        {
            yield return wait;

            if (Screen.width == _lastWindowWidth && Screen.height == _lastWindowHeight)
                continue;

            _lastWindowWidth = Screen.width;
            _lastWindowHeight = Screen.height;
            ApplyCameraRects();
        }
    }
}
