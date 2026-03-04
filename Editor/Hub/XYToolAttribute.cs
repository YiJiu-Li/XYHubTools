using System;

namespace Framework.XYEditor
{
    /// <summary>
    /// 可选的工具元数据 Attribute，灵感来自 Odin Inspector 的 LabelText / PropertyOrder 设计理念。
    /// 附加在 IXYEditorTool 实现类上可控制排序与标签，不影响未标注的工具。
    /// </summary>
    /// <example>
    /// [XYTool(order: 0, tags: new[] { "常用", "贴图" })]
    /// public class MyTool : IXYEditorTool { ... }
    /// </example>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    public sealed class XYToolAttribute : Attribute
    {
        /// <summary>
        /// 在同分类内的排列顺序（值越小越靠前，默认 0）
        /// </summary>
        public int Order { get; }

        /// <summary>
        /// 工具标签，用于扩展搜索命中范围（可不填）
        /// </summary>
        public string[] Tags { get; }

        public XYToolAttribute(int order = 0, string[] tags = null)
        {
            Order = order;
            Tags = tags ?? Array.Empty<string>();
        }
    }
}
