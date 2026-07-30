using UnityEngine;

public class BikeRotation : MonoBehaviour
{
    [SerializeField] private Transform bikeRotationTransform;

    bool _following;

    public void RotateBike(float rotationAngle)
    {
        _following = false;
        transform.localRotation = Quaternion.Euler(0f, rotationAngle, 0f);
    }

    /// <summary>
    /// 호출 시점부터 매 프레임 bikeRotationTransform의 회전값을 계속 따라간다.
    /// </summary>
    public void FollowBikeRotation()
    {
        _following = true;
        ApplyRotation();
    }

    public void StopFollowBikeRotation()
    {
        _following = false;
    }

    void LateUpdate()
    {
        if (_following) ApplyRotation();
    }

    void ApplyRotation()
    {
        if (bikeRotationTransform == null) return;
        transform.localRotation = bikeRotationTransform.localRotation;
    }
}
