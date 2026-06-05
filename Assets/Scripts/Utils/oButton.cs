using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// 하드웨어 O 버튼 및 키보드 입력을 통합 처리하는 유틸리티 클래스.
/// 오브젝트가 활성화된 상태에서만 동작합니다.
/// </summary>
public class oButton : MonoBehaviour
{
    [Header("UI Feedback")]
    [SerializeField] private GameObject selectObject; // 좌키(O선택) 시 활성화될 오브젝트

    [Header("Events")]
    public UnityEvent onExecute; // 실행(O버튼/1번키) 시 발생할 이벤트

    private void OnEnable()
    {
        if (InputManager.Instance != null)
        {
            InputManager.Instance.OnBtnO += HandleExecute;
            InputManager.Instance.OnSelectionChanged += HandleSelectionChanged;
        }
    }

    private void OnDisable()
    {
        if (InputManager.Instance != null)
        {
            InputManager.Instance.OnBtnO -= HandleExecute;
            InputManager.Instance.OnSelectionChanged -= HandleSelectionChanged;
        }
    }

    /// <summary>
    /// 하드웨어 O 버튼 또는 키보드 확인(1) 입력 처리
    /// </summary>
    private void HandleExecute()
    {
        if (!gameObject.activeInHierarchy) return;
        
        // 실행 시 이벤트 호출
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
            // 좌키가 눌리면(isO == true) 활성화, 우키(false)나 초기화(null) 시 비활성화
            selectObject.SetActive(isO == true);
        }
    }
}
