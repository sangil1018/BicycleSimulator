using System.Collections;
using UnityEngine;


/// <summary>
/// 속도 UI 표시 및 네비게이션 방향 애니메이터 제어.
/// GameManager.OnStateChanged를 구독하여 자동으로 Show/Hide 처리.
/// </summary>
public class SpeedUIController : MonoBehaviour
{
    [Header("Canvas Group")]
    [SerializeField] CanvasGroup speedCanvasGroup;

    [Header("Speed Thresholds (km/h)")]
    [Tooltip("노란색 속도 경고 기준값")]
    [SerializeField] float yellowThreshold = 20f;
    [Tooltip("빨간색 속도 경고 기준값")]
    [SerializeField] float redThreshold = 30f;

    [Header("Over Speed UI")]
    [SerializeField] GameObject overSpeedUI;
    [Tooltip("과속 UI 애니메이션 길이를 읽지 못했을 때 사용할 표시 시간(초)")]
    [SerializeField] float overSpeedFallbackDuration = 1f;

    [Header("Navigation Animator")]
    [SerializeField] Animator navigationAnimator;

    [Header("Fade")]
    [SerializeField] float fadeDuration = 0.3f;

    [Header("Speed Fade")]
    [Tooltip("이 속도(km/h) 이상이면 UI 페이드인")]
    [SerializeField] float visibleSpeedThreshold = 1f;

    // ── 내부 상태 ──────────────────────────────────────────────────
    string _currentDirection = "normal";
    SpeedTier _currentTier = SpeedTier.Normal;
    Coroutine _fadeCoroutine;
    bool _stateAllowsShow = false;
    bool _isVisible = false;
    Animator _overSpeedAnimator;
    Coroutine _overSpeedCoroutine;

    enum SpeedTier { Normal, Yellow, Red }

    void Start()
    {
        try
        {
            // InputManager 싱글톤 인스턴스로부터 설정값(Yellow/Red Threshold)을 가져옵니다.
            if (InputManager.Instance != null)
            {
                yellowThreshold = InputManager.Instance.YellowThreshold;
                redThreshold = InputManager.Instance.RedThreshold;
            }

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

            float spd = InputManager.Instance.SpeedKph;

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

            // 속도 등급(Tier) 결정
            SpeedTier tier;
            if (spd >= redThreshold) tier = SpeedTier.Red;
            else if (spd >= yellowThreshold) tier = SpeedTier.Yellow;
            else tier = SpeedTier.Normal;

            if (tier != _currentTier)
            {
                bool enteredRed = tier == SpeedTier.Red && _currentTier != SpeedTier.Red;
                _currentTier = tier;
                UpdateNavigationTrigger();

                // Red 진입 시에만 과속 UI 재생 시작. 재생 중에는 무시하고,
                // 속도가 떨어져도 애니메이션이 끝날 때까지 유지한다.
                if (enteredRed)
                    PlayOverSpeed();
            }
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"SpeedUIController.Update 오류 발생: {ex.Message}");
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

    // ── 네비게이션 트리거 ──────────────────────────────────────────

    /// <summary>Timeline Signal에서 호출. direction: normal / left / right / right_45</summary>
    public void SetDirection(string direction)
    {
        try
        {
            _currentDirection = direction;
            UpdateNavigationTrigger();
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"SpeedUIController.SetDirection 오류 발생: {ex.Message}");
        }
    }

    void UpdateNavigationTrigger()
    {
        try
        {
            if (navigationAnimator == null) return;

            string postfix = _currentTier switch
            {
                SpeedTier.Yellow => "_y",
                SpeedTier.Red => "_r",
                _ => "",
            };

            string trigger = _currentDirection + postfix;
            navigationAnimator.SetTrigger(trigger);
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"SpeedUIController.UpdateNavigationTrigger 오류 발생: {ex.Message}");
        }
    }

    // ── Show / Hide ────────────────────────────────────────────────

    public void Show()
    {
        try
        {
            StartFade(1f);
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"SpeedUIController.Show 오류 발생: {ex.Message}");
        }
    }

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
                    _isVisible = false;              // Update()에서 속도 기반으로 다시 판단
                    _currentTier = SpeedTier.Normal; // 등급 재평가 유도 (Update에서 SetActive/트리거 재발화)
                    break;
                case GameState.OXQuiz:
                case GameState.EventBrake:
                case GameState.EventWarning:
                case GameState.BicycleStop:
                case GameState.CrosswalkWalk:
                    _stateAllowsShow = false;
                    _isVisible = false;
                    _currentTier = SpeedTier.Normal;
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
