using UnityEngine;

/// <summary>
/// 레벨 씬이 참조하는 대형 에셋 목록을 담는 홀더.
/// PreloadManager가 Home 씬에서 Resources.LoadAsync로 이 프리팹을 로드하면
/// 참조된 에셋들이 함께 메모리에 올라와, 이후 레벨 씬 로드가 빨라진다 (인스턴스화하지 않음).
/// 목록은 에디터 메뉴 Tools/Build Preload Manifests(PreloadManifestBuilder)가 생성/갱신한다.
/// </summary>
public class PreloadManifest : MonoBehaviour
{
    public Object[] assets;
}
