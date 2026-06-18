// =======================================================================
//  GitPushWatcherNotifier — 非模态推送通知窗口
//
//  设计要点：
//    - 使用 ShowUtility() 独立窗口，可拖动，不阻塞主线程
//    - 默认显示在主编辑器窗口右上角
//    - 同一时刻最多 1 个通知，多条推送合并显示 "N 个新 commit"
//    - 到达 AutoDismiss 时间后自动隐藏（0 = 不自动消失）
//    - 用户点击 "稍后处理" 即隐藏
//    - 只做提醒，不在通知窗口里提供 pull / merge 等写操作
// =======================================================================

using System;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Framework.XYEditor.GitPushWatcher
{
    internal class GitPushWatcherNotifier : EditorWindow
    {
        private const float WIN_W = 360f;
        private const float WIN_H = 168f;
        private const float MARGIN = 12f;

        private PushEvent _event;
        private double _shownAt;
        private GUIStyle _titleStyle;
        private GUIStyle _bodyStyle;
        private GUIStyle _metaStyle;
        private GUIStyle _btnStyle;
        private GUIStyle _bgStyle;
        private Texture2D _bgTex;
        private static GitPushWatcherNotifier s_current;

        // ── 静态入口 ──────────────────────────────────────────────────────
        public static void ShowEvent(PushEvent evt)
        {
            if (evt == null) return;
            if (GitPushWatcherSettings.NotifyStyle == NotifyStyle.LogOnly)
                return;

            var win = s_current != null ? s_current : CreateInstance<GitPushWatcherNotifier>();
            win.titleContent = new GUIContent("Git 推送通知");
            win._event = evt;
            win._shownAt = EditorApplication.timeSinceStartup;
            win.RepositionToTopRight();
            win.minSize = new Vector2(WIN_W, WIN_H);
            win.maxSize = new Vector2(WIN_W, WIN_H + 60f);
            if (s_current == null)
            {
                s_current = win;
                win.ShowUtility();
            }
            else
            {
                win.Repaint();
                win.Focus();
            }
        }

        private void RepositionToTopRight()
        {
            // 找最大的 EditorWindow 作为参考（通常是主编辑器窗口）
            EditorWindow main = null;
            float maxArea = 0f;
            foreach (var w in Resources.FindObjectsOfTypeAll<EditorWindow>())
            {
                if (w == this) continue;
                if (w.titleContent.text.Contains("推送通知")) continue;
                float a = w.position.width * w.position.height;
                if (a > maxArea)
                {
                    maxArea = a;
                    main = w;
                }
            }
            float x, y;
            if (main != null)
            {
                x = main.position.x + main.position.width - WIN_W - MARGIN;
                y = main.position.y + MARGIN;
            }
            else
            {
                // 兜底：屏幕右上
                x = Screen.currentResolution.width - WIN_W - MARGIN;
                y = MARGIN;
            }
            position = new Rect(x, y, WIN_W, WIN_H);
        }

        // ── 生命周期 ──────────────────────────────────────────────────────
        private void OnEnable()
        {
            _titleStyle = null;
        }

        private void OnDisable()
        {
            if (s_current == this)
                s_current = null;
            if (_bgTex != null) DestroyImmediate(_bgTex);
        }

        private void EnsureStyles()
        {
            if (_titleStyle != null) return;

            bool dark = EditorGUIUtility.isProSkin;
            Color titleCol = dark ? new Color(0.95f, 0.85f, 0.4f) : new Color(0.6f, 0.45f, 0.1f);
            Color bodyCol = dark ? new Color(0.92f, 0.92f, 0.92f) : new Color(0.1f, 0.1f, 0.1f);
            Color metaCol = dark ? new Color(0.65f, 0.65f, 0.65f) : new Color(0.4f, 0.4f, 0.4f);

            _titleStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 13,
                richText = true,
                wordWrap = true,
                normal = { textColor = titleCol },
            };
            _bodyStyle = new GUIStyle(EditorStyles.label)
            {
                fontSize = 11,
                richText = true,
                wordWrap = true,
                normal = { textColor = bodyCol },
            };
            _metaStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                fontSize = 10,
                normal = { textColor = metaCol },
            };
            _btnStyle = new GUIStyle(GUI.skin.button)
            {
                fontSize = 11,
                fixedHeight = 24,
            };

            // 背景
            if (_bgTex == null)
            {
                _bgTex = new Texture2D(1, 1);
                _bgTex.SetPixel(0, 0, dark
                    ? new Color(0.18f, 0.18f, 0.20f, 0.97f)
                    : new Color(0.98f, 0.96f, 0.88f, 0.97f));
                _bgTex.Apply();
            }
            _bgStyle = new GUIStyle { normal = { background = _bgTex } };
        }

        private void OnGUI()
        {
            EnsureStyles();
            if (_event == null)
            {
                Close();
                return;
            }

            // 自动消失
            int dismiss = GitPushWatcherSettings.NotifyAutoDismissSec;
            if (dismiss > 0 && EditorApplication.timeSinceStartup - _shownAt > dismiss)
            {
                Close();
                return;
            }

            // 背景
            GUI.Box(new Rect(0, 0, position.width, position.height), GUIContent.none, _bgStyle);

            float pad = 12f;
            float w = position.width - pad * 2;
            float y = 10f;

            // 标题
            string title = "📥 检测到远程推送";
            GUI.Label(new Rect(pad, y, w, 22), title, _titleStyle);
            y += 26;

            // 主体
            string body =
                $"<b>{_event.Remote}/{_event.Branch}</b>  领先本地 <b>{_event.CommitCount}</b> 个 commit\n"
                + $"最新: <color=#7fbfff>{_event.ShortHash}</color>   检出: {DateTime.Now:HH:mm:ss}";
            GUI.Label(new Rect(pad, y, w, 38), body, _bodyStyle);
            y += 42;

            // 提交摘要（最多 3 行）
            if (!string.IsNullOrWhiteSpace(_event.Summary))
            {
                var sb = new StringBuilder();
                string[] lines = _event.Summary.Split('\n');
                int n = Math.Min(3, lines.Length);
                for (int i = 0; i < n; i++)
                {
                    string line = lines[i].TrimEnd();
                    if (line.Length > 60) line = line.Substring(0, 60) + "...";
                    sb.AppendLine("• " + line);
                }
                float summaryH = n * 16f + 4f;
                GUI.Label(new Rect(pad, y, w, summaryH), sb.ToString(), _metaStyle);
                y += summaryH;
            }

            // 按钮：只确认，不做 pull / merge 等写操作
            float btnY = position.height - 34f;
            float btnH = 24f;

            var oldEnabled = GUI.enabled;
            GUI.enabled = oldEnabled;
            if (GUI.Button(new Rect(pad, btnY, w, btnH), "知道了", _btnStyle))
            {
                GitPushWatcherService.Acknowledge(_event);
                Close();
            }
            GUI.enabled = oldEnabled;

            // 让 ESC 关掉
            if (Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.Escape)
            {
                Close();
                Event.current.Use();
            }
        }
    }
}
