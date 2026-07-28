using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Splines;

/// <summary>
/// Route Spline Baker의 [루트 스플라인 웨이포인트] 탭.
///
/// 씬 뷰를 찍어 포인트를 놓고, 포인트마다 라운드(모서리 반경)를 줘서 경로를 구성한다.
/// 결과는 타임라인 베이크와 동일하게 같은 SplineContainer에 구워지므로,
/// RoadNavigationGuide 등 사용하는 쪽에서는 두 방식을 구분할 필요가 없다.
///
/// 데이터 원본은 씬에 남는 RouteWaypointPath 컴포넌트이므로 창을 닫아도 유지된다.
/// </summary>
public partial class RouteSplineBaker
{
    // ── 상태 ───────────────────────────────────────────────────────

    RouteWaypointPath wpPath;

    bool placingWaypoints;
    int  selectedWaypoint = -1;

    bool      wpSnapToGround = true;
    LayerMask wpGroundMask   = 1;    // 베이크 탭과 같은 기본값(Default 레이어)
    float     wpGroundOffset = 0f;

    bool wpAutoApply = true;

    Vector2 wpListScroll;

    readonly List<Vector3> wpPreview = new();

    Vector3 wpHoverPoint;   // 배치 모드에서 커서가 가리키는 지점
    bool    wpHoverValid;

    string      wpStatus     = "";
    MessageType wpStatusType = MessageType.None;

    // 리스트를 그리는 도중에 원소를 건드리면 IMGUI 레이아웃이 깨지므로,
    // 행에서 누른 버튼은 여기 적어두고 리스트를 다 그린 뒤에 처리한다.
    int wpPendingRemove      = -1;
    int wpPendingInsertAfter = -1;
    int wpPendingSwapFrom    = -1;
    int wpPendingSwapTo      = -1;

    static readonly Color WpLineColor     = new(1f, 0.75f, 0.2f, 0.95f);
    static readonly Color WpPointColor    = new(1f, 0.95f, 0.6f, 1f);
    static readonly Color WpSelectedColor = new(0.3f, 1f, 0.6f, 1f);
    static readonly Color WpRoundColor    = new(0.3f, 0.9f, 1f, 0.6f);

    // ── 수명 주기 ──────────────────────────────────────────────────

    void ResolveWaypointPath()
    {
        wpPath = output != null ? output.GetComponent<RouteWaypointPath>() : null;
    }

    /// <summary>Undo/Redo로 웨이포인트가 되돌아가면 미리보기도 다시 그린다.</summary>
    void OnWaypointUndoRedo()
    {
        SceneView.RepaintAll();
        Repaint();
    }

    // ── GUI ────────────────────────────────────────────────────────

