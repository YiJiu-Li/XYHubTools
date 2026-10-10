// ═══════════════════════════════════════════════════════════════
//  PipeServer — Named Pipe 通信层（连接管理、收发、生命周期）
// ═══════════════════════════════════════════════════════════════

using System;
using System.Collections.Concurrent;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace Framework.XYEditor.Bridge
{
    internal static class PipeServer
    {
        // ───────────────── 常量 ─────────────────
        private const int PipeBufferSize = 64 * 1024;
        private const int TextReaderWriterBufferSize = 16 * 1024;
#if UNITY_2020
        private const PipeOptions ServerPipeOptions = PipeOptions.None;
#else
        private const PipeOptions ServerPipeOptions = PipeOptions.Asynchronous;
#endif

        private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false);

        // ───────────────── Pipe 状态 ─────────────────
        private static string _pipeName;
        private static CancellationTokenSource _cts;
        private static Task _serverTask;
        internal static readonly object ConnectionLock = new object();
        internal static readonly SemaphoreSlim WriteLock = new SemaphoreSlim(1, 1);

        /// <summary>
        /// 当前已接受的连接：server -> writer。
        /// accept 循环不再等待连接处理结束（见 ServerLoop），所以同一时刻可能有多条连接
        /// （MCP 客户端的常驻连接 + unity_list_projects 的临时探测连接），响应必须写回
        /// 请求来源的那一条，不能共用一个 _currentWriter。
        /// </summary>
        private static readonly ConcurrentDictionary<NamedPipeServerStream, StreamWriter> _connections =
            new ConcurrentDictionary<NamedPipeServerStream, StreamWriter>();
        internal static readonly int EditorProcessId = ResolveCurrentProcessId();

        /// <summary>Unity 工程根目录（Assets 的上一级）。管道名与多工程识别都基于它。</summary>
        internal static readonly string ProjectRoot = System.IO.Directory
            .GetParent(UnityEngine.Application.dataPath)
            .FullName;

        // ───────────────── 公开状态 ─────────────────
        internal static bool IsRunning => _serverTask != null && !_serverTask.IsCompleted;
        internal static bool IsConnected => !_connections.IsEmpty;

        // ───────────────── 管道名 ─────────────────
        internal static string PipeName
        {
            get
            {
                if (_pipeName == null)
                    _pipeName = GeneratePipeName();
                return _pipeName;
            }
        }

        private static string GeneratePipeName()
        {
            string sanitized = ProjectRoot
                .Replace('\\', '_')
                .Replace('/', '_')
                .Replace(':', '_')
                .Replace(' ', '_');
            return "xybridge_unity_" + sanitized;
        }

        private static int ResolveCurrentProcessId()
        {
            try
            {
                using (var process = System.Diagnostics.Process.GetCurrentProcess())
                    return process.Id;
            }
            catch
            {
                return 0;
            }
        }

        // ═══════════════════════════════════════════════════════════════
        //  生命周期
        // ═══════════════════════════════════════════════════════════════

        internal static void Start()
        {
            if (_serverTask != null && !_serverTask.IsCompleted)
                return;
            try
            {
                _cts = new CancellationTokenSource();
                _serverTask = Task
                    .Factory.StartNew(
                        () => ServerLoop(_cts.Token),
                        _cts.Token,
                        TaskCreationOptions.LongRunning,
                        TaskScheduler.Default
                    )
                    .Unwrap();
                Debug.Log("[XY Bridge] Started, listening on pipe: " + PipeName);
            }
            catch (Exception ex)
            {
                Debug.LogError("[XY Bridge] Failed to start: " + ex);
            }
        }

        internal static void Stop()
        {
            var cts = _cts;
            var task = _serverTask;
            _cts = null;
            _serverTask = null;

            try
            {
                lock (ConnectionLock)
                {
                    foreach (var kv in _connections)
                    {
                        try
                        {
                            if (kv.Value != null)
                                kv.Value.Dispose();
                        }
                        catch { }
                        try
                        {
                            if (kv.Key != null)
                                kv.Key.Dispose();
                        }
                        catch { }
                    }
                    _connections.Clear();
                }
            }
            catch { }

            if (cts != null)
            {
                try
                {
                    cts.Cancel();
                    if (task != null && !task.IsCompleted)
                        task.Wait(1000);
                }
                catch { }
                finally
                {
                    cts.Dispose();
                }
            }

            Debug.Log("[XY Bridge] Stopped.");
        }

        // ═══════════════════════════════════════════════════════════════
        //  Pipe 服务器循环
        // ═══════════════════════════════════════════════════════════════

        private static async Task ServerLoop(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                NamedPipeServerStream server = null;
                try
                {
                    server = new NamedPipeServerStream(
                        PipeName,
                        PipeDirection.InOut,
                        -1, // MaxNumberOfServerInstances=-1 unlimited clients, reconnect OK
                        PipeTransmissionMode.Byte,
                        ServerPipeOptions,
                        PipeBufferSize,
                        PipeBufferSize
                    );
#if UNITY_2020
                    WaitForConnectionCompat(server, ct);
#else
                    await server.WaitForConnectionAsync(ct);
#endif
                    Debug.Log("[XY Bridge] Pipe client connected: " + PipeName);
                    // 分离处理：accept 循环立刻回到 WaitForConnectionAsync，可以继续接受新连接。
                    // 若在这里 await（旧写法），一个连接占用期间任何新连接都连不上服务端 ——
                    // unity_list_projects 的临时探测连接会直接超时，报「探测失败」。
                    // 命令执行本身仍由 CodeExecutionService.ExecuteCodeLock 串行化，语义不变。
                    _ = HandleConnectionDetachedAsync(server, ct);
                }
                catch (OperationCanceledException)
                {
                    try
                    {
                        if (server != null)
                            server.Dispose();
                    }
                    catch { }
                    break;
                }
                catch (Exception ex)
                {
                    try
                    {
                        if (server != null)
                            server.Dispose();
                    }
                    catch { }
                    Debug.LogError("[XY Bridge] Server error: " + ex);
                    try
                    {
                        await Task.Delay(500, ct);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                }
            }
        }

#if UNITY_2020
        private static void WaitForConnectionCompat(
            NamedPipeServerStream server,
            CancellationToken ct
        )
        {
            if (ct.IsCancellationRequested)
                throw new OperationCanceledException(ct);
            using (
                ct.Register(
                    delegate
                    {
                        try
                        {
                            server.Dispose();
                        }
                        catch { }
                    }
                )
            )
            {
                try
                {
                    server.WaitForConnection();
                }
                catch (ObjectDisposedException)
                {
                    if (ct.IsCancellationRequested)
                        throw new OperationCanceledException(ct);
                    throw;
                }
                catch (IOException)
                {
                    if (ct.IsCancellationRequested)
                        throw new OperationCanceledException(ct);
                    throw;
                }
            }
            if (ct.IsCancellationRequested)
                throw new OperationCanceledException(ct);
        }
#endif

        /// <summary>
        /// 不阻塞 accept 循环的连接处理入口。异常必须在这里吞掉：
        /// 抛回 ServerLoop 会打断整个 accept 循环，一条坏连接就能让服务不再接受新连接。
        /// </summary>
        private static async Task HandleConnectionDetachedAsync(
            NamedPipeServerStream server,
            CancellationToken ct
        )
        {
            try
            {
                await HandleConnectionAsync(server, ct);
            }
            catch (OperationCanceledException) { }
            catch (ObjectDisposedException) { }
            catch (Exception ex)
            {
                Debug.LogError("[XY Bridge] Connection handler failed: " + ex);
                try
                {
                    server.Dispose();
                }
                catch { }
            }
        }

        private static async Task HandleConnectionAsync(
            NamedPipeServerStream server,
            CancellationToken ct
        )
        {
            try
            {
                using (server)
                using (
                    var reader = new StreamReader(
                        server,
                        Utf8NoBom,
                        false,
                        TextReaderWriterBufferSize,
                        true
                    )
                )
                using (
                    var writer = new StreamWriter(
                        server,
                        Utf8NoBom,
                        TextReaderWriterBufferSize,
                        true
                    )
                    {
                        AutoFlush = false,
                    }
                )
                {
                    _connections[server] = writer;

                    await SendEnvelopeAsync(
                        new PipeEnvelope
                        {
                            type = "unity_connected",
                            message = "connected",
                            processId = EditorProcessId,
                        },
                        writer
                    );

                    while (!ct.IsCancellationRequested)
                    {
                        string line = await reader.ReadLineAsync();
                        if (line == null)
                            break;
                        if (string.IsNullOrWhiteSpace(line))
                            continue;
                        string captured = line;
                        _ = ProcessIncomingLineAsync(captured, writer);
                    }

                    StreamWriter dropped;
                    _connections.TryRemove(server, out dropped);
                    // 等待可能仍在进行的写入完成，避免随后 Dispose(writer/server)
                    // 与并发写冲突；获取后立即释放，等价于同步等待写锁空闲
                    await WriteLock.WaitAsync();
                    WriteLock.Release();
                }
            }
            catch (IOException) { }
            catch (ObjectDisposedException) { }
            catch (Exception ex)
            {
                Debug.LogError("[XY Bridge] Connection error: " + ex);
            }
            finally
            {
                StreamWriter dropped;
                _connections.TryRemove(server, out dropped);
                // 只有所有连接都断开时才取消进行中的 execute_code：多连接下，
                // 一条临时探测连接断开不该打断另一条连接正在跑的代码。
                if (_connections.IsEmpty)
                    CodeExecutionService.CancelActive("pipe disconnected");
                Debug.Log("[XY Bridge] Pipe client disconnected: " + PipeName);
            }
        }

        private static async Task ProcessIncomingLineAsync(string json, StreamWriter writer)
        {
            PipeEnvelope request = null;
            try
            {
                request = UnityEngine.JsonUtility.FromJson<PipeEnvelope>(json);
            }
            catch (Exception ex)
            {
                string msg =
                    "[XY Bridge] Invalid JSON from client: " + ex.Message + " | raw=" + json;
                YZJBridge.PostToMainThread(
                    delegate
                    {
                        Debug.LogWarning(msg);
                    }
                );
                return;
            }

            if (request == null || string.IsNullOrEmpty(request.type))
            {
                YZJBridge.PostToMainThread(
                    delegate
                    {
                        Debug.LogWarning("[XY Bridge] Invalid message envelope: " + json);
                    }
                );
                return;
            }

            PipeEnvelope response = await MessageDispatcher.HandleAsync(request);
            if (response != null && !string.IsNullOrEmpty(response.reply_to))
                await SendEnvelopeAsync(response, writer);
        }

        internal static async Task<bool> SendEnvelopeAsync(PipeEnvelope env, StreamWriter writer)
        {
            if (writer == null)
                return false;

            string json;
            try
            {
                json = UnityEngine.JsonUtility.ToJson(env);
            }
            catch (Exception ex)
            {
                Debug.LogError("[XY Bridge] Failed to serialize envelope: " + ex);
                return false;
            }

            await WriteLock.WaitAsync();
            try
            {
                await writer.WriteLineAsync(json);
                await writer.FlushAsync();
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[XY Bridge] Failed to write to pipe: " + ex.Message);
                return false;
            }
            finally
            {
                WriteLock.Release();
            }
        }
    }
}
