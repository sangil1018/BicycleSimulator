using UnityEngine;
using UnityEngine.UI;

public class ResultBoard : MonoBehaviour
{
    [Header("Bike")]
    [SerializeField] private GameObject bike;

    [Header("Score")]
    [SerializeField] private GameObject scoreText;
    [SerializeField] private Sprite[] scoreSprites;

    [Header("Rank")]
    [SerializeField] private GameObject rankText;
    [SerializeField] private Sprite[] rankSprites;
    [SerializeField] private AudioClip[] rankClips;

    [Header("Audio")]
    [SerializeField] private AudioSource audioSource;

    private int finalScore;
    private Image scoreImage;
    private Image rankImage;
    private AudioClip currentClip;

    private void Awake()
    {
        scoreImage = scoreText.GetComponent<Image>();
        rankImage = rankText.GetComponent<Image>();
    }

    private void OnEnable()
    {
        if (QuizManager.Instance == null) return;
        finalScore = QuizManager.Instance.CurrentQuizScore;
        if (rankClips != null && finalScore < rankClips.Length)
            currentClip = rankClips[finalScore];
        PlayRankSound();
    }

    public void ShowBike()
    {
        bike.SetActive(true);
    }

    public void ShowScore()
    {
        scoreImage.sprite = scoreSprites[finalScore];
    }

    public void ShowRank()
    {
        rankImage.sprite = rankSprites[finalScore];
    }

    private void PlayRankSound()
    {
        if (audioSource && currentClip)
        {
            audioSource.PlayOneShot(currentClip);
        }
    }
}
