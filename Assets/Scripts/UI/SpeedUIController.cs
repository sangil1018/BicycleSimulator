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
                _currentTier = tier;
                UpdateNavigationTrigger();
            }

            // 과속 UI 활성화 상태 제어
            if (overSpeedUI != null)
                overSpeedUI.SetActive(tier == SpeedTier.Red);
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"SpeedUIController.Update 오류 발생: {ex.Message}");
        }
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
                    _isVisible = false; // Update()에서 속도 기반으로 다시 판단
                    break;
                case GameState.OXQuiz:
                case GameState.EventBrake:
                case GameState.EventWarning:
                case GameState.BicycleStop:
                case GameState.CrosswalkWalk:
                    _stateAllowsShow = false;
                    _isVisible = false;
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
