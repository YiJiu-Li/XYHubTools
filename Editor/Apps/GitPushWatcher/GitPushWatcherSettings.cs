// =======================================================================
//  GitPushWatcherSettings — Git 推送监听的设置项（EditorPrefs 持久化）
//  所有键名集中在 PREF_* 常量，便于迁移和清理
// =======================================================================

using System;
using UnityEditor;
using UnityEngine;

namespace Framework.XYEditor.GitPushWatcher
{
    /// <summary>
    /// Git 推送监听的设置项。
    /// 全部使用 EditorPrefs 持久化，与 BridgeSetup 风格保持一致。
    /// 默认全部为安全值（关闭监听），用户需主动开启才会检查远程。
    /// </summary>
    internal static class GitPushWatcherSettings
    {
        // ── EditorPrefs 键名 ─────────────────────────────────────────────
        private const string PREF_ENABLED = "XYGitWatcher_Enabled";
        private const string PREF_INTERVAL = "XYGitWatcher_IntervalSec";
        private const string PREF_BRANCH = "XYGitWatcher_Branch";
        private const string PREF_REMOTE = "XYGitWatcher_Remote";
        private const string PREF_AUTO_FETCH = "XYGitWatcher_AutoFetch";
        private const string PREF_NOTIFY_STYLE = "XYGitWatcher_NotifyStyle";
        private const string PREF_NOTIFY_AUTO_DISMISS = "XYGitWatcher_NotifyAutoDismissSec";
        private const string PREF_REPO_OVERRIDE = "XYGitWatcher_RepoPathOverride";
        private const string PREF_LAST_SEEN_HASH = "XYGitWatcher_AcknowledgedRemoteHash";
        private const string PREF_LAST_SEEN_BRANCH = "XYGitWatcher_AcknowledgedBranch";

        // ── 默认值 ───────────────────────────────────────────────────────
        private const bool DEFAULT_ENABLED = false;
        private const int DEFAULT_INTERVAL_SEC = 60;
        private const string DEFAULT_BRANCH = ""; // 空表示自动检测当前分支
        private const string DEFAULT_REMOTE = "origin";
        private const bool DEFAULT_AUTO_FETCH = true;
        private const NotifyStyle DEFAULT_NOTIFY_STYLE = NotifyStyle.FloatingWindow;
        private const int DEFAULT_AUTO_DISMISS_SEC = 0;

        // ===================================================================
        //  公开属性
        // ===================================================================

        /// <summary>是否启用 Git 推送监听（默认关闭，用户主动开启）</summary>
        public static bool Enabled
        {
            get => EditorPrefs.GetBool(PREF_ENABLED, DEFAULT_ENABLED);
            set => EditorPrefs.SetBool(PREF_ENABLED, value);
        }

        /// <summary>轮询间隔（秒）。范围 [10, 3600]，默认 60</summary>
        public static int IntervalSec
        {
            get
            {
                int v = EditorPrefs.GetInt(PREF_INTERVAL, DEFAULT_INTERVAL_SEC);
                if (v < 10) v = 10;
                if (v > 3600) v = 3600;
                return v;
            }
            set
            {
                if (value < 10) value = 10;
                if (value > 3600) value = 3600;
                EditorPrefs.SetInt(PREF_INTERVAL, value);
            }
        }

        /// <summary>要追踪的分支。空字符串 = 自动检测当前 HEAD 分支</summary>
        public static string Branch
        {
            get => EditorPrefs.GetString(PREF_BRANCH, DEFAULT_BRANCH);
            set => EditorPrefs.SetString(PREF_BRANCH, value ?? "");
        }

        /// <summary>远程名，默认 origin</summary>
        public static string Remote
        {
            get
            {
                string v = EditorPrefs.GetString(PREF_REMOTE, DEFAULT_REMOTE);
                return string.IsNullOrWhiteSpace(v) ? DEFAULT_REMOTE : v;
            }
            set => EditorPrefs.SetString(PREF_REMOTE, string.IsNullOrWhiteSpace(value) ? DEFAULT_REMOTE : value);
        }

