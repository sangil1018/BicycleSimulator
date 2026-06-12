using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// 하드웨어 X 버튼 및 키보드 입력을 통합 처리하는 유틸리티 클래스.
/// 오브젝트가 활성화된 상태에서만 동작합니다.
/// </summary>
public class ExitxButton : MonoBehaviour
{
    [Header("UI Feedback")]
    [SerializeField] private GameObject selectObject; // 우키(X선택) 시 활성화될 오브젝트

    [Header("Events")]
    public UnityEvent onExecute; // 실행(X버튼/Esc/우방향키확인) 시 발생할 이벤트

    private void OnEnable()
    {
        if (InputManager.Instance != null)
        {
            InputManager.Instance.OnBtnX += HandleExecute;
        }
    }

    private void OnDisable()
    {
        if (InputManager.Instance != null)
        {
            InputManager.Instance.OnBtnX -= HandleExecute;
        }
    }

    /// <summary>
    /// 하드웨어 X 버튼 또는 키보드 종료(Esc) 입력 처리
    /// </summary>
    private void HandleExecute()
    {
        if (!gameObject.activeInHierarchy) return;

        onExecute?.Invoke();
    }
}
