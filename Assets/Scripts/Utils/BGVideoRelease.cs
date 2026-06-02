using UnityEngine;
using UnityEngine.Video;

public class BGVideoRelease : MonoBehaviour
{
    private VideoPlayer videoPlayer;

    private void OnDisable()
    {
        videoPlayer = GetComponent<VideoPlayer>();

        if (videoPlayer != null)
        {
            videoPlayer.Stop();
            videoPlayer.targetTexture.Release();
        }
    }
}
