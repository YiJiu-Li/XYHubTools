// =======================================================================
//  XY_EditorHub.cs  —— partial 核心（字段 / 生命周期 / 工具发现 / 工具选择）
//  配套文件（均位于 Hub/ 目录，同属 partial class XY_EditorHub）：
//    XY_HubStyles.cs               —— 样式缓存
//    XY_ToolFloatWindow.cs         —— 浮动工具窗口
//    XY_EditorHub.Persistence.cs   —— 收藏 & 最近使用
//    XY_EditorHub.DrawToolbar.cs   —— OnGUI / 工具栏 / 状态栏 / 分隔线
//    XY_EditorHub.DrawList.cs      —— 左侧列表
//    XY_EditorHub.DrawContent.cs   —— 右侧内容区
// =======================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Framework.XYEditor
{
    // *** XY_HubStyles   → XY_HubStyles.cs
    // *** XY_ToolFloatWindow → XY_ToolFloatWindow.cs

    public partial class XY_EditorHub : EditorWindow
    {
        // -- 尺寸常量 --------------------------------------------------------
        private const float MIN_LIST_W = 150f;
        private const float MAX_LIST_W = 380f;
        private const float DEFAULT_LIST_W = 220f;
        private const float SPLITTER_W = 4f;
        private const float TOOLBAR_H = 40f;
        private const float STATUS_BAR_H = 20f;
        private const float SEARCH_H = 32f;
        private const float ITEM_H = 36f;
        private const float CATEGORY_H = 26f;
        private const float ACCENT_W = 3f;
        private const int RECENT_MAX = 3;

        // -- EditorPrefs 键名 ------------------------------------------------
        private const string PREF_LIST_W = "XYHub_ListWidth";
        private const string PREF_FAVS = "XYHub_Favorites";
        private const string PREF_RECENTS = "XYHub_Recents";

        // -- 状态 ------------------------------------------------------------
        private List<IXYEditorTool> _allTools = new();
        private Dictionary<string, List<IXYEditorTool>> _grouped = new();

        /// <summary>工具发现时缓存 XYToolAttribute，避免搜索/排序热路径重复反射。</summary>
        private Dictionary<IXYEditorTool, XYToolAttribute> _attrCache = new();
        private HashSet<string> _foldedCats = new();
        private HashSet<string> _favorites = new();
        private List<string> _recents = new();
        private IXYEditorTool _activeTool;
        private Vector2 _listScroll;
        private string _searchText = "";
        private float _listWidth = DEFAULT_LIST_W;
        private bool _isDragging;
        private readonly List<IXYEditorTool> _navList = new();
        private readonly Dictionary<IXYEditorTool, XY_ToolFloatWindow> _floatWindows = new();
        private bool IsPopped =>
            _activeTool != null && _floatWindows.TryGetValue(_activeTool, out var w) && w != null;

        private Exception _lastError;
        private string _lastErrorStack;

        // -- 入口 ------------------------------------------------------------
        [MenuItem("XY Tools/编辑器工具集 &e")]
        public static void Open()
        {
            var win = GetWindow<XY_EditorHub>(false, "XY 编辑器工具集");
            win.minSize = new Vector2(680, 480);
            win.Show();
        }

        // -- 生命周期 --------------------------------------------------------
        private void OnEnable()
        {
            _listWidth = EditorPrefs.GetFloat(PREF_LIST_W, DEFAULT_LIST_W);
            LoadFavorites();
            LoadRecents();
            DiscoverTools();
            if (_allTools.Count > 0)
                SelectTool(_allTools[0]);
        }

        private void OnDisable()
        {
            _activeTool?.OnDisable();
            EditorPrefs.SetFloat(PREF_LIST_W, _listWidth);
            SaveFavorites();
            SaveRecents();
        }

        // -- 工具发现（反射 + XYToolAttribute 排序 + 属性缓存）--------------
        private void DiscoverTools()
        {
            _allTools.Clear();
            _grouped.Clear();
            _attrCache.Clear();

            var ifaceType = typeof(IXYEditorTool);
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                IEnumerable<Type> types;
                try
                {
                    types = asm.GetTypes();
                }
                catch
                {
                    continue;
                }

                foreach (var t in types)
                {
                    if (t.IsAbstract || t.IsInterface)
                        continue;
                    if (!ifaceType.IsAssignableFrom(t))
                        continue;
                    try
                    {
                        var tool = (IXYEditorTool)Activator.CreateInstance(t);
                        _allTools.Add(tool);
                        // 缓存 Attribute，避免后续搜索/排序重复反射
                        _attrCache[tool] =
                            t.GetCustomAttributes(typeof(XYToolAttribute), false).FirstOrDefault()
                            as XYToolAttribute;
                    }
                    catch { }
                }
            }

            _allTools = _allTools
                .OrderBy(t => t.Category ?? "通用")
                .ThenBy(t => _attrCache.TryGetValue(t, out var a) ? (a?.Order ?? 0) : 0)
                .ThenBy(t => t.ToolName)
                .ToList();

            foreach (var tool in _allTools)
            {
                var cat = string.IsNullOrEmpty(tool.Category) ? "通用" : tool.Category;
                if (!_grouped.ContainsKey(cat))
                    _grouped[cat] = new List<IXYEditorTool>();
                _grouped[cat].Add(tool);
            }
        }

        // 持久化方法（LoadFavorites / SaveFavorites / LoadRecents / SaveRecents /
        // PushRecent / IsFavorite / ToggleFavorite）已移至 XY_EditorHub.Persistence.cs

        // OnGUI/HandleKeyboard/DrawToolbar/DrawSplitter/DrawStatusBar → XY_EditorHub.DrawToolbar.cs
        // DrawList/MatchesFilter/DrawSectionHeader/DrawToolItem/CalcListHeight → XY_EditorHub.DrawList.cs
        // DrawContent/DrawErrorPanel → XY_EditorHub.DrawContent.cs

        // -- 弹出 / 收回 -----------------------------------------------------
        private void PopOut()
        {
            if (_activeTool == null || _floatWindows.ContainsKey(_activeTool))
                return;
            _floatWindows[_activeTool] = XY_ToolFloatWindow.Create(_activeTool, this);
            Repaint();
        }

        private void DockBack()
        {
            if (_activeTool == null)
                return;
            if (_floatWindows.TryGetValue(_activeTool, out var w) && w != null)
                w.Close();
        }

        internal void OnFloatWindowClosed(IXYEditorTool tool)
        {
            _floatWindows.Remove(tool);
            Repaint();
        }

        // -- 工具选择 --------------------------------------------------------
        private void SelectTool(IXYEditorTool tool)
        {
            if (_activeTool == tool)
                return;
            _lastError = null;
            _lastErrorStack = null;
            _activeTool?.OnDisable();
            _activeTool = tool;
            _activeTool?.OnEnable();
            PushRecent(tool);
            Repaint();
        }
    }
}
