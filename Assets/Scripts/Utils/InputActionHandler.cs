using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;

public sealed class InputActionHandler : MonoBehaviour
{
    [field: Space] 
    [field: SerializeField] InputAction Action;

    [field: Space] 
    [field: SerializeField] UnityEvent Event;

    private void OnEnable()
    {
        Action.performed += OnPerformed;
        Action.Enable();
    }

    private void OnDisable()
    {
        Action.Disable();
        Action.performed -= OnPerformed;
    }

    private void OnPerformed(InputAction.CallbackContext ctx)
        => Event.Invoke();
}