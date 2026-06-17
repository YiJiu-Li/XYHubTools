// ═══════════════════════════════════════════════════════════════
//  EventBus — Unity 主动事件轻量订阅/拉取管道
//
//  用法：
//    1. AI 调 unity_subscribe(message="console,compile,scene") 启动订阅
//    2. AI 调 unity_poll_events(message="timeout_ms") 拉取事件
//    3. 事件可累积在内存，最多 200 条；超时或数量满则自动停止
// ═══════════════════════════════════════════════════════════════

using System;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Framework.XYEditor.Bridge
{
    internal static class EventBus
    {
        // ── 事件类型枚举（与 message 字符串一一对应） ──
        public const string EVT_CONSOLE = "console"; // ConsoleService 推过来的新日志
        public const string EVT_COMPILE = "compile"; // 编译完成/失败
        public const string EVT_SCENE = "scene"; // 场景打开/保存/新建

        // ── 订阅位 ──
        [Flags]
        public enum Sub
        {
            None = 0,
            Console = 1 << 0,
            Compile = 1 << 1,
            Scene = 1 << 2,
            All = Console | Compile | Scene,
        }

        // ── 状态 ──
        private static Sub _sub = Sub.None;
        private static readonly object _lock = new object();
        private static readonly Queue<EventItem> _queue = new Queue<EventItem>(64);
        private const int MaxQueue = 200;
        private static bool _hooked;

        [Serializable]
        public class EventItem
        {
            public string type; // console / compile / scene
            public string level; // info/warn/error for console
            public string message;
            public long ts_ms; // unix ms
        }

        public static void EnsureHooked()
        {
            if (_hooked)
                return;
            _hooked = true;
            ConsoleService.EnsureHooked();
            ConsoleService.OnNewEntry += OnConsoleEntry;
            CompilationPipeline.compilationFinished += OnCompileFinished;
            EditorSceneManager.sceneOpened += OnSceneOpened;
            EditorSceneManager.sceneSaved += OnSceneSaved;
            EditorSceneManager.newSceneCreated += OnNewScene;
        }

        // ── 订阅控制 ──
        public static string Subscribe(string typesCsv)
        {
            EnsureHooked();
            lock (_lock)
            {
                _sub = Sub.None;
                if (
                    string.IsNullOrWhiteSpace(typesCsv)
                    || typesCsv.Trim().ToLowerInvariant() == "all"
                )
                {
                    _sub = Sub.All;
                }
                else
                {
                    foreach (
                        var part in typesCsv.Split(
                            new[] { ',', ' ', ';' },
                            StringSplitOptions.RemoveEmptyEntries
                        )
                    )
                    {
                        var p = part.Trim().ToLowerInvariant();
                        if (p == "console")
                            _sub |= Sub.Console;
                        else if (p == "compile")
                            _sub |= Sub.Compile;
                        else if (p == "scene")
                            _sub |= Sub.Scene;
                    }
                }
                _queue.Clear();
            }
            return _sub.ToString();
        }

        public static string Unsubscribe()
        {
            lock (_lock)
            {
                _sub = Sub.None;
                _queue.Clear();
            }
            return "None";
        }

        public static string GetStatus()
        {
            lock (_lock)
            {
                return $"sub={_sub} queue={_queue.Count}/{MaxQueue}";
            }
        }

        // ── 拉取（带可选超时毫秒） ──
        public static string Poll(int timeoutMs, int maxItems)
        {
            EnsureHooked();
            if (maxItems <= 0 || maxItems > MaxQueue)
                maxItems = 50;
            if (timeoutMs < 0)
                timeoutMs = 0;
            if (timeoutMs > 5000)
                timeoutMs = 5000; // 硬上限，避免卡 MCP

            int waited = 0;
            const int slice = 50;
            while (waited <= timeoutMs)
            {
                lock (_lock)
                {
                    if (_sub == Sub.None)
                        return "{\"events\":[],\"sub\":\"None\",\"hint\":\"not subscribed\"}";
                    if (_queue.Count > 0)
                        break;
                }
                if (timeoutMs == 0)
                    break;
                System.Threading.Thread.Sleep(slice);
                waited += slice;
            }

            var sb = new StringBuilder(2048);
            sb.Append("{\"sub\":\"").Append(_sub.ToString()).Append("\",\"events\":[");
            int n = 0;
            lock (_lock)
            {
                while (_queue.Count > 0 && n < maxItems)
                {
                    var e = _queue.Dequeue();
                    if (n > 0)
                        sb.Append(',');
                    sb.Append("{\"type\":\"")
                        .Append(Escape(e.type))
                        .Append("\",\"level\":\"")
                        .Append(Escape(e.level ?? ""))
                        .Append("\",\"ts\":")
                        .Append(e.ts_ms)
                        .Append(",\"message\":\"")
                        .Append(Escape(e.message ?? ""))
                        .Append("\"}");
                    n++;
                }
            }
            sb.Append("],\"returned\":").Append(n).Append("}");
            return sb.ToString();
        }

        // ── 事件源 ──
        private static void OnConsoleEntry(ConsoleService.LogEntry e)
        {
            if ((_sub & Sub.Console) == 0)
                return;
            Enqueue(
                new EventItem
                {
                    type = EVT_CONSOLE,
                    level = e.type?.ToLowerInvariant() ?? "info",
                    message = e.message,
                    ts_ms = NowMs(),
                }
            );
        }

        private static void OnCompileFinished(object result)
        {
            if ((_sub & Sub.Compile) == 0)
                return;
            string summary;
            try
            {
                // result 是 CompilationResults；用反射取 messages 数
                var t = result.GetType();
                var msgCount = t.GetProperty("messageCount")?.GetValue(result);
                var errors = t.GetProperty("errorCount")?.GetValue(result);
                summary = $"compile finished: {msgCount} msgs, {errors} errors";
            }
            catch
            {
                summary = "compile finished";
            }

            Enqueue(
                new EventItem
                {
                    type = EVT_COMPILE,
                    level = "info",
                    message = summary,
                    ts_ms = NowMs(),
                }
            );
        }

        private static void OnSceneOpened(
            UnityEngine.SceneManagement.Scene scene,
            UnityEditor.SceneManagement.OpenSceneMode mode
        )
        {
            if ((_sub & Sub.Scene) == 0)
                return;
            Enqueue(
                new EventItem
                {
                    type = EVT_SCENE,
                    level = "info",
                    message = "opened: " + (scene.path ?? scene.name),
                    ts_ms = NowMs(),
                }
            );
        }

        private static void OnSceneSaved(UnityEngine.SceneManagement.Scene scene)
        {
            if ((_sub & Sub.Scene) == 0)
                return;
            Enqueue(
                new EventItem
                {
                    type = EVT_SCENE,
                    level = "info",
                    message = "saved: " + (scene.path ?? scene.name),
                    ts_ms = NowMs(),
                }
            );
        }

        private static void OnNewScene(
            UnityEngine.SceneManagement.Scene scene,
            UnityEditor.SceneManagement.NewSceneSetup setup,
            UnityEditor.SceneManagement.NewSceneMode mode
        )
        {
            if ((_sub & Sub.Scene) == 0)
                return;
            Enqueue(
                new EventItem
                {
                    type = EVT_SCENE,
                    level = "info",
                    message = "new scene: " + scene.name,
                    ts_ms = NowMs(),
                }
            );
        }

        // ── 工具 ──
        private static void Enqueue(EventItem e)
        {
            lock (_lock)
            {
                if (_sub == Sub.None)
                    return; // 无人订阅
                if (_queue.Count >= MaxQueue)
                    _queue.Dequeue();
                _queue.Enqueue(e);
            }
        }

        private static long NowMs()
        {
            return (long)
                (
                    DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)
                ).TotalMilliseconds;
        }

        private static string Escape(string s)
        {
            if (string.IsNullOrEmpty(s))
                return "";
            return s.Replace("\\", "\\\\")
                .Replace("\"", "\\\"")
                .Replace("\n", "\\n")
                .Replace("\r", "\\r");
        }
    }
}
