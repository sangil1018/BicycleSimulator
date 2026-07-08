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
    [SerializeField] private GameObject gamePanel;
    [SerializeField] private GameObject transitionUI;

    [Header("Video")]
    [SerializeField] private VideoPlayer introVideoPlayer;
    [SerializeField] private float introDuration = 20f;
    [SerializeField] private float transitionStartTime = 18f;

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

    /// <summary>
    /// 인트로 시작 (GameManager.StartLevelIntro()에서 호출)
    /// </summary>
    public void StartIntro()
    {
        _isFinished = false;

        if (introPanel) introPanel.SetActive(true);
        if (transitionUI) transitionUI.SetActive(false);

        // 비디오 재생 및 시퀀스 코루틴 시작
        StopAllCoroutines();
        StartCoroutine(IntroSequence());
    }

    private IEnumerator IntroSequence()
    {
        // yield return new WaitForSeconds(0.5f);

        // 비디오 준비 대기 (최대 5초 — 클립 누락/로딩 실패 시 무한 대기 방지)
        if (introVideoPlayer)
        {
            introVideoPlayer.Prepare();
            float prepTimer = 0f;
            while (!introVideoPlayer.isPrepared && prepTimer < 5f)
            {
                prepTimer += Time.deltaTime;
                yield return null;
            }
            if (introVideoPlayer.isPrepared) introVideoPlayer.Play();
            else Debug.LogWarning("[IntroManager] 비디오 준비 시간 초과 — 영상 없이 인트로 진행");
        }

        float timer = 0f;
        bool transitionTriggered = false;

        while (timer < introDuration)
        {
            if (_isFinished) yield break;
            timer += Time.deltaTime;

            if (!transitionTriggered && timer >= transitionStartTime)
            {
                transitionTriggered = true;
                if (transitionUI) transitionUI.SetActive(true);
            }
            yield return null;
        }

        if (!_isFinished) FinishIntro();
    }

    public void HandleSkip()
    {
        if (_isFinished) return;
        PlayClickSound();
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
        if (_isFinished) return;
        PlayClickSound();
        StartCoroutine(HomeSequence());
    }

    private IEnumerator HomeSequence()
    {
        _isFinished = true;
        yield return new WaitForSeconds(0.3f);
        if (GameManager.Instance != null) GameManager.Instance.GoHome();
    }

    private void FinishIntro()
    {
        if (_isFinished && introPanel != null && !introPanel.activeSelf) return;

        _isFinished = true;
        if (gamePanel) gamePanel.SetActive(true);
        if (introPanel) introPanel.SetActive(false);

        if (GameManager.Instance != null)
            GameManager.Instance.OnIntroFinished();
    }

    private void PlayClickSound()
    {
        if (_audioSource && clickClip) _audioSource.PlayOneShot(clickClip);
    }
}
