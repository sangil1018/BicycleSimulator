using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// 하드웨어 X 버튼 및 키보드 입력을 통합 처리하는 유틸리티 클래스.
/// 오브젝트가 활성화된 상태에서만 동작합니다.
/// </summary>
public class xButton : MonoBehaviour
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
            InputManager.Instance.OnSelectionChanged += HandleSelectionChanged;
        }
    }

    private void OnDisable()
    {
        if (InputManager.Instance != null)
        {
            InputManager.Instance.OnBtnX -= HandleExecute;
            InputManager.Instance.OnSelectionChanged -= HandleSelectionChanged;
        }
    }

    /// <summary>
    /// 하드웨어 X 버튼 또는 키보드 종료(Esc) 입력 처리
    /// </summary>
    private void HandleExecute()
    {
        if (!gameObject.activeInHierarchy) return;

        selectObject.SetActive(true);

        onExecute?.Invoke();
    }

    /// <summary>
    /// 키보드 좌/우 방향키 입력 처리 (선택 상태 변경)
    /// </summary>
    /// <param name="isO">true=좌키(O), false=우키(X), null=초기화</param>
    private void HandleSelectionChanged(bool? isO)
    {
        if (!gameObject.activeInHierarchy) return;

        if (selectObject != null)
        {
            // 우키가 눌리면(isO == false) 활성화, 좌키(true)나 초기화(null) 시 비활성화
            selectObject.SetActive(isO == false);
        }
    }
}