    void OnWaypointGUI()
    {
        EditorGUILayout.HelpBox(
            "씬 뷰를 클릭해 경로 포인트를 찍고, 포인트마다 라운드 값으로 모서리 곡선을 조절합니다.\n" +
            "포인트를 추가/수정할 때마다 같은 오브젝트의 Spline이 다시 연결되며,\n" +
            "결과 스플라인은 타임라인 베이크로 만든 경로와 동일하게 사용할 수 있습니다.",
            MessageType.Info);

        EditorGUILayout.Space(4);
        EditorGUILayout.LabelField("── Target ──────────────────────────", EditorStyles.boldLabel);

        EditorGUI.BeginChangeCheck();
        output = (SplineContainer)EditorGUILayout.ObjectField("Spline Container", output, typeof(SplineContainer), true);
        if (EditorGUI.EndChangeCheck())
            selectedWaypoint = -1;

        if (output == null)
        {
            if (GUILayout.Button("NavigationRoute 오브젝트 생성"))
                CreateOutput();

            DrawWaypointStatus();
            return;
        }

        ResolveWaypointPath();

        if (wpPath == null)
        {
            EditorGUILayout.Space(4);
            EditorGUILayout.HelpBox(
                "이 오브젝트에 웨이포인트 데이터가 없습니다. 컴포넌트를 추가하면 편집을 시작할 수 있습니다.",
                MessageType.Warning);

            if (GUILayout.Button("Route Waypoint Path 컴포넌트 추가", GUILayout.Height(26)))
            {
                wpPath = Undo.AddComponent<RouteWaypointPath>(output.gameObject);

                // 컴포넌트를 막 추가했다면 바로 찍기 시작할 참이므로 배치 모드를 켜준다
                placingWaypoints = true;
                SceneView.RepaintAll();

                SetWpStatus("컴포넌트를 추가하고 배치 모드를 켰습니다. 씬 뷰를 클릭해 포인트를 찍으세요. (ESC로 종료)",
                            MessageType.Info);
            }

            DrawWaypointStatus();
            return;
        }

        var points = wpPath.Waypoints;

        // ── 배치 ───────────────────────────────────────────────────
        EditorGUILayout.Space(6);
        EditorGUILayout.LabelField("── Placement ───────────────────────", EditorStyles.boldLabel);

        EditorGUI.BeginChangeCheck();
        placingWaypoints = GUILayout.Toggle(
            placingWaypoints,
            placingWaypoints ? "■ 배치 모드 켜짐 — 씬 뷰 클릭 = 포인트 추가 (ESC로 종료)"
                             : "▶ 씬 뷰 클릭으로 포인트 찍기",
            "Button", GUILayout.Height(28));
        if (EditorGUI.EndChangeCheck())
            SceneView.RepaintAll();

        using (new EditorGUI.IndentLevelScope())
        {
            float newDefault = Mathf.Max(0f, EditorGUILayout.FloatField(
                new GUIContent("기본 라운드(m)", "새로 찍는 포인트에 들어갈 모서리 반경"),
                wpPath.DefaultRound));

            if (!Mathf.Approximately(newDefault, wpPath.DefaultRound))
            {
                Undo.RecordObject(wpPath, "Change Default Round");
                wpPath.DefaultRound = newDefault;
                EditorUtility.SetDirty(wpPath);
            }

            wpSnapToGround = EditorGUILayout.Toggle(
                new GUIContent("노면에 붙이기", "클릭·이동한 포인트를 도로 표면 높이로 내립니다"),
                wpSnapToGround);

            using (new EditorGUI.DisabledScope(!wpSnapToGround))
            {
                wpGroundMask   = LayerMaskField("노면 레이어", wpGroundMask);
                wpGroundOffset = EditorGUILayout.FloatField("노면 오프셋(m)", wpGroundOffset);
            }

            bool newClosed = EditorGUILayout.Toggle(
                new GUIContent("순환 경로", "마지막 포인트와 첫 포인트를 이어 닫힌 경로로 만듭니다"),
                wpPath.Closed);

            if (newClosed != wpPath.Closed)
            {
                Undo.RecordObject(wpPath, "Toggle Closed Route");
                wpPath.Closed = newClosed;
                EditorUtility.SetDirty(wpPath);
                MarkWaypointsChanged();
            }
        }

        // ── 포인트 목록 ────────────────────────────────────────────
        EditorGUILayout.Space(6);
        EditorGUILayout.LabelField($"── Points ({points.Count}) ──────────────────", EditorStyles.boldLabel);

        if (points.Count == 0)
        {
            EditorGUILayout.HelpBox("포인트가 없습니다. 배치 모드를 켜고 씬 뷰를 클릭하세요.", MessageType.None);
        }
        else
        {
            wpListScroll = EditorGUILayout.BeginScrollView(wpListScroll, GUILayout.MinHeight(80), GUILayout.MaxHeight(260));
            for (int i = 0; i < points.Count; i++)
                DrawWaypointRow(points, i);
            EditorGUILayout.EndScrollView();

            ApplyPendingWaypointOps(points);
        }

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("＋ 끝에 추가"))
                AppendWaypointAtSceneCenter();

