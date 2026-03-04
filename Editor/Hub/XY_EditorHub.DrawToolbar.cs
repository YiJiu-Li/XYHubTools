// XY_EditorHub.DrawToolbar.cs  —— OnGUI / 工具栏 / 分隔线 / 键盘导航 / 状态栏

using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Framework.XYEditor
{
    public partial class XY_EditorHub
    {
        // -- OnGUI 主调度 ----------------------------------------------------
        private void OnGUI()
        {
            XY_HubStyles.EnsureInited();
            HandleKeyboard();

            float bodyH = position.height - TOOLBAR_H - STATUS_BAR_H;

            DrawToolbar(new Rect(0, 0, position.width, TOOLBAR_H));
            DrawList(new Rect(0, TOOLBAR_H, _listWidth, bodyH));
            DrawSplitter(new Rect(_listWidth, TOOLBAR_H, SPLITTER_W, bodyH));
            DrawContent(
                new Rect(
                    _listWidth + SPLITTER_W,
                    TOOLBAR_H,
                    position.width - _listWidth - SPLITTER_W,
                    bodyH
                )
            );
            DrawStatusBar(new Rect(0, TOOLBAR_H + bodyH, position.width, STATUS_BAR_H));
        }

        // -- 键盘导航（↑ / ↓）----------------------------------------------
        private void HandleKeyboard()
        {
            var e = Event.current;
            if (e.type != EventType.KeyDown || _navList.Count == 0)
                return;

            int idx = _activeTool != null ? _navList.IndexOf(_activeTool) : -1;
            if (e.keyCode == KeyCode.DownArrow)
            {
                SelectTool(_navList[Mathf.Min(idx + 1, _navList.Count - 1)]);
                e.Use();
            }
            else if (e.keyCode == KeyCode.UpArrow)
            {
                SelectTool(_navList[idx <= 0 ? 0 : idx - 1]);
                e.Use();
            }
        }

        // -- 顶部工具栏 -------------------------------------------------------
        private void DrawToolbar(Rect rect)
        {
            GUI.Box(rect, GUIContent.none, XY_HubStyles.Toolbar);
            GUI.Label(
                new Rect(rect.x + 14, rect.y, rect.width - 100, rect.height),
                "✦  XY 编辑器工具集",
                XY_HubStyles.Title
            );

            if (
                GUI.Button(
                    new Rect(rect.xMax - 58, rect.y + 9, 50, 22),
                    "刷 新",
                    EditorStyles.miniButton
                )
            )
            {
                var cur = _activeTool?.GetType();
                _activeTool?.OnDisable();
                _activeTool = null;
                DiscoverTools();
                if (cur != null)
                {
                    var found = _allTools.Find(t => t.GetType() == cur);
                    if (found != null)
                        SelectTool(found);
                }
            }
        }

        // -- 可拖拽分割线 ----------------------------------------------------
        private void DrawSplitter(Rect rect)
        {
            EditorGUI.DrawRect(rect, XY_HubStyles.SplitterColor);
            var hot = new Rect(rect.x - 2, rect.y, rect.width + 4, rect.height);
            EditorGUIUtility.AddCursorRect(hot, MouseCursor.ResizeHorizontal);

            var e = Event.current;
            if (e.type == EventType.MouseDown && hot.Contains(e.mousePosition))
                _isDragging = true;

            if (_isDragging)
            {
                if (e.type == EventType.MouseDrag)
                {
                    _listWidth = Mathf.Clamp(_listWidth + e.delta.x, MIN_LIST_W, MAX_LIST_W);
                    e.Use();
                    Repaint();
                }
                else if (e.type == EventType.MouseUp)
                {
                    _isDragging = false;
                    EditorPrefs.SetFloat(PREF_LIST_W, _listWidth);
                }
            }
        }

        // -- 底部状态栏 -------------------------------------------------------
        private void DrawStatusBar(Rect rect)
        {
            EditorGUI.DrawRect(rect, XY_HubStyles.StatusBg);
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width, 1), XY_HubStyles.SeparatorLine);

            string filter = _searchText?.Trim().ToLower() ?? "";
            int total = _allTools.Count;
            int matching = string.IsNullOrEmpty(filter)
                ? total
                : _allTools.Count(t => MatchesFilter(t, filter));

            GUI.Label(
                rect,
                string.IsNullOrEmpty(filter)
                    ? $"共 {total} 个工具  |  {_grouped.Count} 个分类"
                    : $"搜索到 {matching} / {total} 个工具",
                XY_HubStyles.StatusBar
            );
        }
    }
}
