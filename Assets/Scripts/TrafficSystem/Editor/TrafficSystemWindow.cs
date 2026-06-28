using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using TrafficSystem;

// 교통 시스템 통합 에디터 윈도우
// 메뉴: 교통시스템 > 에디터 열기  (단축키: Ctrl+Shift+T)
public class TrafficSystemWindow : EditorWindow
{
    enum Tab { Overview, Nodes, Junctions, Signals, Manager, Validate }
    Tab     _tab;
    Vector2 _scroll;

    // 씬 도구 상태
    bool        _nodeCreateMode;
    bool        _nodeConnectMode;
    TrafficNode _connectSource;

    // 씬 캐시 (OnFocus 및 Refresh에서 갱신)
    List<TrafficNode>                  _nodes       = new();
    List<TrafficJunction>              _junctions   = new();
    List<TrafficSignal>                _signals     = new();
    List<PedestrianSignalController>   _controllers = new();
    TrafficManager                     _manager;

    // 타임라인 가이드 펼침 상태
    bool _guideOpen;

    // 교차로 마법사 설정
    string _wizardLabel       = "교차로";
    int    _wizardPhases      = 2;
    float  _wizardGreenDur    = 25f;

    // 교차로 목록 펼침 상태
    readonly Dictionary<int, bool> _junctionFold = new();

    // 검사 결과
    List<(MessageType type, string msg)> _validResults = new();
    bool _validDone;

    // ── 윈도우 열기 ─────────────────────────────────────────────────────────

    [MenuItem("교통시스템/에디터 열기 %#t", priority = 0)]
    static void Open() => GetWindow<TrafficSystemWindow>("교통 시스템 에디터");

    void OnEnable()
    {
        SceneView.duringSceneGui += OnSceneGUI;
        minSize = new Vector2(420f, 300f);
        Refresh();
    }

    void OnDisable()
    {
        SceneView.duringSceneGui -= OnSceneGUI;
        _nodeCreateMode = false;
        _nodeConnectMode = false;
    }

    void OnFocus() => Refresh();

    void Refresh()
    {
        _nodes       = Object.FindObjectsByType<TrafficNode>                (FindObjectsSortMode.None).OrderBy(n => n.name).ToList();
        _junctions   = Object.FindObjectsByType<TrafficJunction>            (FindObjectsSortMode.None).OrderBy(j => j.name).ToList();
        _signals     = Object.FindObjectsByType<TrafficSignal>              (FindObjectsSortMode.None).ToList();
        _controllers = Object.FindObjectsByType<PedestrianSignalController>(FindObjectsSortMode.None).ToList();
        _manager     = Object.FindFirstObjectByType<TrafficManager>();

        // 삭제된 교차로 인덱스의 Foldout 상태 제거
        var validCount = _junctions.Count;
        foreach (var key in _junctionFold.Keys.Where(k => k >= validCount).ToList())
            _junctionFold.Remove(key);

        Repaint();
    }

    // ── 메인 GUI ────────────────────────────────────────────────────────────

    void OnGUI()
    {
        DrawToolbar();

        _scroll = EditorGUILayout.BeginScrollView(_scroll);
        switch (_tab)
        {
            case Tab.Overview:  DrawOverview();  break;
            case Tab.Nodes:     DrawNodes();     break;
            case Tab.Junctions: DrawJunctions(); break;
            case Tab.Signals:   DrawSignals();   break;
            case Tab.Manager:   DrawManager();   break;
            case Tab.Validate:  DrawValidate();  break;
        }
        EditorGUILayout.EndScrollView();

        DrawStatusBar();
    }

    void DrawToolbar()
    {
        EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
        string[] tabNames = { "개요", "노드", "교차로", "신호기", "매니저", "검사" };
        for (int i = 0; i < tabNames.Length; i++)
        {
            bool sel = (int)_tab == i;
            if (GUILayout.Toggle(sel, tabNames[i], EditorStyles.toolbarButton, GUILayout.Width(54)))
                _tab = (Tab)i;
        }
        GUILayout.FlexibleSpace();
        if (GUILayout.Button("새로고침", EditorStyles.toolbarButton))
            Refresh();
        EditorGUILayout.EndHorizontal();
    }

    void DrawStatusBar()
    {
        EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
        GUILayout.Label(
            $"노드 {_nodes.Count}   교차로 {_junctions.Count}   신호기 {_signals.Count}",
            EditorStyles.miniLabel);
        GUILayout.FlexibleSpace();

        if (_nodeCreateMode)
        {
            GUI.color = Color.yellow;
            GUILayout.Label("● 생성 모드  Shift+클릭", EditorStyles.miniLabel);
            GUI.color = Color.white;
        }
        else if (_nodeConnectMode)
        {
            GUI.color = new Color(0.4f, 0.9f, 1f);
            GUILayout.Label(
                _connectSource ? $"● 연결: {_connectSource.name} → ?" : "● 연결 모드  노드 클릭",
                EditorStyles.miniLabel);
            GUI.color = Color.white;
        }
        EditorGUILayout.EndHorizontal();
    }

    // ════════════════════════════════════════════════════════════════════════
    // 탭: 개요
    // ════════════════════════════════════════════════════════════════════════

