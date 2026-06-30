using System.Collections;
using UnityEngine;

public class DebugInputPanel : MonoBehaviour
{
    string _lastEcho = "—";   // ESP32 에코 마지막 수신 메시지
    string _lastSent = "—";   // 마지막으로 보낸 명령

    void OnEnable()
    {
        if (InputManager.Instance != null)
            InputManager.Instance.OnErrorMessage += OnEsp32Echo;
    }

    void OnDisable()
    {
        if (InputManager.Instance != null)
            InputManager.Instance.OnErrorMessage -= OnEsp32Echo;
    }

    void OnEsp32Echo(string msg) => _lastEcho = msg;

    public void TestVibrate(VibeState state)
    {
        if (InputManager.Instance == null) { Debug.LogWarning("[VibeTest] InputManager 없음"); return; }
        _lastSent = $"V{(int)state} ({state})";
        _lastEcho = "대기 중...";
        Debug.Log($"[VibeTest] {state} 전송");
        InputManager.Instance.SendVibrate(state);
    }

    public void TestVibrateAll()
    {
        StartCoroutine(VibrateAllRoutine());
    }

    IEnumerator VibrateAllRoutine()
    {
        VibeState[] sequence = { VibeState.Ready, VibeState.Danger, VibeState.Success, VibeState.Correct, VibeState.Wrong, VibeState.Walk };
        foreach (var state in sequence)
        {
            Debug.Log($"[VibeTest] 순차 테스트 → {state}");
            InputManager.Instance?.SendVibrate(state);
            yield return new WaitForSeconds(1.5f);
        }
        Debug.Log("[VibeTest] 전체 패턴 완료");
    }

#if UNITY_EDITOR
    void OnGUI()
    {
        var im = InputManager.Instance;
        bool connected = im != null && im.IsConnected;

        const float panelW  = 600f;
        const float pad     = 10f;
        const float statusH = 60f;
        const float echoH   = 55f;   // 송신/수신 에코 표시
        const float connectH = 70f;
        const float titleH  = 40f;
        const float btnGap  = 8f;
        const int   btnCount = 7;
        const float bh      = 110f;
        float panelH = titleH + statusH + echoH + connectH + pad + (bh + btnGap) * btnCount;

        float px = Screen.width - panelW;
        float py = 0f;
        float bx = px + pad;
        float bw = panelW - pad * 2;

        var boxStyle    = new GUIStyle(GUI.skin.box)    { fontSize = (int)(titleH  * 0.6f) };
        var statusStyle = new GUIStyle(GUI.skin.label)  { fontSize = (int)(statusH * 0.45f), alignment = TextAnchor.MiddleCenter };
        var echoStyle   = new GUIStyle(GUI.skin.label)  { fontSize = (int)(echoH   * 0.35f), alignment = TextAnchor.MiddleCenter };
        var connectStyle = new GUIStyle(GUI.skin.button) { fontSize = (int)(connectH * 0.4f) };
        var btnStyle    = new GUIStyle(GUI.skin.button) { fontSize = (int)(bh      * 0.45f) };

        GUI.Box(new Rect(px, py, panelW, panelH), "진동 테스트", boxStyle);
        float y = py + titleH;

        // 연결 상태
        statusStyle.normal.textColor = connected ? Color.green : Color.red;
        GUI.Label(new Rect(bx, y, bw, statusH),
            connected ? "● 시리얼 연결됨" : "● 시리얼 미연결", statusStyle);
        y += statusH;

        // 송신 / ESP32 에코 표시
        echoStyle.normal.textColor = Color.yellow;
        GUI.Label(new Rect(bx, y, bw, echoH),
            $"송신: {_lastSent}   에코: {_lastEcho}", echoStyle);
        y += echoH;

        // 재연결 버튼
        if (GUI.Button(new Rect(bx, y, bw, connectH), "재연결 (Connect)", connectStyle))
            im?.Connect();
        y += connectH + pad;

        // 진동 버튼 — 미연결 시 비활성화
        GUI.enabled = connected;
        if (GUI.Button(new Rect(bx, y, bw, bh), "▶ 전체 순차", btnStyle))  TestVibrateAll();                  y += bh + btnGap;
        if (GUI.Button(new Rect(bx, y, bw, bh), "V1 Danger",   btnStyle))  TestVibrate(VibeState.Danger);    y += bh + btnGap;
        if (GUI.Button(new Rect(bx, y, bw, bh), "V2 Success",  btnStyle))  TestVibrate(VibeState.Success);   y += bh + btnGap;
        if (GUI.Button(new Rect(bx, y, bw, bh), "V3 Correct",  btnStyle))  TestVibrate(VibeState.Correct);   y += bh + btnGap;
        if (GUI.Button(new Rect(bx, y, bw, bh), "V4 Wrong",    btnStyle))  TestVibrate(VibeState.Wrong);     y += bh + btnGap;
        if (GUI.Button(new Rect(bx, y, bw, bh), "V5 Walk",     btnStyle))  TestVibrate(VibeState.Walk);      y += bh + btnGap;
        if (GUI.Button(new Rect(bx, y, bw, bh), "V6 Ready",    btnStyle))  TestVibrate(VibeState.Ready);
        GUI.enabled = true;
    }
#endif
}
