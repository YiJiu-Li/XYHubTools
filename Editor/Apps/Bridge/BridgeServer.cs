// ═══════════════════════════════════════════════════════════════
//  BridgeServer — 本机 TCP 通信层（连接管理、收发、生命周期）
// ═══════════════════════════════════════════════════════════════
//  2.0 起取代原来的 Named Pipe 实现，目的只有一个：跨平台。
//
//    · 监听 127.0.0.1 上由系统分配的端口，Windows / macOS / Linux 同一套代码
//    · MCP 侧只用 Python 标准库 socket，不再需要 pywin32
//    · 端口、一次性 token、工程路径写入 ~/.xybridge/instances/，
//      MCP server 扫描该目录即可发现所有存活 Unity 实例
//    · 协议不变：NDJSON，一行一条 JSON 信封（BridgeEnvelope）
//
//  与 Named Pipe 的语义差异（都有对应处理，不要误改）：
//    · 管道"存在即可枚举"，TCP 不行 → 用 instance 文件 + 进程 PID 落盘
//    · instance 文件可能因崩溃残留 → MCP 侧以能否连上为准，连不上即视为失效
//    · 端口是系统分配且每次重启都变 → MCP 侧每次连接前重读 instance 文件
// ═══════════════════════════════════════════════════════════════

