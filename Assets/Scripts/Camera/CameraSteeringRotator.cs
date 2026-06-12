using System;
using System.IO;
using UnityEngine;

/// <summary>
/// anim_rot 오브젝트에 부착.
/// InputManager.SteeringAngle(-45~+45) → Y축 회전 1:1 반영.
/// config.ini: CameraSteerSmoothTime
/// </summary>
public class CameraSteeringRotator : MonoBehaviour
{
    [SerializeField] float smoothTime = 0.12f;

    float _currentAngle;
    float _velocity;

    void Start()
    {
        LoadConfig();
    }

    void LoadConfig()
    {
        string path = Path.Combine(Application.dataPath, "../config.ini");
        if (!File.Exists(path)) return;

        try
        {
            foreach (var line in File.ReadAllLines(path))
            {
                if (string.IsNullOrWhiteSpace(line) || line.TrimStart().StartsWith(";")) continue;
                var parts = line.Split('=');
                if (parts.Length != 2) continue;

                if (parts[0].Trim() == "CameraSteerSmoothTime")
                    if (float.TryParse(parts[1].Trim(), out float t)) smoothTime = Mathf.Max(0f, t);
            }
            Debug.Log($"[CameraSteer] SmoothTime={smoothTime}s");
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[CameraSteer] config 읽기 실패: {e.Message}");
        }
    }

    void Update()
    {
        float target = InputManager.Instance != null ? InputManager.Instance.SteeringAngle : 0f;
        _currentAngle = Mathf.SmoothDamp(_currentAngle, target, ref _velocity, smoothTime);
        transform.localEulerAngles = new Vector3(0f, _currentAngle, 0f);
    }
}
