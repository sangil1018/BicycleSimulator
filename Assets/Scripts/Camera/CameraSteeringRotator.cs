using UnityEngine;

/// <summary>
/// anim_rot 오브젝트에 부착.
/// InputManager.SteeringAngle(-45~+45) → Y축 회전 1:1 반영.
/// smoothTime은 InputManager가 config.ini에서 로드한 CameraSteerSmoothTime 사용.
/// </summary>
public class CameraSteeringRotator : MonoBehaviour
{
    [SerializeField] float smoothTime = 0.12f;

    float _currentAngle;
    float _velocity;

    void Start()
    {
        if (InputManager.Instance != null)
        {
            smoothTime = InputManager.Instance.CameraSteerSmoothTime;
            Debug.Log($"[CameraSteer] SmoothTime={smoothTime}s");
        }
    }

    void Update()
    {
        float target = InputManager.Instance != null ? InputManager.Instance.SteeringAngle : 0f;
        _currentAngle = Mathf.SmoothDamp(_currentAngle, target, ref _velocity, smoothTime);
        transform.localEulerAngles = new Vector3(0f, -_currentAngle / 3, 0f);
    }
}
