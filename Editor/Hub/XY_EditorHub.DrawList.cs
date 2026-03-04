// XY_EditorHub.DrawList.cs  —— 左侧工具列表绘制
//
// 性能优化：
//   · MatchesFilter 使用 _attrCache 代替每次反射
//   · DrawToolItem 复用 XY_HubStyles 缓存样式，不再每帧 new GUIStyle

using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Framework.XYEditor
{
    public partial class XY_EditorHub
    {
        // -- 左侧列表主绘制 ---------------------------------------------------
        private void DrawList(Rect rect)
        {
            EditorGUI.DrawRect(rect, XY_HubStyles.ListBg);

            // 搜索框
            var newSearch = EditorGUI.TextField(
                new Rect(rect.x + 8, rect.y + 6, rect.width - 16, SEARCH_H - 8),
                _searchText,
                XY_HubStyles.Search
            );
            if (newSearch != _searchText)
            {
                _searchText = newSearch;
                Repaint();
            }

            EditorGUI.DrawRect(
                new Rect(rect.x, rect.y + SEARCH_H, rect.width, 1),
                XY_HubStyles.SeparatorLine
            );

            var scrollRect = new Rect(
                rect.x,
                rect.y + SEARCH_H + 2,
                rect.width,
                rect.height - SEARCH_H - 2
            );
            _navList.Clear();
            var viewRect = new Rect(0, 0, scrollRect.width - 14, Mathf.Max(CalcListHeight(), 10));
            _listScroll = GUI.BeginScrollView(scrollRect, _listScroll, viewRect, false, false);

            float y = 0;
            string filter = _searchText?.Trim().ToLower() ?? "";
            bool isFilter = !string.IsNullOrEmpty(filter);

            // 收藏区（无搜索时显示）
            if (!isFilter)
            {
                var favTools = _allTools.Where(IsFavorite).ToList();
                if (favTools.Count > 0)
                {
                    y = DrawSectionHeader("★ 收藏", viewRect.width, y);
                    foreach (var t in favTools)
                    {
                        DrawToolItem(new Rect(0, y, viewRect.width, ITEM_H), t);
                        _navList.Add(t);
                        y += ITEM_H;
                    }
                    EditorGUI.DrawRect(
                        new Rect(8, y, viewRect.width - 16, 1),
                        new Color(0.35f, 0.35f, 0.35f, 0.35f)
                    );
                    y += 4;
                }

                // 最近使用区（不重复收藏项）
                var recentTools = _recents
                    .Select(k => _allTools.Find(t => t.GetType().FullName == k))
                    .Where(t => t != null && !IsFavorite(t))
                    .ToList();
                if (recentTools.Count > 0)
                {
                    y = DrawSectionHeader("⏱ 最近使用", viewRect.width, y);
                    foreach (var t in recentTools)
                    {
                        DrawToolItem(new Rect(0, y, viewRect.width, ITEM_H), t);
                        _navList.Add(t);
                        y += ITEM_H;
                    }
                    EditorGUI.DrawRect(
                        new Rect(8, y, viewRect.width - 16, 1),
                        new Color(0.35f, 0.35f, 0.35f, 0.35f)
                    );
                    y += 4;
                }
            }

            // 分类列表
            foreach (var kv in _grouped)
            {
                var filtered = isFilter
                    ? kv.Value.Where(t => MatchesFilter(t, filter)).ToList()
                    : kv.Value;
                if (filtered.Count == 0)
                    continue;

                bool folded = _foldedCats.Contains(kv.Key);
                bool newFolded = !EditorGUI.Foldout(
                    new Rect(4, y + 2, viewRect.width - 8, CATEGORY_H - 4),
                    !folded,
                    kv.Key,
                    true,
                    XY_HubStyles.CategoryHeader
                );

                if (newFolded && !folded)
                    _foldedCats.Add(kv.Key);
                if (!newFolded && folded)
                    _foldedCats.Remove(kv.Key);
                y += CATEGORY_H;

                if (!newFolded)
                    foreach (var tool in filtered)
                    {
                        DrawToolItem(new Rect(0, y, viewRect.width, ITEM_H), tool);
                        _navList.Add(tool);
                        y += ITEM_H;
                    }
            }

            GUI.EndScrollView();
        }

        // -- 区段标题 ---------------------------------------------------------
        private static float DrawSectionHeader(string label, float width, float y)
        {
            const float h = 18f;
            EditorGUI.DrawRect(new Rect(0, y, width, h), new Color(0f, 0f, 0f, 0.12f));
            GUI.Label(new Rect(0, y, width, h), label, XY_HubStyles.SectionLabel);
            return y + h;
        }

        // -- 搜索匹配（使用缓存 Attribute，避免热路径反射）--------------------
        private bool MatchesFilter(IXYEditorTool tool, string filter)
        {
            if (tool.ToolName.ToLower().Contains(filter))
                return true;
            if (tool.Description?.ToLower().Contains(filter) == true)
                return true;

            if (_attrCache.TryGetValue(tool, out var attr) && attr?.Tags != null)
                foreach (var tag in attr.Tags)
                    if (tag.ToLower().Contains(filter))
                        return true;

            return false;
        }

        // -- 工具列表项（Accent Bar + 右键收藏 + 缓存样式）--------------------
        private void DrawToolItem(Rect rect, IXYEditorTool tool)
        {
            bool selected = _activeTool == tool;
            bool fav = IsFavorite(tool);

            if (
                GUI.Button(
                    rect,
                    GUIContent.none,
                    selected ? XY_HubStyles.ToolItemSelected : XY_HubStyles.ToolItem
                )
            )
                SelectTool(tool);

            // 左侧蓝色竖条（选中指示条）
            if (selected)
                EditorGUI.DrawRect(
                    new Rect(rect.x, rect.y, ACCENT_W, rect.height),
                    XY_HubStyles.AccentBar
                );

            float iconSz = 18f;
            float iconX = rect.x + (selected ? ACCENT_W + 8f : 12f);
            var ir = new Rect(iconX, rect.y + (rect.height - iconSz) * 0.5f, iconSz, iconSz);

            if (tool.Icon != null)
                GUI.DrawTexture(ir, tool.Icon, ScaleMode.ScaleToFit);
            else
                EditorGUI.DrawRect(
                    new Rect(ir.x + 5, ir.y + 5, 8, 8),
                    selected ? Color.white : XY_HubStyles.AccentBar
                );

            // 使用缓存样式，不再每帧 new GUIStyle
            float nameX = ir.xMax + 6;
            var nameStyle = selected
                ? XY_HubStyles.ToolItemNameSelected
                : XY_HubStyles.ToolItemNameNormal;
            GUI.Label(
                new Rect(nameX, rect.y, rect.width - nameX - (fav ? 18f : 4f), rect.height),
                tool.ToolName,
                nameStyle
            );

            if (fav)
                GUI.Label(
                    new Rect(rect.xMax - 18, rect.y, 16, rect.height),
                    "★",
                    XY_HubStyles.StarLabel
                );

            if (!string.IsNullOrEmpty(tool.Description))
                GUI.Label(rect, new GUIContent("", tool.Description), GUIStyle.none);

            // 右键菜单：收藏 / 取消收藏
            if (
                Event.current.type == EventType.ContextClick
                && rect.Contains(Event.current.mousePosition)
            )
            {
                var menu = new GenericMenu();
                menu.AddItem(
                    new GUIContent(fav ? "取消收藏" : "添加到收藏 ★"),
                    false,
                    () => ToggleFavorite(tool)
                );
                menu.ShowAsContext();
                Event.current.Use();
            }
        }

        // -- 列表虚高（用于 ScrollView viewRect）-----------------------------
        private float CalcListHeight()
        {
            string filter = _searchText?.Trim().ToLower() ?? "";
            bool isFilter = !string.IsNullOrEmpty(filter);
            float h = 0;

            if (!isFilter)
            {
                int fc = _allTools.Count(IsFavorite);
                if (fc > 0)
                    h += 18f + fc * ITEM_H + 5f;

                int rc = _recents
                    .Select(k => _allTools.Find(t => t.GetType().FullName == k))
                    .Count(t => t != null && !IsFavorite(t));
                if (rc > 0)
                    h += 18f + rc * ITEM_H + 5f;
            }

            foreach (var kv in _grouped)
            {
                var filtered = isFilter
                    ? kv.Value.Where(t => MatchesFilter(t, filter)).ToList()
                    : kv.Value;
                if (filtered.Count == 0)
                    continue;
                h += CATEGORY_H;
                if (!_foldedCats.Contains(kv.Key))
                    h += filtered.Count * ITEM_H;
            }
            return h;
        }
    }
}
