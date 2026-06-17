// ═══════════════════════════════════════════════════════════════
//  PipeServer — Named Pipe 通信层（连接管理、收发、生命周期）
// ═══════════════════════════════════════════════════════════════

using System;
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
        private static NamedPipeServerStream _currentServer;
        private static StreamWriter _currentWriter;
        private static volatile bool _pipeConnected;
        internal static readonly int EditorProcessId = ResolveCurrentProcessId();

        // ───────────────── 公开状态 ─────────────────
        internal static bool IsRunning => _serverTask != null && !_serverTask.IsCompleted;
        internal static bool IsConnected => _pipeConnected;

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
            string projectPath = System
                .IO.Directory.GetParent(UnityEngine.Application.dataPath)
                .FullName;
            string sanitized = projectPath
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
                    try
                    {
                        if (_currentWriter != null)
                            _currentWriter.Dispose();
                    }
                    catch { }
                    try
                    {
                        if (_currentServer != null)
                            _currentServer.Dispose();
                    }
                    catch { }
                    _currentWriter = null;
                    _currentServer = null;
                    _pipeConnected = false;
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
                    await HandleConnectionAsync(server, ct);
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
                    lock (ConnectionLock)
                    {
                        _currentServer = server;
                        _currentWriter = writer;
                    }

                    await SendEnvelopeAsync(
                        new PipeEnvelope
                        {
                            type = "unity_connected",
                            message = "connected",
                            processId = EditorProcessId,
                        }
                    );

                    lock (ConnectionLock)
                    {
                        if (ReferenceEquals(_currentServer, server))
                            _pipeConnected = true;
                    }

                    while (!ct.IsCancellationRequested)
                    {
                        string line = await reader.ReadLineAsync();
                        if (line == null)
                            break;
                        if (string.IsNullOrWhiteSpace(line))
                            continue;
                        string captured = line;
                        _ = ProcessIncomingLineAsync(captured);
                    }

                    lock (ConnectionLock)
                    {
                        _currentWriter = null;
                        _currentServer = null;
                        _pipeConnected = false;
                    }
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
                lock (ConnectionLock)
                {
                    if (ReferenceEquals(_currentServer, server))
                    {
                        _currentWriter = null;
                        _currentServer = null;
                        _pipeConnected = false;
                    }
                }
                CodeExecutionService.CancelActive("pipe disconnected");
                Debug.Log("[XY Bridge] Pipe client disconnected: " + PipeName);
            }
        }

        private static async Task ProcessIncomingLineAsync(string json)
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
                await SendEnvelopeAsync(response);
        }

        internal static async Task<bool> SendEnvelopeAsync(PipeEnvelope env)
        {
            StreamWriter writer;
            lock (ConnectionLock)
            {
                writer = _currentWriter;
            }
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
