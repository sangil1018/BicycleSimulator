using System;
/// <summary>
/// ESP32-S3 JSON 패킷 v4 — PAS 12자석 홀 센서 기반
/// rpm = 케이던스 RPM (PAS 12자석 기반 크랭크 회전속도)
/// spd = 가상 속도 km/h (cadenceRpm × 0.25)
/// struct: JsonUtility.FromJson 호출이 50 Hz이므로 class 대신 struct로 GC 부하 제거
/// </summary>
[Serializable]
public struct BikeInputData
{
    public int   id;
    public float rpm;   // 케이던스 RPM
    public float spd;   // 가상 속도 km/h
    public float str;   // 핸들 조향각 (-45~+45)
    public int   brkL;
    public int   brkR;
    public int   o;
    public int   x;
}
