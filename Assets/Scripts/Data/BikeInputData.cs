using System;
/// <summary>
/// ESP32-S3 JSON 패킷 v6.1 — 50 Hz, bicycle_sim_x
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

    // ── PAS 진단 (게임 로직 미사용) ──
    // "버튼은 정상인데 rpm만 0" 현상의 원인을 현장에서 구분하기 위한 필드.
    // 페달을 돌리는데 pl이 토글하지 않으면 센서 전원/배선, pl은 토글하는데
    // pc가 늘지 않으면 PAS 인터럽트가 죽은 것 (R 명령으로 복구 가능).
    // pc는 펌웨어에서 uint32지만 int로 받는다 — JsonUtility의 uint 지원을 신뢰하지 않기 위해서다.
    // 파싱이 조용히 실패해 pc가 항상 0이 되면 PAS 워치독이 영구 오발동한다.
    // 12극 PAS에서 int 범위(21억)를 넘기려면 수천 년이 걸리므로 잃는 것은 없다.
    public int   pc;    // 부팅/재초기화 후 누적 PAS 펄스 수
    public int   pl;    // PAS 핀 레벨 0/1
}
