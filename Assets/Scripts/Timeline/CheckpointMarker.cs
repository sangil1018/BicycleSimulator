using UnityEngine;
using UnityEngine.Timeline;

/// <summary>
/// 진행도 슬라이더의 균등 분할 지점과 매칭되는 체크포인트 마커.
/// TimelineGameController가 시작 시 타임라인 에셋에서 시간을 수집해
/// 슬라이더 리매핑 기준점으로 쓰고, 통과 시 해당 인덱스의 이벤트를 발동한다.
/// (노티피케이션이 아닌 시간 직접 감지 방식이라 수신자 바인딩이 필요 없음)
/// </summary>
public class CheckpointMarker : Marker
{
    [Tooltip("체크포인트 인덱스 (0~7) — TimelineGameController의 On Checkpoint 슬롯과 대응")]
    [SerializeField] private int index;

    public int Index => index;
}
