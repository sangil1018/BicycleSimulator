using UnityEngine;

public class QuizNumber : MonoBehaviour
{
    [SerializeField] private Animator scoreAnimator;
    [SerializeField] private AudioSource audioSource;
    [SerializeField] private AudioClip[] audioClips;

    private void OnEnable()
    {
        var triggerName = $"q{QuizManager.Instance.CurrentQuizNumber - 1}";

        scoreAnimator.SetTrigger(triggerName);
        audioSource.PlayOneShot(audioClips[QuizManager.Instance.CurrentQuizNumber - 1]);
    }
}
