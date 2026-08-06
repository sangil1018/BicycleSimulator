using System.Collections;
using UnityEngine;


/// <summary>
/// 속도 UI(속도 텍스트 · 과속 경고) 표시 제어.
/// GameManager.OnStateChanged를 구독하여 자동으로 Show/Hide 처리.
///
/// 속도 등급(Normal/Yellow/Red) 판정은 이 컴포넌트가 하지 않는다 —
/// RoadNavigationGuide가 단독 판정하고 여기서는 통지를 받기만 한다.
/// 두 곳에서 따로 계산하면 셰브론 색과 과속 UI의 전환 시점이 어긋나기 때문.
/// 주행 방향 안내 역시 RoadNavigationGuide(노면 셰브론)가 담당한다.
/// </summary>
public class SpeedUIController : MonoBehaviour
{
    [Header("Canvas Group")]
    [SerializeField] CanvasGroup speedCanvasGroup;

    [Header("Speed Text")]
    [Tooltip("속도 숫자 텍스트. 영속 싱글톤인 InputManager가 아니라 씬 스코프인 여기서 갱신한다 — 씬 전환 시 참조가 끊기지 않도록.")]
    [SerializeField] TMPro.TMP_Text speedText;

    [Header("Over Speed UI")]
    [SerializeField] GameObject overSpeedUI;
    [Tooltip("과속 UI 애니메이션 길이를 읽지 못했을 때 사용할 표시 시간(초)")]
    [SerializeField] float overSpeedFallbackDuration = 1f;

    [Header("Fade")]
    [SerializeField] float fadeDuration = 0.3f;

    [Header("Speed Fade")]
    [Tooltip("이 속도(km/h) 이상이면 UI 페이드인")]
    [SerializeField] float visibleSpeedThreshold = 1f;

    // ── 내부 상태 ──────────────────────────────────────────────────
    Coroutine _fadeCoroutine;
    bool _stateAllowsShow = false;
    bool _isVisible = false;
    Animator _overSpeedAnimator;
    Coroutine _overSpeedCoroutine;
    RoadNavigationGuide _tierSource;

