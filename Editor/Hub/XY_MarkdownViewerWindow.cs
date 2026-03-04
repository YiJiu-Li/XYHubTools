using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Framework.XYEditor
{
    /// <summary>
    /// 简易 Markdown 文档查看器
    /// 支持：# 标题、**粗体**、`行内代码`、``` 代码块、- 列表、--- 分割线
    /// </summary>
    internal class XY_MarkdownViewerWindow : EditorWindow
    {
        private string _docTitle;
        private string[] _lines;
        private Vector2 _scroll;
        private bool _stylesInited;

        private GUIStyle _h1Style;
        private GUIStyle _h2Style;
        private GUIStyle _h3Style;
        private GUIStyle _bodyStyle;
        private GUIStyle _codeBlockStyle;
        private GUIStyle _codeInlineStyle;

        // ─── 入口 ────────────────────────────────────────────────
        internal static void Open(string toolName, string mdPath)
        {
            // 尝试将相对路径转为绝对路径
            if (!Path.IsPathRooted(mdPath))
            {
                string projectRoot = Path.GetDirectoryName(Application.dataPath);
                mdPath = Path.GetFullPath(Path.Combine(projectRoot, mdPath));
            }

            if (!File.Exists(mdPath))
            {
                EditorUtility.DisplayDialog("文档未找到", $"找不到文档文件：\n{mdPath}", "确定");
                return;
            }

            var win = GetWindow<XY_MarkdownViewerWindow>(true, $"📖 {toolName} 文档");
            win._docTitle = toolName;
            win._lines = File.ReadAllLines(mdPath);
            win.minSize = new Vector2(520, 560);
            win._stylesInited = false;
            win.Show();
        }

        // ─── 样式初始化 ──────────────────────────────────────────
        private void InitStyles()
        {
            if (_stylesInited)
                return;
            _stylesInited = true;

            bool dark = EditorGUIUtility.isProSkin;
            Color textColor = dark
                ? new Color(0.88f, 0.88f, 0.88f)
                : new Color(0.08f, 0.08f, 0.08f);

            _h1Style = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 20,
                wordWrap = true,
                normal =
                {
                    textColor = dark ? new Color(0.65f, 0.88f, 1f) : new Color(0.08f, 0.28f, 0.60f),
                },
            };
            _h2Style = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 15,
                wordWrap = true,
                normal =
                {
                    textColor = dark ? new Color(0.75f, 0.92f, 1f) : new Color(0.12f, 0.32f, 0.64f),
                },
            };
            _h3Style = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 13,
                wordWrap = true,
                normal =
                {
                    textColor = dark ? new Color(0.85f, 0.95f, 1f) : new Color(0.18f, 0.38f, 0.68f),
                },
            };
            _bodyStyle = new GUIStyle(EditorStyles.label)
            {
                fontSize = 12,
                wordWrap = true,
                richText = true,
                normal = { textColor = textColor },
            };
            _codeBlockStyle = new GUIStyle(EditorStyles.label)
            {
                fontSize = 11,
                wordWrap = false,
                richText = false,
                padding = new RectOffset(8, 8, 4, 4),
                normal =
                {
                    background = MakeTex(
                        dark ? new Color(0.13f, 0.13f, 0.13f) : new Color(0.84f, 0.84f, 0.84f)
                    ),
                    textColor = dark
                        ? new Color(0.90f, 0.78f, 0.55f)
                        : new Color(0.30f, 0.10f, 0.05f),
                },
            };
        }

        // ─── OnGUI ───────────────────────────────────────────────
        private void OnGUI()
        {
            InitStyles();

            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            EditorGUILayout.Space(12);

            bool inCodeBlock = false;

            foreach (var rawLine in _lines)
            {
                // ── 代码块切换 ──
                if (rawLine.TrimStart().StartsWith("```"))
                {
                    inCodeBlock = !inCodeBlock;
                    if (inCodeBlock)
                        EditorGUILayout.Space(4);
                    else
                        EditorGUILayout.Space(4);
                    continue;
                }

                if (inCodeBlock)
                {
                    EditorGUILayout.LabelField(rawLine, _codeBlockStyle);
                    continue;
                }

                // ── 标题 ──
                if (rawLine.StartsWith("# "))
                {
                    EditorGUILayout.Space(10);
                    EditorGUILayout.LabelField(rawLine.Substring(2), _h1Style);
                    var r = GUILayoutUtility.GetRect(
                        GUIContent.none,
                        GUIStyle.none,
                        GUILayout.Height(1)
                    );
                    EditorGUI.DrawRect(r, new Color(0.35f, 0.60f, 0.90f, 0.55f));
                    EditorGUILayout.Space(4);
                    continue;
                }
                if (rawLine.StartsWith("## "))
                {
                    EditorGUILayout.Space(8);
                    EditorGUILayout.LabelField(rawLine.Substring(3), _h2Style);
                    EditorGUILayout.Space(2);
                    continue;
                }
                if (rawLine.StartsWith("### "))
                {
                    EditorGUILayout.Space(5);
                    EditorGUILayout.LabelField(rawLine.Substring(4), _h3Style);
                    continue;
                }

                // ── 分割线 ──
                if (
                    rawLine.StartsWith("---")
                    || rawLine.StartsWith("***") && rawLine.Trim('*').Length == 0
                )
                {
                    EditorGUILayout.Space(4);
                    var r = GUILayoutUtility.GetRect(
                        GUIContent.none,
                        GUIStyle.none,
                        GUILayout.Height(1)
                    );
                    EditorGUI.DrawRect(r, new Color(0.5f, 0.5f, 0.5f, 0.45f));
                    EditorGUILayout.Space(4);
                    continue;
                }

                // ── 空行 ──
                if (string.IsNullOrWhiteSpace(rawLine))
                {
                    EditorGUILayout.Space(5);
                    continue;
                }

                // ── 无序列表 ──
                if (rawLine.StartsWith("- ") || rawLine.StartsWith("* "))
                {
                    string content = ProcessInline(rawLine.Substring(2));
                    EditorGUILayout.BeginHorizontal();
                    GUILayout.Space(18);
                    EditorGUILayout.LabelField("•  " + content, _bodyStyle);
                    EditorGUILayout.EndHorizontal();
                    continue;
                }

                // ── 有序列表（1. 2. …）──
                if (rawLine.Length > 2 && char.IsDigit(rawLine[0]) && rawLine[1] == '.')
                {
                    string content = ProcessInline(rawLine);
                    EditorGUILayout.BeginHorizontal();
                    GUILayout.Space(8);
                    EditorGUILayout.LabelField(content, _bodyStyle);
                    EditorGUILayout.EndHorizontal();
                    continue;
                }

                // ── 普通段落 ──
                EditorGUILayout.LabelField(ProcessInline(rawLine), _bodyStyle);
            }

            EditorGUILayout.Space(20);
            EditorGUILayout.EndScrollView();
        }

        // ─── 行内格式处理 ────────────────────────────────────────
        private static string ProcessInline(string text)
        {
            bool dark = EditorGUIUtility.isProSkin;
            string codeCol = dark ? "#E5C07B" : "#9C3300";

            var sb = new StringBuilder();
            int i = 0;

            while (i < text.Length)
            {
                // **bold**
                if (i + 1 < text.Length && text[i] == '*' && text[i + 1] == '*')
                {
                    int end = text.IndexOf("**", i + 2);
                    if (end >= 0)
                    {
                        sb.Append("<b>").Append(text, i + 2, end - i - 2).Append("</b>");
                        i = end + 2;
                        continue;
                    }
                }
                // `code`
                if (text[i] == '`')
                {
                    int end = text.IndexOf('`', i + 1);
                    if (end >= 0)
                    {
                        sb.Append($"<color={codeCol}>")
                            .Append(text, i + 1, end - i - 1)
                            .Append("</color>");
                        i = end + 1;
                        continue;
                    }
                }
                sb.Append(text[i]);
                i++;
            }
            return sb.ToString();
        }

        // ─── 工具 ────────────────────────────────────────────────
        private static Texture2D MakeTex(Color col)
        {
            var t = new Texture2D(1, 1);
            t.SetPixel(0, 0, col);
            t.Apply();
            return t;
        }
    }
}