    void DrawOverview()
    {
        Header("시스템 현황");
        DrawStat("TrafficManager",      _manager ? "✓ 있음" : "✗ 없음",  _manager ? Color.green : Color.red);
        DrawStat("TrafficNode (노드)",   _nodes.Count.ToString(),          Color.white);
        DrawStat("TrafficJunction (교차로)", _junctions.Count.ToString(), Color.white);
        DrawStat("TrafficSignal (신호기)", _signals.Count.ToString(),       Color.white);

        // ── 교차로 마법사 ──────────────────────────────────────────────────
        Header("교차로 마법사");

        using (new EditorGUILayout.VerticalScope("box"))
        {
            EditorGUILayout.HelpBox(
                "차량 신호기(TrafficSignal)와 교차로(TrafficJunction)를 한 번에 생성합니다.\n" +
                "생성 후 각 신호기 오브젝트를 TrafficNode의 Stop Signal에 드래그하세요.",
                MessageType.Info);

            _wizardLabel    = EditorGUILayout.TextField("교차로 이름",   _wizardLabel);
            _wizardPhases   = EditorGUILayout.IntSlider("페이즈 수",     _wizardPhases, 1, 4);
            _wizardGreenDur = EditorGUILayout.FloatField("초록 지속(초)", _wizardGreenDur);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("4방향 교차로 자동 생성", GUILayout.Height(30)))
                WizardCreate4Way();
            if (GUILayout.Button("선택 신호기로 교차로 구성", GUILayout.Height(30)))
                WizardFromSelection();
            EditorGUILayout.EndHorizontal();
        }