            using (new EditorGUI.DisabledScope(points.Count == 0))
            {
                if (GUILayout.Button("모두 지우기") &&
                    EditorUtility.DisplayDialog("포인트 삭제", $"포인트 {points.Count}개를 모두 지울까요?", "지우기", "취소"))
                {
                    Undo.RecordObject(wpPath, "Clear Route Waypoints");
                    points.Clear();
                    selectedWaypoint = -1;
                    EditorUtility.SetDirty(wpPath);
                    MarkWaypointsChanged();
                }
            }
        }

        if (GUILayout.Button(new GUIContent("현재 스플라인에서 웨이포인트 가져오기",
                                            "타임라인 베이크 결과 등 이미 만들어진 스플라인의 노트를 웨이포인트로 변환합니다")))
            ImportFromSpline();

        // ── 스플라인 ───────────────────────────────────────────────
        EditorGUILayout.Space(6);
        EditorGUILayout.LabelField("── Spline ──────────────────────────", EditorStyles.boldLabel);

        wpAutoApply = EditorGUILayout.Toggle(
            new GUIContent("자동 적용", "포인트를 바꿀 때마다 스플라인을 다시 굽습니다"),
            wpAutoApply);

        using (new EditorGUI.DisabledScope(points.Count < 2))
        {
            if (GUILayout.Button("▶ 스플라인 갱신", GUILayout.Height(28)))
            {
                ApplyWaypointsToSpline();
                SetWpStatus($"✓ 스플라인 갱신 완료 — 포인트 {points.Count}개 → 노트 {output.Spline.Count}개\n" +
                            $"  경로 길이 약 {WaypointPathLength():F1} m",
                            MessageType.Info);
            }
        }

        DrawWaypointStatus();
    }

    void DrawWaypointRow(List<RouteWaypointPath.Waypoint> points, int i)
    {
        bool selected = i == selectedWaypoint;

        var boxStyle = selected ? EditorStyles.helpBox : GUIStyle.none;
        using (new EditorGUILayout.VerticalScope(boxStyle))
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Toggle(selected, $"#{i}", EditorStyles.miniButton, GUILayout.Width(36)) != selected)
                {
                    selectedWaypoint = selected ? -1 : i;
                    SceneView.RepaintAll();
                }

                Vector3 world = wpPath.transform.TransformPoint(points[i].position);

                EditorGUI.BeginChangeCheck();
                Vector3 newWorld = EditorGUILayout.Vector3Field(GUIContent.none, world);
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(wpPath, "Move Route Waypoint");
                    points[i].position = wpPath.transform.InverseTransformPoint(newWorld);
                    EditorUtility.SetDirty(wpPath);
                    MarkWaypointsChanged();
                }
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("라운드(m)", GUILayout.Width(60));

                EditorGUI.BeginChangeCheck();
                float round = EditorGUILayout.FloatField(points[i].round, GUILayout.Width(56));
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(wpPath, "Change Waypoint Round");
                    points[i].round = Mathf.Max(0f, round);
                    EditorUtility.SetDirty(wpPath);
                    MarkWaypointsChanged();
                }

                // 입력값이 그대로 안 먹는 경우(구간이 짧거나 경로 끝점) 실제 값을 알려준다
                float effective = RouteWaypointPath.EffectiveRound(points, wpPath.Closed, i);
                if (points[i].round > 0.001f && Mathf.Abs(effective - points[i].round) > 0.01f)
                {
                    EditorGUILayout.LabelField(
                        new GUIContent($"→ {effective:F2}", "구간 길이의 절반까지만 적용됩니다. 경로의 첫/끝 포인트는 모서리가 아니라 0입니다."),
                        EditorStyles.miniLabel, GUILayout.Width(48));
                }

                GUILayout.FlexibleSpace();

                using (new EditorGUI.DisabledScope(i == 0))
                {
                    if (GUILayout.Button(new GUIContent("▲", "위로"), EditorStyles.miniButtonLeft, GUILayout.Width(24)))
                    {
                        wpPendingSwapFrom = i;
                        wpPendingSwapTo   = i - 1;
                    }
                }

                using (new EditorGUI.DisabledScope(i == points.Count - 1))
                {
                    if (GUILayout.Button(new GUIContent("▼", "아래로"), EditorStyles.miniButtonMid, GUILayout.Width(24)))
                    {
                        wpPendingSwapFrom = i;
                        wpPendingSwapTo   = i + 1;
                    }
                }

                if (GUILayout.Button(new GUIContent("＋", "다음 포인트와의 중간에 삽입"), EditorStyles.miniButtonMid, GUILayout.Width(24)))
                    wpPendingInsertAfter = i;

                if (GUILayout.Button(new GUIContent("✕", "삭제"), EditorStyles.miniButtonRight, GUILayout.Width(24)))
                    wpPendingRemove = i;
            }
        }
    }

    void DrawWaypointStatus()
    {
        if (string.IsNullOrEmpty(wpStatus)) return;

        EditorGUILayout.Space(6);
        EditorGUILayout.HelpBox(wpStatus, wpStatusType);
    }

    // ── 씬 뷰 ──────────────────────────────────────────────────────

    void OnWaypointSceneGUI(SceneView view)
    {
        ResolveWaypointPath();
        if (wpPath == null) return;

        var points = wpPath.Waypoints;
        Transform t = wpPath.transform;

        RebuildWaypointPreview();

        // 경로 미리보기
        if (wpPreview.Count > 1)
        {
            Handles.color = WpLineColor;
            Handles.DrawAAPolyLine(4f, wpPreview.ToArray());
        }

        // 포인트
        for (int i = 0; i < points.Count; i++)
        {
            Vector3 world = t.TransformPoint(points[i].position);
            float   size  = HandleUtility.GetHandleSize(world);
            bool    sel   = i == selectedWaypoint;

            // 실제로 먹는 라운드 반경 표시 (입력값이 구간 길이에 잘릴 수 있다)
            float effective = RouteWaypointPath.EffectiveRound(points, wpPath.Closed, i);
            if (effective > 0.001f)
            {
                Handles.color = WpRoundColor;
                Handles.DrawWireDisc(world, Vector3.up, effective);
            }

            Handles.color = sel ? WpSelectedColor : WpPointColor;

            if (placingWaypoints)
            {
                // 배치 중에는 클릭을 전부 "추가"로 쓰기 위해 선택 핸들을 만들지 않는다
                Handles.SphereHandleCap(0, world, Quaternion.identity, size * 0.18f, EventType.Repaint);
            }
            else if (Handles.Button(world, Quaternion.identity, size * 0.18f, size * 0.25f, Handles.SphereHandleCap))
            {
                selectedWaypoint = sel ? -1 : i;
                Repaint();
            }

            Handles.color = Color.white;
            Handles.Label(world + Vector3.up * size * 0.35f, $"{i}");
        }

        // 선택 포인트 조작
        if (!placingWaypoints && selectedWaypoint >= 0 && selectedWaypoint < points.Count)
        {
            var wp = points[selectedWaypoint];
            Vector3 world = t.TransformPoint(wp.position);

            EditorGUI.BeginChangeCheck();
            Vector3 moved = Handles.PositionHandle(world, Quaternion.identity);
            if (EditorGUI.EndChangeCheck())
            {
                if (wpSnapToGround) moved = SnapToGround(moved);

                Undo.RecordObject(wpPath, "Move Route Waypoint");
                wp.position = t.InverseTransformPoint(moved);
                EditorUtility.SetDirty(wpPath);
                MarkWaypointsChanged();
                Repaint();
            }

            Handles.color = WpRoundColor;
            EditorGUI.BeginChangeCheck();
            float newRound = Handles.RadiusHandle(Quaternion.identity, world, Mathf.Max(wp.round, 0.05f), true);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(wpPath, "Change Waypoint Round");
                wp.round = Mathf.Max(0f, newRound);
                EditorUtility.SetDirty(wpPath);
                MarkWaypointsChanged();
                Repaint();
            }
        }

        HandleWaypointPlacement(view);
        DrawWaypointOverlay(view);
    }

    void HandleWaypointPlacement(SceneView view)
    {
        if (!placingWaypoints)
        {
            wpHoverValid = false;
            return;
        }

        // 씬 오브젝트 선택 대신 우리가 클릭을 받는다
        int id = GUIUtility.GetControlID(FocusType.Passive);
        if (Event.current.type == EventType.Layout)
            HandleUtility.AddDefaultControl(id);

        Event e = Event.current;

        switch (e.type)
        {
            case EventType.MouseDown when e.button == 0 && !e.alt:
                if (TryPickScenePoint(e.mousePosition, out Vector3 world))
                {
                    AppendWaypoint(world);
                    e.Use();
                }
                break;

            case EventType.KeyDown when e.keyCode == KeyCode.Escape:
                placingWaypoints = false;
                wpHoverValid = false;
                e.Use();
                Repaint();
                break;

            // 씬 픽킹은 Repaint 중에 하면 안 되므로 마우스 이동 때 미리 구해둔다
            case EventType.MouseMove:
            case EventType.MouseDrag:
                wpHoverValid = TryPickScenePoint(e.mousePosition, out wpHoverPoint);
                view.Repaint();
                break;
        }

        if (e.type == EventType.Repaint && wpHoverValid)
        {
            float size = HandleUtility.GetHandleSize(wpHoverPoint);

            Handles.color = WpSelectedColor;
            Handles.SphereHandleCap(0, wpHoverPoint, Quaternion.identity, size * 0.15f, EventType.Repaint);

            var points = wpPath.Waypoints;
            if (points.Count > 0)
            {
                Handles.color = new Color(WpSelectedColor.r, WpSelectedColor.g, WpSelectedColor.b, 0.5f);
                Handles.DrawDottedLine(wpPath.transform.TransformPoint(points[^1].position), wpHoverPoint, 4f);
            }
        }
    }

    void DrawWaypointOverlay(SceneView view)
    {
        Handles.BeginGUI();

        var rect = new Rect(10, 10, 300, placingWaypoints ? 54 : 38);
        GUILayout.BeginArea(rect, GUI.skin.box);

        GUILayout.Label(placingWaypoints
            ? "Route Waypoints — 배치 모드"
            : "Route Waypoints — 편집 모드", EditorStyles.boldLabel);

        GUILayout.Label(placingWaypoints
            ? "클릭 = 포인트 추가 / ESC = 종료"
            : "포인트 클릭 = 선택 · 핸들로 이동/라운드 조절", EditorStyles.miniLabel);

        GUILayout.EndArea();
        Handles.EndGUI();
    }

    // ── 편집 동작 ──────────────────────────────────────────────────

    void AppendWaypoint(Vector3 world)
    {
        Undo.RecordObject(wpPath, "Add Route Waypoint");

        if (wpSnapToGround) world = SnapToGround(world);

        var local = wpPath.transform.InverseTransformPoint(world);
        wpPath.Waypoints.Add(new RouteWaypointPath.Waypoint(local, wpPath.DefaultRound));
        selectedWaypoint = wpPath.Waypoints.Count - 1;

        EditorUtility.SetDirty(wpPath);
        MarkWaypointsChanged();
        Repaint();
    }

    void AppendWaypointAtSceneCenter()
    {
        var points = wpPath.Waypoints;

        Vector3 world;
        if (points.Count >= 2)
        {
            // 마지막 진행 방향으로 10m 연장
            Vector3 a = wpPath.transform.TransformPoint(points[^2].position);
            Vector3 b = wpPath.transform.TransformPoint(points[^1].position);
            Vector3 dir = (b - a).sqrMagnitude > 1e-6f ? (b - a).normalized : Vector3.forward;
            world = b + dir * 10f;
        }
        else if (points.Count == 1)
        {
            world = wpPath.transform.TransformPoint(points[0].position) + Vector3.forward * 10f;
        }
        else
        {
            var sv = SceneView.lastActiveSceneView;
            world = sv != null ? sv.pivot : wpPath.transform.position;
        }

        AppendWaypoint(world);
    }

    /// <summary>리스트를 다 그린 뒤에 실제 추가/삭제/순서 변경을 처리한다.</summary>
    void ApplyPendingWaypointOps(List<RouteWaypointPath.Waypoint> points)
    {
        if (wpPendingRemove < 0 && wpPendingInsertAfter < 0 && wpPendingSwapFrom < 0) return;

        if (wpPendingRemove >= 0 && wpPendingRemove < points.Count)
        {
            Undo.RecordObject(wpPath, "Delete Route Waypoint");

            points.RemoveAt(wpPendingRemove);
            if (selectedWaypoint >= points.Count) selectedWaypoint = points.Count - 1;

            EditorUtility.SetDirty(wpPath);
            MarkWaypointsChanged();
        }
        else if (wpPendingInsertAfter >= 0 && wpPendingInsertAfter < points.Count)
        {
            Undo.RecordObject(wpPath, "Insert Route Waypoint");

            int i = wpPendingInsertAfter;
            Vector3 local = i < points.Count - 1
                ? (points[i].position + points[i + 1].position) * 0.5f
                : points[i].position + Vector3.forward * 5f;

            points.Insert(i + 1, new RouteWaypointPath.Waypoint(local, wpPath.DefaultRound));
            selectedWaypoint = i + 1;

            EditorUtility.SetDirty(wpPath);
            MarkWaypointsChanged();
        }
        else if (wpPendingSwapFrom >= 0 && wpPendingSwapFrom < points.Count &&
                 wpPendingSwapTo   >= 0 && wpPendingSwapTo   < points.Count)
        {
            Undo.RecordObject(wpPath, "Reorder Route Waypoints");

            (points[wpPendingSwapFrom], points[wpPendingSwapTo]) = (points[wpPendingSwapTo], points[wpPendingSwapFrom]);
            selectedWaypoint = wpPendingSwapTo;

            EditorUtility.SetDirty(wpPath);
            MarkWaypointsChanged();
        }

        wpPendingRemove      = -1;
        wpPendingInsertAfter = -1;
        wpPendingSwapFrom    = -1;
        wpPendingSwapTo      = -1;

        Repaint();
    }

    void ImportFromSpline()
    {
        var spline = output.Spline;
        if (spline == null || spline.Count < 2)
        {
            SetWpStatus("✗ 가져올 노트가 없습니다. 먼저 타임라인 베이크로 경로를 만들거나 스플라인을 편집하세요.", MessageType.Error);
            return;
        }

        if (wpPath.Waypoints.Count > 0 &&
            !EditorUtility.DisplayDialog("웨이포인트 가져오기",
                $"현재 포인트 {wpPath.Waypoints.Count}개를 스플라인 노트 {spline.Count}개로 교체할까요?",
                "가져오기", "취소"))
            return;

        Undo.RecordObject(wpPath, "Import Route Waypoints");

        wpPath.Waypoints.Clear();
        for (int i = 0; i < spline.Count; i++)
            wpPath.Waypoints.Add(new RouteWaypointPath.Waypoint((Vector3)spline[i].Position, wpPath.DefaultRound));

        wpPath.Closed    = spline.Closed;
        selectedWaypoint = -1;

        EditorUtility.SetDirty(wpPath);
        MarkWaypointsChanged();

        SetWpStatus($"✓ 노트 {spline.Count}개를 웨이포인트로 가져왔습니다. 라운드는 기본값 {wpPath.DefaultRound:F1} m로 들어갔습니다.",
                    MessageType.Info);
    }

    // ── 적용/유틸 ──────────────────────────────────────────────────

    /// <summary>포인트가 바뀔 때마다 호출. 자동 적용이 켜져 있으면 스플라인을 다시 굽는다.</summary>
    void MarkWaypointsChanged()
    {
        if (wpAutoApply) ApplyWaypointsToSpline();
        else             SceneView.RepaintAll();
    }

    void ApplyWaypointsToSpline()
    {
        if (wpPath == null || wpPath.Container == null) return;

        Undo.RecordObject(wpPath.Container, "Apply Route Waypoints");

        wpPath.ApplyToSpline();

        EditorUtility.SetDirty(wpPath.Container);
        EditorSceneManager.MarkSceneDirty(wpPath.gameObject.scene);

        SceneView.RepaintAll();
    }

    /// <summary>
    /// 씬에 그릴 경로 폴리라인을 다시 만든다. 인스펙터에서 값을 고쳐도 바로 반영되도록
    /// 캐시 없이 매번 계산한다 (포인트 수백 개 수준에서는 비용이 무시할 만하다).
    /// </summary>
    void RebuildWaypointPreview()
    {
        RouteWaypointPath.BuildPolyline(wpPath.Waypoints, wpPath.Closed, 12, wpPreview);

        Transform t = wpPath.transform;
        for (int i = 0; i < wpPreview.Count; i++)
            wpPreview[i] = t.TransformPoint(wpPreview[i]);
    }

    float WaypointPathLength()
    {
        RebuildWaypointPreview();

        float len = 0f;
        for (int i = 1; i < wpPreview.Count; i++)
            len += Vector3.Distance(wpPreview[i - 1], wpPreview[i]);

        return len;
    }

    /// <summary>마우스 위치의 씬 지점을 찾는다. 콜라이더 → 렌더러 → 수평면 순서.</summary>
    bool TryPickScenePoint(Vector2 guiPosition, out Vector3 world)
    {
        Ray ray = HandleUtility.GUIPointToWorldRay(guiPosition);

        if (wpSnapToGround &&
            Physics.Raycast(ray, out RaycastHit hit, 10000f, wpGroundMask, QueryTriggerInteraction.Ignore))
        {
            world = hit.point + Vector3.up * wpGroundOffset;
            return true;
        }

        // 콜라이더가 없어도 렌더러 지오메트리로 집어준다
        if (HandleUtility.PlaceObject(guiPosition, out Vector3 placed, out _))
        {
            world = placed + Vector3.up * (wpSnapToGround ? wpGroundOffset : 0f);
            return true;
        }

        // 폴백: 마지막 포인트 높이의 수평면
        float y = 0f;
        var points = wpPath.Waypoints;
        if (points.Count > 0)
            y = wpPath.transform.TransformPoint(points[^1].position).y;

        var plane = new Plane(Vector3.up, new Vector3(0f, y, 0f));
        if (plane.Raycast(ray, out float dist))
        {
            world = ray.GetPoint(dist);
            return true;
        }

        world = default;
        return false;
    }

    Vector3 SnapToGround(Vector3 world)
    {
        if (Physics.Raycast(world + Vector3.up * 50f, Vector3.down, out RaycastHit hit,
                            1000f, wpGroundMask, QueryTriggerInteraction.Ignore))
            return hit.point + Vector3.up * wpGroundOffset;

        return world;
    }

    void SetWpStatus(string msg, MessageType type)
    {
        wpStatus     = msg;
        wpStatusType = type;
        Repaint();
    }
}
