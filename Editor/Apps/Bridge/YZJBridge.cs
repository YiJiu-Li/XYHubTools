// ═══════════════════════════════════════════════════════════════════════
//  YZJBridge — Unity Editor Named Pipe 桥接（顶层协调器）
//  ══════════════════════════════════════════════════════════════════════
//  功能说明：
//    1. Named Pipe Server — 委托给 PipeServer
//    2. execute_code      — 委托给 CodeExecutionService + SnippetCompiler
//    3. 消息路由          — 委托给 MessageDispatcher
//    4. 主线程队列        — PostToMainThread / PumpMainThreadQueue
//
//  启动方式：Unity Editor 加载时自动启动（[InitializeOnLoad]）
//  管道名：  xybridge_unity_ + 项目路径（特殊字符替换为 _）
// ═══════════════════════════════════════════════════════════════════════

using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Framework.XYEditor.Bridge
{
    [InitializeOnLoad]
    public static class YZJBridge
    {
        // ───────────────── 常量 ─────────────────
        private const int MaxMainThreadActionsPerUpdate = 32;

        // ───────────────── 主线程队列 ─────────────────
        private static readonly object _mainThreadQueueLock = new object();
        private static readonly Queue<Action> _mainThreadQueue = new Queue<Action>(64);

        // ───────────────── 编辑器状态（供 MessageDispatcher 读取）─────────────────
        internal static volatile bool IsPlaying;
        internal static volatile bool IsPaused;
        internal static volatile string ActiveScenePath = "";

        // ═══════════════════════════════════════════════════════════════
        //  生命周期
        // ═══════════════════════════════════════════════════════════════

        static YZJBridge()
        {
            ConsoleService.EnsureHooked();
            EditorApplication.update += PumpMainThreadQueue;
            EditorApplication.delayCall += Start;
            AssemblyReloadEvents.beforeAssemblyReload += Stop;
        }

        public static void Start() => PipeServer.Start();

        public static void Stop()
        {
            CodeExecutionService.CancelActive("bridge stopped");
            PipeServer.Stop();
            ClearMainThreadQueue();
        }

        // ═══════════════════════════════════════════════════════════════
        //  公开状态（供 BridgeStatusTool 面板读取）
        // ═══════════════════════════════════════════════════════════════

        public static bool IsPipeServerRunning => PipeServer.IsRunning;

        public static bool IsPipeConnected => PipeServer.IsConnected;

        public static string PipeDisplayName => PipeServer.PipeName;

        // ═══════════════════════════════════════════════════════════════
        //  主线程泵
        // ═══════════════════════════════════════════════════════════════

        private static void PumpMainThreadQueue()
        {
            RefreshCachedEditorState();
            if (CodeExecutionService.ActiveAsyncCount > 0)
                CodeExecutionService.PumpRuntime();

            for (int i = 0; i < MaxMainThreadActionsPerUpdate; i++)
            {
                Action action = null;
                lock (_mainThreadQueueLock)
                {
                    if (_mainThreadQueue.Count > 0)
                        action = _mainThreadQueue.Dequeue();
                }
                if (action == null)
                    break;
                try
                {
                    action();
                }
                catch (Exception ex)
                {
                    Debug.LogError("[XY Bridge] Main-thread action failed: " + ex);
                }
            }
        }

        internal static void PostToMainThread(Action action)
        {
            if (action == null)
                return;
            lock (_mainThreadQueueLock)
            {
                _mainThreadQueue.Enqueue(action);
            }
        }

        internal static void ClearMainThreadQueue()
        {
            lock (_mainThreadQueueLock)
            {
                _mainThreadQueue.Clear();
            }
        }

        private static void RefreshCachedEditorState()
        {
            IsPlaying = EditorApplication.isPlaying;
            IsPaused = EditorApplication.isPaused;
            ActiveScenePath = EditorSceneManager.GetActiveScene().path ?? "";
        }

        // ═══════════════════════════════════════════════════════════════
        //  响应构造器（供 CodeExecutionService / MessageDispatcher 调用）
        // ═══════════════════════════════════════════════════════════════

        internal static PipeEnvelope Ok(string replyTo, string message) =>
            new PipeEnvelope
            {
                type = "response",
                reply_to = replyTo,
                ok = true,
                message = message,
            };

        internal static PipeEnvelope Err(string replyTo, string error) =>
            new PipeEnvelope
            {
                type = "response",
                reply_to = replyTo,
                ok = false,
                error = error,
            };
    }
}