        // ── 빠른 도구 ──────────────────────────────────────────────────────
        Header("빠른 도구");
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("모든 노드 방향 자동 설정"))
                AutoOrientAll();
            if (GUILayout.Button("검사 실행"))
            {
                _validResults = RunValidation();
                _validDone = true;
                _tab = Tab.Validate;
            }
        }
    }

    // ── 마법사: 4방향 신규 생성 ─────────────────────────────────────────────

    void WizardCreate4Way()
    {
        var parent = new GameObject(_wizardLabel);
        Undo.RegisterCreatedObjectUndo(parent, "Create 4Way Intersection");

        string[]  dirs    = { "N", "S", "E", "W" };
        Vector3[] offsets = {
            new Vector3( 0, 0,  6), new Vector3(0, 0, -6),
            new Vector3( 6, 0,  0), new Vector3(-6, 0,  0)
        };

        var sigs = new TrafficSignal[4];
        for (int i = 0; i < 4; i++)
        {
            var sigGo = new GameObject($"Signal_{dirs[i]}");
            Undo.RegisterCreatedObjectUndo(sigGo, "Create Signal");
            sigGo.transform.SetParent(parent.transform);
            sigGo.transform.localPosition = offsets[i];
            sigs[i] = sigGo.AddComponent<TrafficSignal>();
        }

        var jGo = new GameObject($"{_wizardLabel}_Junction");
        Undo.RegisterCreatedObjectUndo(jGo, "Create Junction");
        jGo.transform.SetParent(parent.transform);
        var junction = jGo.AddComponent<TrafficJunction>();

        var so = new SerializedObject(junction);
        so.Update();
        so.FindProperty("yellowDuration").floatValue = 4f;

        var phases = so.FindProperty("phases");
        phases.arraySize = 2;
        WritePhase(phases, 0, "남북 직진", new[] { sigs[0], sigs[1] }, null, _wizardGreenDur);
        WritePhase(phases, 1, "동서 직진", new[] { sigs[2], sigs[3] }, null, _wizardGreenDur);
        so.ApplyModifiedProperties();

        Refresh();
        var capturedParent = parent;
        EditorApplication.delayCall += () =>
        {
            if (capturedParent)
            {
                Selection.activeGameObject = capturedParent;
                EditorGUIUtility.PingObject(capturedParent);
            }
        };
        Debug.Log($"[교통시스템] {_wizardLabel} 4방향 교차로 생성 완료. 각 신호기를 TrafficNode.StopSignal에 연결하세요.");
    }

    // ── 마법사: 선택한 신호기로 교차로 구성 ────────────────────────────────

    void WizardFromSelection()
    {
        var sigs = Selection.gameObjects
            .Select(g => g.GetComponent<TrafficSignal>())
            .Where(s => s)
            .ToArray();

        if (sigs.Length == 0)
        {
            EditorUtility.DisplayDialog("교통 시스템 에디터",
                "Hierarchy에서 TrafficSignal 컴포넌트가 있는 오브젝트를 선택한 뒤 실행하세요.", "확인");
            return;
        }

        int phaseCount  = Mathf.Min(_wizardPhases, sigs.Length);
        int perPhase    = Mathf.Max(1, sigs.Length / phaseCount);

        var jGo = new GameObject($"{_wizardLabel}_Junction");
        Undo.RegisterCreatedObjectUndo(jGo, "Create Junction from Selection");
        var junction = jGo.AddComponent<TrafficJunction>();

        var so = new SerializedObject(junction);
        so.Update();
        so.FindProperty("yellowDuration").floatValue = 4f;

        var phases = so.FindProperty("phases");
        phases.arraySize = phaseCount;
        for (int p = 0; p < phaseCount; p++)
        {
            int start     = p * perPhase;
            int count     = (p == phaseCount - 1) ? sigs.Length - start : perPhase;
            var phaseSigs = sigs.Skip(start).Take(count).ToArray();
            WritePhase(phases, p, $"페이즈 {p}", phaseSigs, null, _wizardGreenDur);
        }
        so.ApplyModifiedProperties();

        Refresh();
        var capturedJGo = jGo;
        EditorApplication.delayCall += () =>
        {
            if (capturedJGo)
            {
                Selection.activeGameObject = capturedJGo;
                EditorGUIUtility.PingObject(capturedJGo);
            }
        };
        Debug.Log($"[교통시스템] {_wizardLabel} 교차로 생성 완료. ({phaseCount}페이즈, 신호기 {sigs.Length}개)");
    }

    static void WritePhase(SerializedProperty phases, int idx, string label,
        TrafficSignal[] vehicleGreen, TrafficSignal[] pedestrianGreen, float greenDur)
    {
        var ph = phases.GetArrayElementAtIndex(idx);
        ph.FindPropertyRelative("label").stringValue = label;
        ph.FindPropertyRelative("greenDuration").floatValue = greenDur;
        ph.FindPropertyRelative("pedestrianCountdown").floatValue = Mathf.Max(0f, greenDur * 0.4f);

        var vg = ph.FindPropertyRelative("vehicleGreen");
        if (vehicleGreen != null && vehicleGreen.Length > 0)
        {
            vg.arraySize = vehicleGreen.Length;
            for (int i = 0; i < vehicleGreen.Length; i++)
                vg.GetArrayElementAtIndex(i).objectReferenceValue = vehicleGreen[i];
        }
        else { vg.arraySize = 0; }

        var pg = ph.FindPropertyRelative("pedestrianGreen");
        if (pedestrianGreen != null && pedestrianGreen.Length > 0)
        {
            pg.arraySize = pedestrianGreen.Length;
            for (int i = 0; i < pedestrianGreen.Length; i++)
                pg.GetArrayElementAtIndex(i).objectReferenceValue = pedestrianGreen[i];
        }
        else { pg.arraySize = 0; }
    }

    // ════════════════════════════════════════════════════════════════════════
    // 탭: 노드
    // ════════════════════════════════════════════════════════════════════════

    void DrawNodes()
    {
        // 씬 도구 토글
        EditorGUILayout.Space(4);
        using (new EditorGUILayout.HorizontalScope())
        {
            bool nc = GUILayout.Toggle(_nodeCreateMode, "📍 노드 생성 모드", "Button", GUILayout.Height(26));
            if (nc != _nodeCreateMode)
            {
                _nodeCreateMode = nc;
                if (_nodeCreateMode) { _nodeConnectMode = false; _connectSource = null; }
                SceneView.RepaintAll();
            }

            bool nl = GUILayout.Toggle(_nodeConnectMode, "🔗 노드 연결 모드", "Button", GUILayout.Height(26));
            if (nl != _nodeConnectMode)
            {
                _nodeConnectMode = nl;
                if (_nodeConnectMode) { _nodeCreateMode = false; _connectSource = null; }
                SceneView.RepaintAll();
            }
        }

        if (_nodeCreateMode)
            EditorGUILayout.HelpBox("씬 뷰에서  Shift + 좌클릭  으로 노드를 생성합니다.  ESC → 종료", MessageType.Info);
        else if (_nodeConnectMode)
            EditorGUILayout.HelpBox(
                _connectSource == null
                    ? "씬 뷰에서 출발 노드를 클릭하세요."
                    : $"[{_connectSource.name}]  →  도착 노드를 클릭하세요.  (ESC → 선택 취소)",
                MessageType.Info);

        // 빠른 생성 버튼
        EditorGUILayout.Space(4);
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("원점에 노드 생성"))        CreateNode(Vector3.zero);
            if (GUILayout.Button("선택 오브젝트에 노드 추가")) AddNodeToSelected();
            if (GUILayout.Button("모두 방향 자동 설정"))      AutoOrientAll();
        }

        // 노드 목록
        Header($"노드 목록 ({_nodes.Count}개)");
        HRule();

        foreach (var node in _nodes)
        {
            if (!node) continue;
            var  so        = new SerializedObject(node);
            int  exitCount = so.FindProperty("exits").arraySize;
            bool hasSig    = node.StopSignal != null;

            using (new EditorGUILayout.HorizontalScope("box"))
            {
                // 색상 도트
                GUI.color = hasSig ? new Color(1f, 0.45f, 0.45f) : new Color(1f, 0.85f, 0f);
                GUILayout.Label("●", GUILayout.Width(16));
                GUI.color = Color.white;

                GUILayout.Label(node.name, GUILayout.MinWidth(110));
                GUILayout.Label($"출구: {exitCount}", GUILayout.Width(54));

                if (hasSig)
                {
                    GUI.color = new Color(1f, 0.65f, 0.65f);
                    GUILayout.Label($"⚡ {node.StopSignal.name}", GUILayout.Width(130));
                    GUI.color = Color.white;
                }
                else
                {
                    GUI.color = new Color(0.6f, 0.6f, 0.6f);
                    GUILayout.Label("신호 없음", GUILayout.Width(70));
                    GUI.color = Color.white;
                }

                if (GUILayout.Button("선택", EditorStyles.miniButton, GUILayout.Width(36)))
                    Selection.activeObject = node;
                if (GUILayout.Button("핑", EditorStyles.miniButton, GUILayout.Width(28)))
                    EditorGUIUtility.PingObject(node);
            }
        }
    }

    TrafficNode CreateNode(Vector3 pos)
    {
        var go   = new GameObject($"TrafficNode_{_nodes.Count:D3}");
        go.transform.position = pos;
        var node = go.AddComponent<TrafficNode>();
        Undo.RegisterCreatedObjectUndo(go, "Create TrafficNode");
        Refresh();
        var capturedGo = go;
        EditorApplication.delayCall += () => { if (capturedGo) Selection.activeGameObject = capturedGo; };
        return node;
    }

    void AddNodeToSelected()
    {
        foreach (var go in Selection.gameObjects)
            if (!go.GetComponent<TrafficNode>())
                Undo.AddComponent<TrafficNode>(go);
        Refresh();
    }

    void AutoOrientAll()
    {
        Undo.SetCurrentGroupName("Auto Orient All TrafficNodes");
        int grp = Undo.GetCurrentGroup();
        foreach (var n in _nodes)
        {
            Undo.RecordObject(n.transform, "Auto Orient");
            n.AutoOrient();
        }
        Undo.CollapseUndoOperations(grp);
    }

    void ConnectNodes(TrafficNode from, TrafficNode to)
    {
        var so    = new SerializedObject(from);
        so.Update();
        var exits = so.FindProperty("exits");

        // 중복 체크
        for (int i = 0; i < exits.arraySize; i++)
            if (exits.GetArrayElementAtIndex(i).FindPropertyRelative("node").objectReferenceValue == to)
            {
                Debug.LogWarning($"[교통시스템] {from.name} → {to.name} 이미 연결되어 있습니다.");
                return;
            }

        exits.arraySize++;
        var e = exits.GetArrayElementAtIndex(exits.arraySize - 1);
        e.FindPropertyRelative("node").objectReferenceValue   = to;
        e.FindPropertyRelative("weight").intValue             = 10;
        so.ApplyModifiedProperties();
        EditorUtility.SetDirty(from);
        Debug.Log($"[교통시스템] 연결 완료: {from.name}  →  {to.name}");
    }

    // ════════════════════════════════════════════════════════════════════════
    // 탭: 교차로
    // ════════════════════════════════════════════════════════════════════════

    void DrawJunctions()
    {
        EditorGUILayout.Space(4);
        if (GUILayout.Button("새 TrafficJunction 생성", GUILayout.Height(28)))
        {
            var go = new GameObject($"TrafficJunction_{_junctions.Count:D2}");
            go.AddComponent<TrafficJunction>();
            Undo.RegisterCreatedObjectUndo(go, "Create TrafficJunction");
            Selection.activeGameObject = go;
            Refresh();
        }

        Header($"교차로 목록 ({_junctions.Count}개)");
        HRule();

        for (int ji = 0; ji < _junctions.Count; ji++)
        {
            var jct = _junctions[ji];
            if (!jct) continue;

            if (!_junctionFold.ContainsKey(ji)) _junctionFold[ji] = false;

            using (new EditorGUILayout.VerticalScope("box"))
            {
                // 헤더 행
                using (new EditorGUILayout.HorizontalScope())
                {
                    _junctionFold[ji] = EditorGUILayout.Foldout(_junctionFold[ji], jct.name, true, EditorStyles.foldoutHeader);
                    GUILayout.FlexibleSpace();
                    if (GUILayout.Button("선택", EditorStyles.miniButton, GUILayout.Width(36)))
                        Selection.activeObject = jct;
                    if (GUILayout.Button("핑",   EditorStyles.miniButton, GUILayout.Width(28)))
                        EditorGUIUtility.PingObject(jct);
                }

                var so     = new SerializedObject(jct);
                var phases = so.FindProperty("phases");
                float yell = so.FindProperty("yellowDuration").floatValue;
                GUILayout.Label($"페이즈: {phases.arraySize}개  |  황색: {yell}s", EditorStyles.miniLabel);

                if (_junctionFold[ji])
                {
                    EditorGUILayout.Space(2);
                    for (int pi = 0; pi < phases.arraySize; pi++)
                    {
                        var ph   = phases.GetArrayElementAtIndex(pi);
                        string l = ph.FindPropertyRelative("label").stringValue;
                        float  g = ph.FindPropertyRelative("greenDuration").floatValue;
                        float  c = ph.FindPropertyRelative("pedestrianCountdown").floatValue;
                        int   vc = ph.FindPropertyRelative("vehicleGreen").arraySize;
                        int   pc = ph.FindPropertyRelative("pedestrianGreen").arraySize;

                        // 페이즈 헤더
                        using (new EditorGUILayout.HorizontalScope())
                        {
                            GUI.color = PhaseColor(pi);
                            GUILayout.Label("■", GUILayout.Width(14));
                            GUI.color = Color.white;
                            GUILayout.Label($"[{pi}] {l}  —  초록 {g}s  카운트다운 {c}s  |  차량:{vc}  보행:{pc}");
                        }

                        // 차량 신호기 목록
                        var vg = ph.FindPropertyRelative("vehicleGreen");
                        for (int si = 0; si < vg.arraySize; si++)
                        {
                            var sig = vg.GetArrayElementAtIndex(si).objectReferenceValue as TrafficSignal;
                            if (!sig) continue;
                            using (new EditorGUILayout.HorizontalScope())
                            {
                                GUILayout.Space(24);
                                GUILayout.Label($"차량신호: {sig.name}", GUILayout.MinWidth(160));
                                if (GUILayout.Button("선택", EditorStyles.miniButton, GUILayout.Width(36)))
                                    Selection.activeObject = sig;
                            }
                        }

                        // 보행 신호기 목록
                        var pg = ph.FindPropertyRelative("pedestrianGreen");
                        for (int li = 0; li < pg.arraySize; li++)
                        {
                            var pedSig = pg.GetArrayElementAtIndex(li).objectReferenceValue as TrafficSignal;
                            if (!pedSig) continue;
                            using (new EditorGUILayout.HorizontalScope())
                            {
                                GUILayout.Space(24);
                                GUILayout.Label($"보행신호: {pedSig.name}", GUILayout.MinWidth(160));
                                if (GUILayout.Button("선택", EditorStyles.miniButton, GUILayout.Width(36)))
                                    Selection.activeObject = pedSig;
                            }
                        }

                        if (pi < phases.arraySize - 1)
                            HRule();
                    }
                }
            }
            GUILayout.Space(2);
        }
    }

    // ════════════════════════════════════════════════════════════════════════
    // 탭: 신호기
    // ════════════════════════════════════════════════════════════════════════

    void DrawSignals()
    {
        // 교차로 → 신호기 역방향 조회 테이블 빌드
        var sigJunction = new Dictionary<TrafficSignal, string>();
        foreach (var jct in _junctions)
        {
            if (!jct) continue;
            var so     = new SerializedObject(jct);
            var phases = so.FindProperty("phases");
            for (int pi = 0; pi < phases.arraySize; pi++)
            {
                var ph  = phases.GetArrayElementAtIndex(pi);
                string l = ph.FindPropertyRelative("label").stringValue;

                var vg = ph.FindPropertyRelative("vehicleGreen");
                for (int si = 0; si < vg.arraySize; si++)
                {
                    var sig = vg.GetArrayElementAtIndex(si).objectReferenceValue as TrafficSignal;
                    if (sig) sigJunction[sig] = $"{jct.name}  [{pi}: {l}]";
                }
            }
        }

        // 노드 → 신호기 역방향 조회 테이블
        var sigNode = new Dictionary<TrafficSignal, string>();
        foreach (var node in _nodes)
            if (node && node.StopSignal) sigNode[node.StopSignal] = node.name;

        // 현재 선택된 TrafficNode (신호기를 노드에 바로 연결하는 데 사용)
        var selectedNode = Selection.activeGameObject?.GetComponent<TrafficNode>();

        // ── 차량 신호기 ───────────────────────────────────────────────────
        Header($"신호기 — TrafficSignal  ({_signals.Count}개)");

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("새 TrafficSignal 생성", GUILayout.Height(26)))
            {
                var go = new GameObject($"TrafficSignal_{_signals.Count:D2}");
                go.AddComponent<TrafficSignal>();
                Undo.RegisterCreatedObjectUndo(go, "Create TrafficSignal");
                Selection.activeGameObject = go;
                Refresh();
            }
        }

        if (selectedNode)
        {
            EditorGUILayout.HelpBox(
                $"선택 중인 노드: [{selectedNode.name}]  →  아래 신호기의 [노드 연결] 버튼으로 StopSignal을 지정할 수 있습니다.",
                MessageType.Info);
        }

        HRule();

        foreach (var sig in _signals)
        {
            if (!sig) continue;
            var  so        = new SerializedObject(sig);
            bool hasRed      = so.FindProperty("redRenderer").objectReferenceValue;
            bool hasYellow   = so.FindProperty("yellowRenderer").objectReferenceValue;
            bool hasGreen    = so.FindProperty("greenRenderer").objectReferenceValue;
            bool hasPedRed   = so.FindProperty("pedestrianRedLight").objectReferenceValue;
            bool hasPedGreen = so.FindProperty("pedestrianGreenLight").objectReferenceValue;
            bool renderOk    = hasRed && hasYellow && hasGreen && hasPedRed && hasPedGreen;

            string jctInfo  = sigJunction.TryGetValue(sig, out var jn) ? jn : "교차로 미등록";
            string nodeInfo = sigNode.TryGetValue(sig, out var nd)      ? nd : "";
            bool isCurrentNodeSig = selectedNode && selectedNode.StopSignal == sig;

            using (new EditorGUILayout.HorizontalScope("box"))
            {
                // 렌더러 완비 여부 표시
                GUI.color = renderOk ? Color.green : new Color(1f, 0.4f, 0.3f);
                GUILayout.Label("●", GUILayout.Width(16));
                GUI.color = Color.white;

                GUILayout.Label(sig.name, GUILayout.MinWidth(110));

                // 렌더러 칩 (R / Y / G)
                RendererChip("R",  hasRed,      new Color(1f,   0.3f, 0.3f));
                RendererChip("Y",  hasYellow,   new Color(1f,   0.85f,0.1f));
                RendererChip("G",  hasGreen,    new Color(0.2f, 0.9f, 0.3f));
                RendererChip("PR", hasPedRed,   new Color(1f,   0.3f, 0.3f));
                RendererChip("PG", hasPedGreen, new Color(0.2f, 0.9f, 0.3f));

                // 교차로 및 노드 정보
                GUI.color = new Color(0.65f, 0.65f, 0.65f);
                GUILayout.Label(jctInfo, GUILayout.MinWidth(130));
                if (!string.IsNullOrEmpty(nodeInfo))
                {
                    GUI.color = new Color(1f, 0.85f, 0.4f);
                    GUILayout.Label($"↔ {nodeInfo}", GUILayout.Width(90));
                }
                GUI.color = Color.white;

                // 선택한 노드에 StopSignal로 연결
                if (selectedNode)
                {
                    GUI.backgroundColor = isCurrentNodeSig ? new Color(0.3f,1f,0.5f) : Color.white;
                    if (GUILayout.Button(isCurrentNodeSig ? "연결됨" : "노드 연결", EditorStyles.miniButton, GUILayout.Width(60)))
                    {
                        if (!isCurrentNodeSig)
                        {
                            var nso = new SerializedObject(selectedNode);
                            nso.Update();
                            nso.FindProperty("stopSignal").objectReferenceValue = sig;
                            nso.ApplyModifiedProperties();
                            EditorUtility.SetDirty(selectedNode);
                            Refresh();
                        }
                    }
                    GUI.backgroundColor = Color.white;
                }

                if (GUILayout.Button("선택", EditorStyles.miniButton, GUILayout.Width(36)))
                    Selection.activeObject = sig;
                if (GUILayout.Button("핑", EditorStyles.miniButton, GUILayout.Width(28)))
                    EditorGUIUtility.PingObject(sig);
            }
        }

        // ── Timeline 제어 컨트롤러 ────────────────────────────────────────
        Header($"보행 신호등 시각 제어 — PedestrianSignalController  ({_controllers.Count}개)");

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("새 컨트롤러 생성", GUILayout.Height(26)))
            {
                var go = new GameObject($"PedestrianSignalCtrl_{_controllers.Count:D2}");
                go.AddComponent<PedestrianSignalController>();
                Undo.RegisterCreatedObjectUndo(go, "Create PedestrianSignalController");
                Selection.activeGameObject = go;
                Refresh();
            }
        }

        HRule();

        foreach (var ctrl in _controllers)
        {
            if (!ctrl) continue;
            var so      = new SerializedObject(ctrl);
            var jctProp = so.FindProperty("junction");
            var lgtProp = so.FindProperty("lights");
            var jct     = jctProp.objectReferenceValue as TrafficJunction;
            int lgtCnt  = lgtProp.arraySize;

            using (new EditorGUILayout.HorizontalScope("box"))
            {
                GUILayout.Label(ctrl.name, GUILayout.MinWidth(140));

                GUI.color = jct ? Color.white : new Color(0.5f, 0.5f, 0.5f);
                GUILayout.Label(jct ? $"교차로: {jct.name}" : "교차로: 없음", GUILayout.Width(140));
                GUI.color = Color.white;

                GUILayout.Label($"개별신호: {lgtCnt}개", GUILayout.Width(80));

                if (GUILayout.Button("선택", EditorStyles.miniButton, GUILayout.Width(36)))
                    Selection.activeObject = ctrl;
                if (GUILayout.Button("핑", EditorStyles.miniButton, GUILayout.Width(28)))
                    EditorGUIUtility.PingObject(ctrl);
            }
        }

        // ── 타임라인 설정 가이드 ─────────────────────────────────────────
        EditorGUILayout.Space(8);
        _guideOpen = EditorGUILayout.Foldout(_guideOpen, "  타임라인 보행 신호등 제어 방법  ▸", true, EditorStyles.foldoutHeader);

        if (_guideOpen)
        {
            using (new EditorGUILayout.VerticalScope("box"))
            {
                EditorGUILayout.HelpBox(
                    "보행자 신호등은 시각 연출 전용입니다.\n" +
                    "보행자 AI는 신호에 반응하지 않으며, 항상 웨이포인트만 따라 이동합니다.\n" +
                    "아래 설정은 시나리오(Timeline)에서 신호등 라이트 색상을 제어하기 위한 것입니다.",
                    MessageType.Info);

                EditorGUILayout.Space(4);
                EditorGUILayout.LabelField("① 씬 준비", EditorStyles.boldLabel);
                EditorGUILayout.HelpBox(
                    "1. 빈 오브젝트 생성\n" +
                    "2. PedestrianSignalController 컴포넌트 추가\n" +
                    "3. Junction 슬롯에 교차로 드래그 (또는 Lights 슬롯에 개별 신호기)\n" +
                    "4. 같은 오브젝트에 Signal Receiver 컴포넌트 추가",
                    MessageType.None);

                EditorGUILayout.Space(4);
                EditorGUILayout.LabelField("② Timeline 설정", EditorStyles.boldLabel);
                EditorGUILayout.HelpBox(
                    "1. Timeline 창 열기  (Window > Sequencing > Timeline)\n" +
                    "2. Signal Track 추가 → 바인딩: 위에서 만든 오브젝트\n" +
                    "3. Signal Asset 생성  (예: PedGreen / PedRed / PedClear)\n" +
                    "4. 원하는 시각에 Signal Emitter 배치 후 Signal Asset 지정",
                    MessageType.None);

                EditorGUILayout.Space(4);
                EditorGUILayout.LabelField("③ Signal Receiver 연결", EditorStyles.boldLabel);
                EditorGUILayout.HelpBox(
                    "Signal Receiver 컴포넌트에서 각 Signal Asset을 함수에 매핑:\n\n" +
                    "  PedGreen   →  PedestrianSignalController.SetGreen\n" +
                    "  PedRed     →  PedestrianSignalController.SetRed\n" +
                    "  PedClear   →  PedestrianSignalController.ClearOverride\n\n" +
                    "※ 함수 선택 시 목록 하단 'Static Parameters' 항목을 선택하세요.\n" +
                    "   Dynamic Parameters 항목은 인자가 있어 연결되지 않습니다.",
                    MessageType.None);

                EditorGUILayout.Space(4);
                EditorGUILayout.LabelField("④ 동작 설명", EditorStyles.boldLabel);
                EditorGUILayout.HelpBox(
                    "SetGreen / SetRed  →  Junction 자동 사이클을 무시하고 신호등 라이트를 고정합니다.\n" +
                    "ClearOverride      →  고정을 해제하고 Junction 자동 사이클을 복원합니다.\n\n" +
                    "※ 보행자 AI는 이 설정에 반응하지 않습니다. 라이트 시각 연출 전용입니다.",
                    MessageType.None);
            }
        }
    }

    // ── 렌더러 칩 (R / Y / G 색상 배지) ──────────────────────────────────
    static void RendererChip(string label, bool assigned, Color onColor)
    {
        GUI.color = assigned ? onColor : new Color(0.28f, 0.28f, 0.28f);
        GUILayout.Label(label, EditorStyles.boldLabel, GUILayout.Width(16));
        GUI.color = Color.white;
    }

    // ════════════════════════════════════════════════════════════════════════
    // 탭: 매니저
    // ════════════════════════════════════════════════════════════════════════

    void DrawManager()
    {
        EditorGUILayout.Space(4);

        if (!_manager)
        {
            EditorGUILayout.HelpBox("씬에 TrafficManager가 없습니다.", MessageType.Warning);
            if (GUILayout.Button("TrafficManager 생성", GUILayout.Height(28)))
            {
                var go = new GameObject("TrafficManager");
                go.AddComponent<TrafficManager>();
                Undo.RegisterCreatedObjectUndo(go, "Create TrafficManager");
                Refresh();
            }
            return;
        }

        var so = new SerializedObject(_manager);
        so.Update();

        Header("차량 풀");
        EditorGUILayout.PropertyField(so.FindProperty("vehiclePrefabs"), new GUIContent("차량 프리팹"), true);
        EditorGUILayout.PropertyField(so.FindProperty("vehicleCount"),   new GUIContent("차량 수"));
        EditorGUILayout.PropertyField(so.FindProperty("heightOffset"),   new GUIContent("스폰 높이 오프셋"));

        Header("스폰 노드");
        EditorGUILayout.PropertyField(so.FindProperty("spawnNodes"), new GUIContent("스폰 노드"), true);
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("씬의 모든 노드로 설정"))
            {
                var sp = so.FindProperty("spawnNodes");
                sp.arraySize = _nodes.Count;
                for (int i = 0; i < _nodes.Count; i++)
                    sp.GetArrayElementAtIndex(i).objectReferenceValue = _nodes[i];
            }
            if (GUILayout.Button("선택한 노드만 설정"))
            {
                var sel = Selection.gameObjects
                    .Select(g => g.GetComponent<TrafficNode>())
                    .Where(n => n).ToArray();
                var sp = so.FindProperty("spawnNodes");
                sp.arraySize = sel.Length;
                for (int i = 0; i < sel.Length; i++)
                    sp.GetArrayElementAtIndex(i).objectReferenceValue = sel[i];
            }
        }

        Header("성능");
        EditorGUILayout.PropertyField(so.FindProperty("spawnPerFrame"), new GUIContent("프레임당 스폰 수"));
        EditorGUILayout.PropertyField(so.FindProperty("vehicleLayer"),  new GUIContent("차량 레이어"));

        so.ApplyModifiedProperties();
    }

    // ════════════════════════════════════════════════════════════════════════
    // 탭: 검사
    // ════════════════════════════════════════════════════════════════════════

    void DrawValidate()
    {
        EditorGUILayout.Space(4);
        if (GUILayout.Button("검사 실행", GUILayout.Height(30)))
        {
            Refresh();
            _validResults = RunValidation();
            _validDone    = true;
        }

        if (!_validDone) return;

        EditorGUILayout.Space(4);
        foreach (var (type, msg) in _validResults)
            EditorGUILayout.HelpBox(msg, type);
    }

    List<(MessageType, string)> RunValidation()
    {
        var r = new List<(MessageType, string)>();

        // ── TrafficManager ────────────────────────────────────────────────
        if (!_manager)
        {
            r.Add((MessageType.Error, "TrafficManager가 없습니다."));
        }
        else
        {
            var so = new SerializedObject(_manager);
            if (so.FindProperty("vehiclePrefabs").arraySize == 0)
                r.Add((MessageType.Error, "TrafficManager: 차량 프리팹이 없습니다."));
            if (so.FindProperty("spawnNodes").arraySize == 0)
                r.Add((MessageType.Error, "TrafficManager: 스폰 노드가 없습니다."));
        }

        // ── 노드 ─────────────────────────────────────────────────────────
        if (_nodes.Count == 0)
            r.Add((MessageType.Warning, "씬에 TrafficNode가 없습니다."));

        foreach (var node in _nodes)
        {
            if (!node) continue;
            var so    = new SerializedObject(node);
            var exits = so.FindProperty("exits");
            if (exits.arraySize > 0)
            {
                bool anyValid = false;
                for (int i = 0; i < exits.arraySize; i++)
                    if (exits.GetArrayElementAtIndex(i).FindPropertyRelative("node").objectReferenceValue)
                    { anyValid = true; break; }
                if (!anyValid)
                    r.Add((MessageType.Warning, $"노드 [{node.name}]: 출구가 모두 null입니다."));
            }
        }

        // ── 교차로 ───────────────────────────────────────────────────────
        foreach (var jct in _junctions)
        {
            if (!jct) continue;
            var so     = new SerializedObject(jct);
            var phases = so.FindProperty("phases");
            if (phases.arraySize == 0)
            { r.Add((MessageType.Error, $"교차로 [{jct.name}]: 페이즈가 없습니다.")); continue; }

            for (int i = 0; i < phases.arraySize; i++)
            {
                var ph = phases.GetArrayElementAtIndex(i);
                if (ph.FindPropertyRelative("vehicleGreen").arraySize == 0)
                    r.Add((MessageType.Warning, $"교차로 [{jct.name}] 페이즈[{i}]: 차량 신호기가 없습니다."));
                if (ph.FindPropertyRelative("greenDuration").floatValue <= 0f)
                    r.Add((MessageType.Error, $"교차로 [{jct.name}] 페이즈[{i}]: 초록 지속 시간이 0 이하입니다."));
            }
        }

        // ── TrafficSignal 렌더러 (차량 + 보행자) ─────────────────────────
        foreach (var sig in _signals)
        {
            if (!sig) continue;
            var so  = new SerializedObject(sig);
            bool r_  = so.FindProperty("redRenderer").objectReferenceValue;
            bool y_  = so.FindProperty("yellowRenderer").objectReferenceValue;
            bool g_  = so.FindProperty("greenRenderer").objectReferenceValue;
            bool pr_ = so.FindProperty("pedestrianRedLight").objectReferenceValue;
            bool pg_ = so.FindProperty("pedestrianGreenLight").objectReferenceValue;
            if (!r_ || !y_ || !g_ || !pr_ || !pg_)
                r.Add((MessageType.Warning,
                    $"신호기 [{sig.name}]: 렌더러 미할당  " +
                    $"(R:{(r_?"✓":"✗")}  Y:{(y_?"✓":"✗")}  G:{(g_?"✓":"✗")}  PR:{(pr_?"✓":"✗")}  PG:{(pg_?"✓":"✗")})"));
        }

        // ── 어느 교차로에도 등록되지 않은 신호기 ────────────────────────
        var registered = new HashSet<TrafficSignal>();
        foreach (var jct in _junctions)
        {
            if (!jct) continue;
            var so     = new SerializedObject(jct);
            var phases = so.FindProperty("phases");
            for (int i = 0; i < phases.arraySize; i++)
            {
                var vg = phases.GetArrayElementAtIndex(i).FindPropertyRelative("vehicleGreen");
                for (int j = 0; j < vg.arraySize; j++)
                {
                    var sig = vg.GetArrayElementAtIndex(j).objectReferenceValue as TrafficSignal;
                    if (sig) registered.Add(sig);
                }
            }
        }
        foreach (var sig in _signals)
            if (sig && !registered.Contains(sig))
                r.Add((MessageType.Warning, $"신호기 [{sig.name}]: 어떤 교차로에도 등록되지 않았습니다."));

        if (r.Count == 0)
            r.Add((MessageType.None, "모든 검사를 통과했습니다! ✓"));

        return r;
    }

    // ════════════════════════════════════════════════════════════════════════
    // 씬 뷰 GUI
    // ════════════════════════════════════════════════════════════════════════

    void OnSceneGUI(SceneView sv)
    {
        if (!_nodeCreateMode && !_nodeConnectMode) return;

        Event e = Event.current;

        // ── 노드 생성 모드 ──────────────────────────────────────────────
        if (_nodeCreateMode)
        {
            Handles.BeginGUI();
            GUI.color = new Color(1f, 1f, 0.5f);
            GUI.Label(new Rect(10, 10, 480, 22),
                "노드 생성 모드:  Shift + 좌클릭 → 노드 생성  |  ESC → 종료",
                EditorStyles.boldLabel);
            GUI.color = Color.white;
            Handles.EndGUI();

            if (e.type == EventType.MouseDown && e.button == 0 && e.shift)
            {
                Ray ray = HandleUtility.GUIPointToWorldRay(e.mousePosition);
                Vector3 pos = Physics.Raycast(ray, out RaycastHit hit) ? hit.point : ray.GetPoint(10f);
                CreateNode(pos);
                e.Use();
                Repaint();
            }
        }

        // ── 노드 연결 모드 ──────────────────────────────────────────────
        if (_nodeConnectMode)
        {
            Handles.BeginGUI();
            string hint = _connectSource
                ? $"[{_connectSource.name}]  →  도착 노드를 클릭  (ESC = 취소)"
                : "연결 모드: 출발 노드를 클릭  |  ESC → 종료";
            GUI.color = new Color(0.5f, 1f, 1f);
            GUI.Label(new Rect(10, 10, 500, 22), hint, EditorStyles.boldLabel);
            GUI.color = Color.white;
            Handles.EndGUI();

            foreach (var node in _nodes)
            {
                if (!node) continue;
                bool isSource = node == _connectSource;
                float sz = HandleUtility.GetHandleSize(node.transform.position) * 0.3f;
                Handles.color = isSource
                    ? new Color(0.2f, 1f, 1f)
                    : new Color(1f, 1f, 0.2f, 0.75f);

                if (Handles.Button(node.transform.position, Quaternion.identity, sz, sz, Handles.SphereHandleCap))
                {
                    if (_connectSource == null)
                    {
                        _connectSource = node;
                    }
                    else if (_connectSource != node)
                    {
                        ConnectNodes(_connectSource, node);
                        _connectSource = null;
                        Repaint();
                    }
                }
            }

            // 출발 → 마우스 미리보기 선
            if (_connectSource)
            {
                Ray   r2  = HandleUtility.GUIPointToWorldRay(e.mousePosition);
                Vector3 mp = Physics.Raycast(r2, out RaycastHit h2) ? h2.point : r2.GetPoint(10f);
                Handles.color = new Color(0.3f, 1f, 1f, 0.6f);
                Handles.DrawDottedLine(_connectSource.transform.position, mp, 5f);
                Handles.Label(_connectSource.transform.position + Vector3.up * 0.9f,
                    "출발", EditorStyles.boldLabel);
            }
        }

        // ESC 처리
        if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape)
        {
            if (_connectSource)
            {
                _connectSource = null;
            }
            else
            {
                _nodeCreateMode  = false;
                _nodeConnectMode = false;
            }
            e.Use();
            Repaint();
            sv.Repaint();
        }

        sv.Repaint();
    }

    // ════════════════════════════════════════════════════════════════════════
    // 공통 헬퍼
    // ════════════════════════════════════════════════════════════════════════

    static void Header(string label)
    {
        EditorGUILayout.Space(6);
        EditorGUILayout.LabelField(label, EditorStyles.boldLabel);
    }

    static void HRule()
    {
        var rect = GUILayoutUtility.GetRect(0f, 1f, GUILayout.ExpandWidth(true));
        EditorGUI.DrawRect(rect, new Color(0.5f, 0.5f, 0.5f, 0.3f));
    }

    static void DrawStat(string label, string value, Color color)
    {
        using (new EditorGUILayout.HorizontalScope())
        {
            EditorGUILayout.LabelField(label, GUILayout.Width(220));
            GUI.color = color;
            EditorGUILayout.LabelField(value, EditorStyles.boldLabel);
            GUI.color = Color.white;
        }
    }

    static Color PhaseColor(int idx)
    {
        Color[] palette = {
            new Color(0.25f, 0.9f, 0.35f),
            new Color(0.3f,  0.6f, 1f),
            new Color(1f,    0.6f, 0.15f),
            new Color(0.9f,  0.3f, 0.9f),
        };
        return palette[idx % palette.Length];
    }
}
