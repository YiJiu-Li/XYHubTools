using UnityEngine;

namespace Framework.XYEditor
{
    /// <summary>
    /// 编辑器工具接口
    /// 所有注册到 XY_EditorHub 的工具均需实现此接口
    /// </summary>
    public interface IXYEditorTool
    {
        /// <summary>工具显示名称</summary>
        string ToolName { get; }

        /// <summary>工具分组（用于左侧列表分类）</summary>
        string Category { get; }

        /// <summary>工具描述（鼠标悬停提示）</summary>
        string Description { get; }

        /// <summary>
        /// 工具说明文档路径（相对于 Assets 的路径，或绝对路径）
        /// 返回 null 或空字符串表示无文档
        /// </summary>
        string DocumentPath { get; }

        /// <summary>工具图标（可为 null）</summary>
        Texture2D Icon { get; }

        /// <summary>窗口被激活时调用</summary>
        void OnEnable();

        /// <summary>窗口被停用时调用</summary>
        void OnDisable();

        /// <summary>
        /// 绘制右侧容器内容
        /// </summary>
        /// <param name="rect">右侧可用区域</param>
        void OnGUI(Rect rect);
    }
}
