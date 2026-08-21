using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace AssetTools.Editor
{
    /// <summary>
    /// 에셋 정리용 툴 모음 윈도우.
    /// 왼쪽 탭 목록 + 오른쪽 내용으로 구성되며,
    /// 탭은 <see cref="AssetToolPage"/> 상속 클래스를 만들면 자동으로 추가된다.
    /// </summary>
    public class AssetToolsWindow : EditorWindow
    {
        const float SidebarWidth = 150f;

        readonly List<AssetToolPage> _pages = new List<AssetToolPage>();
        int _pageIndex;
        Vector2 _sidebarScroll;
        Vector2 _contentScroll;

        [MenuItem("Tools/Asset Tools %#t")]
        public static void Open()
        {
            var window = GetWindow<AssetToolsWindow>("Asset Tools");
            window.minSize = new Vector2(420f, 260f);
            window.Show();
        }

        void OnEnable()
        {
            BuildPages();
        }

        void BuildPages()
        {
            _pages.Clear();

            foreach (var type in TypeCache.GetTypesDerivedFrom<AssetToolPage>())
            {
                if (type.IsAbstract || type.IsGenericType) continue;
                if (type.GetConstructor(System.Type.EmptyTypes) == null)
                {
                    Debug.LogWarning($"[Asset Tools] {type.Name} 에 기본 생성자가 없어 탭에서 제외됩니다.");
                    continue;
                }

                var page = (AssetToolPage)System.Activator.CreateInstance(type);
                page.Bind(this);
                _pages.Add(page);
            }

            _pages.Sort((a, b) =>
            {
                int byOrder = a.Order.CompareTo(b.Order);
                return byOrder != 0 ? byOrder : string.CompareOrdinal(a.Title, b.Title);
            });

            _pageIndex = Mathf.Clamp(_pageIndex, 0, Mathf.Max(0, _pages.Count - 1));
        }

        void OnSelectionChange()
        {
            foreach (var page in _pages) page.OnSelectionChanged();
            Repaint();
        }

        void OnGUI()
        {
            if (_pages.Count == 0)
            {
                EditorGUILayout.HelpBox("사용 가능한 툴이 없습니다. AssetToolPage 를 상속하는 클래스를 추가하세요.", MessageType.Info);
                return;
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                DrawSidebar();
                DrawContent();
            }
        }

        void DrawSidebar()
        {
            using (var scroll = new EditorGUILayout.ScrollViewScope(_sidebarScroll, GUILayout.Width(SidebarWidth)))
            {
                _sidebarScroll = scroll.scrollPosition;

                for (int i = 0; i < _pages.Count; i++)
                {
                    bool selected = i == _pageIndex;
                    if (GUILayout.Toggle(selected, _pages[i].Title, TabStyle, GUILayout.Height(24f)) != selected)
                    {
                        _pageIndex = i;
                        GUI.FocusControl(null);
                    }
                }
            }
        }

        static GUIStyle _tabStyle;

        static GUIStyle TabStyle =>
            _tabStyle ??= new GUIStyle("Button")
            {
                alignment = TextAnchor.MiddleLeft,
                padding = new RectOffset(8, 4, 2, 2)
            };

        void DrawContent()
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            using (var scroll = new EditorGUILayout.ScrollViewScope(_contentScroll))
            {
                _contentScroll = scroll.scrollPosition;

                var page = _pages[_pageIndex];
                EditorGUILayout.LabelField(page.Title, EditorStyles.boldLabel);
                EditorGUILayout.Space(4f);
                page.OnGUI();
            }
        }
    }
}
