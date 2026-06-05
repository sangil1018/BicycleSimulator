using System;
using System.Collections.Generic;
using UnityEngine;

public enum WaypointType
{
    BrakeEvent,     // 돌발 이벤트 + 브레이크 판정
    OXQuiz,         // OX 퀴즈
    Crosswalk,      // 횡단보도 보행 시퀀스
    DirectionHint,  // 방향 화살표 표시
}

[Serializable]
public struct VideoWaypoint
{
    [Tooltip("이벤트 라벨 (경고 문구 또는 방향 텍스트)")]
    public string label;

    [Tooltip("영상에서 이 이벤트가 발생하는 시각 (초)")]
    public float triggerTime;

    public WaypointType type;

    [Tooltip("OXQuiz 타입일 때 퀴즈 번호 (1~4)")]
    public int quizIndex;
}

/// <summary>
/// 영상 웨이포인트 테이블 ScriptableObject.
/// 생성: Project 패널 우클릭 → Create → BicycleSim → Waypoint Table
/// 
/// [고급 기준 웨이포인트 예시]
/// 0  triggerTime:12.5  BrakeEvent   "보행자 돌발 주의!"
/// 1  triggerTime:18.0  OXQuiz       quizIndex:0
/// 2  triggerTime:35.2  BrakeEvent   "우회전 사각지대 주의!"
/// 3  triggerTime:41.0  OXQuiz       quizIndex:1
/// 4  triggerTime:55.0  Crosswalk    "잠시 후 좌회전"
/// 5  triggerTime:78.5  BrakeEvent   "도어링 사고 주의!"
/// 6  triggerTime:84.0  OXQuiz       quizIndex:2
/// 7  triggerTime:102.0 BrakeEvent   "스마트폰 사용 위험!"
/// 8  triggerTime:108.0 OXQuiz       quizIndex:3
/// </summary>
[CreateAssetMenu(fileName = "WaypointTable_Level2",
                 menuName  = "BicycleSim/Waypoint Table")]
public class VideoWaypointTable : ScriptableObject
{
    [Tooltip("triggerTime 오름차순으로 정렬할 것")]
    public List<VideoWaypoint> waypoints = new();
}
