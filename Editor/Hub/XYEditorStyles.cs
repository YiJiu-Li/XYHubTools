using UnityEditor;
using UnityEngine;

namespace Framework.XYEditor
{
    /// <summary>
    /// 全局缓存 GUIStyle —— 避免在 OnGUI 中每帧 new GUIStyle()。
    /// 参考 Odin Inspector SirenixGUIStyles 的懒加载缓存模式。
    /// 域重载时静态字段自动清零，下次访问时重建。
    /// </summary>
    public static class XYEditorStyles
    {
        // ── 标题 ──────────────────────────────────────────────────────────

        private static GUIStyle _headerTitle;

        /// <summary>14px 居中加粗标题</summary>
        public static GUIStyle HeaderTitle =>
            _headerTitle ??= new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 14,
                alignment = TextAnchor.MiddleCenter,
            };

        private static GUIStyle _headerTitleLarge;

        /// <summary>18px 左对齐加粗标题（欢迎页等）</summary>
        public static GUIStyle HeaderTitleLarge =>
            _headerTitleLarge ??= new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 18,
                alignment = TextAnchor.MiddleLeft,
                wordWrap = false,
                normal =
                {
                    textColor = EditorGUIUtility.isProSkin
                        ? new Color(0.85f, 0.93f, 1f)
                        : new Color(0.1f, 0.2f, 0.45f),
                },
            };

        private static GUIStyle _centeredBoldTitle;

        /// <summary>16px 居中加粗标题（CopyApp 等大标题）</summary>
        public static GUIStyle CenteredBoldTitle =>
            _centeredBoldTitle ??= new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 16,
                alignment = TextAnchor.MiddleCenter,
                fontStyle = FontStyle.Bold,
            };

        private static GUIStyle _headerSubtitle;

        /// <summary>居中自动换行的小字副标题</summary>
        public static GUIStyle HeaderSubtitle =>
            _headerSubtitle ??= new GUIStyle(EditorStyles.miniLabel)
            {
                alignment = TextAnchor.MiddleCenter,
                wordWrap = true,
            };

        // ── 页脚 ──────────────────────────────────────────────────────────

        private static GUIStyle _footerLink;

        /// <summary>居中浅紫色链接样式</summary>
        public static GUIStyle FooterLink =>
            _footerLink ??= new GUIStyle(EditorStyles.miniLabel)
            {
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = new Color(0.5f, 0.5f, 0.8f) },
            };

        // ── 日志 ──────────────────────────────────────────────────────────

        private static GUIStyle _logTitle;

        /// <summary>12px 蓝色加粗日志标题</summary>
        public static GUIStyle LogTitle =>
            _logTitle ??= new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 12,
                normal = { textColor = new Color(0.4f, 0.6f, 0.8f) },
            };

        private static GUIStyle _logEntry;

        /// <summary>自动换行、富文本、带内边距的日志条目</summary>
        public static GUIStyle LogEntry =>
            _logEntry ??= new GUIStyle(EditorStyles.wordWrappedLabel)
            {
                richText = true,
                padding = new RectOffset(5, 5, 2, 2),
                margin = new RectOffset(0, 0, 1, 1),
            };

        // ── 通用 ──────────────────────────────────────────────────────────

        private static GUIStyle _wrapLabel;

        /// <summary>自动换行的普通 Label</summary>
        public static GUIStyle WrapLabel =>
            _wrapLabel ??= new GUIStyle(EditorStyles.label) { wordWrap = true };

        private static GUIStyle _richLabel;

        /// <summary>12px 自动换行富文本 Label</summary>
        public static GUIStyle RichLabel =>
            _richLabel ??= new GUIStyle(EditorStyles.label)
            {
                fontSize = 12,
                wordWrap = true,
                richText = true,
                normal =
                {
                    textColor = EditorGUIUtility.isProSkin
                        ? new Color(0.82f, 0.82f, 0.82f)
                        : new Color(0.15f, 0.15f, 0.15f),
                },
            };

        private static GUIStyle _codeBlock;

        /// <summary>等宽绿色代码块（欢迎页）</summary>
        public static GUIStyle CodeBlock =>
            _codeBlock ??= new GUIStyle(EditorStyles.textArea)
            {
                fontSize = 11,
                wordWrap = false,
                fontStyle = FontStyle.Normal,
                normal =
                {
                    textColor = EditorGUIUtility.isProSkin
                        ? new Color(0.7f, 0.95f, 0.7f)
                        : new Color(0.1f, 0.35f, 0.1f),
                },
            };

        private static GUIStyle _tipBox;

        /// <summary>11px 自动换行富文本 HelpBox</summary>
        public static GUIStyle TipBox =>
            _tipBox ??= new GUIStyle(EditorStyles.helpBox)
            {
                fontSize = 11,
                wordWrap = true,
                richText = true,
            };
    }
}
