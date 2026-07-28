using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Splines;

/// <summary>
/// 도로 위에 셰브론(꺾쇠) 유도선을 깔아주는 월드 공간 네비게이션.
/// 경로 스플라인(SplineContainer)에서 라이더 최근접 지점을 찾아,
/// 그 앞 구간에만 셰브론을 일정 간격으로 배치한다.
///
/// 셰브론은 풀링되며 GPU Instancing 머티리얼 기준 드로우콜 1~2개로 처리된다.
/// 경로 스플라인은 Tools ▸ Navigation ▸ Route Spline Baker 로 카메라 애니메이션에서 구울 수 있다.
/// </summary>
[DisallowMultipleComponent]
public class RoadNavigationGuide : MonoBehaviour
{
    // ── Inspector ──────────────────────────────────────────────────

    [Header("Route")]
    [Tooltip("주행 경로 스플라인. Route Spline Baker로 생성 가능")]
    [SerializeField] SplineContainer route;
    [Tooltip("기준이 되는 시점. 비워두면 Camera.main")]
    [SerializeField] Transform viewer;

    [Header("Chevron")]
    [Tooltip("셰브론 머티리얼 (Unlit/Transparent 권장, GPU Instancing 체크)")]
    [SerializeField] Material chevronMaterial;
    [Tooltip("셰브론 가로 폭(m)")]
    [SerializeField] float chevronWidth = 0.9f;
    [Tooltip("셰브론 진행 방향 길이(m)")]
    [SerializeField] float chevronLength = 2.0f;
    [Tooltip("풀 크기. (farDistance - nearDistance) / spacing 보다 커야 한다")]
    [SerializeField] int poolSize = 16;

    [Header("Placement")]
    [Tooltip("셰브론 간격(m)")]
    [SerializeField] float spacing = 2.5f;
    [Tooltip("라이더 앞 몇 m부터 표시할지")]
    [SerializeField] float nearDistance = 2f;
    [Tooltip("라이더 앞 몇 m까지 표시할지")]
    [SerializeField] float farDistance = 18f;
    [Tooltip("경로 기준 좌우 오프셋(m). +가 오른쪽")]
    [SerializeField] float lateralOffset = 0f;

    [Header("Ground Fit")]
    [Tooltip("노면에 레이캐스트해서 높이/기울기를 맞춘다")]
    [SerializeField] bool conformToGround = true;
    [Tooltip("노면으로 인정할 레이어. 차량/보행자/신호등은 제외할 것")]
    [SerializeField] LayerMask groundMask = 1;
    [Tooltip("레이 시작 높이(스플라인 기준 위쪽, m)")]
    [SerializeField] float rayHeight = 3f;
    [Tooltip("레이 길이(m)")]
    [SerializeField] float rayLength = 8f;
    [Tooltip("Z-파이팅 회피용 노면 띄움(m)")]
    [SerializeField] float groundOffset = 0.02f;

    [Header("Flow")]
    [Tooltip("셰브론이 흐르는 속도(m/s). 0이면 노면에 그려진 것처럼 고정")]
    [SerializeField] float flowSpeed = 3.0f;
    [Tooltip("체크 시 flowSpeed를 자전거 속도에 비례시킨다 (flowSpeed가 배율로 동작)")]
    [SerializeField] bool flowFollowsBikeSpeed = true;

    [Header("Fade")]
    [Tooltip("가까운 쪽 페이드 구간 길이(m)")]
    [SerializeField] float nearFade = 3f;
    [Tooltip("먼 쪽 페이드 구간 길이(m)")]
    [SerializeField] float farFade = 12f;
    [Tooltip("전체 불투명도 상한")]
    [Range(0f, 1f)]
    [SerializeField] float maxAlpha = 1f;

    [Header("Speed Tier Color")]
    [Tooltip("속도 등급에 따라 셰브론 색을 바꾼다 (SpeedUIController와 동일 기준)")]
    [SerializeField] bool tintBySpeed = true;
    [SerializeField] Color normalColor = new Color(0.01f, 0.92f, 0.87f, 1f);
    [SerializeField] Color yellowColor = new Color(1f, 0.87f, 0.0f, 1f);
    [SerializeField] Color redColor = new Color(0.9f, 0.02f, 0.1f, 1f);
    [Tooltip("색을 넣을 셰이더 프로퍼티 (URP Unlit = _BaseColor)")]
    [SerializeField] string colorProperty = "_BaseColor";
    [Tooltip("등급 전환 시 색 보간 시간(초)")]
    [SerializeField] float colorBlendDuration = 0.25f;

