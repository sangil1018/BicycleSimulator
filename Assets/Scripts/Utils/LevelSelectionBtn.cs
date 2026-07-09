using UnityEngine;

/// <summary>
/// 레벨 선택 버튼의 Animator를 OnSelectionChanged 이벤트에 따라 제어하는 컴포넌트.
/// isBeginnerBtn: true=초급(O/좌키 선택 시 활성), false=고급(X/우키 선택 시 활성)
/// </summary>
public class LevelSelectionBtn : MonoBehaviour
{
    [SerializeField] private bool isBeginnerBtn;
    private Animator _animator;

    private void Awake()
    {
        _animator = GetComponent<Animator>();
    }

    private void OnEnable()
    {
        if (InputManager.Instance != null)
            InputManager.Instance.OnSelectionChanged += OnSelectionChanged;
    }

    private void OnDisable()
    {
        if (InputManager.Instance != null)
            InputManager.Instance.OnSelectionChanged -= OnSelectionChanged;
    }

    private void OnSelectionChanged(bool? selection)
    {
        if (_animator == null) return;

        bool isSelected    = isBeginnerBtn ? selection == true  : selection == false;
        bool isOtherPicked = isBeginnerBtn ? selection == false : selection == true;

        if (isSelected)
            _animator.SetTrigger("Select");
        else if (isOtherPicked)
            _animator.SetTrigger("Inactive");
        else
            _animator.SetTrigger("StartLoop");
    }

    public void SetSelected(bool selected)
    {
        if (_animator == null) return;
        _animator.SetTrigger(selected ? "Select" : "Inactive");
    }

    public void ResetToLoop()
    {
        if (_animator == null) return;
        _animator.SetTrigger("StartLoop");
    }
}
