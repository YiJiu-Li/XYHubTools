using Framework.XYEditor;
using UnityEditor;
using UnityEngine;

namespace Framework.XYEditor.Tools
{
    /// <summary>
    /// 欢迎页工具 —— 展示如何实现 IXYEditorTool
    /// </summary>
    // XYToolAttribute：设置同分类内排序（order:0 最靠前），并附加搜索标签
    [XYTool(order: 0, tags: new[] { "欢迎", "说明", "帮助" })]
    public class WelcomeTool : IXYEditorTool
    {
        public string ToolName => "欢迎";
        public string Category => "关于";
        public string Description => "XY 编辑器工具集使用说明";
        public string DocumentPath => null;
        public Texture2D Icon => EditorGUIUtility.FindTexture("d_UnityLogo");

        private Vector2 _scroll;

        public void OnEnable()
        {
            _scroll = Vector2.zero;
        }

        public void OnDisable() { }

        public void OnGUI(Rect rect)
        {
            _scroll = GUI.BeginScrollView(rect, _scroll, new Rect(0, 0, rect.width - 16, 800));

            float pad = 24f;
            float w = rect.width - pad * 2 - 16f;
            float y = 20f;

            // 标题
            GUI.Label(
                new Rect(pad, y, w, 30),
                "✦  XY 编辑器工具集",
                XYEditorStyles.HeaderTitleLarge
            );
            y += 38;

            // 分割线
            EditorGUI.DrawRect(new Rect(pad, y, w, 1), new Color(0.4f, 0.4f, 0.4f, 0.5f));
            y += 10;

            // 正文
            var bodyStyle = XYEditorStyles.RichLabel;

            string intro =
                "XY 编辑器工具集是一个<b>可扩展的编辑器工具聚合窗口</b>，"
                + "所有工具通过统一接口注册，自动发现并分类展示。\n\n"
                + "<b>✦ 新功能（参考 Odin Inspector 设计理念）：</b>\n"
                + "  • <b>可拖拽分割线</b>：拖拽左右分界线自由调整列表宽度，下次打开自动恢复\n"
                + "  • <b>收藏工具</b>：右键工具项 → 添加到收藏 ★，收藏区置顶显示，持久化保存\n"
                + "  • <b>最近使用</b>：自动记录最近切换过的工具，方便快速回跳\n"
                + "  • <b>键盘导航</b>：↑↓ 方向键切换工具，无需鼠标\n"
                + "  • <b>底部状态栏</b>：实时显示工具总数 / 搜索命中数\n"
                + "  • <b>错误面板</b>：工具异常时显示错误信息，支持复制堆栈与重试\n\n"
                + "<b>如何添加新工具：</b>\n"
                + "  1. 创建 C# 类，实现 <b>IXYEditorTool</b> 接口\n"
                + "  2. 填写 ToolName、Category、Description 属性\n"
                + "  3. 在 OnGUI(Rect rect) 中绘制工具界面\n"
                + "  4. 无需手动注册 —— 系统通过<b>反射自动发现</b>所有实现类\n"
                + "  5. 可选：添加 <b>[XYTool(order:0, tags:...)]</b> 控制排序与搜索标签\n\n"
                + "<b>接口定义（Assets/Editor/Hub/IXYEditorTool.cs）：</b>";

            float textH = bodyStyle.CalcHeight(new GUIContent(intro), w);
            GUI.Label(new Rect(pad, y, w, textH), intro, bodyStyle);
            y += textH + 8;

            // 代码示例框
            var codeStyle = XYEditorStyles.CodeBlock;

            string code =
                "[XYTool(order: 10, tags: new[] { \"贴图\", \"纹理\" })]\n"
                + @"public class MyTool : IXYEditorTool
{
    public string    ToolName    => ""我的工具"";
    public string    Category    => ""自定义"";
    public string    Description => ""工具描述"";
    public string    DocumentPath => null;   // 可选：Markdown 文档路径
    public Texture2D Icon        => null;

    public void OnEnable()  { /* 初始化 */ }
    public void OnDisable() { /* 清理  */ }

    public void OnGUI(Rect rect)
    {
        GUILayout.BeginArea(rect);
        GUILayout.Label(""Hello, XY Editor Hub!"");
        GUILayout.EndArea();
    }
}";
            float codeH = 200f;
            GUI.TextArea(new Rect(pad, y, w, codeH), code, codeStyle);
            y += codeH + 16;

            // 快捷键提示
            var tipStyle = XYEditorStyles.TipBox;
            string tip =
                "<b>快捷键：</b>  Alt+E 快速打开工具集  |  ↑↓ 键盘导航工具列表  |  右键工具项收藏/取消收藏";
            float tipH = tipStyle.CalcHeight(new GUIContent(tip), w);
            GUI.Label(new Rect(pad, y, w, tipH + 4), tip, tipStyle);

            GUI.EndScrollView();
        }
    }
}
