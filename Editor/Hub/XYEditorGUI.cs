using System;
using UnityEditor;
using UnityEngine;

namespace Framework.XYEditor
{
    /// <summary>
    /// 可复用的 Editor GUI 绘制工具集 —— 参考 Odin Inspector 的 SirenixEditorGUI，
    /// 使用纯 Unity Editor API 实现，消除各面板中的重复绘制代码。
    /// </summary>
    public static class XYEditorGUI
    {
        // ═══════════════════════════════════════════════════════════════════
        //  区块 / 分组
        // ═══════════════════════════════════════════════════════════════════

        /// <summary>
        /// 带标题的 HelpBox 区块（等同 Odin 的 [BoxGroup]）。
        /// </summary>
        /// <param name="title">区块标题</param>
        /// <param name="content">区块内容绘制委托</param>
        /// <param name="indent">是否对内容增加一级缩进</param>
        /// <param name="spacing">区块后间距</param>
        public static void DrawSection(
            string title,
            Action content,
            bool indent = false,
            float spacing = 5f
        )
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            GUILayout.Label(title, EditorStyles.boldLabel);

            if (indent)
                EditorGUI.indentLevel++;
            content?.Invoke();
            if (indent)
                EditorGUI.indentLevel--;

            EditorGUILayout.EndVertical();

            if (spacing > 0f)
                EditorGUILayout.Space(spacing);
        }

        /// <summary>
        /// 无标题的 HelpBox 包装盒。
        /// </summary>
        public static void DrawBox(Action content, float spacing = 0f)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            content?.Invoke();
            EditorGUILayout.EndVertical();

            if (spacing > 0f)
                EditorGUILayout.Space(spacing);
        }

        /// <summary>
        /// 可折叠的 HelpBox 区块（等同 Odin 的 [FoldoutGroup]）。
        /// </summary>
        /// <param name="title">标题（支持 emoji）</param>
        /// <param name="expanded">当前折叠状态（ref）</param>
        /// <param name="content">内容绘制委托</param>
        public static void DrawFoldoutSection(string title, ref bool expanded, Action content)
        {
            expanded = EditorGUILayout.Foldout(expanded, title, true);
            if (expanded)
            {
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                content?.Invoke();
                EditorGUILayout.EndVertical();
            }
        }

        // ═══════════════════════════════════════════════════════════════════
        //  路径选择器
        // ═══════════════════════════════════════════════════════════════════

        /// <summary>
        /// 带"浏览"按钮的文件夹路径字段（等同 Odin 的 [FolderPath]）。
        /// </summary>
        /// <returns>用户修改后的路径值</returns>
        public static string FolderPathField(
            string label,
            string value,
            string dialogTitle = null,
            float browseWidth = 60f,
            string browseLabel = "浏览"
        )
        {
            EditorGUILayout.BeginHorizontal();
            value = EditorGUILayout.TextField(label, value);
            if (GUILayout.Button(browseLabel, GUILayout.Width(browseWidth)))
            {
                string path = EditorUtility.OpenFolderPanel(dialogTitle ?? "选择文件夹", "", "");
                if (!string.IsNullOrEmpty(path))
                    value = path;
            }
            EditorGUILayout.EndHorizontal();
            return value;
        }

        // ═══════════════════════════════════════════════════════════════════
        //  标题 / 分割线 / 页脚
        // ═══════════════════════════════════════════════════════════════════

        /// <summary>
        /// 绘制居中大标题 + 可选副标题。
        /// </summary>
        public static void DrawHeader(string title, string subtitle = null, int fontSize = 14)
        {
            if (fontSize != 14)
            {
                var style = new GUIStyle(EditorStyles.boldLabel)
                {
                    fontSize = fontSize,
                    alignment = TextAnchor.MiddleCenter,
                };
                EditorGUILayout.LabelField(title, style);
            }
            else
            {
                EditorGUILayout.LabelField(title, XYEditorStyles.HeaderTitle);
            }

            if (!string.IsNullOrEmpty(subtitle))
                EditorGUILayout.LabelField(subtitle, XYEditorStyles.HeaderSubtitle);
        }

        /// <summary>
        /// 水平分割线。
        /// </summary>
        public static void DrawSeparator()
        {
            EditorGUILayout.Space();
            Rect line = EditorGUILayout.GetControlRect(false, 1);
            EditorGUI.DrawRect(line, new Color(0.5f, 0.5f, 0.5f, 1));
            EditorGUILayout.Space();
        }

        /// <summary>
        /// 带可选链接的页脚（分割线 + 居中文字）。
        /// </summary>
        public static void DrawFooter(string text, string url = null)
        {
            DrawSeparator();
            if (!string.IsNullOrEmpty(url))
            {
                if (GUILayout.Button(text, XYEditorStyles.FooterLink))
                    Application.OpenURL(url);
            }
            else
            {
                GUILayout.Label(text, XYEditorStyles.FooterLink);
            }
        }

        // ═══════════════════════════════════════════════════════════════════
        //  进度条
        // ═══════════════════════════════════════════════════════════════════

        /// <summary>
        /// 绘制进度条。
        /// </summary>
        public static void DrawProgressBar(float progress, string status, float height = 20f)
        {
            Rect rect = EditorGUILayout.GetControlRect(false, height);
            EditorGUI.ProgressBar(rect, progress, status);
        }

        // ═══════════════════════════════════════════════════════════════════
        //  代码扩展名列表（CopyApp 等共用）
        // ═══════════════════════════════════════════════════════════════════

        /// <summary>
        /// 绘制只读的扩展名勾选列表。
        /// </summary>
        public static void DrawReadonlyExtensionList(string title, string[] extensions)
        {
            DrawSection(
                title,
                () =>
                {
                    GUI.enabled = false;
                    foreach (var ext in extensions)
                        EditorGUILayout.Toggle(ext, true);
                    GUI.enabled = true;
                }
            );
        }
    }
}