using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace Framework.XYEditor.Bridge
{
    internal static class BridgeServer
    {
        // ───────────────── 常量 ─────────────────
        /// <summary>与 MCP~ / xy_mcp_server.py 的 SERVER_VERSION 保持一致。</summary>
        internal const string BridgeVersion = "2.0.0";

        /// <summary>instance 文件协议版本。MCP 侧只认这个值，旧版本文件直接跳过。</summary>
        private const int SchemaVersion = 2;

        private const int TextReaderWriterBufferSize = 16 * 1024;
        private const string LoopbackHost = "127.0.0.1";

        private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false);

        // ───────────────── 服务状态 ─────────────────
        private static TcpListener _listener;
        private static int _port;
        private static string _token;
        private static string _instanceFile;
        private static CancellationTokenSource _cts;
        private static Task _serverTask;

        internal static readonly object ConnectionLock = new object();
        internal static readonly SemaphoreSlim WriteLock = new SemaphoreSlim(1, 1);

        /// <summary>
        /// 当前已接受的连接：client -> writer。
        /// accept 循环不再等待连接处理结束（见 ServerLoop），所以同一时刻可能有多条连接
        /// （MCP 客户端的常驻连接 + unity_list_projects 的临时探测连接），响应必须写回
        /// 请求来源的那一条，不能共用一个 _currentWriter。
        /// </summary>
        private static readonly ConcurrentDictionary<TcpClient, StreamWriter> _connections =
            new ConcurrentDictionary<TcpClient, StreamWriter>();

        internal static readonly int EditorProcessId = ResolveCurrentProcessId();

        /// <summary>Unity 工程根目录（Assets 的上一级）。多工程识别与 instance 文件名都基于它。</summary>
        internal static readonly string ProjectRoot = System.IO.Directory
            .GetParent(UnityEngine.Application.dataPath)
            .FullName;

        // ───────────────── 公开状态 ─────────────────
        internal static bool IsRunning => _serverTask != null && !_serverTask.IsCompleted;
        internal static bool IsConnected => !_connections.IsEmpty;

        /// <summary>供面板显示的监听地址，例如 127.0.0.1:52341。</summary>
        internal static string Endpoint => _port > 0 ? LoopbackHost + ":" + _port : "—";

        /// <summary>实例文件路径（未运行时为 null）。MCP 侧靠它发现本实例。</summary>
        internal static string InstanceFile => _instanceFile;

        /// <summary>实例文件所在目录，与 MCP 侧约定一致。</summary>
        internal static string InstanceDir =>
            Path.Combine(BridgeUserRoot, ".xybridge", "instances");

        private static string BridgeUserRoot
        {
            get
            {
                string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                if (string.IsNullOrEmpty(home))
                    home = Environment.GetEnvironmentVariable("HOME");
                if (string.IsNullOrEmpty(home))
                    home = Path.GetTempPath();
                return home;
            }
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
                _token = NewToken();
                _listener = new TcpListener(IPAddress.Loopback, 0);
                _listener.Start();
                _port = ((IPEndPoint)_listener.LocalEndpoint).Port;

                // 先落盘再开 accept：MCP 侧可能在任何时刻扫描目录
                WriteInstanceFile();

                _serverTask = Task
                    .Factory.StartNew(
                        () => ServerLoop(_cts.Token),
                        _cts.Token,
                        TaskCreationOptions.LongRunning,
                        TaskScheduler.Default
                    )
                    .Unwrap();

                Debug.Log("[XY Bridge] Started, listening on " + Endpoint);
            }
            catch (Exception ex)
            {
                Debug.LogError("[XY Bridge] Failed to start: " + ex);
                // 半启动状态必须回收，否则下一次 Start 会看到 _serverTask 为空但端口仍占着
                try
                {
                    if (_listener != null)
                        _listener.Stop();
                }
                catch { }
                _listener = null;
                _port = 0;
                _serverTask = null;
                DeleteInstanceFile();
                try
                {
                    if (_cts != null)
                        _cts.Dispose();
                }
                catch { }
                _cts = null;
            }
        }

        internal static void Stop()
        {
            var cts = _cts;
            var task = _serverTask;
            _cts = null;
            _serverTask = null;

            // 先删 instance 文件：新的客户端不该再被指向这个即将关闭的端口
            DeleteInstanceFile();

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
                                kv.Key.Close();
                        }
                        catch { }
                    }
                    _connections.Clear();
                }
            }
            catch { }

            try
            {
                if (_listener != null)
                    _listener.Stop();
            }
            catch { }
            _listener = null;
            _port = 0;

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
        //  Accept 循环
        // ═══════════════════════════════════════════════════════════════

        private static async Task ServerLoop(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                TcpClient client = null;
                try
                {
                    var listener = _listener;
                    if (listener == null)
                        break;

                    client = await listener.AcceptTcpClientAsync();
                    if (ct.IsCancellationRequested)
                    {
                        client.Close();
                        break;
                    }

                    // 请求都是一问一答的小包，Nagle 会把它们攒在一起，直接关掉
                    client.NoDelay = true;
                    Debug.Log("[XY Bridge] Client connected: " + Endpoint);

                    // 分离处理：accept 循环立刻回到 AcceptTcpClientAsync，可以继续接受新连接。
                    // 若在这里 await，一个连接占用期间任何新连接都连不上服务端 ——
                    // unity_list_projects 的临时探测连接会直接超时，报「探测失败」。
                    // 命令执行本身仍由 CodeExecutionService.ExecuteCodeLock 串行化，语义不变。
                    _ = HandleConnectionDetachedAsync(client, ct);
                }
                catch (ObjectDisposedException)
                {
                    // listener.Stop() 打断挂起的 accept
                    break;
                }
                catch (InvalidOperationException)
                {
                    // listener 已停止
                    break;
                }
                catch (OperationCanceledException)
                {
                    TryClose(client);
                    break;
                }
                catch (SocketException ex)
                {
                    TryClose(client);
                    if (ct.IsCancellationRequested)
                        break;
                    Debug.LogError("[XY Bridge] Server error: " + ex.Message);
                    try
                    {
                        await Task.Delay(200, ct);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                }
                catch (Exception ex)
                {
                    TryClose(client);
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

        private static void TryClose(TcpClient client)
        {
            try
            {
                if (client != null)
                    client.Close();
            }
            catch { }
        }

        /// <summary>
        /// 不阻塞 accept 循环的连接处理入口。异常必须在这里吞掉：
        /// 抛回 ServerLoop 会打断整个 accept 循环，一条坏连接就能让服务不再接受新连接。
        /// </summary>
        private static async Task HandleConnectionDetachedAsync(
            TcpClient client,
            CancellationToken ct
        )
        {
            try
            {
                await HandleConnectionAsync(client, ct);
            }
            catch (OperationCanceledException) { }
            catch (ObjectDisposedException) { }
            catch (IOException) { }
            catch (Exception ex)
            {
                Debug.LogError("[XY Bridge] Connection handler failed: " + ex);
                TryClose(client);
            }
        }

        private static async Task HandleConnectionAsync(TcpClient client, CancellationToken ct)
        {
            try
            {
                using (client)
                using (NetworkStream stream = client.GetStream())
                using (
                    var reader = new StreamReader(
                        stream,
                        Utf8NoBom,
                        false,
                        TextReaderWriterBufferSize,
                        true
                    )
                )
                using (
                    var writer = new StreamWriter(
                        stream,
                        Utf8NoBom,
                        TextReaderWriterBufferSize,
                        true
                    )
                    {
                        AutoFlush = false,
                    }
                )
                {
                    _connections[client] = writer;

                    // 首条推送不含 token / 工程路径：此时还没有认证，
                    // 只告诉客户端"协议在、可以发请求了"。真实信息由必带 token 的 status 取。
                    await SendEnvelopeAsync(
                        new BridgeEnvelope
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
                        _ = ProcessIncomingLineAsync(line, writer);
                    }

                    StreamWriter dropped;
                    _connections.TryRemove(client, out dropped);
                    // 等待可能仍在进行的写入完成，避免随后 Dispose(writer/stream)
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
                _connections.TryRemove(client, out dropped);
                // 只有所有连接都断开时才取消进行中的 execute_code：多连接下，
                // 一条临时探测连接断开不该打断另一条连接正在跑的代码。
                if (_connections.IsEmpty)
                    CodeExecutionService.CancelActive("all clients disconnected");
                Debug.Log("[XY Bridge] Client disconnected: " + Endpoint);
            }
        }

        private static async Task ProcessIncomingLineAsync(string json, StreamWriter writer)
        {
            BridgeEnvelope request = null;
            try
            {
                request = UnityEngine.JsonUtility.FromJson<BridgeEnvelope>(json);
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

            if (!string.IsNullOrEmpty(_token) && request.token != _token)
            {
                string rejected = request.type;
                YZJBridge.PostToMainThread(
                    delegate
                    {
                        Debug.LogWarning(
                            "[XY Bridge] 拒绝令牌不匹配的请求: "
                                + rejected
                                + "（Editor 可能已重载，客户端需重读实例文件）"
                        );
                    }
                );
                await SendEnvelopeAsync(
                    YZJBridge.Err(
                        request.id,
                        "认证失败：令牌不匹配。Editor 可能已重载，请重连以重新读取实例文件。"
                    ),
                    writer
                );
                return;
            }

            BridgeEnvelope response = await MessageDispatcher.HandleAsync(request);
            if (response != null && !string.IsNullOrEmpty(response.reply_to))
                await SendEnvelopeAsync(response, writer);
        }

        internal static async Task<bool> SendEnvelopeAsync(BridgeEnvelope env, StreamWriter writer)
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
                Debug.LogWarning("[XY Bridge] Failed to write to socket: " + ex.Message);
                return false;
            }
            finally
            {
                WriteLock.Release();
            }
        }

        // ═══════════════════════════════════════════════════════════════
        //  实例文件（MCP 侧的发现依据）
        // ═══════════════════════════════════════════════════════════════

        private static string NewToken()
        {
            var bytes = new byte[24];
            using (var rng = RandomNumberGenerator.Create())
                rng.GetBytes(bytes);
            var sb = new StringBuilder(bytes.Length * 2);
            foreach (byte b in bytes)
                sb.Append(b.ToString("x2"));
            return sb.ToString();
        }

        /// <summary>工程路径的短指纹：避免长路径撑爆文件名，也避免直接暴露目录名。</summary>
        private static string ProjectFingerprint()
        {
            using (var sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(ProjectRoot.ToLowerInvariant()));
                var sb = new StringBuilder(12);
                for (int i = 0; i < 6; i++)
                    sb.Append(hash[i].ToString("x2"));
                return sb.ToString();
            }
        }

        private static string BuildInstanceJson()
        {
            return "{"
                + "\"schema\":" + SchemaVersion
                + ",\"transport\":\"tcp\""
                + ",\"host\":\"" + LoopbackHost + "\""
                + ",\"port\":" + _port
                + ",\"pid\":" + EditorProcessId
                + ",\"token\":\"" + JsonEscape.String(_token) + "\""
                + ",\"projectPath\":\"" + JsonEscape.String(ProjectRoot) + "\""
                + ",\"unityVersion\":\"" + JsonEscape.String(Application.unityVersion) + "\""
                + ",\"bridgeVersion\":\"" + BridgeVersion + "\""
                + ",\"startedAt\":\"" + DateTime.UtcNow.ToString("o") + "\""
                + "}";
        }

        private static void WriteInstanceFile()
        {
            try
            {
                Directory.CreateDirectory(InstanceDir);
                string path = Path.Combine(
                    InstanceDir,
                    "xy-" + ProjectFingerprint() + "-" + EditorProcessId + ".json"
                );
                string temporary = path + ".tmp";
                File.WriteAllText(temporary, BuildInstanceJson(), Utf8NoBom);

                // 文件名带 PID，正常情况不会撞名；撞上说明上次异常退出留了残留，覆盖即可
                try
                {
                    File.Move(temporary, path);
                }
                catch (Exception ex)
                    when (ex is IOException
                        || ex is UnauthorizedAccessException
                        || ex is NotSupportedException
                        || ex is PlatformNotSupportedException)
                {
                    // macOS / Linux 上 File.Move 覆盖已存在文件的行为与 Windows 不同，
                    // 这里统一退回到 Copy(overwrite)：写不进去才继续往外抛
                    File.Copy(temporary, path, true);
                    try
                    {
                        File.Delete(temporary);
                    }
                    catch { }
                }

                _instanceFile = path;
            }
            catch (Exception ex)
            {
                // 写不进去只影响「被发现」，不影响已经建立的连接，因此不当作致命错误
                Debug.LogError(
                    "[XY Bridge] 无法写入实例文件，MCP 将发现不了本实例: " + ex.Message
                );
                _instanceFile = null;
            }
        }

        private static void DeleteInstanceFile()
        {
            try
            {
                if (!string.IsNullOrEmpty(_instanceFile) && File.Exists(_instanceFile))
                    File.Delete(_instanceFile);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[XY Bridge] 删除实例文件失败: " + ex.Message);
            }
            _instanceFile = null;
        }
    }
}
