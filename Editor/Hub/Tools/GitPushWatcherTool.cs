// =======================================================================
//  GitPushWatcherTool — 把 Git 推送监听面板注册到 XY_EditorHub
//  XY_EditorHub 通过反射发现所有实现 IXYEditorTool 的类，无需手动注册。
// =======================================================================

using Framework.XYEditor.GitPushWatcher;
using UnityEditor;
using UnityEngine;

namespace Framework.XYEditor.Tools
{
    /// <summary>
    /// Git 推送监听面板的 Hub 适配器。
    /// 工具分类: 项目工具
    /// </summary>
    [XYTool(order: 50, tags: new[] { "Git", "版本控制", "协作", "推送", "监听" })]
    public class GitPushWatcherTool : IXYEditorTool
    {
        private GitPushWatcherPanel _panel;
        private Vector2 _scroll;

        public string ToolName => "Git 推送监听";
        public string Category => "项目工具";
        public string Description =>
            "监听 Git 远端是否有新推送，并在 Unity 内显示非模态提醒";
        public string DocumentPath =>
            "Assets/XYHubTools/Editor/Apps/GitPushWatcher/README.md";
        public Texture2D Icon => EditorGUIUtility.FindTexture("d_VcsOverlay Icon");

        public void OnEnable()
        {
            if (_panel == null)
                _panel = new GitPushWatcherPanel();
            _panel.Init();
        }

        public void OnDisable()
        {
            _panel?.Cleanup();
        }

        public void OnGUI(Rect rect)
        {
            if (_panel == null)
                OnEnable();
            // 必须用 BeginArea 把 EditorGUILayout 限定在 rect 内，
            // 否则 EditorGUILayout.BeginScrollView 会以全窗口宽度为基准，
            // 滚动区会从工具区溢出到整个 Hub 窗口，导致布局错乱。
            GUILayout.BeginArea(rect);
            _panel.Draw(rect.width);
            GUILayout.EndArea();
        }
    }
}
