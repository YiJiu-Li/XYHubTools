// ═══════════════════════════════════════════════════════════════
//  ConsoleService — Unity Console 日志收集
// ═══════════════════════════════════════════════════════════════

using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace Framework.XYEditor.Bridge
{
    internal static class ConsoleService
    {
        private const int MaxLogEntries = 500;

        private static readonly List<LogEntry> _entries = new List<LogEntry>(256);
        private static readonly object _lock = new object();
        private static bool _hooked;

        /// <summary>新日志到达事件（外部订阅用）</summary>
        internal static event Action<LogEntry> OnNewEntry;

        [Serializable]
        internal class LogEntry
        {
            public string type;
            public string message;
            public string stackTrace;
            public string timestamp;
        }

        [Serializable]
        internal class LogsResult
        {
            public int total;
            public int returned;
            public LogEntry[] entries;
        }

        internal static void EnsureHooked()
        {
            if (_hooked)
                return;
            _hooked = true;
            Application.logMessageReceived += OnLogMessageReceived;
        }

        private static void OnLogMessageReceived(string condition, string stackTrace, LogType type)
        {
            LogEntry e;
            lock (_lock)
            {
                if (_entries.Count >= MaxLogEntries)
                    _entries.RemoveAt(0);
                e = new LogEntry
                {
                    type = type.ToString(),
                    message = condition,
                    stackTrace = string.IsNullOrEmpty(stackTrace) ? null : stackTrace,
                    timestamp = DateTime.Now.ToString("HH:mm:ss.fff"),
                };
                _entries.Add(e);
            }
            // 事件回调在锁外触发，避免订阅者内部再回调 ConsoleService 死锁
            try
            {
                OnNewEntry?.Invoke(e);
            }
            catch
            { /* 订阅者异常不影响主流程 */
            }
        }

        internal static string GetRecent(int count)
        {
            if (count <= 0)
                count = 50;
            lock (_lock)
            {
                int start = Math.Max(0, _entries.Count - count);
                int actual = Math.Min(count, _entries.Count);
                var result = new LogsResult
                {
                    total = _entries.Count,
                    returned = actual,
                    entries = new LogEntry[actual],
                };
                for (int i = 0; i < actual; i++)
                    result.entries[i] = _entries[start + i];
                return JsonUtility.ToJson(result);
            }
        }

        [Serializable]
        private class ClearResult
        {
            public int cleared;
        }

        internal static string Clear()
        {
            lock (_lock)
            {
                int cleared = _entries.Count;
                _entries.Clear();
                return JsonUtility.ToJson(new ClearResult { cleared = cleared });
            }
        }
    }
}
