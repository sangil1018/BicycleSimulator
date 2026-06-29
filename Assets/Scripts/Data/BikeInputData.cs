using System;
/// <summary>
/// ESP32-S3 JSON 패킷 v5.8 — 50 Hz, bicycle_sim_x
/// struct: 50 Hz JsonUtility.FromJson GC 부하 제거
/// </summary>
[Serializable]
public struct BikeInputData
{
    public int   id;
    public float rpm;   // 케이던스 RPM
    public float spd;   // 가상 속도 km/h (rpm × 0.25)
    public float str;   // 핸들 조향각 -45~+45 (DMP Yaw)
    public int   brk;   // 브레이크 0/1
    public int   o;     // O 버튼 0/1
    public int   x;     // X 버튼 0/1
}
