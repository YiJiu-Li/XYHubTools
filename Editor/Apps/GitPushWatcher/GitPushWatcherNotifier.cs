// =======================================================================
//  GitPushWatcherNotifier — 非模态推送通知窗口
//
//  设计要点：
//    - 使用 ShowUtility() 独立窗口，可拖动，不阻塞主线程
//    - 默认显示在主编辑器窗口右上角
//    - 同一时刻最多 1 个通知窗口；多条推送合并显示 "N 个新 commit"
//    - 到达 AutoDismiss 时间后自动隐藏（0 = 不自动消失）
//    - 用户点击 "稍后处理" 即隐藏
//    - 只做提醒，不在通知窗口里提供 pull / merge 等写操作
//    - 显示作者、提交时间、完整 commit 标题；超出窗口高度时可滚动
// =======================================================================

using System;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Framework.XYEditor.GitPushWatcher
{
    internal class GitPushWatcherNotifier : EditorWindow
    {
        private const float WIN_W = 380f;
        private const float WIN_H = 280f;
        private const float MAX_WIN_H = 520f;
        private const float MARGIN = 12f;

        private PushEvent _event;
        private double _shownAt;
        private Vector2 _scroll;

        private GUIStyle _titleStyle;
        private GUIStyle _bodyStyle;
        private GUIStyle _metaStyle;
        private GUIStyle _subjectStyle;
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
            win._scroll = Vector2.zero;

            // 依据 commit 数量动态计算窗口高度
            int commitRows = evt.Commits != null && evt.Commits.Count > 0
                ? Math.Min(evt.Commits.Count, 6)
                : Math.Min(evt.CommitCount, 6);
            float dynamicH = Mathf.Clamp(170f + commitRows * 36f, WIN_H, MAX_WIN_H);

            win.minSize = new Vector2(WIN_W, dynamicH);
            win.maxSize = new Vector2(WIN_W, dynamicH + 40f);
            win.RepositionToTopRight(dynamicH);
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

        private void RepositionToTopRight(float h)
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
            position = new Rect(x, y, WIN_W, h);
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
                richText = true,
                normal = { textColor = metaCol },
            };
            _subjectStyle = new GUIStyle(EditorStyles.label)
            {
                fontSize = 11,
                richText = true,
                wordWrap = true,
                normal = { textColor = bodyCol },
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

            // ── 标题 ────────────────────────────────────────────────────
            string title = "📥 检测到远程推送";
            GUI.Label(new Rect(pad, y, w, 22), title, _titleStyle);
            y += 24;

            // ── 主体：分支 + 提交数 + 检出时间 ───────────────────────────
            string authorTag = string.IsNullOrEmpty(_event.PrimaryAuthor)
                ? ""
                : $"   作者 <b>{_event.PrimaryAuthor}</b>";
            string body =
                $"<b>{_event.Remote}/{_event.Branch}</b>  领先本地 <b>{_event.CommitCount}</b> 个 commit{authorTag}\n"
                + $"最新: <color=#7fbfff>{_event.ShortHash}</color>   检出: {DateTime.Now:HH:mm:ss}";
            float bodyH = _bodyStyle.CalcHeight(new GUIContent(body), w);
            GUI.Label(new Rect(pad, y, w, bodyH), body, _bodyStyle);
            y += bodyH + 6;

            // ── Commit 详细列表（可滚动）────────────────────────────────
            float listH = Mathf.Max(0f, position.height - y - 40f);
            DrawCommitList(pad, y, w, listH);

            // ── 底部按钮 ────────────────────────────────────────────────
            float btnY = position.height - 34f;
            float btnH = 24f;
            if (GUI.Button(new Rect(pad, btnY, w, btnH), "知道了", _btnStyle))
            {
                GitPushWatcherService.Acknowledge(_event);
                Close();
            }

            // 让 ESC 关掉
            if (Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.Escape)
            {
                Close();
                Event.current.Use();
            }
        }

        private void DrawCommitList(float pad, float y, float w, float h)
        {
            // 没有详细 commits 时回退到原来的 Summary 文本
            if (_event.Commits == null || _event.Commits.Count == 0)
            {
                if (string.IsNullOrWhiteSpace(_event.Summary)) return;
                _scroll = GUI.BeginScrollView(new Rect(pad, y, w, h), _scroll,
                    new Rect(0, 0, w - 16, CalcSummaryHeight(w)));
                var sb = new StringBuilder();
                string[] lines = _event.Summary.Split('\n');
                int n = Math.Min(5, lines.Length);
                for (int i = 0; i < n; i++)
                {
                    string line = lines[i].TrimEnd();
                    if (line.Length > 70) line = line.Substring(0, 70) + "...";
                    sb.AppendLine("• " + line);
                }
                GUI.Label(new Rect(0, 0, w - 16, n * 16f + 4f), sb.ToString(), _metaStyle);
                GUI.EndScrollView();
                return;
            }

            float contentH = CalcCommitListHeight(_event.Commits, w);
            _scroll = GUI.BeginScrollView(new Rect(pad, y, w, h), _scroll,
                new Rect(0, 0, w - 16, contentH));

            float cy = 0f;
            int shown = 0;
            foreach (var c in _event.Commits)
            {
                DrawCommitRow(c, 0, cy, w - 16);
                cy += CommitRowHeight(c, w - 16) + 4f;
                shown++;
            }
            if (shown < _event.CommitCount)
            {
                string more = $"… 还有 {_event.CommitCount - shown} 个 commit 未显示";
                GUI.Label(new Rect(0, cy, w - 16, 18), more, _metaStyle);
            }
            GUI.EndScrollView();
        }

        private float CalcSummaryHeight(float w)
        {
            int n = string.IsNullOrEmpty(_event?.Summary) ? 0 : Math.Min(5, _event.Summary.Split('\n').Length);
            return n * 16f + 4f;
        }

        private float CalcCommitListHeight(List<CommitEntry> commits, float w)
        {
            float total = 0f;
            foreach (var c in commits)
                total += CommitRowHeight(c, w) + 4f;
            return total + 18f; // 末尾 “还有 N 个” 提示
        }

        private float CommitRowHeight(CommitEntry c, float w)
        {
            // 第 1 行: hash + 时间   ~14
            // 第 2 行: 作者         ~14
            // 第 3 行: subject（可换行）~ CalcHeight
            float subjectH = _subjectStyle.CalcHeight(new GUIContent(c.Subject ?? ""), w);
            return 14f + 14f + subjectH + 4f;
        }

        private void DrawCommitRow(CommitEntry c, float x, float y, float w)
        {
            // 第 1 行：hash + 时间
            string line1 = $"<b>{c.ShortHash}</b>   <color=#888888>{c.DisplayTime}</color>";
            GUI.Label(new Rect(x, y, w, 14), line1, _metaStyle);

            // 第 2 行：作者
            string author = string.IsNullOrEmpty(c.AuthorEmail)
                ? c.DisplayAuthor
                : $"{c.DisplayAuthor} <color=#888888>&lt;{c.AuthorEmail}&gt;</color>";
            GUI.Label(new Rect(x, y + 14, w, 14), author, _metaStyle);

            // 第 3 行：subject
            float subjectH = _subjectStyle.CalcHeight(new GUIContent(c.Subject ?? ""), w);
            GUI.Label(new Rect(x, y + 28, w, subjectH), c.Subject ?? "", _subjectStyle);
        }
    }
}

