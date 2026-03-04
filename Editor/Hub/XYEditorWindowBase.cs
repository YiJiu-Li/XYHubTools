using UnityEditor;
using UnityEngine;

namespace Framework.XYEditor
{
    /// <summary>
    /// 泛型窗口基类 —— 消除所有 Window 壳子中的重复代码。
    /// 子类仅需实现 CreatePanel 工厂方法 + 提供 MenuItem ShowWindow。
    /// </summary>
    /// <typeparam name="TPanel">面板类型，需实现 <see cref="IXYPanel"/></typeparam>
    public abstract class XYEditorWindowBase<TPanel> : EditorWindow
        where TPanel : class, IXYPanel
    {
        private TPanel _panel;

        /// <summary>
        /// 创建面板实例。基类会自动传入 <see cref="EditorWindow.Repaint"/>。
        /// </summary>
        protected abstract TPanel CreatePanel();

        protected virtual void OnEnable()
        {
            _panel = CreatePanel();
            _panel.Init();
        }

        protected virtual void OnDisable()
        {
            _panel?.Cleanup();
        }

        protected virtual void OnGUI()
        {
            _panel?.Draw(position.width);
        }
    }
}