        /// <summary>是否启用自动 git fetch（仅 fetch，不 pull）。关闭后只能手动触发。</summary>
        public static bool AutoFetch
        {
            get => EditorPrefs.GetBool(PREF_AUTO_FETCH, DEFAULT_AUTO_FETCH);
            set => EditorPrefs.SetBool(PREF_AUTO_FETCH, value);
        }

        /// <summary>通知展示方式</summary>
        public static NotifyStyle NotifyStyle
        {
            get
            {
                int v = EditorPrefs.GetInt(PREF_NOTIFY_STYLE, (int)DEFAULT_NOTIFY_STYLE);
                if (v < 0 || v >= Enum.GetValues(typeof(NotifyStyle)).Length) v = (int)DEFAULT_NOTIFY_STYLE;
                return (NotifyStyle)v;
            }
            set => EditorPrefs.SetInt(PREF_NOTIFY_STYLE, (int)value);
        }

        /// <summary>通知自动消失秒数，0 表示不自动消失</summary>
        public static int NotifyAutoDismissSec
        {
            get => EditorPrefs.GetInt(PREF_NOTIFY_AUTO_DISMISS, DEFAULT_AUTO_DISMISS_SEC);
            set => EditorPrefs.SetInt(PREF_NOTIFY_AUTO_DISMISS, Math.Max(0, value));
        }

        /// <summary>仓库路径覆盖（高级）。空 = 使用 Application.dataPath 上一级目录</summary>
        public static string RepoPathOverride
        {
            get => EditorPrefs.GetString(PREF_REPO_OVERRIDE, "");
            set => EditorPrefs.SetString(PREF_REPO_OVERRIDE, value ?? "");
        }

        // ── 状态持久化（避免重复通知同一推送）──────────────────────

        /// <summary>最近一次通知时记录的远程 HEAD hash</summary>
        public static string LastSeenRemoteHash
        {
            get => EditorPrefs.GetString(PREF_LAST_SEEN_HASH, "");
            set => EditorPrefs.SetString(PREF_LAST_SEEN_HASH, value ?? "");
        }

        /// <summary>最近一次通知时记录的分支（切换分支时重置）</summary>
        public static string LastSeenBranch
        {
            get => EditorPrefs.GetString(PREF_LAST_SEEN_BRANCH, "");
            set => EditorPrefs.SetString(PREF_LAST_SEEN_BRANCH, value ?? "");
        }

        // ===================================================================
        //  工具方法
        // ===================================================================

        /// <summary>当分支切换时调用，清空 last seen 状态</summary>
        public static void ResetSeenState()
        {
            LastSeenRemoteHash = "";
            LastSeenBranch = "";
        }

        /// <summary>清除所有 GitWatcher 相关 EditorPrefs</summary>
        public static void ResetAll()
        {
            EditorPrefs.DeleteKey(PREF_ENABLED);
            EditorPrefs.DeleteKey(PREF_INTERVAL);
            EditorPrefs.DeleteKey(PREF_BRANCH);
            EditorPrefs.DeleteKey(PREF_REMOTE);
            EditorPrefs.DeleteKey(PREF_AUTO_FETCH);
            EditorPrefs.DeleteKey(PREF_NOTIFY_STYLE);
            EditorPrefs.DeleteKey(PREF_NOTIFY_AUTO_DISMISS);
            EditorPrefs.DeleteKey(PREF_REPO_OVERRIDE);
            EditorPrefs.DeleteKey(PREF_LAST_SEEN_HASH);
            EditorPrefs.DeleteKey(PREF_LAST_SEEN_BRANCH);
        }
    }

    /// <summary>通知展示方式</summary>
    public enum NotifyStyle
    {
        /// <summary>右上角可拖动小窗（非模态）</summary>
        FloatingWindow = 0,
        /// <summary>只在面板内日志区记录，不弹窗</summary>
        LogOnly = 1,
    }
}
