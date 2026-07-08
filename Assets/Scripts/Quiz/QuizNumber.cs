using UnityEngine;

public class QuizNumber : MonoBehaviour
{
    [SerializeField] private Animator scoreAnimator;
    [SerializeField] private AudioSource audioSource;
    [SerializeField] private AudioClip[] audioClips;

    private void OnEnable()
    {
        if (QuizManager.Instance == null) return;

        int idx = QuizManager.Instance.CurrentQuizNumber - 1;

        if (scoreAnimator != null)
            scoreAnimator.SetTrigger($"q{idx}");

        if (audioSource != null && audioClips != null && idx >= 0 && idx < audioClips.Length)
            audioSource.PlayOneShot(audioClips[idx]);
        else if (audioClips == null || idx < 0 || idx >= (audioClips?.Length ?? 0))
            Debug.LogWarning($"[QuizNumber] 오디오 인덱스 {idx} 범위 초과 (배열 크기 {audioClips?.Length ?? 0})");
    }
}