    [Header("Visibility")]
    [Tooltip("주행(NormalRiding) 상태가 아니면 숨긴다")]
    [SerializeField] bool hideOnNonRiding = true;
    [Tooltip("이 속도(km/h) 미만이면 숨긴다. 0이면 속도 무관")]
    [SerializeField] float visibleSpeedThreshold = 1f;
    [Tooltip("표시/숨김 페이드 시간(초)")]
    [SerializeField] float fadeDuration = 0.3f;

    // ── 내부 상태 ──────────────────────────────────────────────────

    const float LutStep = 0.5f;   // 경로 룩업 테이블 간격(m)
    const float SearchRadius = 30f; // 최근접 지점 국소 탐색 반경(m)

    Vector3[] _lutPos;
    Vector3[] _lutFwd;
    float _routeLength;

    readonly List<Transform> _pool = new List<Transform>();
    readonly List<MeshRenderer> _poolRenderer = new List<MeshRenderer>();
    Mesh _quad;
    MaterialPropertyBlock _mpb;
    int _colorPropId;

    float _viewerDist;
    bool _hasViewerDist;
    float _flowOffset;

    float _alpha;         // 현재 마스터 알파
    float _targetAlpha;   // 목표 마스터 알파
    Color _tint;          // 현재 틴트

    float _yellowThreshold = 20f;
    float _redThreshold = 30f;

    bool _stateAllowsShow = true;

    // ── Unity 수명주기 ─────────────────────────────────────────────

