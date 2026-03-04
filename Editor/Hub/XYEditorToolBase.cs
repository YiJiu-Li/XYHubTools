using System;
using UnityEditor;
using UnityEngine;

namespace Framework.XYEditor
{
    /// <summary>
    /// 泛型工具基类 —— 消除所有 Tool 适配器中的重复代码。
    /// 子类仅需填写属性 + 实现 CreatePanel 工厂方法。
    /// <para>参考 Odin Inspector 的声明式设计理念：属性即配置，生命周期自动管理。</para>
    /// </summary>
    /// <typeparam name="TPanel">面板类型，需实现 <see cref="IXYPanel"/></typeparam>
    public abstract class XYEditorToolBase<TPanel> : IXYEditorTool
        where TPanel : class, IXYPanel
    {
        private TPanel _panel;

        // ── 子类必须实现的属性 ────────────────────────────────────────────
        public abstract string ToolName { get; }
        public abstract string Category { get; }
        public abstract string Description { get; }
        public virtual string DocumentPath => null;
        public virtual Texture2D Icon => null;

        /// <summary>
        /// 创建面板实例。<paramref name="repaint"/> 为重绘委托，
        /// 面板在需要刷新时应调用它。
        /// </summary>
        protected abstract TPanel CreatePanel(Action repaint);

        // ── 生命周期（自动管理）──────────────────────────────────────────
        public void OnEnable()
        {
            _panel = CreatePanel(() => EditorWindow.focusedWindow?.Repaint());
            _panel.Init();
        }

        public void OnDisable()
        {
            _panel?.Cleanup();
            _panel = null;
        }

        public void OnGUI(Rect rect)
        {
            if (_panel == null)
                OnEnable();

            GUILayout.BeginArea(rect);
            _panel.Draw(rect.width);
            GUILayout.EndArea();
        }
    }
}
