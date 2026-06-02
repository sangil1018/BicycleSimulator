using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Video;

/// <summary>
/// 레벨 시작 전 인트로(가이드 영상) 로직을 관리하는 매니저.
/// 사용자 요청 맵핑: 왼쪽(O 버튼) = 스킵, 오른쪽(X 버튼) = 홈
/// </summary>
public class IntroManager : MonoBehaviour
{
    [Header("UI Panels")]
    [SerializeField] private GameObject introPanel;
    [SerializeField] private GameObject transitionUI;

    [Header("Video")]
    [SerializeField] private VideoPlayer introVideoPlayer;
    [SerializeField] private float introDuration = 20f;
    [SerializeField] private float transitionStartTime = 18f;

    [Header("Button Reactions (Highlight & Click)")]
    [Tooltip("스킵(왼쪽 선택) 시 활성화될 오브젝트")]
    [SerializeField] private GameObject skipReaction;
    [Tooltip("홈(오른쪽 선택) 시 활성화될 오브젝트")]
    [SerializeField] private GameObject homeReaction;

    [Header("Hardware Icon Reactions")]
    [Tooltip("하드웨어 O 버튼 아이콘 (스킵과 연동)")]
    [SerializeField] private GameObject oButtonReaction;
    [Tooltip("하드웨어 X 버튼 아이콘 (홈과 연동)")]
    [SerializeField] private GameObject xButtonReaction;

    [Header("Audio")]
    [SerializeField] private AudioClip clickClip;
    private AudioSource _audioSource;

    private bool _isFinished = false;

    void Awake()
    {
        _audioSource = GetComponent<AudioSource>();
        if (_audioSource == null)
        {
            _audioSource = gameObject.AddComponent<AudioSource>();
            _audioSource.playOnAwake = false;
        }
    }

    void Start()
    {
        // 씬이 준비되면 자동으로 인트로 시작
        StartIntro();
    }

    void OnDestroy()
    {
        CleanupEvents();
    }

    /// <summary>
    /// 인트로 시작
    /// </summary>
    public void StartIntro()
    {
        _isFinished = false;

        if (introPanel) introPanel.SetActive(true);
        if (transitionUI) transitionUI.SetActive(false);

        // 모든 리액션 초기화
        UpdateSelectionVisual(null);

        // 입력 이벤트 연결 (InputManager.cs 연동)
        if (InputManager.Instance != null)
        {
            CleanupEvents(); // 중복 등록 방지

            // 전역 맵핑: O=좌(true), X=우(false)
            InputManager.Instance.OnBtnO += HandleSkip;   // O(좌) -> 스킵
            InputManager.Instance.OnBtnX += HandleHome;   // X(우) -> 홈
            InputManager.Instance.OnSelectionChanged += OnSelectionChanged;

            Debug.Log("[Intro] Mapping Applied: O(Left)=Skip, X(Right)=Home");
        }

        // 비디오 재생 및 시퀀스 코루틴 시작 (코루틴 내부에서 준비 대기)
        StopAllCoroutines();
        StartCoroutine(IntroSequence());
    }

    private IEnumerator IntroSequence()
    {
        // 비디오 준비 대기
        if (introVideoPlayer)
        {
            Debug.Log("[Intro] Preparing video...");
            introVideoPlayer.Prepare();
            while (!introVideoPlayer.isPrepared)
            {
                yield return null;
            }
            Debug.Log("[Intro] Video prepared, starting playback");
            introVideoPlayer.Play();
        }

        float timer = 0f;
        bool transitionTriggered = false;

        Debug.Log($"[Intro] Sequence started. Duration: {introDuration}, TransitionAt: {transitionStartTime}");

        while (timer < introDuration)
        {
            if (_isFinished) yield break;
            timer += Time.deltaTime;

            // 18초에 트랜지션 자동 재생
            if (!transitionTriggered && timer >= transitionStartTime)
            {
                transitionTriggered = true;
                if (transitionUI)
                {
                    Debug.Log("[Intro] Auto-triggering transitionUI at 18s");
                    transitionUI.SetActive(true);
                }
            }
            yield return null;
        }

        if (!_isFinished)
        {
            Debug.Log("[Intro] Sequence ended naturally");
            FinishIntro();
        }
    }

    private void OnSelectionChanged(bool? selection)
    {
        if (_isFinished || introPanel == null || !introPanel.activeSelf) return;

        if (selection != null) PlayClickSound();
        UpdateSelectionVisual(selection);
    }

    /// <summary>
    /// 선택 상태에 따라 하이라이트/리액션 오브젝트를 켜거나 끕니다.
    /// </summary>
    private void UpdateSelectionVisual(bool? selection)
    {
        // selection == true (왼쪽/O) -> Skip + O 아이콘
        // selection == false (오른쪽/X) -> Home + X 아이콘

        if (skipReaction) skipReaction.SetActive(selection == true);
        if (oButtonReaction) oButtonReaction.SetActive(selection == true);

        if (homeReaction) homeReaction.SetActive(selection == false);
        if (xButtonReaction) xButtonReaction.SetActive(selection == false);
    }

    public void HandleSkip()
    {
        if (_isFinished || (introPanel != null && !introPanel.activeSelf)) return;

        Debug.Log("[Intro] HandleSkip (O Button) triggered");
        PlayClickSound();
        UpdateSelectionVisual(true); // 왼쪽(스킵/O) 비주얼 고정
        StartCoroutine(SkipSequence());
    }

    private IEnumerator SkipSequence()
    {
        _isFinished = true;
        if (transitionUI) transitionUI.SetActive(true);
        yield return new WaitForSeconds(0.5f);
        FinishIntro();
    }

    public void HandleHome()
    {
        if (_isFinished || (introPanel != null && !introPanel.activeSelf)) return;

        Debug.Log("[Intro] HandleHome (X Button) triggered");
        PlayClickSound();
        UpdateSelectionVisual(false); // 오른쪽(홈/X) 비주얼 고정
        StartCoroutine(HomeSequence());
    }

    private IEnumerator HomeSequence()
    {
        _isFinished = true;
        CleanupEvents();

        yield return new WaitForSeconds(0.3f);
        if (GameManager.Instance != null) GameManager.Instance.GoHome();
    }

    private void FinishIntro()
    {
        if (_isFinished && introPanel != null && !introPanel.activeSelf) return;

        _isFinished = true;
        CleanupEvents();
        if (introPanel) introPanel.SetActive(false);
        
        // 인트로 종료 후 GameManager에게 주행 준비를 요청
        if (GameManager.Instance != null)
        {
            GameManager.Instance.OnIntroFinished();
        }
    }

    private void CleanupEvents()
    {
        if (InputManager.Instance != null)
        {
            InputManager.Instance.OnBtnO -= HandleSkip;
            InputManager.Instance.OnBtnX -= HandleHome;
            InputManager.Instance.OnSelectionChanged -= OnSelectionChanged;
        }
    }

    private void PlayClickSound()
    {
        if (_audioSource && clickClip) _audioSource.PlayOneShot(clickClip);
    }
}