    void Awake()
    {
        try
        {
            _colorPropId = Shader.PropertyToID(colorProperty);
            _mpb = new MaterialPropertyBlock();
            _tint = normalColor;

            BuildLut();
            BuildPool();
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"RoadNavigationGuide.Awake 오류 발생: {ex.Message}");
        }
    }

    void Start()
    {
        try
        {
            if (InputManager.Instance != null)
            {
                _yellowThreshold = InputManager.Instance.YellowThreshold;
                _redThreshold = InputManager.Instance.RedThreshold;
            }

            if (viewer == null && Camera.main != null)
                viewer = Camera.main.transform;
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"RoadNavigationGuide.Start 오류 발생: {ex.Message}");
        }
    }

    void OnEnable()
    {
        try
        {
            if (GameManager.Instance != null)
            {
                GameManager.Instance.OnStateChanged += OnGameStateChanged;
                OnGameStateChanged(GameManager.Instance.CurrentState);
            }
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"RoadNavigationGuide.OnEnable 오류 발생: {ex.Message}");
        }
    }

    void OnDisable()
    {
        try
        {
            if (GameManager.Instance != null)
                GameManager.Instance.OnStateChanged -= OnGameStateChanged;

            HideAll();
            _hasViewerDist = false;
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"RoadNavigationGuide.OnDisable 오류 발생: {ex.Message}");
        }
    }

    void OnDestroy()
    {
        if (_quad != null)
        {
            if (Application.isPlaying) Destroy(_quad);
            else DestroyImmediate(_quad);
        }
    }

    void LateUpdate()
    {
        try
        {
            if (_lutPos == null || viewer == null) return;

            float kph = InputManager.Instance != null ? InputManager.Instance.SpeedKph : 0f;

            UpdateFlow(kph);
            UpdateAlpha(kph);
            UpdateTint(kph);

            if (_alpha <= 0.001f)
            {
                HideAll();
                return;
            }

            _viewerDist = FindDistanceOnRoute(viewer.position);
            _hasViewerDist = true;

            PlaceChevrons(_viewerDist, _alpha, _tint);
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"RoadNavigationGuide.LateUpdate 오류 발생: {ex.Message}");
        }
    }

    // ── 공개 API ───────────────────────────────────────────────────

    /// <summary>런타임에 경로를 교체한다.</summary>
    public void SetRoute(SplineContainer newRoute)
    {
        route = newRoute;
        _hasViewerDist = false;
        BuildLut();
    }

    /// <summary>강제로 표시/숨김을 전환한다. (상태 구독과 별개)</summary>
    public void SetVisible(bool visible)
    {
        _stateAllowsShow = visible;
    }

    public bool HasRoute => _lutPos != null && _lutPos.Length > 1;
    public float RouteLength => _routeLength;

    // ── 흐름 / 알파 / 색 ───────────────────────────────────────────

    void UpdateFlow(float kph)
    {
        float mps = flowFollowsBikeSpeed ? kph * (1000f / 3600f) * flowSpeed : flowSpeed;
        if (Mathf.Abs(mps) < 1e-5f) return;

        _flowOffset += mps * Time.deltaTime;
        // 간격 단위로 감아 두면 좌표 정밀도 손실 없이 동일한 배치가 유지된다.
        _flowOffset = Mathf.Repeat(_flowOffset, Mathf.Max(0.01f, spacing));
    }

    void UpdateAlpha(float kph)
    {
        bool show = _stateAllowsShow && (visibleSpeedThreshold <= 0f || kph >= visibleSpeedThreshold);
        _targetAlpha = show ? maxAlpha : 0f;

        float step = fadeDuration > 0.001f ? Time.deltaTime / fadeDuration : 1f;
        _alpha = Mathf.MoveTowards(_alpha, _targetAlpha, step);
    }

    void UpdateTint(float kph)
    {
        Color target = normalColor;
        if (tintBySpeed)
        {
            if (kph >= _redThreshold) target = redColor;
            else if (kph >= _yellowThreshold) target = yellowColor;
        }

        float step = colorBlendDuration > 0.001f ? Time.deltaTime / colorBlendDuration : 1f;
        _tint = Color.Lerp(_tint, target, Mathf.Clamp01(step));
    }

    // ── 배치 ───────────────────────────────────────────────────────

    void PlaceChevrons(float viewerDist, float alpha, Color tint)
    {
        float from = viewerDist + nearDistance;
        float to = Mathf.Min(viewerDist + farDistance, _routeLength);

        // spacing 격자에 스냅해야 셰브론이 노면에 붙어 있는 것처럼 보인다.
        float step = Mathf.Max(0.05f, spacing);
        float first = Mathf.Ceil((from - _flowOffset) / step) * step + _flowOffset;

        int used = 0;
        for (float d = first; d <= to && used < _pool.Count; d += step)
        {
            Vector3 pos = PositionAt(d);
            Vector3 fwd = ForwardAt(d);
            Vector3 up = Vector3.up;

            if (Mathf.Abs(lateralOffset) > 0.001f)
                pos += Vector3.Cross(up, fwd).normalized * -lateralOffset;

            if (conformToGround &&
                Physics.Raycast(pos + Vector3.up * rayHeight, Vector3.down, out RaycastHit hit,
                                rayHeight + rayLength, groundMask, QueryTriggerInteraction.Ignore))
            {
                pos = hit.point;
                up = hit.normal;
            }

            pos += up * groundOffset;

            Vector3 planarFwd = Vector3.ProjectOnPlane(fwd, up);
            if (planarFwd.sqrMagnitude < 1e-6f) planarFwd = fwd;

            var tr = _pool[used];
            tr.SetPositionAndRotation(pos, Quaternion.LookRotation(planarFwd, up));

            float a = alpha * EdgeFade(d - viewerDist);
            var c = tint;
            c.a *= a;

            _mpb.SetColor(_colorPropId, c);
            _poolRenderer[used].SetPropertyBlock(_mpb);
            _poolRenderer[used].enabled = a > 0.002f;

            used++;
        }

        for (int i = used; i < _poolRenderer.Count; i++)
            _poolRenderer[i].enabled = false;
    }

    /// <summary>표시 구간 앞뒤 끝에서 부드럽게 사라지게 하는 계수.</summary>
    float EdgeFade(float relative)
    {
        float a = 1f;

        if (nearFade > 0.001f)
            a *= Mathf.Clamp01((relative - nearDistance) / nearFade);

        if (farFade > 0.001f)
            a *= Mathf.Clamp01((farDistance - relative) / farFade);

        return a;
    }

    void HideAll()
    {
        for (int i = 0; i < _poolRenderer.Count; i++)
            if (_poolRenderer[i] != null) _poolRenderer[i].enabled = false;
    }

    // ── 경로 룩업 테이블 ───────────────────────────────────────────

    /// <summary>
    /// 스플라인을 균등 t로 잘게 샘플링해 누적 길이를 구한 뒤,
    /// 거리 기준 등간격(LutStep) 위치/접선 테이블로 재구성한다.
    /// 런타임 조회를 전부 이 테이블로 처리하므로 매 프레임 스플라인 평가가 없다.
    /// </summary>
    void BuildLut()
    {
        _lutPos = null;
        _lutFwd = null;
        _routeLength = 0f;

        if (route == null || route.Spline == null || route.Spline.Count < 2) return;

        int fine = Mathf.Clamp(route.Spline.Count * 128, 512, 20000);

        var pts = new Vector3[fine + 1];
        for (int i = 0; i <= fine; i++)
            pts[i] = (Vector3)route.EvaluatePosition(i / (float)fine);

        var cum = new float[fine + 1];
        for (int i = 1; i <= fine; i++)
            cum[i] = cum[i - 1] + Vector3.Distance(pts[i - 1], pts[i]);

        _routeLength = cum[fine];
        if (_routeLength < 0.01f) return;

        int n = Mathf.CeilToInt(_routeLength / LutStep);
        _lutPos = new Vector3[n + 1];
        _lutFwd = new Vector3[n + 1];

        int cursor = 0;
        for (int i = 0; i <= n; i++)
        {
            float d = Mathf.Min(i * LutStep, _routeLength);
            while (cursor < fine - 1 && cum[cursor + 1] < d) cursor++;

            float segLen = cum[cursor + 1] - cum[cursor];
            float f = segLen > 1e-6f ? (d - cum[cursor]) / segLen : 0f;
            _lutPos[i] = Vector3.Lerp(pts[cursor], pts[cursor + 1], f);
        }

        for (int i = 0; i <= n; i++)
        {
            int a = Mathf.Max(0, i - 1);
            int b = Mathf.Min(n, i + 1);
            Vector3 dir = _lutPos[b] - _lutPos[a];
            _lutFwd[i] = dir.sqrMagnitude > 1e-8f ? dir.normalized : Vector3.forward;
        }
    }

    Vector3 PositionAt(float distance)
    {
        float x = Mathf.Clamp(distance, 0f, _routeLength) / LutStep;
        int i = Mathf.Clamp((int)x, 0, _lutPos.Length - 2);
        return Vector3.Lerp(_lutPos[i], _lutPos[i + 1], x - i);
    }

    Vector3 ForwardAt(float distance)
    {
        float x = Mathf.Clamp(distance, 0f, _routeLength) / LutStep;
        int i = Mathf.Clamp((int)x, 0, _lutFwd.Length - 2);
        Vector3 f = Vector3.Slerp(_lutFwd[i], _lutFwd[i + 1], x - i);
        return f.sqrMagnitude > 1e-8f ? f.normalized : Vector3.forward;
    }

    /// <summary>
    /// 시점 위치에 가장 가까운 경로상 거리를 찾는다.
    /// 직전 프레임 주변만 국소 탐색하고, 결과가 탐색창 경계에 걸리면 전역 재탐색한다.
    /// (타임라인 시크·레벨 재시작 대응)
    /// </summary>
    float FindDistanceOnRoute(Vector3 p)
    {
        int last = _lutPos.Length - 1;
        int lo = 0, hi = last;

        if (_hasViewerDist)
        {
            int center = Mathf.Clamp(Mathf.RoundToInt(_viewerDist / LutStep), 0, last);
            int r = Mathf.CeilToInt(SearchRadius / LutStep);
            lo = Mathf.Max(0, center - r);
            hi = Mathf.Min(last, center + r);
        }

        int best = NearestIndex(p, lo, hi);

        // 경계에 붙었다면 국소 탐색이 실패한 것으로 보고 전체를 다시 훑는다.
        if (_hasViewerDist && (best == lo && lo > 0 || best == hi && hi < last))
            best = NearestIndex(p, 0, last);

        // 인접 구간에 투영해 정밀도 보정
        float d = best * LutStep;
        if (best < last)
        {
            Vector3 seg = _lutPos[best + 1] - _lutPos[best];
            float len2 = seg.sqrMagnitude;
            if (len2 > 1e-8f)
            {
                float t = Mathf.Clamp01(Vector3.Dot(p - _lutPos[best], seg) / len2);
                d += t * LutStep;
            }
        }

        return Mathf.Clamp(d, 0f, _routeLength);
    }

    int NearestIndex(Vector3 p, int lo, int hi)
    {
        int best = lo;
        float bestSqr = float.MaxValue;

        for (int i = lo; i <= hi; i++)
        {
            float sqr = (_lutPos[i] - p).sqrMagnitude;
            if (sqr < bestSqr) { bestSqr = sqr; best = i; }
        }

        return best;
    }

    // ── 풀 ─────────────────────────────────────────────────────────

    void BuildPool()
    {
        ClearPool();

        if (chevronMaterial == null)
        {
            Debug.LogWarning($"RoadNavigationGuide({name}): Chevron Material이 없어 셰브론을 생성하지 않습니다.");
            return;
        }

        _quad = BuildQuadMesh(chevronWidth, chevronLength);

        int count = Mathf.Max(1, poolSize);
        for (int i = 0; i < count; i++)
        {
            var go = new GameObject($"Chevron_{i:00}");
            go.transform.SetParent(transform, false);
            go.layer = gameObject.layer;

            if (!Application.isPlaying)
                go.hideFlags = HideFlags.DontSave | HideFlags.HideInHierarchy;

            var mf = go.AddComponent<MeshFilter>();
            mf.sharedMesh = _quad;

            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = chevronMaterial;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            mr.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            mr.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            mr.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
            mr.allowOcclusionWhenDynamic = false;
            mr.enabled = false;

            _pool.Add(go.transform);
            _poolRenderer.Add(mr);
        }
    }

    void ClearPool()
    {
        for (int i = 0; i < _pool.Count; i++)
        {
            if (_pool[i] == null) continue;
            if (Application.isPlaying) Destroy(_pool[i].gameObject);
            else DestroyImmediate(_pool[i].gameObject);
        }

        _pool.Clear();
        _poolRenderer.Clear();

        // 에디터 미리보기 후 도메인 리로드로 참조가 끊긴 잔여 오브젝트 정리
        for (int i = transform.childCount - 1; i >= 0; i--)
        {
            var child = transform.GetChild(i);
            if (!child.name.StartsWith("Chevron_")) continue;

            if (Application.isPlaying) Destroy(child.gameObject);
            else DestroyImmediate(child.gameObject);
        }
    }

    /// <summary>XZ 평면에 눕고 +Z를 향하는 쿼드. 노면 유도선용.</summary>
    static Mesh BuildQuadMesh(float width, float length)
    {
        float hw = Mathf.Max(0.01f, width) * 0.5f;
        float hl = Mathf.Max(0.01f, length) * 0.5f;

        var mesh = new Mesh { name = "NavChevronQuad" };
        mesh.vertices = new[]
        {
            new Vector3(-hw, 0f, -hl),
            new Vector3(-hw, 0f,  hl),
            new Vector3( hw, 0f, -hl),
            new Vector3( hw, 0f,  hl),
        };
        mesh.uv = new[]
        {
            new Vector2(0f, 0f),
            new Vector2(0f, 1f),
            new Vector2(1f, 0f),
            new Vector2(1f, 1f),
        };
        mesh.normals = new[] { Vector3.up, Vector3.up, Vector3.up, Vector3.up };
        mesh.triangles = new[] { 0, 1, 2, 2, 1, 3 };
        mesh.RecalculateBounds();
        return mesh;
    }

    // ── GameState 구독 ─────────────────────────────────────────────

    void OnGameStateChanged(GameState state)
    {
        try
        {
            if (!hideOnNonRiding)
            {
                _stateAllowsShow = true;
                return;
            }

            _stateAllowsShow = state == GameState.NormalRiding;
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"RoadNavigationGuide.OnGameStateChanged 오류 발생: {ex.Message}");
        }
    }

    // ── 에디터 미리보기 ────────────────────────────────────────────

