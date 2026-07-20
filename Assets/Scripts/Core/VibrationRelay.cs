using UnityEngine;

/// <summary>
/// [폐기됨] USB 릴레이 진동 제어기.
///
/// 진동은 ESP32-S3가 GPIO2(MOSFET 광커플러 절연 드라이버 모듈, SIG↔GPIO2 / GND 공통)로
/// 직접 구동한다. Unity는 InputManager.SendVibrate()로 패턴 번호(V0~V6)만 보내며,
/// 펌웨어의 진동 시퀀서가 ON/OFF 타이밍을 담당한다.
///
/// 이 컴포넌트는 더 이상 시리얼 포트를 열지 않는다. Home/Level1/Level2 씬에 오브젝트가
/// 그대로 남아 있어 스크립트를 삭제하면 참조가 깨지므로, 껍데기만 남겨 무동작으로 둔다.
/// 씬에서 오브젝트를 정리한 뒤에는 이 파일도 함께 지워도 된다.
/// </summary>
[System.Obsolete("진동은 ESP32(GPIO2)가 처리한다. InputManager.SendVibrate()를 사용할 것.")]
public class VibrationRelay : Singleton<VibrationRelay>
{
    /// <summary>항상 false — 릴레이 경로는 폐기됐다.</summary>
    public bool IsConnected => false;

    protected override void Awake()
    {
        base.Awake();
        if (Instance != this) return;
        UnityEngine.Debug.Log("[VibrationRelay] 폐기된 USB 릴레이 경로 — 포트를 열지 않음 (진동은 ESP32 GPIO2가 처리)");
    }

    public void Connect() { }
    public void Disconnect() { }
    public void VibrateShort() { }
    public void VibrateMedium() { }
    public void VibrateLong() { }
    public void Vibrate(float duration) { }
}
