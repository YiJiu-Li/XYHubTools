// ═══════════════════════════════════════════════════════════════
//  execute_code 执行引擎 — 编译、运行、超时、进度、异步泵
// ═══════════════════════════════════════════════════════════════

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace Framework.XYEditor.Bridge
{
    /// <summary>
    /// execute_code 执行引擎。
    /// 负责代码片段编译、主线程执行、超时监控、进度报告、异步泵。
    /// </summary>
    internal static class CodeExecutionService
    {
        // ───────────────── 常量 ─────────────────
        internal const int ExecuteTimeoutMs = 30000;
        internal const double AsyncExecutePumpRequestIntervalSeconds = 0.05;
        internal const int AsyncExecuteInactivityPollMs = 250;
        internal const int ExecuteCodeLockWaitTimeoutMs = 30000;
        internal const int ExecuteClientHeartbeatTimeoutMs = 120000;

        // ───────────────── 状态 ─────────────────
        internal static readonly SemaphoreSlim ExecuteCodeLock = new SemaphoreSlim(1, 1);
        internal static readonly object ContinuationQueueLock = new object();
        internal static readonly List<ExecuteCodeWaitState> ContinuationQueue =
            new List<ExecuteCodeWaitState>(64);
        internal static readonly object ProgressLock = new object();
        internal static int EditorUpdateTick;
        internal static int ActiveAsyncCount;
        internal static bool HasSavedRunInBackground;
        internal static bool SavedRunInBackground;
        internal static double LastAsyncPumpRequestSeconds;
        internal static readonly object RequestStateLock = new object();
        internal static ExecuteCodeRequestState ActiveRequest;
        internal static ExecuteCodeProgressSnapshot Progress = new ExecuteCodeProgressSnapshot
        {
            active = false,
            title = "",
            info = "",
            progress = 0,
            revision = 0,
        };
        internal static int ProgressRevision;

        // ═══════════════════════════════════════════════════════════════
        //  进度管理
        // ═══════════════════════════════════════════════════════════════

        internal static void ResetProgress()
        {
            lock (ProgressLock)
            {
                ProgressRevision++;
                Progress = new ExecuteCodeProgressSnapshot
                {
                    active = false,
                    title = "",
                    info = "",
                    progress = 0,
                    revision = ProgressRevision,
                    source = "",
                };
            }
        }

        internal static void SetProgress(string title, string info, float progress) =>
            SetProgressSnapshot(title, info, progress, "api");

        internal static void SetStage(string info) => SetProgressSnapshot(info, "", 0, "stage");

        internal static void SetProgressSnapshot(
            string title,
            string info,
            float progress,
            string source
        )
        {
            lock (ProgressLock)
            {
                ProgressRevision++;
                Progress = new ExecuteCodeProgressSnapshot
                {
                    active = true,
                    title = string.IsNullOrEmpty(title) ? "YZJBridge" : title,
                    info = info ?? "",
                    progress = Mathf.Clamp01(progress),
                    revision = ProgressRevision,
                    source = string.IsNullOrEmpty(source) ? "api" : source,
                };
            }
        }

        internal static string GetProgressJson()
        {
            lock (ProgressLock)
            {
                return JsonUtility.ToJson(Progress);
            }
        }

        // ═══════════════════════════════════════════════════════════════
        //  请求状态管理
        // ═══════════════════════════════════════════════════════════════

        internal static ExecuteCodeRequestState GetActiveRequest()
        {
            lock (RequestStateLock)
            {
                return ActiveRequest;
            }
        }

        internal static void TouchHeartbeat()
        {
            var rs = GetActiveRequest();
            if (rs != null)
                rs.TouchClientHeartbeat();
        }

        internal static void CancelActive(string reason)
        {
            var rs = GetActiveRequest();
            if (rs == null)
                return;
            rs.Cancel();
            ResetProgress();
            if (!string.IsNullOrEmpty(reason))
                Debug.LogWarning("[XY Bridge] execute_code canceled: " + reason);
        }

        // ═══════════════════════════════════════════════════════════════
        //  请求处理
        // ═══════════════════════════════════════════════════════════════

        internal static PipeEnvelope HandleCancel(string requestId)
        {
            ExecuteCodeRequestState rs;
            lock (RequestStateLock)
            {
                rs = ActiveRequest;
            }
            if (rs == null)
            {
                ResetProgress();
                return YZJBridge.Ok(requestId, "no active execute_code");
            }
            rs.Cancel();
            ResetProgress();
            return YZJBridge.Ok(requestId, "execute_code cancellation requested");
        }

        internal static async Task MonitorHeartbeatAsync(ExecuteCodeRequestState rs)
        {
            if (rs == null)
                return;
            try
            {
                while (!rs.IsCancellationRequested)
                {
                    await Task.Delay(AsyncExecuteInactivityPollMs).ConfigureAwait(false);
                    if (rs.IsCancellationRequested)
                        return;
                    if (rs.ClientHeartbeatCount <= 0)
                        continue;
                    if (rs.ClientHeartbeatIdleSeconds < ExecuteClientHeartbeatTimeoutMs / 1000.0)
                        continue;
                    rs.Cancel();
                    ResetProgress();
                    Debug.LogWarning(
                        "[XY Bridge] execute_code canceled: client heartbeat timed out after "
                            + (ExecuteClientHeartbeatTimeoutMs / 1000)
                            + " seconds"
                    );
                    return;
                }
            }
            catch (Exception ex)
            {
                Debug.LogError("[XY Bridge] execute_code heartbeat monitor failed: " + ex);
            }
        }

        /// <summary>处理 execute_code：编译 + 执行完整流水线</summary>
        internal static async Task<PipeEnvelope> HandleAsync(
            string requestId,
            string code,
            Func<Action<string>, CancellationToken, Task<string>> ensureReady,
            Action<Action> postToMainThread
        )
        {
            if (string.IsNullOrWhiteSpace(code))
                return YZJBridge.Err(requestId, "empty code");
            if (GetActiveRequest() == null)
                SetStage("Waiting for Unity execute lock");

            bool lockTaken = false;
            try
            {
                if (!await ExecuteCodeLock.WaitAsync(ExecuteCodeLockWaitTimeoutMs))
                {
                    if (GetActiveRequest() == null)
                        ResetProgress();
                    return YZJBridge.Err(
                        requestId,
                        "execute_code lock wait timed out after "
                            + (ExecuteCodeLockWaitTimeoutMs / 1000)
                            + " seconds"
                    );
                }
                lockTaken = true;
            }
            catch (ObjectDisposedException ex)
            {
                if (GetActiveRequest() == null)
                    ResetProgress();
                return YZJBridge.Err(requestId, "execute_code lock unavailable: " + ex.Message);
            }

            ExecuteCodeRequestState requestState = null;
            try
            {
                requestState = new ExecuteCodeRequestState();
                lock (RequestStateLock)
                {
                    ActiveRequest = requestState;
                }
                _ = MonitorHeartbeatAsync(requestState);
                ResetProgress();
                SetStage("Checking compiler cache");

                string prepareError = await ensureReady(SetStage, requestState.Cancellation.Token);
                if (!string.IsNullOrEmpty(prepareError))
                {
                    requestState.ThrowIfCancellationRequested();
                    SetStage("Compiler preparation failed");
                    return YZJBridge.Err(requestId, prepareError);
                }
                requestState.ThrowIfCancellationRequested();

                CompiledAsyncSnippet snippet;
                try
                {
                    SetStage("Compiling snippet");
                    requestState.ThrowIfCancellationRequested();
                    snippet = SnippetCompiler.Compile(code);
                    requestState.ThrowIfCancellationRequested();
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    SetStage("Compilation failed");
                    return YZJBridge.Err(
                        requestId,
                        "async snippet compilation exception: " + ex.Message
                    );
                }

                SetStage("Executing snippet");
                string resultText = await ExecuteOnMainThreadAsync(
                    snippet,
                    requestState,
                    postToMainThread
                );
                if (resultText.StartsWith("__ERROR__: ", StringComparison.Ordinal))
                {
                    requestState.ThrowIfCancellationRequested();
                    SetStage("Execution failed");
                    return YZJBridge.Err(requestId, resultText.Substring("__ERROR__: ".Length));
                }

                requestState.ThrowIfCancellationRequested();
                SetStage("Execution complete");
                return YZJBridge.Ok(requestId, resultText);
            }
            catch (OperationCanceledException)
            {
                return YZJBridge.Err(requestId, "execute_code canceled");
            }
            finally
            {
                lock (RequestStateLock)
                {
                    if (ReferenceEquals(ActiveRequest, requestState))
                        ActiveRequest = null;
                }
                if (requestState != null)
                    requestState.Dispose();
                ResetProgress();
                if (lockTaken)
                    ExecuteCodeLock.Release();
            }
        }

        // ═══════════════════════════════════════════════════════════════
        //  主线程执行
        // ═══════════════════════════════════════════════════════════════

        internal static Task<string> ExecuteOnMainThreadAsync(
            CompiledAsyncSnippet snippet,
            ExecuteCodeRequestState requestState,
            Action<Action> postToMainThread
        )
        {
            var execution = new AsyncSnippetExecution();
            if (requestState != null)
                requestState.SetExecution(execution);

            if (requestState != null && requestState.IsCancellationRequested)
            {
                execution.Cancel();
                execution.Completion.TrySetResult("__ERROR__: execution canceled");
                return execution.Completion.Task;
            }

            postToMainThread(() =>
            {
                if (requestState != null && requestState.IsCancellationRequested)
                {
                    execution.Cancel();
                    execution.Completion.TrySetResult("__ERROR__: execution canceled");
                    return;
                }
                RunOnMainThread(snippet, execution, requestState);
            });

            _ = MonitorInactivityAsync(execution);
            return execution.Completion.Task;
        }

        internal static async Task MonitorInactivityAsync(AsyncSnippetExecution execution)
        {
            try
            {
                while (!execution.Completion.Task.IsCompleted)
                {
                    await Task.Delay(AsyncExecuteInactivityPollMs).ConfigureAwait(false);
                    if (execution.Completion.Task.IsCompleted)
                        return;
                    if (execution.IdleSeconds < ExecuteTimeoutMs / 1000.0)
                        continue;
                    execution.Cancel();
                    execution.Completion.TrySetResult(
                        "__ERROR__: execution timed out after "
                            + (ExecuteTimeoutMs / 1000)
                            + " seconds without print/progress output"
                    );
                    return;
                }
            }
            catch (Exception ex)
            {
                Debug.LogError("[XY Bridge] Async execute timeout monitor failed: " + ex);
            }
        }

        internal static async void RunOnMainThread(
            CompiledAsyncSnippet snippet,
            AsyncSnippetExecution execution,
            ExecuteCodeRequestState requestState
        )
        {
            BeginRuntime();
            ExecuteCodeContext ctx = null;
            try
            {
                if (requestState != null)
                    requestState.ThrowIfCancellationRequested();
                var globals = new ScriptGlobals(execution.TouchActivity);
                ctx = new ExecuteCodeContext(execution.Cancellation, execution.TouchActivity);
                // 进用户代码前先 touch 一次：用户代码可能没有 print/printJson，
                // 不预先 touch 的话，MonitorInactivityAsync 会误判为 30s 无活动。
                execution.TouchActivity();
                object returnValue = await snippet.Executor(
                    globals,
                    ctx,
                    execution.Cancellation.Token
                );
                if (returnValue != null)
                    globals.print(returnValue);
                // 退出前再 touch 一次，避免在 TrySetResult 之前 activity 被重置
                execution.TouchActivity();
                execution.Completion.TrySetResult(globals.GetOutput());
            }
            catch (OperationCanceledException)
            {
                execution.Completion.TrySetResult("__ERROR__: execution canceled");
            }
            catch (Exception ex)
            {
                execution.Completion.TrySetResult("__ERROR__: runtime error: " + ex);
            }
            finally
            {
                if (ctx != null)
                    ctx.ClearProgress();
                if (requestState != null)
                    requestState.ClearExecution(execution);
                execution.Dispose();
                EndRuntime();
            }
        }

        // ═══════════════════════════════════════════════════════════════
        //  异步运行时泵
        // ═══════════════════════════════════════════════════════════════

        internal static void PumpRuntime()
        {
            EditorUpdateTick++;
            PumpContinuations();
            RequestPump();
        }

        internal static void BeginRuntime()
        {
            if (ActiveAsyncCount == 0)
            {
                try
                {
                    SavedRunInBackground = Application.runInBackground;
                    HasSavedRunInBackground = true;
                    Application.runInBackground = true;
                }
                catch
                {
                    HasSavedRunInBackground = false;
                }
            }
            ActiveAsyncCount++;
            RequestPump();
        }

        internal static void EndRuntime()
        {
            if (ActiveAsyncCount > 0)
                ActiveAsyncCount--;
            if (ActiveAsyncCount != 0)
                return;
            try
            {
                EditorUtility.ClearProgressBar();
            }
            catch { }
            if (HasSavedRunInBackground)
            {
                try
                {
                    Application.runInBackground = SavedRunInBackground;
                }
                catch { }
            }
            HasSavedRunInBackground = false;
        }

        internal static void ScheduleContinuation(ExecuteCodeWaitState state)
        {
            if (state == null || state.Continuation == null)
                return;
            lock (ContinuationQueueLock)
            {
                ContinuationQueue.Add(state);
            }
            RequestPump();
        }

        internal static void RequestPump()
        {
            if (ActiveAsyncCount <= 0)
                return;
            try
            {
                double now = EditorApplication.timeSinceStartup;
                if (now - LastAsyncPumpRequestSeconds < AsyncExecutePumpRequestIntervalSeconds)
                    return;
                LastAsyncPumpRequestSeconds = now;
                EditorApplication.QueuePlayerLoopUpdate();
            }
            catch { }
        }

        internal static void PumpContinuations()
        {
            List<ExecuteCodeWaitState> ready = null;
            double now = EditorApplication.timeSinceStartup;
            lock (ContinuationQueueLock)
            {
                if (ContinuationQueue.Count == 0)
                    return;
                for (int i = ContinuationQueue.Count - 1; i >= 0; i--)
                {
                    var state = ContinuationQueue[i];
                    if (state == null || state.IsReady(EditorUpdateTick, now))
                    {
                        ContinuationQueue.RemoveAt(i);
                        if (state != null)
                        {
                            if (ready == null)
                                ready = new List<ExecuteCodeWaitState>();
                            ready.Add(state);
                        }
                    }
                }
            }
            if (ready == null)
                return;
            for (int i = ready.Count - 1; i >= 0; i--)
            {
                try
                {
                    ready[i].InvokeContinuation();
                }
                catch (Exception ex)
                {
                    Debug.LogError("[XY Bridge] Async execute continuation failed: " + ex);
                }
            }
        }
    }
}
