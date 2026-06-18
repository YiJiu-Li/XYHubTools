// =======================================================================
//  GitPushWatcherPanel — Git 推送监听的可视化面板
//  展示当前分支状态、轮询控制、设置项、推送历史、错误信息。
//  通过订阅 GitPushWatcherService 事件保持数据同步。
// =======================================================================

using System;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Framework.XYEditor.GitPushWatcher
{
    public class GitPushWatcherPanel
    {
        // ── 状态 ──────────────────────────────────────────────────────────
        private Vector2 _scroll;
        private bool _settingsFoldout = true;
        private bool _historyFoldout = true;
        private bool _logFoldout = true;

        private string _intervalText;
        private string _branchText;
        private string _remoteText;
        private string _repoText;
        private string _autoDismissText;
        private bool _initialized;

        private readonly List<LogEntry> _logs = new List<LogEntry>(64);
        private const int MAX_LOGS = 100;

        // ── 样式（懒加载）─────────────────────────────────────────────────
        private GUIStyle _statusOkStyle;
        private GUIStyle _statusWarnStyle;
        private GUIStyle _statusErrStyle;
        private GUIStyle _metaStyle;
        private GUIStyle _logInfoStyle;
        private GUIStyle _logWarnStyle;
        private GUIStyle _logErrStyle;
        private GUIStyle _sectionTitleStyle;
        private GUIStyle _hashStyle;

        private struct LogEntry
        {
            public DateTime Time;
            public string Message;
            public GitPushWatcherService.LogLevel Level;
        }

        // ═══════════════════════════════════════════════════════════════════
        //  IXYPanel
        // ═══════════════════════════════════════════════════════════════════

        public void Init()
        {
            if (!_initialized)
            {
                _intervalText = GitPushWatcherSettings.IntervalSec.ToString();
                _branchText = GitPushWatcherSettings.Branch;
                _remoteText = GitPushWatcherSettings.Remote;
                _repoText = GitPushWatcherSettings.RepoPathOverride;
                _autoDismissText = GitPushWatcherSettings.NotifyAutoDismissSec.ToString();
                _initialized = true;
            }

            // 订阅事件
            GitPushWatcherService.OnTick -= OnTick;
            GitPushWatcherService.OnTick += OnTick;
            GitPushWatcherService.OnLog -= OnLogMsg;
            GitPushWatcherService.OnLog += OnLogMsg;
        }

        public void Cleanup()
        {
            GitPushWatcherService.OnTick -= OnTick;
            GitPushWatcherService.OnLog -= OnLogMsg;
        }

        public void Draw(float containerWidth)
        {
            EnsureStyles();

            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            EditorGUILayout.Space(8);

            DrawStatusSection(containerWidth);
            EditorGUILayout.Space(6);
            DrawControlsSection(containerWidth);
            EditorGUILayout.Space(6);
            DrawSettingsSection(containerWidth);
            EditorGUILayout.Space(6);
            DrawHistorySection(containerWidth);
            EditorGUILayout.Space(6);
            DrawLogSection(containerWidth);

            EditorGUILayout.Space(8);
            EditorGUILayout.EndScrollView();
        }

        // ═══════════════════════════════════════════════════════════════════
        //  状态区
        // ═══════════════════════════════════════════════════════════════════

        private void DrawStatusSection(float w)
        {
            XYEditorGUI.DrawSection("当前状态", () =>
            {
                // ── 始终显示解析后的路径 + 重新检测按钮 ──────────────
                string repoRoot = GitPushWatcherService.ResolvedRepoRoot;
                string gitDir = GitPushWatcherService.ResolvedGitDir;

                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField("仓库根", _metaStyle, GUILayout.Width(60));
                EditorGUILayout.SelectableLabel(
                    string.IsNullOrEmpty(repoRoot) ? "—" : repoRoot,
                    _metaStyle,
                    GUILayout.Height(EditorGUIUtility.singleLineHeight)
                );
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField(".git", _metaStyle, GUILayout.Width(60));
                EditorGUILayout.SelectableLabel(
                    string.IsNullOrEmpty(gitDir) ? "—" : gitDir,
                    _metaStyle,
                    GUILayout.Height(EditorGUIUtility.singleLineHeight)
                );
                if (GUILayout.Button("重新检测", GUILayout.Width(72), GUILayout.Height(18)))
                {
                    GitPushWatcherService.RefreshPaths();
                }
                EditorGUILayout.EndHorizontal();

                // ── 未找到 .git 时的就地修复按钮 ────────────────────
                if (string.IsNullOrEmpty(gitDir))
                {
                    EditorGUILayout.Space(6);
                    EditorGUILayout.HelpBox(
                        "未找到 .git 目录。本工具仅作用于 Git 仓库项目。\n"
                            + "常见原因：项目根不是 git 仓库，或 .git 在 Assets/ 下的子包里。\n"
                            + "可点击下方按钮手动指定，或在「设置」里填路径。",
                        MessageType.Warning
                    );
                    EditorGUILayout.BeginHorizontal();
                    if (GUILayout.Button("浏览仓库根目录", GUILayout.Height(24)))
                    {
                        string picked = EditorUtility.OpenFolderPanel(
                            "选择仓库根目录（含 .git 的目录）",
                            Application.dataPath,
                            ""
                        );
                        if (!string.IsNullOrEmpty(picked))
                            SetRepoOverride(picked);
                    }
                    if (GUILayout.Button("浏览 .git 目录", GUILayout.Height(24)))
                    {
                        string picked = EditorUtility.OpenFolderPanel(
                            "选择 .git 目录",
                            Application.dataPath,
                            ""
                        );
                        if (
                            !string.IsNullOrEmpty(picked)
                            && System.IO.Path.GetFileName(picked)
                                .Equals(".git", StringComparison.Ordinal)
                        )
                            SetRepoOverride(picked);
                        else if (!string.IsNullOrEmpty(picked))
                            EditorUtility.DisplayDialog(
                                "路径无效",
                                "所选文件夹不是 .git 目录。\n请选择名为 .git 的文件夹，或改用「浏览仓库根目录」。",
                                "确定"
                            );
                    }
                    EditorGUILayout.EndHorizontal();
                    return;
                }

                // ── 正常状态：显示分支 / hash 信息 ────────────────────
                var snap = GitPushWatcherService.LastSnapshot;
                if (snap == null)
                {
                    EditorGUILayout.LabelField("⏳ 等待首次检查...", _statusWarnStyle);
                    return;
                }

                if (!string.IsNullOrEmpty(GitPushWatcherService.LastError))
                {
                    EditorGUILayout.HelpBox(GitPushWatcherService.LastError, MessageType.Error);
                }

                // 状态行
                string stateText;
                GUIStyle stateStyle;
                if (snap.InSync)
                {
                    stateText = "✓ 与远程同步";
                    stateStyle = _statusOkStyle;
                }
                else if (snap.IsBehind)
                {
                    stateText = $"↓ 落后远程 {snap.Behind} 个 commit";
                    stateStyle = _statusWarnStyle;
                }
                else if (snap.IsAhead)
                {
                    stateText = $"↑ 领先远程 {snap.Ahead} 个 commit";
                    stateStyle = _metaStyle;
                }
                else
                {
                    stateText = "— 未知";
                    stateStyle = _statusErrStyle;
                }
                EditorGUILayout.LabelField(stateText, stateStyle);

                EditorGUILayout.Space(4);
                EditorGUILayout.LabelField($"分支:    {snap.Branch}", _metaStyle);
                EditorGUILayout.LabelField($"远程:    {snap.Remote}", _metaStyle);
                EditorGUILayout.LabelField(
                    $"本地:    {ShortHash(snap.LocalHash)}",
                    _metaStyle
                );
                EditorGUILayout.LabelField(
                    $"远程:    {ShortHash(snap.RemoteHash)}",
                    _metaStyle
                );
                string fetched = snap.LastFetchTime.HasValue
                    ? snap.LastFetchTime.Value.ToString("yyyy-MM-dd HH:mm:ss")
                    : "—";
                EditorGUILayout.LabelField($"上次 fetch: {fetched}", _metaStyle);
            });
        }

        // ═══════════════════════════════════════════════════════════════════
        //  控制区
        // ═══════════════════════════════════════════════════════════════════

        private void DrawControlsSection(float w)
        {
            XYEditorGUI.DrawSection("控制", () =>
            {
                bool enabled = GitPushWatcherSettings.Enabled;
                bool busy = GitPushWatcherService.IsBusy;

                EditorGUILayout.BeginHorizontal();
                bool newEnabled = GUILayout.Toggle(
                    enabled,
                    enabled ? "● 监听运行中" : "○ 监听已停止",
                    "Button",
                    GUILayout.Height(26)
                );
                if (newEnabled != enabled)
                {
                    GitPushWatcherSettings.Enabled = newEnabled;
                }

                using (new EditorGUI.DisabledScope(busy))
                {
                    if (GUILayout.Button("立即 fetch", GUILayout.Height(26), GUILayout.Width(100)))
                        GitPushWatcherService.FetchAsync();
                    if (GUILayout.Button("立即检查", GUILayout.Height(26), GUILayout.Width(100)))
                        GitPushWatcherService.CheckAsync();
                }
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.Space(2);
                EditorGUILayout.LabelField(
                    busy ? "⏳ 正在执行 git 命令..." : "空闲",
                    _metaStyle
                );
            });
        }

        // ═══════════════════════════════════════════════════════════════════
        //  设置区
        // ═══════════════════════════════════════════════════════════════════

        private void DrawSettingsSection(float w)
        {
            _settingsFoldout = EditorGUILayout.Foldout(
                _settingsFoldout,
                "设置",
                true,
                EditorStyles.foldout
            );
            if (!_settingsFoldout) return;

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUI.indentLevel++;

            // 轮询间隔
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("轮询间隔(秒)", GUILayout.Width(120));
            EditorGUI.BeginChangeCheck();
            string newInterval = EditorGUILayout.TextField(_intervalText, GUILayout.Width(80));
            if (EditorGUI.EndChangeCheck())
            {
                _intervalText = newInterval;
                if (int.TryParse(newInterval, out int v))
                    GitPushWatcherSettings.IntervalSec = v;
            }
            EditorGUILayout.LabelField(
                "[10-3600]，推荐 60",
                _metaStyle
            );
            EditorGUILayout.EndHorizontal();

            // 远程
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("远程名", GUILayout.Width(120));
            EditorGUI.BeginChangeCheck();
            string newRemote = EditorGUILayout.TextField(_remoteText, GUILayout.Width(120));
            if (EditorGUI.EndChangeCheck())
            {
                _remoteText = newRemote;
                GitPushWatcherSettings.Remote = newRemote;
            }
            EditorGUILayout.LabelField("默认 origin", _metaStyle);
            EditorGUILayout.EndHorizontal();

            // 分支
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("追踪分支", GUILayout.Width(120));
            EditorGUI.BeginChangeCheck();
            string newBranch = EditorGUILayout.TextField(_branchText, GUILayout.Width(120));
            if (EditorGUI.EndChangeCheck())
            {
                _branchText = newBranch;
                GitPushWatcherSettings.Branch = newBranch;
                GitPushWatcherSettings.ResetSeenState();
            }
            EditorGUILayout.LabelField("留空 = 跟随当前 HEAD", _metaStyle);
            EditorGUILayout.EndHorizontal();

            // 自动 fetch
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("自动 fetch", GUILayout.Width(120));
            bool newAuto = EditorGUILayout.Toggle(
                GitPushWatcherSettings.AutoFetch,
                GUILayout.Width(20)
            );
            if (newAuto != GitPushWatcherSettings.AutoFetch)
                GitPushWatcherSettings.AutoFetch = newAuto;
            EditorGUILayout.LabelField("关闭后只能用「立即检查」", _metaStyle);
            EditorGUILayout.EndHorizontal();

            // 通知方式
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("通知方式", GUILayout.Width(120));
            NotifyStyle currentStyle = GitPushWatcherSettings.NotifyStyle;
            NotifyStyle newStyle = (NotifyStyle)EditorGUILayout.EnumPopup(
                currentStyle,
                GUILayout.Width(120)
            );
            if (newStyle != currentStyle)
                GitPushWatcherSettings.NotifyStyle = newStyle;
            EditorGUILayout.LabelField(
                currentStyle == NotifyStyle.FloatingWindow
                    ? "右上角可拖动小窗"
                    : "仅写入面板日志",
                _metaStyle
            );
            EditorGUILayout.EndHorizontal();

            // 自动消失
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("自动消失(秒)", GUILayout.Width(120));
            EditorGUI.BeginChangeCheck();
            string newDismiss = EditorGUILayout.TextField(_autoDismissText, GUILayout.Width(80));
            if (EditorGUI.EndChangeCheck())
            {
                _autoDismissText = newDismiss;
                if (int.TryParse(newDismiss, out int v))
                    GitPushWatcherSettings.NotifyAutoDismissSec = v;
            }
            EditorGUILayout.LabelField("0 = 不自动消失", _metaStyle);
            EditorGUILayout.EndHorizontal();

            // 仓库路径覆盖
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("仓库路径", GUILayout.Width(120));
            EditorGUI.BeginChangeCheck();
            string newRepo = EditorGUILayout.TextField(_repoText);
            if (EditorGUI.EndChangeCheck())
            {
                _repoText = newRepo;
                GitPushWatcherSettings.RepoPathOverride = newRepo;
                GitPushWatcherService.RefreshPaths();
            }
            if (GUILayout.Button("浏览", GUILayout.Width(60)))
            {
                string picked = EditorUtility.OpenFolderPanel("选择仓库根目录", _repoText, "");
                if (!string.IsNullOrEmpty(picked))
                {
                    _repoText = picked;
                    GitPushWatcherSettings.RepoPathOverride = picked;
                    GitPushWatcherService.RefreshPaths();
                }
            }
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.LabelField("留空 = 当前 Unity 项目根（支持 Assets/ 下递归）", _metaStyle);

            EditorGUILayout.Space(4);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("重置 last-seen"))
                GitPushWatcherSettings.ResetSeenState();
            if (GUILayout.Button("清空所有设置"))
            {
                if (EditorUtility.DisplayDialog(
                    "清空 Git 监听设置",
                    "将清空所有 GitWatcher 相关 EditorPrefs。\n是否继续？",
                    "清空",
                    "取消"
                ))
                {
                    GitPushWatcherSettings.ResetAll();
                    _intervalText = "60";
                    _branchText = "";
                    _remoteText = "origin";
                    _repoText = "";
                    _autoDismissText = "30";
                    GitPushWatcherService.RefreshPaths();
                }
            }
            if (GUILayout.Button("测试通知"))
            {
                var fake = new PushEvent
                {
                    Branch = "main",
                    Remote = "origin",
                    RemoteHash = "deadbeef1234567",
                    LocalHash = "cafebabe0000000",
                    CommitCount = 3,
                    DetectedAt = DateTime.Now,
                    Summary = "abc1234 测试通知\ndef5678 Add feature\nghi9012 Fix bug",
                };
                GitPushWatcherNotifier.ShowEvent(fake);
            }
            EditorGUILayout.EndHorizontal();

            EditorGUI.indentLevel--;
            EditorGUILayout.EndVertical();
        }

        // ═══════════════════════════════════════════════════════════════════
        //  通知历史
        // ═══════════════════════════════════════════════════════════════════

        private void DrawHistorySection(float w)
        {
            _historyFoldout = EditorGUILayout.Foldout(
                _historyFoldout,
                $"推送历史 ({GitPushWatcherService.History.Count})",
                true,
                EditorStyles.foldout
            );
            if (!_historyFoldout) return;

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            var history = GitPushWatcherService.History;
            if (history.Count == 0)
            {
                EditorGUILayout.LabelField("暂无推送记录", _metaStyle);
            }
            else
            {
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField("共 " + history.Count + " 条", _metaStyle);
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("清空历史", GUILayout.Width(80)))
                    GitPushWatcherService.ClearHistory();
                EditorGUILayout.EndHorizontal();

                int show = Math.Min(history.Count, 10);
                for (int i = 0; i < show; i++)
                {
                    var e = history[i];
                    DrawHistoryItem(e);
                }
                if (history.Count > show)
                    EditorGUILayout.LabelField(
                        $"... 还有 {history.Count - show} 条更早的记录",
                        _metaStyle
                    );
            }
            EditorGUILayout.EndVertical();
        }

        private void DrawHistoryItem(PushEvent e)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField(
                $"🕐 {e.DetectedAt:yyyy-MM-dd HH:mm:ss}   {e.Remote}/{e.Branch}   +{e.CommitCount} commit",
                EditorStyles.boldLabel
            );
            EditorGUILayout.LabelField($"最新: {e.ShortHash}", _hashStyle);

            if (!string.IsNullOrWhiteSpace(e.Summary))
            {
                var sb = new StringBuilder();
                string[] lines = e.Summary.Split('\n');
                int n = Math.Min(5, lines.Length);
                for (int i = 0; i < n; i++)
                    sb.AppendLine("• " + lines[i].TrimEnd());
                if (lines.Length > n) sb.AppendLine($"... 还有 {lines.Length - n} 条");
                EditorGUILayout.LabelField(sb.ToString(), _metaStyle);
            }

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("重新显示通知", GUILayout.Height(20), GUILayout.Width(110)))
                GitPushWatcherNotifier.ShowEvent(e);
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();
        }

        // ═══════════════════════════════════════════════════════════════════
        //  日志区
        // ═══════════════════════════════════════════════════════════════════

        private void DrawLogSection(float w)
        {
            _logFoldout = EditorGUILayout.Foldout(
                _logFoldout,
                $"操作日志 ({_logs.Count})",
                true,
                EditorStyles.foldout
            );
            if (!_logFoldout) return;

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("最近 100 条", _metaStyle);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("清空", GUILayout.Width(60)))
                _logs.Clear();
            EditorGUILayout.EndHorizontal();

            int n = Math.Min(_logs.Count, 30);
            if (n == 0)
            {
                EditorGUILayout.LabelField("暂无日志", _metaStyle);
            }
            else
            {
                // 用嵌套 helpBox 当背景框；EditorGUILayout 会按内容自动算高度，
                // 避免之前 GetRect + GUILayout.Label 混用导致底色与文字脱节。
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                for (int i = Math.Max(0, _logs.Count - n); i < _logs.Count; i++)
                {
                    var log = _logs[i];
                    GUIStyle st = log.Level == GitPushWatcherService.LogLevel.Error
                        ? _logErrStyle
                        : log.Level == GitPushWatcherService.LogLevel.Warn
                            ? _logWarnStyle
                            : _logInfoStyle;
                    EditorGUILayout.LabelField(
                        $"[{log.Time:HH:mm:ss}] {log.Message}",
                        st
                    );
                }
                EditorGUILayout.EndVertical();
            }
            EditorGUILayout.EndVertical();
        }

        // ═══════════════════════════════════════════════════════════════════
        //  事件回调
        // ═══════════════════════════════════════════════════════════════════

        private void OnTick(GitStatusSnapshot snap)
        {
            // 通过 SceneView.RepaintAll 触发面板重绘
            try { SceneView.RepaintAll(); } catch { }
        }

        private void OnLogMsg(string msg, GitPushWatcherService.LogLevel level)
        {
            _logs.Add(new LogEntry { Time = DateTime.Now, Message = msg, Level = level });
            if (_logs.Count > MAX_LOGS)
                _logs.RemoveAt(0);
        }

        // ═══════════════════════════════════════════════════════════════════
        //  工具
        // ═══════════════════════════════════════════════════════════════════

        private static string ShortHash(string h)
        {
            if (string.IsNullOrEmpty(h)) return "—";
            return h.Length <= 7 ? h : h.Substring(0, 7);
        }

        /// <summary>统一处理「仓库路径」修改：写 EditorPrefs + 刷新缓存 + 同步 UI</summary>
        private void SetRepoOverride(string newPath)
        {
            _repoText = newPath ?? "";
            GitPushWatcherSettings.RepoPathOverride = _repoText;
            GitPushWatcherService.RefreshPaths();
        }

        private void EnsureStyles()
        {
            if (_statusOkStyle != null) return;

            bool dark = EditorGUIUtility.isProSkin;
            _statusOkStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 13,
                normal = { textColor = dark ? new Color(0.3f, 0.95f, 0.45f) : new Color(0.1f, 0.55f, 0.2f) },
            };
            _statusWarnStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 13,
                normal = { textColor = dark ? new Color(1f, 0.75f, 0.3f) : new Color(0.7f, 0.4f, 0.05f) },
            };
            _statusErrStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 13,
                normal = { textColor = dark ? new Color(1f, 0.4f, 0.4f) : new Color(0.75f, 0.15f, 0.15f) },
            };
            _metaStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                fontSize = 11,
                normal = { textColor = dark ? new Color(0.7f, 0.7f, 0.7f) : new Color(0.35f, 0.35f, 0.35f) },
            };
            _hashStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                fontSize = 11,
                fontStyle = FontStyle.Bold,
                normal = { textColor = dark ? new Color(0.6f, 0.85f, 1f) : new Color(0.1f, 0.3f, 0.7f) },
            };
            _logInfoStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                fontSize = 11,
                normal = { textColor = dark ? new Color(0.75f, 0.75f, 0.75f) : new Color(0.2f, 0.2f, 0.2f) },
            };
            _logWarnStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                fontSize = 11,
                normal = { textColor = dark ? new Color(1f, 0.75f, 0.3f) : new Color(0.7f, 0.4f, 0.05f) },
            };
            _logErrStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                fontSize = 11,
                normal = { textColor = dark ? new Color(1f, 0.4f, 0.4f) : new Color(0.75f, 0.15f, 0.15f) },
            };
            _sectionTitleStyle = new GUIStyle(EditorStyles.boldLabel) { fontSize = 12 };
        }
    }
}
