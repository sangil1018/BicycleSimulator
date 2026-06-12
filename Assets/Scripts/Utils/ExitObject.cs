using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// 외부에서 이벤트를 트리거할 수 있도록 돕는 유틸리티 클래스.
/// 인스펙터에서 원하는 동작(오브젝트 활성화 등)을 연결한 뒤 Execute()를 호출하여 사용합니다.
/// </summary>
public class ExitObject : MonoBehaviour
{
    [Header("Events")]
    public UnityEvent onExecute;

    /// <summary>
    /// 등록된 이벤트를 실행합니다. 외부(애니메이션, 타 스크립트 등)에서 호출 가능합니다.
    /// </summary>
    public void ExecuteExit()
    {
        onExecute?.Invoke();
    }
}
