using UnityEngine;

/// <summary>
/// 씬 스코프 싱글톤 베이스 클래스.
/// Singleton&lt;T&gt;와 달리 DontDestroyOnLoad 하지 않으며, 씬이 로드될 때마다
/// 새 인스턴스가 Instance를 차지한다.
///
/// 씬 로컬 UI/오브젝트를 [SerializeField]로 참조하는 매니저에 사용한다.
/// (영속 싱글톤이면 최초 씬의 인스턴스가 유지되어, 씬 언로드 후 파괴된
///  UI를 계속 참조하는 stale reference 문제가 발생하기 때문이다.)
/// </summary>
public abstract class SceneSingleton<T> : MonoBehaviour where T : MonoBehaviour
{
    static T _instance;

    public static T Instance
    {
        get
        {
            // 파괴된 인스턴스(fake-null)면 현재 씬에서 다시 탐색
            if (_instance == null)
                _instance = FindFirstObjectByType<T>();
            return _instance;
        }
    }

    protected virtual void Awake()
    {
        // 씬마다 새 인스턴스가 차지 (영속하지 않음)
        _instance = this as T;
    }
}
