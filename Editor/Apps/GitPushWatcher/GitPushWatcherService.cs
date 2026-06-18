// =======================================================================
//  GitPushWatcherService — 后台轮询 + git 命令执行
//  通过 EditorApplication.update 驱动定时器；通过 FileSystemWatcher
//  监听 .git 目录变化（其它工具 fetch 时可即时响应）。
//  所有 git 命令都用 Process 异步执行，CreateNoWindow=true 不闪控制台。
// =======================================================================

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Framework.XYEditor.GitPushWatcher
{
    /// <summary>
    /// Git 推送监听服务。单例，整个 Editor 会话共享。
    /// 事件驱动模型：
    ///   OnTick          — 每次 fetch / 检查完成后触发
    ///   OnPushDetected  — 发现新推送时触发
    ///   OnLog           — 日志消息（用于面板显示）
    /// </summary>
    public static class GitPushWatcherService
    {
        // ── 事件 ──────────────────────────────────────────────────────────
        public static event Action<GitStatusSnapshot> OnTick;
        public static event Action<PushEvent> OnPushDetected;
        public static event Action<string, LogLevel> OnLog;

        public enum LogLevel { Info, Warn, Error }

        // ── 状态 ──────────────────────────────────────────────────────────
        private static double s_nextCheckTime;
        private static bool s_isFetching;
        private static FileSystemWatcher s_fsWatcher;
        private static string s_watchedGitDir;
        private static readonly object s_lock = new object();
        private static readonly Queue<Action> s_mainThreadActions = new Queue<Action>();

        public static GitStatusSnapshot LastSnapshot { get; private set; }
        public static string LastError { get; private set; }
        public static DateTime? LastFetchTime { get; private set; }
        public static bool IsBusy => s_isFetching;

        // 通知历史（最近 50 条）
        private const int MAX_HISTORY = 50;
        private static readonly List<PushEvent> s_history = new List<PushEvent>();
        public static IReadOnlyList<PushEvent> History => s_history;

        // ── 解析缓存 ──────────────────────────────────────────────────────
        private static string s_cachedRepoRoot = "";
        private static string s_cachedGitDir = "";

        /// <summary>解析后的仓库根目录（可能为空字符串表示未找到）</summary>
        public static string ResolvedRepoRoot
        {
            get { EnsurePathsResolved(); return s_cachedRepoRoot ?? ""; }
        }

        /// <summary>解析后的 .git 目录 / .git 文件路径（空字符串表示未找到）</summary>
        public static string ResolvedGitDir
        {
            get { EnsurePathsResolved(); return s_cachedGitDir ?? ""; }
        }

        /// <summary>手动触发一次路径解析并刷新缓存。修改 override 后调用。</summary>
        public static void RefreshPaths()
        {
            s_cachedRepoRoot = "";
            s_cachedGitDir = "";
            ResetFileSystemWatcher();
            EnsurePathsResolved();
            TryAttachFileSystemWatcher();
        }

        // ── 初始化 / 反初始化 ────────────────────────────────────────────
        [InitializeOnLoadMethod]
        private static void Init()
        {
            EditorApplication.update -= OnEditorUpdate;
            EditorApplication.update += OnEditorUpdate;
            s_nextCheckTime = EditorApplication.timeSinceStartup + 3.0;

            TryAttachFileSystemWatcher();

            EditorApplication.quitting -= OnQuitting;
            EditorApplication.quitting += OnQuitting;
        }

        private static void OnQuitting()
        {
            try
            {
                s_fsWatcher?.Dispose();
                s_fsWatcher = null;
            }
            catch { }
        }

        // ── 主循环 ────────────────────────────────────────────────────────
        private static void OnEditorUpdate()
        {
            DrainMainThreadActions();

            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
                return;

            if (!GitPushWatcherSettings.Enabled)
                return;

            if (!s_isFetching && EditorApplication.timeSinceStartup >= s_nextCheckTime)
            {
                int interval = GitPushWatcherSettings.IntervalSec;
                s_nextCheckTime = EditorApplication.timeSinceStartup + interval;
                if (GitPushWatcherSettings.AutoFetch)
                    FetchAsync();
                else
                    CheckAsync();
            }
        }

        // ── FileSystemWatcher ─────────────────────────────────────────────
        private static void TryAttachFileSystemWatcher()
        {
            try
            {
                // 触发路径解析（用户可能在 domain reload 前刚修改了 override）
                EnsurePathsResolved();
                string gitDir = s_cachedGitDir;
                if (string.IsNullOrEmpty(gitDir))
                {
                    ResetFileSystemWatcher();
                    return;
                }

                // Worktree / submodule 场景下 .git 可能是一个文件。
                // FileSystemWatcher 只能监听目录，这里跳过 watcher，轮询仍然可用。
                if (File.Exists(gitDir))
                {
                    ResetFileSystemWatcher();
                    Log(".git 是文件，已跳过 FileSystemWatcher；定时 fetch 仍可正常工作。", LogLevel.Info);
                    return;
                }

                if (s_fsWatcher != null && s_watchedGitDir == gitDir)
                    return;

                ResetFileSystemWatcher();

                s_fsWatcher = new FileSystemWatcher(gitDir)
                {
                    IncludeSubdirectories = true,
                    NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.DirectoryName,
                    InternalBufferSize = 8192,
                };
                s_fsWatcher.Changed += OnGitDirChanged;
                s_fsWatcher.Created += OnGitDirChanged;
                s_fsWatcher.Renamed += OnGitDirChanged;
                s_fsWatcher.EnableRaisingEvents = true;
                s_watchedGitDir = gitDir;
                Log("已挂载 FileSystemWatcher: " + gitDir, LogLevel.Info);
            }
            catch (Exception ex)
            {
                Log("FileSystemWatcher 挂载失败: " + ex.Message, LogLevel.Warn);
            }
        }

        private static void ResetFileSystemWatcher()
        {
            try
            {
                s_fsWatcher?.Dispose();
            }
            catch { }
            s_fsWatcher = null;
            s_watchedGitDir = "";
        }

        private static void OnGitDirChanged(object sender, FileSystemEventArgs e)
        {
            string name = e.Name ?? "";
            if (name.Replace('\\', '/').Contains("refs/") || name.Contains("FETCH_HEAD"))
            {
                if (!s_isFetching && GitPushWatcherSettings.Enabled)
                {
                    s_nextCheckTime = EditorApplication.timeSinceStartup;
                }
            }
        }

        // ── 公开 API ──────────────────────────────────────────────────────

        /// <summary>立即执行 git fetch（手动触发）</summary>
        public static void FetchAsync()
        {
            if (s_isFetching) return;
            EnsurePathsResolved();
            string gitDir = s_cachedGitDir;
            string repoRoot = s_cachedRepoRoot;
            if (string.IsNullOrEmpty(gitDir))
            {
                LastError = "找不到 .git 目录。请在设置里指定仓库路径。";
                EmitTick();
                return;
            }

            string branch = ResolveBranch();
            string remote = GitPushWatcherSettings.Remote;

            s_isFetching = true;
            Log($"开始 git fetch {remote} --prune ...", LogLevel.Info);

            ThreadPool.QueueUserWorkItem(_ =>
            {
                GitCommandResult fetchResult = RunGit(
                    repoRoot,
                    $"fetch {QuoteArg(remote)} --prune"
                );

                EnqueueMainThread(() =>
                {
                    s_isFetching = false;
                    LastFetchTime = DateTime.Now;
                    if (!fetchResult.Ok)
                    {
                        LastError = fetchResult.Stderr ?? fetchResult.Stdout;
                        Log($"git fetch 失败: {LastError}", LogLevel.Error);
                    }
                    else
                    {
                        LastError = null;
                        Log("git fetch 完成", LogLevel.Info);
                    }
                    DetectPush(repoRoot, branch, remote);
                });
            });
        }

        /// <summary>不执行 fetch，仅读取当前 remote ref 并比对</summary>
        public static void CheckAsync()
        {
            EnsurePathsResolved();
            string repoRoot = s_cachedRepoRoot;
            string gitDir = s_cachedGitDir;
            if (string.IsNullOrEmpty(gitDir) || string.IsNullOrEmpty(repoRoot))
            {
                LastError = "找不到 .git 目录。请在设置里指定仓库路径。";
                EmitTick();
                return;
            }

            string branch = ResolveBranch();
            string remote = GitPushWatcherSettings.Remote;

            ThreadPool.QueueUserWorkItem(_ =>
            {
                EnqueueMainThread(() =>
                {
                    DetectPush(repoRoot, branch, remote);
                });
            });
        }

        // ── 检测推送核心逻辑 ──────────────────────────────────────────────
        private static void DetectPush(string repoRoot, string branch, string remote)
        {
            var remoteHashResult = RunGit(repoRoot, $"rev-parse {QuoteArg(remote)}/{QuoteArg(branch)}");
            string remoteHash = remoteHashResult.Ok ? (remoteHashResult.Stdout ?? "").Trim() : "";

            var localHashResult = RunGit(repoRoot, "rev-parse HEAD");
            string localHash = localHashResult.Ok ? (localHashResult.Stdout ?? "").Trim() : "";

            int ahead = 0, behind = 0;
            string logSummary = "";
            if (!string.IsNullOrEmpty(remoteHash) && !string.IsNullOrEmpty(localHash))
            {
                var countResult = RunGit(
                    repoRoot,
                    $"rev-list --left-right --count {QuoteArg(localHash)}...{QuoteArg(remoteHash)}"
                );
                if (countResult.Ok && !string.IsNullOrWhiteSpace(countResult.Stdout))
                {
                    string[] parts = (countResult.Stdout ?? "").Trim().Split('\t');
                    if (parts.Length == 2)
                    {
                        int.TryParse(parts[0], out ahead);
                        int.TryParse(parts[1], out behind);
                    }
                }

                if (behind > 0)
                {
                    var logResult = RunGit(
                        repoRoot,
                        $"log --oneline -10 {QuoteArg(localHash)}..{QuoteArg(remoteHash)}"
                    );
                    if (logResult.Ok)
                        logSummary = (logResult.Stdout ?? "").Trim();
                }
            }

            string lastHash = GitPushWatcherSettings.LastSeenRemoteHash;
            string lastBranch = GitPushWatcherSettings.LastSeenBranch;
            bool branchChanged = !string.Equals(lastBranch, branch, StringComparison.Ordinal);
            bool isNewPush =
                behind > 0
                && (
                    (!string.IsNullOrEmpty(remoteHash) && remoteHash != lastHash)
                    || branchChanged
                );

            var snapshot = new GitStatusSnapshot
            {
                Branch = branch,
                Remote = remote,
                LocalHash = localHash,
                RemoteHash = remoteHash,
                Ahead = ahead,
                Behind = behind,
                LastFetchTime = LastFetchTime,
                IsRepo = !string.IsNullOrEmpty(repoRoot),
                NewCommitsSummary = logSummary,
            };
            LastSnapshot = snapshot;
            EmitTick();

            if (isNewPush)
            {
                var evt = new PushEvent
                {
                    Branch = branch,
                    Remote = remote,
                    RemoteHash = remoteHash,
                    LocalHash = localHash,
                    CommitCount = behind,
                    DetectedAt = DateTime.Now,
                    Summary = logSummary,
                };
                lock (s_lock)
                {
                    s_history.Insert(0, evt);
                    if (s_history.Count > MAX_HISTORY)
                        s_history.RemoveAt(s_history.Count - 1);
                }
                GitPushWatcherSettings.LastSeenRemoteHash = remoteHash;
                GitPushWatcherSettings.LastSeenBranch = branch;
                Log($"检测到 {behind} 个新 commit: {branch}", LogLevel.Info);
                OnPushDetected?.Invoke(evt);
            }
        }

        private static void EmitTick()
        {
            try { OnTick?.Invoke(LastSnapshot); } catch { }
        }

        // ═══════════════════════════════════════════════════════════════════
        //  路径解析（核心：自动检测 + 递归找 Assets/ 下的 .git）
        // ═══════════════════════════════════════════════════════════════════

        private static void EnsurePathsResolved()
        {
            // 缓存命中
            if (!string.IsNullOrEmpty(s_cachedGitDir))
                return;
            DoResolvePaths();
        }

        private static void DoResolvePaths()
        {
            s_cachedRepoRoot = "";
            s_cachedGitDir = "";

            // 1) 优先用用户 override
            string overridePath = GitPushWatcherSettings.RepoPathOverride;
            if (!string.IsNullOrWhiteSpace(overridePath))
            {
                if (Directory.Exists(overridePath))
                {
                    if (TryGetGitDir(overridePath, out string g))
                    {
                        s_cachedRepoRoot = overridePath;
                        s_cachedGitDir = g;
                    }
                    else if (Path.GetFileName(overridePath).Equals(".git", StringComparison.Ordinal))
                    {
                        // 用户直接指定了 .git 目录
                        s_cachedRepoRoot = Path.GetDirectoryName(overridePath) ?? "";
                        s_cachedGitDir = overridePath;
                    }
                    else
                    {
                        // 路径有效但找不到 .git，也记录 repoRoot 让 UI 显示
                        s_cachedRepoRoot = overridePath;
                    }
                }
                return;
            }

            // 2) 自动检测
            string projectRoot = Path.GetDirectoryName(Application.dataPath);
            if (string.IsNullOrEmpty(projectRoot))
                return;

            // 2.1) project root 本身
            if (TryGetGitDir(projectRoot, out string gitDir))
            {
                s_cachedRepoRoot = projectRoot;
                s_cachedGitDir = gitDir;
                return;
            }

            // 2.2) 向上 5 层（嵌套项目）
            string dir = projectRoot;
            for (int i = 0; i < 5; i++)
            {
                var parent = Directory.GetParent(dir);
                if (parent == null) break;
                dir = parent.FullName;
                if (TryGetGitDir(dir, out gitDir))
                {
                    s_cachedRepoRoot = dir;
                    s_cachedGitDir = gitDir;
                    return;
                }
            }

            // 2.3) Assets/ 下递归（深度 3，跳过 Unity 噪声目录）
            //      关键修复：兼容 Assets/XYHubTools/.git 这类带独立 .git 的子包
            string assetsPath = Application.dataPath;
            if (Directory.Exists(assetsPath))
            {
                gitDir = FindGitDirRecursive(assetsPath, 3, out string foundRoot);
                if (!string.IsNullOrEmpty(gitDir))
                {
                    s_cachedRepoRoot = foundRoot;
                    s_cachedGitDir = gitDir;
                }
            }
        }

        private static bool TryGetGitDir(string dir, out string gitDir)
        {
            gitDir = "";
            try
            {
                string candidate = Path.Combine(dir, ".git");
                if (Directory.Exists(candidate))
                {
                    gitDir = candidate;
                    return true;
                }
                if (File.Exists(candidate))
                {
                    // worktree：.git 是文件，指向真正的 git 目录
                    gitDir = candidate;
                    return true;
                }
            }
            catch { }
            return false;
        }

        private static string FindGitDirRecursive(string dir, int maxDepth, out string repoRoot)
        {
            repoRoot = "";
            if (maxDepth <= 0) return "";
            try
            {
                foreach (var sub in Directory.EnumerateDirectories(dir))
                {
                    string name = Path.GetFileName(sub);
                    if (string.IsNullOrEmpty(name)) continue;
                    if (name.Equals(".git", StringComparison.Ordinal))
                    {
                        repoRoot = Path.GetDirectoryName(sub) ?? "";
                        return sub;
                    }
                    if (name.StartsWith(".")) continue;
                    // 跳过常见 Unity / 构建噪声目录
                    if (name == "Library" || name == "Temp" || name == "obj" || name == "Build"
                        || name == "Logs" || name == "UserSettings" || name == "Packages"
                        || name == "ProjectSettings" || name == "node_modules" || name == "Pods"
                        || name == "DerivedDataCache" || name == "Builds" || name == "dist"
                        || name == "build" || name == "out")
                        continue;

                    string found = FindGitDirRecursive(sub, maxDepth - 1, out string foundRoot);
                    if (!string.IsNullOrEmpty(found))
                    {
                        repoRoot = foundRoot;
                        return found;
                    }
                }
            }
            catch { }
            return "";
        }

        private static string ResolveBranch()
        {
            string configured = GitPushWatcherSettings.Branch;
            if (!string.IsNullOrWhiteSpace(configured))
                return configured;

            string repoRoot = s_cachedRepoRoot;
            if (string.IsNullOrEmpty(repoRoot))
            {
                EnsurePathsResolved();
                repoRoot = s_cachedRepoRoot;
            }
            if (string.IsNullOrEmpty(repoRoot)) return "";

            var r = RunGit(repoRoot, "symbolic-ref --short HEAD");
            if (r.Ok && !string.IsNullOrWhiteSpace(r.Stdout))
                return (r.Stdout ?? "").Trim();

            r = RunGit(repoRoot, "rev-parse --abbrev-ref HEAD");
            if (r.Ok && !string.IsNullOrWhiteSpace(r.Stdout))
            {
                string b = (r.Stdout ?? "").Trim();
                if (b != "HEAD") return b;
            }
            return "main";
        }

        // ── Git 命令底层封装 ─────────────────────────────────────────────
        private static GitCommandResult RunGit(string workingDir, string args)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "git",
                    Arguments = args,
                    WorkingDirectory = workingDir,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8,
                };
                using (var proc = Process.Start(psi))
                {
                    if (proc == null)
                        return new GitCommandResult { Ok = false, Stderr = "无法启动 git 进程" };

                    string stdout = "";
                    string stderr = "";
                    var stdoutThread = new Thread(() => stdout = proc.StandardOutput.ReadToEnd());
                    var stderrThread = new Thread(() => stderr = proc.StandardError.ReadToEnd());
                    stdoutThread.Start();
                    stderrThread.Start();

                    if (!proc.WaitForExit(15000))
                    {
                        try { proc.Kill(); } catch { }
                        return new GitCommandResult { Ok = false, Stderr = "git 命令超时（15s）" };
                    }

                    stdoutThread.Join(1000);
                    stderrThread.Join(1000);
                    return new GitCommandResult
                    {
                        Ok = proc.ExitCode == 0,
                        ExitCode = proc.ExitCode,
                        Stdout = stdout,
                        Stderr = stderr,
                    };
                }
            }
            catch (Exception ex)
            {
                return new GitCommandResult { Ok = false, Stderr = ex.Message };
            }
        }

        private static string QuoteArg(string s)
        {
            if (string.IsNullOrEmpty(s)) return "\"\"";
            if (s.IndexOfAny(new[] { ' ', '\t', '"', '\\' }) < 0) return s;
            return "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
        }

        // ── 日志转发 ──────────────────────────────────────────────────────
        private static void Log(string msg, LogLevel level)
        {
            try { OnLog?.Invoke(msg, level); } catch { }
            switch (level)
            {
                case LogLevel.Error:
                    Debug.LogError("[GitWatcher] " + msg);
                    break;
                case LogLevel.Warn:
                    Debug.LogWarning("[GitWatcher] " + msg);
                    break;
                default:
                    Debug.Log("[GitWatcher] " + msg);
                    break;
            }
        }

        private static void EnqueueMainThread(Action action)
        {
            if (action == null) return;
            lock (s_mainThreadActions)
            {
                s_mainThreadActions.Enqueue(action);
            }
        }

        private static void DrainMainThreadActions()
        {
            while (true)
            {
                Action action = null;
                lock (s_mainThreadActions)
                {
                    if (s_mainThreadActions.Count == 0)
                        break;
                    action = s_mainThreadActions.Dequeue();
                }

                try { action?.Invoke(); }
                catch (Exception ex) { Debug.LogException(ex); }
            }
        }

        /// <summary>清空通知历史</summary>
        public static void ClearHistory()
        {
            lock (s_lock) { s_history.Clear(); }
        }
    }

    /// <summary>一次 git 命令的结果</summary>
    public class GitCommandResult
    {
        public bool Ok;
        public int ExitCode;
        public string Stdout;
        public string Stderr;
    }

    /// <summary>当前仓库状态快照</summary>
    public class GitStatusSnapshot
    {
        public string Branch;
        public string Remote;
        public string LocalHash;
        public string RemoteHash;
        public int Ahead;
        public int Behind;
        public DateTime? LastFetchTime;
        public bool IsRepo = true;
        public string NewCommitsSummary;

        public bool IsAhead => Ahead > 0;
        public bool IsBehind => Behind > 0;
        public bool InSync => Ahead == 0 && Behind == 0 && !string.IsNullOrEmpty(LocalHash);
    }

    /// <summary>一次推送事件</summary>
    public class PushEvent
    {
        public string Branch;
        public string Remote;
        public string RemoteHash;
        public string LocalHash;
        public int CommitCount;
        public DateTime DetectedAt;
        public string Summary;

        public string ShortHash => string.IsNullOrEmpty(RemoteHash) ? "" : RemoteHash.Substring(0, Math.Min(7, RemoteHash.Length));
    }
}
