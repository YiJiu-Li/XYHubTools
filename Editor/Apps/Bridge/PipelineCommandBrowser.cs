using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Framework.XYEditor.Bridge
{
    internal sealed class PipelineCommandBrowser
    {
        [Serializable] private class Catalog { public Entry[] commands; public string error; }
        [Serializable] private class Entry
        {
            public string name, description, package, schema;
            [NonSerialized] public string originalDescription;
            public string[] tags;
            public Parameter[] parameters;
            public bool mainThreadRequired;
        }
        [Serializable] private class Parameter { public string name, description, type; public bool required; }
        private Entry[] _commands = Array.Empty<Entry>();
        private string[] _tags = new[] { "全部分类" };
        private string _search = "", _selected, _error;
        private int _tag;
        private bool _expanded = true, _loading, _requested;
        private Vector2 _listScroll, _detailScroll;

        internal void Draw()
        {
            _expanded = EditorGUILayout.Foldout(_expanded, "Pipeline 命令浏览器（" + _commands.Length + " 个命令）", true, BridgeStyles.Foldout);
            if (!_expanded) return;
            if (!_requested) Refresh();
            GUILayout.Label("查看当前工程可用命令的功能与参数。选择命令查看详情，可复制名称或参数规范。", BridgeStyles.MutedWrap);
            using (new EditorGUILayout.HorizontalScope())
            {
                var search = EditorGUILayout.TextField(_search, EditorStyles.toolbarSearchField);
                var tag = EditorGUILayout.Popup(_tag, _tags, GUILayout.Width(180));
                if (search != _search || tag != _tag) _listScroll = Vector2.zero;
                _search = search;
                _tag = tag;
                using (new EditorGUI.DisabledScope(_loading))
                    if (GUILayout.Button(_loading ? "读取中…" : "刷新命令", GUILayout.Width(90))) Refresh();
            }
            if (!string.IsNullOrEmpty(_error)) EditorGUILayout.HelpBox(_error, MessageType.Warning);
            if (_commands.Length == 0)
            {
                GUILayout.Label(_loading ? "正在读取 Pipeline 命令…" : "暂无命令，请确认 Pipeline 已启动后刷新。", BridgeStyles.Muted);
                return;
            }
            var query = _search.Trim();
            var filtered = _commands.Where(c =>
                (_tag == 0 || (c.tags ?? Array.Empty<string>()).Contains(_tags[_tag])) &&
                (Contains(c.name, query) || Contains(c.description, query) || Contains(c.originalDescription, query) ||
                 (c.tags ?? Array.Empty<string>()).Any(t => Contains(t, query)))).ToArray();
            GUILayout.Label("显示 " + filtered.Length + " / " + _commands.Length + "　·　功能说明已中文化", BridgeStyles.Muted);
            _listScroll = EditorGUILayout.BeginScrollView(_listScroll, GUILayout.Height(260));
            foreach (var c in filtered)
            {
                using (new EditorGUILayout.HorizontalScope(c.name == _selected ? BridgeStyles.RowOk : BridgeStyles.RowIdle))
                {
                    if (GUILayout.Button(c.name, EditorStyles.linkLabel, GUILayout.Width(260)))
                    {
                        _selected = c.name;
                        _detailScroll = Vector2.zero;
                    }
                    GUILayout.Label(new GUIContent(c.description, c.description), BridgeStyles.MutedWrap);
                }
            }
            if (filtered.Length == 0) GUILayout.Label("没有匹配的命令。可搜索命令名、中英文功能说明或分类标签。", BridgeStyles.MutedWrap);
            EditorGUILayout.EndScrollView();
            var selected = _commands.FirstOrDefault(c => c.name == _selected);
            if (selected == null)
            {
                GUILayout.Label("点击上方命令名称查看参数。", BridgeStyles.Muted);
                return;
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label(selected.name, EditorStyles.boldLabel);
                if (GUILayout.Button("复制名称", GUILayout.Width(80))) EditorGUIUtility.systemCopyBuffer = selected.name;
                using (new EditorGUI.DisabledScope(string.IsNullOrEmpty(selected.schema)))
                    if (GUILayout.Button("复制参数 Schema", GUILayout.Width(125))) EditorGUIUtility.systemCopyBuffer = selected.schema;
            }
            _detailScroll = EditorGUILayout.BeginScrollView(_detailScroll, GUILayout.Height(180));
            GUILayout.Label(selected.description, EditorStyles.wordWrappedLabel);
            GUILayout.Label("分类：" + string.Join(" / ", selected.tags ?? Array.Empty<string>()) + "　来源：" + selected.package, BridgeStyles.MutedWrap);
            var parameters = selected.parameters ?? Array.Empty<Parameter>();
            if (parameters.Length == 0) GUILayout.Label("无需参数。", BridgeStyles.Muted);
            foreach (var p in parameters)
            {
                GUILayout.Label(p.name + "  ·  " + p.type + (p.required ? "  ·  必填" : "  ·  可选"), EditorStyles.boldLabel);
                GUILayout.Label(p.description, EditorStyles.wordWrappedLabel);
            }
            if (parameters.Length > 0) GUILayout.Label("默认值、枚举与约束见“复制参数 Schema”。", BridgeStyles.Muted);
            EditorGUILayout.EndScrollView();
        }

        private static bool Contains(string value, string query) =>
            (value ?? "").IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;

        private async void Refresh()
        {
            if (_loading) return;
            _requested = true;
            _loading = true;
            _error = null;
            try
            {
                var json = await PipelineSelfTest.RequestJson("/api/commands");
                var catalog = JsonUtility.FromJson<Catalog>(json);
                if (catalog == null || catalog.commands == null || !string.IsNullOrEmpty(catalog.error))
                    throw new Exception(catalog?.error ?? "命令列表格式无效");
                _commands = catalog.commands.Where(c => c != null && !string.IsNullOrEmpty(c.name)).OrderBy(c => c.name).ToArray();
                foreach (var command in _commands)
                {
                    command.originalDescription = command.description;
                    command.description = PipelineCommandDescriptions.Get(command.name, command.description);
                }
                _tags = new[] { "全部分类" }.Concat(_commands.SelectMany(c => c.tags ?? Array.Empty<string>()).Distinct().OrderBy(t => t)).ToArray();
                _tag = 0;
                if (!_commands.Any(c => c.name == _selected)) _selected = null;
            }
            catch (Exception e) { _error = "读取失败：" + e.Message + (_commands.Length > 0 ? "（当前显示上次读取的列表）" : ""); }
            finally
            {
                _loading = false;
                foreach (var w in Resources.FindObjectsOfTypeAll<EditorWindow>())
                    if (w.GetType().Namespace == "Framework.XYEditor") w.Repaint();
            }
        }
    }
}
