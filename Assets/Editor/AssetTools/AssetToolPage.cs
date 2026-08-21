using UnityEditor;

namespace AssetTools.Editor
{
    /// <summary>
    /// AssetToolsWindow 에 표시되는 탭 한 개.
    /// 이 클래스를 상속하는 클래스를 만들면 윈도우에 자동으로 탭이 추가된다.
    /// (TypeCache 로 자동 검색되므로 별도 등록 코드 불필요)
    /// </summary>
    public abstract class AssetToolPage
    {
        /// <summary>왼쪽 탭 목록에 표시될 이름.</summary>
        public abstract string Title { get; }

        /// <summary>탭 정렬 순서. 작을수록 위쪽.</summary>
        public virtual int Order => 100;

        /// <summary>이 페이지를 담고 있는 윈도우.</summary>
        protected EditorWindow Window { get; private set; }

        internal void Bind(EditorWindow window)
        {
            Window = window;
            OnEnable();
        }

        /// <summary>윈도우가 열릴 때 1회 호출.</summary>
        public virtual void OnEnable() { }

        /// <summary>에디터 선택이 바뀔 때마다 호출.</summary>
        public virtual void OnSelectionChanged() { }

        /// <summary>탭 내용 그리기.</summary>
        public abstract void OnGUI();
    }
}