    void Start()
    {
        try
        {
            if (overSpeedUI != null)
                _overSpeedAnimator = overSpeedUI.GetComponentInChildren<Animator>(true);
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"SpeedUIController.Start 오류 발생: {ex.Message}");
        }
    }

    void OnEnable()
    {
        try
        {
            if (GameManager.Instance != null)
                GameManager.Instance.OnStateChanged += OnGameStateChanged;

            // 등급 판정의 소유자. 같은 씬에 상주하므로 Awake 순서와 무관하게 찾을 수 있다.
            if (_tierSource == null)
                _tierSource = FindFirstObjectByType<RoadNavigationGuide>();

            if (_tierSource != null)
                _tierSource.OnTierChanged += OnSpeedTierChanged;
            else
                Debug.LogWarning($"SpeedUIController({name}): 씬에 RoadNavigationGuide가 없어 과속 UI가 동작하지 않습니다.");
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"SpeedUIController.OnEnable 오류 발생: {ex.Message}");
        }
    }

    void OnDisable()
    {
        try
        {
            if (GameManager.Instance != null)
                GameManager.Instance.OnStateChanged -= OnGameStateChanged;

            // 비활성 상태에서 통지를 받으면 StartCoroutine이 실패하므로 반드시 해제한다.
            if (_tierSource != null)
                _tierSource.OnTierChanged -= OnSpeedTierChanged;

            // 비활성화되면 코루틴이 중단되므로 핸들을 비워 재활성화 시 다시 재생될 수 있게 한다.
            _overSpeedCoroutine = null;
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"SpeedUIController.OnDisable 오류 발생: {ex.Message}");
        }
    }

    void Update()
    {
        try
        {
            if (InputManager.Instance == null) return;

            float spd = GameManager.Instance.CurrentState == GameState.GameResult ? 0f : InputManager.Instance.SpeedKph;

            if (speedText != null) speedText.text = $"{spd:F0}";

            // 속도 기반 페이드인/아웃 (NormalRiding 상태일 때만)
            if (_stateAllowsShow)
            {
                bool shouldBeVisible = spd >= visibleSpeedThreshold;
                if (shouldBeVisible != _isVisible)
                {
                    _isVisible = shouldBeVisible;
                    StartFade(_isVisible ? 1f : 0f);
                }
            }
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"SpeedUIController.Update 오류 발생: {ex.Message}");
        }
    }

    // ── 속도 등급 통지 ─────────────────────────────────────────────

    /// <summary>RoadNavigationGuide가 등급 변화를 알려올 때 호출된다.</summary>
    void OnSpeedTierChanged(SpeedTier tier)
    {
        try
        {
            // Red 진입 시에만 과속 UI 재생 시작. 재생 중에는 무시하고,
            // 속도가 떨어져도 애니메이션이 끝날 때까지 유지한다.
            if (tier == SpeedTier.Red)
                PlayOverSpeed();
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"SpeedUIController.OnSpeedTierChanged 오류 발생: {ex.Message}");
        }
    }

    // ── 과속 UI ────────────────────────────────────────────────────

    /// <summary>과속 UI를 표시하고 애니메이션이 끝나면 숨긴다. 재생 중 호출은 무시된다.</summary>
    void PlayOverSpeed()
    {
        try
        {
            if (overSpeedUI == null) return;
            if (_overSpeedCoroutine != null) return; // 재생 중 중복 호출 회피

            _overSpeedCoroutine = StartCoroutine(OverSpeedRoutine());
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"SpeedUIController.PlayOverSpeed 오류 발생: {ex.Message}");
        }
    }

    IEnumerator OverSpeedRoutine()
    {
        overSpeedUI.SetActive(true);

        float duration = overSpeedFallbackDuration;

        if (_overSpeedAnimator != null && _overSpeedAnimator.runtimeAnimatorController != null)
        {
            // SetActive 직후에는 상태 정보가 아직 갱신되지 않으므로 강제로 평가한다.
            _overSpeedAnimator.Rebind();
            _overSpeedAnimator.Update(0f);

            var info = _overSpeedAnimator.GetCurrentAnimatorStateInfo(0);
            float speed = Mathf.Abs(_overSpeedAnimator.speed * info.speed);
            if (info.length > 0f && !info.loop && speed > 0.0001f)
                duration = info.length / speed;
        }

        yield return new WaitForSeconds(duration);

        overSpeedUI.SetActive(false);
        _overSpeedCoroutine = null;
    }

    /// <summary>재생 중이던 과속 UI를 즉시 중단하고 숨긴다.</summary>
    void ResetOverSpeed()
    {
        if (_overSpeedCoroutine != null)
        {
            StopCoroutine(_overSpeedCoroutine);
            _overSpeedCoroutine = null;
        }

        if (overSpeedUI != null)
            overSpeedUI.SetActive(false);
    }

    // ── Show / Hide ────────────────────────────────────────────────

    public void Hide()
    {
        try
        {
            StartFade(0f);
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"SpeedUIController.Hide 오류 발생: {ex.Message}");
        }
    }

    void StartFade(float targetAlpha)
    {
        try
        {
            if (_fadeCoroutine != null) StopCoroutine(_fadeCoroutine);
            _fadeCoroutine = StartCoroutine(FadeRoutine(targetAlpha));
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"SpeedUIController.StartFade 오류 발생: {ex.Message}");
        }
    }

    IEnumerator FadeRoutine(float target)
    {
        if (speedCanvasGroup == null) yield break;

        float start = speedCanvasGroup.alpha;
        float elapsed = 0f;

        while (elapsed < fadeDuration)
        {
            elapsed += Time.deltaTime;
            speedCanvasGroup.alpha = Mathf.Lerp(start, target, elapsed / fadeDuration);
            yield return null;
        }

        speedCanvasGroup.alpha = target;
    }

    // ── GameState 구독 ─────────────────────────────────────────────

    void OnGameStateChanged(GameState state)
    {
        try
        {
            switch (state)
            {
                case GameState.NormalRiding:
                    _stateAllowsShow = true;
                    _isVisible = false; // Update()에서 속도 기반으로 다시 판단
                    // 과속 UI 재무장은 RoadNavigationGuide가 등급을 재통지하며 처리한다.
                    break;
                case GameState.OXQuiz:
                case GameState.EventBrake:
                case GameState.EventWarning:
                case GameState.BicycleStop:
                case GameState.CrosswalkWalk:
                    _stateAllowsShow = false;
                    _isVisible = false;
                    ResetOverSpeed(); // 비주행 상태에선 과속 UI off
                    Hide();
                    break;
            }
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"SpeedUIController.OnGameStateChanged 오류 발생: {ex.Message}");
        }
    }
}
