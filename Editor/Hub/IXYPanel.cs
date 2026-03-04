namespace Framework.XYEditor
{
    /// <summary>
    /// 面板接口 —— 所有可嵌入 XYEditorToolBase / XYEditorWindowBase 的面板需实现此接口。
    /// 已有面板（MaterialTransparencyAnimationPanel、CopyAppPanel 等）均已具备同签名方法，
    /// 只需添加 ": IXYPanel" 即可适配。
    /// </summary>
    public interface IXYPanel
    {
        /// <summary>面板激活时调用（读取 EditorPrefs、初始化资源等）</summary>
        void Init();

        /// <summary>面板停用时调用（保存 EditorPrefs、释放资源等）</summary>
        void Cleanup();

        /// <summary>
        /// 主绘制方法
        /// </summary>
        /// <param name="containerWidth">当前容器可用宽度（px）</param>
        void Draw(float containerWidth);
    }
}
