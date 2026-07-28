using UnityEngine.Timeline;

// CheckpointMarker는 노티피케이션이 아니라 TimelineGameController가 에셋에서 직접 읽으므로
// 다른 마커 트랙과 달리 TrackBindingType(수신자)이 필요 없다.
[TrackColor(0.30f, 0.78f, 0.45f)]
public class CheckpointTrack : MarkerTrack { }
