using UnityEditor;
using UnityEditor.Timeline;
using UnityEngine;
using UnityEngine.Timeline;

// Timeline 마커 색상은 USS 커스텀 변수로 적용 불가.
// DrawOverlay에서 빌트인 마커 텍스처를 GUI.color로 틴팅하는 방식으로 구현 (Annotation 공식 샘플 패턴).

static class MarkerTextures
{
    const string k_Base = "Packages/com.unity.timeline/Editor/StyleSheets/Images/";

    static Texture2D Load(string skin, string name)
        => EditorGUIUtility.Load($"{k_Base}{skin}/{name}.png") as Texture2D;

    static string Skin => EditorGUIUtility.isProSkin ? "DarkSkin" : "LightSkin";

    public static Texture2D Normal    => Load(Skin, "TimelineMarkerItem");
    public static Texture2D Selected  => Load(Skin, "TimelineMarkerItemSelected");
    public static Texture2D Collapsed => Load(Skin, "TimelineMarkerItemCollapsed");

    public static Texture2D Pick(MarkerUIStates state)
    {
        if (state.HasFlag(MarkerUIStates.Selected))  return Selected;
        if (state.HasFlag(MarkerUIStates.Collapsed)) return Collapsed;
        return Normal;
    }
}

static class MarkerOverlayDrawer
{
    public static void Draw(MarkerOverlayRegion region, MarkerUIStates state, Color color)
    {
        var tex = MarkerTextures.Pick(state);
        if (tex == null) return;

        var prev = GUI.color;
        GUI.color = color;
        GUI.DrawTexture(region.markerRegion, tex);
        GUI.color = prev;
    }
}

// ── GameEventMarker ────────────────────────────────────────────

[CustomTimelineEditor(typeof(GameEventMarker))]
public class GameEventMarkerEditor : MarkerEditor
{
    static readonly Color k_Color = new Color(0.86f, 0.23f, 0.23f);

    public override MarkerDrawOptions GetMarkerOptions(IMarker marker)
    {
        var m = (GameEventMarker)marker;
        return new MarkerDrawOptions { tooltip = m.EventType.ToString() };
    }

    public override void DrawOverlay(IMarker marker, MarkerUIStates uiState, MarkerOverlayRegion region)
        => MarkerOverlayDrawer.Draw(region, uiState, k_Color);
}

// ── QuizMarker ─────────────────────────────────────────────────

[CustomTimelineEditor(typeof(QuizMarker))]
public class QuizMarkerEditor : MarkerEditor
{
    static readonly Color k_Color = new Color(0.86f, 0.70f, 0.16f);

    public override MarkerDrawOptions GetMarkerOptions(IMarker marker)
    {
        var m = (QuizMarker)marker;
        return new MarkerDrawOptions { tooltip = $"OXQuiz [{m.QuizIndex}]" };
    }

    public override void DrawOverlay(IMarker marker, MarkerUIStates uiState, MarkerOverlayRegion region)
        => MarkerOverlayDrawer.Draw(region, uiState, k_Color);
}

// ── DirectionMarker ────────────────────────────────────────────

[CustomTimelineEditor(typeof(DirectionMarker))]
public class DirectionMarkerEditor : MarkerEditor
{
    static readonly Color k_Color = new Color(0.23f, 0.55f, 0.86f);

    public override MarkerDrawOptions GetMarkerOptions(IMarker marker)
    {
        var m = (DirectionMarker)marker;
        return new MarkerDrawOptions { tooltip = $"Dir: {m.Direction}" };
    }

    public override void DrawOverlay(IMarker marker, MarkerUIStates uiState, MarkerOverlayRegion region)
        => MarkerOverlayDrawer.Draw(region, uiState, k_Color);
}
