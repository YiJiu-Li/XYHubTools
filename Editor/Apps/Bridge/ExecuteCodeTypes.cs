// ═══════════════════════════════════════════════════════════════
//  ExecuteCodeTypes — execute_code 执行管道内部类型
// ═══════════════════════════════════════════════════════════════

using System;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace Framework.XYEditor.Bridge
{
    // ─────────────────────────────────────────────────────────────
    //  编译产物包装
    // ─────────────────────────────────────────────────────────────

    internal sealed class CompiledAsyncSnippet
    {
        public readonly Func<
            ScriptGlobals,
            ExecuteCodeContext,
            CancellationToken,
            Task<object>
        > Executor;

        public CompiledAsyncSnippet(
            Func<ScriptGlobals, ExecuteCodeContext, CancellationToken, Task<object>> executor
        )
        {
            Executor = executor;
        }
    }

    // ─────────────────────────────────────────────────────────────
    //  异步执行句柄
    // ─────────────────────────────────────────────────────────────

    internal sealed class AsyncSnippetExecution : IDisposable
    {
        private long _lastActivityTimestamp;
        public readonly CancellationTokenSource Cancellation = new CancellationTokenSource();
        public readonly TaskCompletionSource<string> Completion =
            new TaskCompletionSource<string>();

        public AsyncSnippetExecution()
        {
            TouchActivity();
        }

        public void TouchActivity()
        {
            Interlocked.Exchange(
                ref _lastActivityTimestamp,
                System.Diagnostics.Stopwatch.GetTimestamp()
            );
        }

        public double IdleSeconds
        {
            get
            {
                long last = Interlocked.Read(ref _lastActivityTimestamp);
                long now = System.Diagnostics.Stopwatch.GetTimestamp();
                long elapsed = now - last;
                if (elapsed <= 0)
                    return 0;
                return elapsed / (double)System.Diagnostics.Stopwatch.Frequency;
            }
        }

        public void Cancel()
        {
            try
            {
                Cancellation.Cancel();
            }
            catch { }
        }

        public void Dispose()
        {
            Cancellation.Dispose();
        }
    }

    // ─────────────────────────────────────────────────────────────
    //  请求状态（含客户端心跳 + 取消协调）
    // ─────────────────────────────────────────────────────────────

    internal sealed class ExecuteCodeRequestState : IDisposable
    {
        private readonly object _lock = new object();
        private AsyncSnippetExecution _execution;
        private long _lastClientHeartbeatTimestamp;
        private int _clientHeartbeatCount;
        private volatile bool _disposed;

        public readonly CancellationTokenSource Cancellation = new CancellationTokenSource();

        public bool IsCancellationRequested
        {
            get
            {
                if (_disposed)
                    return true;
                try
                {
                    return Cancellation.IsCancellationRequested;
                }
                catch (ObjectDisposedException)
                {
                    return true;
                }
            }
        }

        public void SetExecution(AsyncSnippetExecution execution)
        {
            if (execution == null)
                return;
            bool shouldCancel;
            lock (_lock)
            {
                _execution = execution;
                shouldCancel = Cancellation.IsCancellationRequested;
            }
            if (shouldCancel)
                execution.Cancel();
        }

        public void TouchClientHeartbeat()
        {
            if (_disposed)
                return;
            Interlocked.Exchange(
                ref _lastClientHeartbeatTimestamp,
                System.Diagnostics.Stopwatch.GetTimestamp()
            );
            Interlocked.Increment(ref _clientHeartbeatCount);
        }

        public int ClientHeartbeatCount =>
            Interlocked.CompareExchange(ref _clientHeartbeatCount, 0, 0);

        public double ClientHeartbeatIdleSeconds
        {
            get
            {
                long last = Interlocked.Read(ref _lastClientHeartbeatTimestamp);
                if (last <= 0)
                    return 0;
                long now = System.Diagnostics.Stopwatch.GetTimestamp();
                long elapsed = now - last;
                if (elapsed <= 0)
                    return 0;
                return elapsed / (double)System.Diagnostics.Stopwatch.Frequency;
            }
        }

        public void ClearExecution(AsyncSnippetExecution execution)
        {
            lock (_lock)
            {
                if (ReferenceEquals(_execution, execution))
                    _execution = null;
            }
        }

        public void Cancel()
        {
            AsyncSnippetExecution execution;
            try
            {
                if (!_disposed)
                    Cancellation.Cancel();
            }
            catch { }
            lock (_lock)
            {
                execution = _execution;
            }
            if (execution != null)
                execution.Cancel();
        }

        public void ThrowIfCancellationRequested()
        {
            if (_disposed)
                throw new OperationCanceledException();
            Cancellation.Token.ThrowIfCancellationRequested();
        }

        public void Dispose()
        {
            Cancel();
            _disposed = true;
            Cancellation.Dispose();
        }
    }

    // ─────────────────────────────────────────────────────────────
    //  用户侧上下文（代码片段内访问 progress / wait）
    // ─────────────────────────────────────────────────────────────

    public sealed class ExecuteCodeContext
    {
        private readonly CancellationTokenSource _cancellation;
        private readonly Action _touchActivity;
        private Exception _waitException;

        internal ExecuteCodeContext(CancellationTokenSource cancellation, Action touchActivity)
        {
            _cancellation = cancellation;
            _touchActivity = touchActivity;
        }

        public CancellationToken CancellationToken => _cancellation.Token;
        public CancellationToken cancellationToken => _cancellation.Token;
        public bool IsCancellationRequested => _cancellation.IsCancellationRequested;
        public ExecuteCodeFrameAwaitable wait => WaitFrame();

        public ExecuteCodeFrameAwaitable WaitFrame() =>
            new ExecuteCodeFrameAwaitable(this, 1, 0, null);

        public ExecuteCodeFrameAwaitable WaitFrames(int frames) =>
            new ExecuteCodeFrameAwaitable(this, Math.Max(1, frames), 0, null);

        public ExecuteCodeFrameAwaitable WaitSeconds(float seconds)
        {
            double normalized = seconds < 0 ? 0 : seconds;
            return new ExecuteCodeFrameAwaitable(this, 1, normalized, null);
        }

        public ExecuteCodeFrameAwaitable WaitUntil(Func<bool> predicate)
        {
            if (predicate == null)
                throw new ArgumentNullException("predicate");
            return new ExecuteCodeFrameAwaitable(this, 0, 0, predicate);
        }

        public bool Progress(string title, string info, float progress)
        {
            TouchActivity();
            ThrowIfCancellationRequested();
            string normalizedTitle = string.IsNullOrEmpty(title) ? "YZJBridge" : title;
            CodeExecutionService.SetProgress(normalizedTitle, info ?? "", Mathf.Clamp01(progress));
            TouchActivity();
            return _cancellation.IsCancellationRequested;
        }

        public bool Progress(string info, float progress) => Progress("YZJBridge", info, progress);

        public bool Progress(float progress) => Progress("YZJBridge", "", progress);

        public void ClearProgress()
        {
            CodeExecutionService.ResetProgress();
            try
            {
                EditorUtility.ClearProgressBar();
            }
            catch { }
        }

        public void ThrowIfCancellationRequested()
        {
            _cancellation.Token.ThrowIfCancellationRequested();
            if (_waitException != null)
            {
                Exception ex = _waitException;
                _waitException = null;
                throw ex;
            }
        }

        internal bool ShouldResumeImmediately =>
            _cancellation.IsCancellationRequested || _waitException != null;

        private void TouchActivity()
        {
            try
            {
                if (_touchActivity != null)
                    _touchActivity();
            }
            catch { }
        }

        internal bool IsWaitReady(int targetTick, double targetTime, Func<bool> predicate)
        {
            if (_cancellation.IsCancellationRequested)
                return true;
            if (_waitException != null)
                return true;
            if (targetTick >= 0 && CodeExecutionService.EditorUpdateTick < targetTick)
                return false;
            if (targetTime > 0 && EditorApplication.timeSinceStartup < targetTime)
                return false;
            if (predicate == null)
                return true;
            try
            {
                return predicate();
            }
            catch (Exception ex)
            {
                _waitException = ex;
                return true;
            }
        }

        internal void ScheduleWait(
            Action continuation,
            int frames,
            double seconds,
            Func<bool> predicate
        )
        {
            if (continuation == null)
                return;
            int targetTick = frames <= 0 ? -1 : CodeExecutionService.EditorUpdateTick + frames;
            double targetTime = seconds <= 0 ? 0 : EditorApplication.timeSinceStartup + seconds;
            CodeExecutionService.ScheduleContinuation(
                new ExecuteCodeWaitState(this, continuation, targetTick, targetTime, predicate)
            );
        }
    }

    // ─────────────────────────────────────────────────────────────
    //  可 await 的帧等待结构
    // ─────────────────────────────────────────────────────────────

    public struct ExecuteCodeFrameAwaitable
    {
        private readonly ExecuteCodeContext _context;
        private readonly int _frames;
        private readonly double _seconds;
        private readonly Func<bool> _predicate;

        internal ExecuteCodeFrameAwaitable(
            ExecuteCodeContext context,
            int frames,
            double seconds,
            Func<bool> predicate
        )
        {
            _context = context;
            _frames = frames;
            _seconds = seconds;
            _predicate = predicate;
        }

        public Awaiter GetAwaiter() => new Awaiter(_context, _frames, _seconds, _predicate);

        public struct Awaiter : ICriticalNotifyCompletion
        {
            private readonly ExecuteCodeContext _context;
            private readonly int _frames;
            private readonly double _seconds;
            private readonly Func<bool> _predicate;

            internal Awaiter(
                ExecuteCodeContext context,
                int frames,
                double seconds,
                Func<bool> predicate
            )
            {
                _context = context;
                _frames = frames;
                _seconds = seconds;
                _predicate = predicate;
            }

            public bool IsCompleted
            {
                get
                {
                    if (_context == null)
                        return true;
                    if (_frames > 0 || _seconds > 0)
                        return false;
                    return _context.IsWaitReady(-1, 0, _predicate);
                }
            }

            public void GetResult()
            {
                if (_context != null)
                    _context.ThrowIfCancellationRequested();
            }

            public void OnCompleted(Action continuation)
            {
                if (_context == null)
                {
                    continuation();
                    return;
                }
                _context.ScheduleWait(continuation, _frames, _seconds, _predicate);
            }

            public void UnsafeOnCompleted(Action continuation) => OnCompleted(continuation);
        }
    }

    // ─────────────────────────────────────────────────────────────
    //  帧等待延续状态
    // ─────────────────────────────────────────────────────────────

    internal sealed class ExecuteCodeWaitState
    {
        private readonly ExecuteCodeContext _context;
        private readonly int _targetTick;
        private readonly double _targetTime;
        private readonly Func<bool> _predicate;
        public readonly Action Continuation;

        public ExecuteCodeWaitState(
            ExecuteCodeContext context,
            Action continuation,
            int targetTick,
            double targetTime,
            Func<bool> predicate
        )
        {
            _context = context;
            Continuation = continuation;
            _targetTick = targetTick;
            _targetTime = targetTime;
            _predicate = predicate;
        }

        public bool IsReady(int currentTick, double currentTime)
        {
            if (_context == null)
                return true;
            if (_context.ShouldResumeImmediately)
                return true;
            if (_targetTick >= 0 && currentTick < _targetTick)
                return false;
            if (_targetTime > 0 && currentTime < _targetTime)
                return false;
            return _context.IsWaitReady(-1, 0, _predicate);
        }

        public void InvokeContinuation()
        {
            Continuation();
        }
    }
}
