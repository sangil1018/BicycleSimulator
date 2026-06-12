using UnityEngine;
using UnityEngine.Timeline;
using UnityEngine.Playables;

/// <summary>
/// 네비게이션 방향 변경 시그널.
/// direction: normal / left / right / right_45
/// </summary>
[System.Serializable]
public class DirectionSignal : ScriptableObject, INotification
{
    public PropertyName id => new PropertyName(name);

    [Tooltip("normal / left / right / right_45")]
    public string direction = "normal";
}
