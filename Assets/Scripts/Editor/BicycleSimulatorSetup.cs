#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;
using TMPro;

/// <summary>씬 자동 생성 에디터 도구 (v3 — 자전거 속도계 버전)</summary>
public static class BicycleSimulatorSetup
{
    [MenuItem("Tools/Bicycle Simulator/⚙ Create Game Scene")]
    static void CreateGameScene()
    {
        if (!EditorUtility.DisplayDialog("씬 생성",
            "현재 씬을 저장하고 새 Game 씬을 생성합니다.\n계속하시겠습니까?", "확인", "취소"))
            return;

        EditorSceneManager.SaveOpenScenes();
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        var managers = new GameObject("=== MANAGERS ===");
        managers.AddComponent<GameManager>();
        managers.AddComponent<InputManager>();

        var camGO = new GameObject("Main Camera");
        camGO.tag = "MainCamera";
        var cam = camGO.AddComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = Color.black;
        camGO.AddComponent<AudioListener>();

        var railGO = new GameObject("VideoRailController");
        var vp = railGO.AddComponent<VideoPlayer>();
        vp.renderMode  = VideoRenderMode.CameraFarPlane;
        vp.targetCamera = cam;
        vp.playOnAwake = false;
        railGO.AddComponent<VideoRailController>();
        railGO.AddComponent<WaypointChecker>();

        var canvasGO = CreateCanvas("Canvas_Main");
        canvasGO.AddComponent<UIManager>();

        foreach (var name in new[] {
            "Panel_Home","Panel_GuideVideo","Panel_GameReady",
            "HUD_Riding","Panel_Event","Panel_OXQuiz",
            "Panel_Crosswalk","Panel_Result","Popup_Quit","Arrow_Direction"
        })
        {
            var panel = CreatePanel(canvasGO, name, new Color(0,0,0,0.85f));
            panel.SetActive(false);
        }

        var debugPanel = CreatePanel(canvasGO, "DEBUG_InputPanel", new Color(0,0,0,0.7f));
        debugPanel.AddComponent<DebugInputPanel>();
        debugPanel.SetActive(false);

        var esSO = new GameObject("EventSystem");
        esSO.AddComponent<UnityEngine.EventSystems.EventSystem>();
        esSO.AddComponent<UnityEngine.EventSystems.StandaloneInputModule>();

        System.IO.Directory.CreateDirectory("Assets/Scenes");
        EditorSceneManager.SaveScene(scene, "Assets/Scenes/Game.unity");
        AssetDatabase.Refresh();

        EditorUtility.DisplayDialog("완료",
            "씬 생성 완료: Assets/Scenes/Game.unity\n\n" +
            "[ Inspector 설정 ]\n" +
            "1. GameManager → Rail, WaypointChecker, UIManager, QuizDB 할당\n" +
            "2. VideoRailController → baseSpeedKph 현장 캘리브레이션 (기본 15)\n" +
            "3. WaypointChecker → Level1/Level2 테이블 할당\n" +
            "4. InputManager → COM 포트 번호 설정\n\n" +
            "[ 속도계 캘리브레이션 ]\n" +
            "• DebugInputPanel 활성화 후 슬라이더로 테스트\n" +
            "• 79 RPM ≈ 10 km/h  /  158 RPM ≈ 20 km/h\n" +
            "• 실제 자전거로 주행 시 baseSpeedKph 조정", "확인");

        Debug.Log("[Setup] Game 씬 생성 완료 (v3 — 속도계 버전)");
    }

    [MenuItem("Tools/Bicycle Simulator/📋 Create ScriptableObjects")]
    static void CreateScriptableObjects()
    {
        string dir = "Assets/Resources/ScriptableObjects";
        System.IO.Directory.CreateDirectory(dir);

        var quizDB = ScriptableObject.CreateInstance<QuizDatabase>();
        AssetDatabase.CreateAsset(quizDB, $"{dir}/QuizDatabase.asset");

        var wt1 = ScriptableObject.CreateInstance<VideoWaypointTable>();
        AssetDatabase.CreateAsset(wt1, $"{dir}/WaypointTable_Level1.asset");

        var wt2 = ScriptableObject.CreateInstance<VideoWaypointTable>();
        wt2.waypoints.Add(new VideoWaypoint{label="보행자 돌발 주의!",triggerTime=12.5f,type=WaypointType.BrakeEvent});
        wt2.waypoints.Add(new VideoWaypoint{label="Q1",triggerTime=18.0f,type=WaypointType.OXQuiz,quizIndex=0});
        wt2.waypoints.Add(new VideoWaypoint{label="우회전 사각지대 주의!",triggerTime=35.2f,type=WaypointType.BrakeEvent});
        wt2.waypoints.Add(new VideoWaypoint{label="Q2",triggerTime=41.0f,type=WaypointType.OXQuiz,quizIndex=1});
        wt2.waypoints.Add(new VideoWaypoint{label="잠시 후 좌회전",triggerTime=55.0f,type=WaypointType.Crosswalk});
        wt2.waypoints.Add(new VideoWaypoint{label="도어링 사고 주의!",triggerTime=78.5f,type=WaypointType.BrakeEvent});
        wt2.waypoints.Add(new VideoWaypoint{label="Q3",triggerTime=84.0f,type=WaypointType.OXQuiz,quizIndex=2});
        wt2.waypoints.Add(new VideoWaypoint{label="스마트폰 사용 위험!",triggerTime=102.0f,type=WaypointType.BrakeEvent});
        wt2.waypoints.Add(new VideoWaypoint{label="Q4",triggerTime=108.0f,type=WaypointType.OXQuiz,quizIndex=3});
        AssetDatabase.CreateAsset(wt2, $"{dir}/WaypointTable_Level2.asset");

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        EditorUtility.DisplayDialog("완료",
            $"ScriptableObjects 생성: {dir}/\n\nWaypointTable triggerTime은 실제 영상 보며 수정하세요.", "확인");
    }

    [MenuItem("Tools/Bicycle Simulator/✅ Validate Project Settings")]
    static void ValidateProjectSettings()
    {
        var sb = new System.Text.StringBuilder();
        bool ok = true;
        sb.AppendLine("=== 프로젝트 설정 검사 ===\n");

        var buildTargetGroup = BuildPipeline.GetBuildTargetGroup(EditorUserBuildSettings.activeBuildTarget);
        var namedBuildTarget  = NamedBuildTarget.FromBuildTargetGroup(buildTargetGroup);
        var apiLevel          = PlayerSettings.GetApiCompatibilityLevel(namedBuildTarget);
        if (apiLevel != ApiCompatibilityLevel.NET_4_6 &&
            apiLevel != ApiCompatibilityLevel.NET_Unity_4_8)
        { sb.AppendLine("❌ API Level: .NET Framework 로 변경 필요"); ok = false; }
        else sb.AppendLine("✅ API Level: OK");

        sb.AppendLine(ok ? "\n모든 설정 정상." : "\n위 항목을 수정하세요.");
        EditorUtility.DisplayDialog("검사 결과", sb.ToString(), "확인");
    }

    static GameObject CreateCanvas(string name)
    {
        var go = new GameObject(name);
        var canvas = go.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 10;
        var scaler = go.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1080, 1920);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;
        go.AddComponent<GraphicRaycaster>();
        return go;
    }

    static GameObject CreatePanel(GameObject parent, string name, Color bg)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent.transform, false);
        var rect = go.AddComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
        if (bg.a > 0f) { var img = go.AddComponent<Image>(); img.color = bg; }
        return go;
    }
}
#endif
