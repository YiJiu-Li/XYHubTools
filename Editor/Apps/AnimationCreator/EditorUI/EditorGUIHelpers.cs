using UnityEditor;
using UnityEngine;

namespace YZJ.AnimationCreator.Editor
{
    /// <summary>
    /// 编辑器 GUI 通用工具类
    /// </summary>
    public static class EditorGUIHelpers
    {
        private static GUIStyle _sectionHeaderStyle;
        private static GUIStyle _subHeaderStyle;

        public static GUIStyle SectionHeaderStyle =>
            _sectionHeaderStyle ??= new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 12,
                richText = true,
            };

        public static GUIStyle SubHeaderStyle =>
            _subHeaderStyle ??= new GUIStyle(EditorStyles.miniLabel)
            {
                fontStyle = FontStyle.Bold,
                normal = { textColor = new Color(0.7f, 0.7f, 0.7f) },
            };

        /// <summary>带标题的 box 区域</summary>
        public static void DrawBoxSection(string title, System.Action content)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField(title, SectionHeaderStyle);
            content?.Invoke();
            EditorGUILayout.EndVertical();
        }

        /// <summary>带颜色条 + 浅底的子模块标题</summary>
        public static void DrawModuleHeader(string label, Color color)
        {
            EditorGUILayout.Space(2);
            var rect = EditorGUILayout.GetControlRect(false, 18);
            // 色条
            EditorGUI.DrawRect(new Rect(rect.x, rect.y + 1, 3, 16), color);
            // 浅色背景
            var bgColor = new Color(color.r, color.g, color.b, 0.08f);
            EditorGUI.DrawRect(new Rect(rect.x + 3, rect.y, rect.width - 3, 18), bgColor);
            // 文字
            EditorGUI.LabelField(
                new Rect(rect.x + 8, rect.y, rect.width - 8, 18),
                label,
                EditorStyles.boldLabel
            );
        }

        /// <summary>子标题（灰色小字）</summary>
        public static void DrawSubLabel(string text)
        {
            EditorGUILayout.LabelField(text, SubHeaderStyle);
        }

        /// <summary>水平绘制 From → To</summary>
        public static void DrawFromTo(ref float from, ref float to)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.PrefixLabel("起始");
            from = EditorGUILayout.FloatField(from);
            GUILayout.Label("→", GUILayout.Width(18));
            to = EditorGUILayout.FloatField(to);
            EditorGUILayout.EndHorizontal();
        }

        /// <summary>分隔线</summary>
        public static void DrawSeparator()
        {
            EditorGUILayout.Space(2);
            var rect = EditorGUILayout.GetControlRect(false, 1);
            EditorGUI.DrawRect(rect, new Color(0.35f, 0.35f, 0.35f, 0.5f));
            EditorGUILayout.Space(2);
        }
    }
}