#if UNITY_EDITOR
    /// <summary>에디터에서 Play 없이 배치를 확인한다.</summary>
    public void EditorPreview()
    {
        _colorPropId = Shader.PropertyToID(colorProperty);
        _mpb ??= new MaterialPropertyBlock();

        BuildLut();
        BuildPool();

        if (_lutPos == null) return;

        Transform v = viewer != null ? viewer :
                      (Camera.main != null ? Camera.main.transform : null);
        if (v == null) return;

        _hasViewerDist = false;
        _viewerDist = FindDistanceOnRoute(v.position);
        _hasViewerDist = true;

        PlaceChevrons(_viewerDist, maxAlpha, normalColor);
    }

    /// <summary>미리보기로 만든 셰브론을 제거한다.</summary>
    public void EditorClearPreview()
    {
        ClearPool();
        if (_quad != null) { DestroyImmediate(_quad); _quad = null; }
    }

    public SplineContainer Route => route;
    public float EditorNearDistance => nearDistance;
    public float EditorFarDistance => farDistance;
    public float EditorSpacing => spacing;
    public Material EditorMaterial => chevronMaterial;
    public int EditorPoolSize => poolSize;

    void OnValidate()
    {
        chevronWidth = Mathf.Max(0.01f, chevronWidth);
        chevronLength = Mathf.Max(0.01f, chevronLength);
        spacing = Mathf.Max(0.05f, spacing);
        nearDistance = Mathf.Max(0f, nearDistance);
        farDistance = Mathf.Max(nearDistance + spacing, farDistance);
        poolSize = Mathf.Max(1, poolSize);
    }
#endif
}
