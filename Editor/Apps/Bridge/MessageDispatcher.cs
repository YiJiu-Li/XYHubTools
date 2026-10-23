// ═══════════════════════════════════════════════════════════════
//  MessageDispatcher — 消息路由与处理（25 种消息类型）
// ═══════════════════════════════════════════════════════════════

using System;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;

namespace Framework.XYEditor.Bridge
{
    internal static class MessageDispatcher
    {
        /// <summary>请求入口：计时 + 记请求日志，再交给 HandleCoreAsync。</summary>
        internal static async Task<BridgeEnvelope> HandleAsync(BridgeEnvelope msg)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            BridgeEnvelope result;
            try { result = await HandleCoreAsync(msg); }
            catch (Exception ex) { result = YZJBridge.Err(msg.id, ex.ToString()); }
            sw.Stop();

            try
            {
                RequestLog.Record(
                    msg.type,
                    result != null && result.ok,
                    sw.Elapsed.TotalMilliseconds,
                    msg.client
                );
            }
            catch { /* 日志失败不能影响主流程 */ }

            return result;
        }

        static async Task<BridgeEnvelope> HandleCoreAsync(BridgeEnvelope msg)
        {
            string reqId = msg.id;
            try
            {
                switch (msg.type)
                {
                    case "ping":
                    {
                        // 把进程号与工程路径带在响应里：Python 侧 unity_ping /
                        // unity_list_projects 读的是响应，不填的话恒为默认值
                        var pong = YZJBridge.Ok(reqId, "pong");
                        pong.processId = BridgeServer.EditorProcessId;
                        pong.projectPath = BridgeServer.ProjectRoot;
                        return pong;
                    }
                    case "status":
                    {
                        string s = YZJBridge.IsPlaying
                            ? (YZJBridge.IsPaused ? "playing_paused" : "playing")
                            : "editing";
                        string scene = YZJBridge.ActiveScenePath ?? "";
                        var st = YZJBridge.Ok(
                            reqId,
                            s + (string.IsNullOrEmpty(scene) ? "" : " " + scene)
                        );
                        st.processId = BridgeServer.EditorProcessId;
                        st.projectPath = BridgeServer.ProjectRoot;
                        return st;
                    }
                    case "execute_code":
                        return await CodeExecutionService.HandleAsync(
                            reqId,
                            msg.message,
                            (stage, ct) =>
                                CompilationService.EnsureReadyAsync(
                                    YZJBridge.PostToMainThread,
                                    stage,
                                    CodeExecutionService.ExecuteTimeoutMs,
                                    ct
                                ),
                            YZJBridge.PostToMainThread
                        );
                    case "cancel_execute_code":
                        return CodeExecutionService.HandleCancel(reqId);
                    case "execute_code_progress":
                        CodeExecutionService.TouchHeartbeat();
                        return YZJBridge.Ok(reqId, CodeExecutionService.GetProgressJson());
                    case "request_recompile":
                    {
                        YZJBridge.PostToMainThread(
                            delegate
                            {
                                CompilationService.InvalidateCache();
                                CompilationPipeline.RequestScriptCompilation();
                            }
                        );
                        return YZJBridge.Ok(reqId, "recompile_started");
                    }
                    case "import_assets":
                    {
                        string paths = msg.message ?? "";
                        var lines = paths.Split(
                            new[] { '\n' },
                            StringSplitOptions.RemoveEmptyEntries
                        );
                        var tcs = new TaskCompletionSource<BridgeEnvelope>();
                        YZJBridge.PostToMainThread(
                            delegate
                            {
                                try
                                {
                                    foreach (string ap in lines)
                                    {
                                        string p = ap.Trim();
                                        if (!string.IsNullOrEmpty(p))
                                            AssetDatabase.ImportAsset(
                                                p,
                                                ImportAssetOptions.ForceUpdate
                                            );
                                    }
                                    tcs.SetResult(
                                        YZJBridge.Ok(reqId, lines.Length + " assets imported")
                                    );
                                }
                                catch (Exception ex)
                                {
                                    tcs.SetResult(YZJBridge.Err(reqId, ex.ToString()));
                                }
                            }
                        );
                        return await tcs.Task;
                    }
                    case "select_asset":
                    {
                        string assetPath = (msg.message ?? "").Trim();
                        YZJBridge.PostToMainThread(
                            delegate
                            {
                                var obj = AssetDatabase.LoadMainAssetAtPath(assetPath);
                                if (obj != null)
                                {
                                    Selection.activeObject = obj;
                                    EditorGUIUtility.PingObject(obj);
                                }
                            }
                        );
                        return YZJBridge.Ok(reqId, null);
                    }
                    case "refresh_asset_database":
                    {
                        var tcs = new TaskCompletionSource<BridgeEnvelope>();
                        YZJBridge.PostToMainThread(
                            delegate
                            {
                                try
                                {
                                    AssetDatabase.Refresh();
                                    tcs.SetResult(YZJBridge.Ok(reqId, "ok"));
                                }
                                catch (Exception ex)
                                {
                                    tcs.SetResult(YZJBridge.Err(reqId, ex.ToString()));
                                }
                            }
                        );
                        return await tcs.Task;
                    }
                    // ═══════════════════════════════════════════════════
                    //  Console
                    case "get_console_logs":
                    {
                        ConsoleService.EnsureHooked();
                        int count;
                        int.TryParse(msg.message, out count);
                        return YZJBridge.Ok(
                            reqId,
                            ConsoleService.GetRecent(count > 0 ? count : 50)
                        );
                    }
                    case "clear_console":
                    {
                        ConsoleService.EnsureHooked();
                        return YZJBridge.Ok(reqId, ConsoleService.Clear());
                    }
                    // ═══════════════════════════════════════════════════
                    //  Selection & Find
                    case "get_selection":
                    {
                        var tcs = new TaskCompletionSource<string>();
                        YZJBridge.PostToMainThread(() =>
                        {
                            try
                            {
                                var sel = Selection.activeObject;
                                if (sel == null)
                                    tcs.SetResult("{\"selected\":null}");
                                else
                                    tcs.SetResult(
                                        "{\"selected\":{\"name\":\""
                                            + JsonEscape.String(sel.name)
                                            + "\",\"type\":\""
                                            + JsonEscape.String(sel.GetType().Name)
                                            + "\",\"path\":\""
                                            + JsonEscape.String(AssetDatabase.GetAssetPath(sel) ?? "")
                                            + "\",\"instanceId\":"
                                            + sel.GetInstanceID()
                                            + "}}"
                                    );
                            }
                            catch (Exception ex)
                            {
                                tcs.SetException(ex);
                            }
                        });
                        return YZJBridge.Ok(reqId, await tcs.Task);
                    }
                    case "find_assets":
                    {
                        var tcs = new TaskCompletionSource<string>();
                        string filter = msg.message ?? "";
                        YZJBridge.PostToMainThread(() =>
                        {
                            try
                            {
                                var guids = AssetDatabase.FindAssets(filter);
                                var sb = new System.Text.StringBuilder();
                                sb.Append("{\"count\":")
                                    .Append(guids.Length)
                                    .Append(",\"results\":[");
                                int max = Math.Min(guids.Length, 100);
                                for (int i = 0; i < max; i++)
                                {
                                    if (i > 0)
                                        sb.Append(',');
                                    string p = AssetDatabase.GUIDToAssetPath(guids[i]);
                                    sb.Append("{\"guid\":\"")
                                        .Append(guids[i])
                                        .Append("\",\"path\":\"")
                                        .Append(JsonEscape.String(p))
                                        .Append("\"}");
                                }
                                sb.Append("]}");
                                tcs.SetResult(sb.ToString());
                            }
                            catch (Exception ex)
                            {
                                tcs.SetException(ex);
                            }
                        });
                        return YZJBridge.Ok(reqId, await tcs.Task);
                    }
                    // ═══════════════════════════════════════════════════
                    //  Scene
                    case "get_scene_graph":
                    {
                        var tcs = new TaskCompletionSource<string>();
                        int depth;
                        int.TryParse(msg.message, out depth);
                        YZJBridge.PostToMainThread(() =>
                        {
                            try
                            {
                                tcs.SetResult(SceneService.GetGraph(depth));
                            }
                            catch (Exception ex)
                            {
                                tcs.SetException(ex);
                            }
                        });
                        return YZJBridge.Ok(reqId, await tcs.Task);
                    }
                    case "get_scene_roots":
                    {
                        var tcs = new TaskCompletionSource<string>();
                        YZJBridge.PostToMainThread(() =>
                        {
                            try
                            {
                                tcs.SetResult(SceneService.GetRootNames());
                            }
                            catch (Exception ex)
                            {
                                tcs.SetException(ex);
                            }
                        });
                        return YZJBridge.Ok(reqId, await tcs.Task);
                    }
                    case "play_mode":
                    {
                        // 只读：报告当前 play mode 状态，禁用 enter/exit（会触发域重载、卡 Bridge）
                        bool isPlaying = YZJBridge.IsPlaying;
                        bool isPaused = YZJBridge.IsPaused;
                        string r = isPlaying
                            ? (
                                isPaused
                                    ? "{\"isPlaying\":true,\"isPaused\":true}"
                                    : "{\"isPlaying\":true,\"isPaused\":false}"
                            )
                            : "{\"isPlaying\":false,\"isPaused\":false}";
                        return YZJBridge.Ok(reqId, r);
                    }
                    // ═══════════════════════════════════════════════════
                    //  事件订阅（推 → 拉模型）
                    case "subscribe":
                    {
                        YZJBridge.PostToMainThread(() => EventBus.EnsureHooked());
                        string sub = EventBus.Subscribe(msg.message ?? "all");
                        return YZJBridge.Ok(reqId, "{\"sub\":\"" + sub + "\"}");
                    }
                    case "unsubscribe":
                    {
                        string s = EventBus.Unsubscribe();
                        return YZJBridge.Ok(reqId, "{\"sub\":\"" + s + "\"}");
                    }
                    case "subscribe_status":
                    {
                        return YZJBridge.Ok(reqId, "{\"status\":\"" + EventBus.GetStatus() + "\"}");
                    }
                    case "poll_events":
                    {
                        // message 格式: "timeoutMs|maxItems"，默认 500/50
                        int timeout = 500,
                            maxItems = 50;
                        if (!string.IsNullOrEmpty(msg.message))
                        {
                            var ps = msg.message.Split('|');
                            int.TryParse(ps[0], out timeout);
                            if (ps.Length > 1)
                                int.TryParse(ps[1], out maxItems);
                        }
                        return YZJBridge.Ok(reqId, EventBus.Poll(timeout, maxItems));
                    }
                    // ═══════════════════════════════════════════════════
                    //  Screenshot 写到文件（避免 base64 巨大 JSON 卡传输）
                    case "screenshot_to_file":
                    {
                        var tcs = new TaskCompletionSource<string>();
                        // message 格式: "target|path"，target=game/scene, path=绝对或项目相对
                        var parts = (msg.message ?? "game|").Split('|');
                        string target = parts[0].Trim();
                        string path = parts.Length > 1 ? parts[1].Trim() : "";
                        YZJBridge.PostToMainThread(() =>
                        {
                            try
                            {
                                tcs.SetResult(ScreenshotService.CaptureToFile(target, path));
                            }
                            catch (Exception ex)
                            {
                                tcs.SetException(ex);
                            }
                        });
                        return YZJBridge.Ok(reqId, await tcs.Task);
                    }
                    // ═══════════════════════════════════════════════════
                    //  Screenshot
                    case "screenshot":
                    {
                        var tcs = new TaskCompletionSource<string>();
                        string target = msg.message ?? "game";
                        YZJBridge.PostToMainThread(() =>
                        {
                            try
                            {
                                tcs.SetResult(ScreenshotService.Capture(target, 0, 0));
                            }
                            catch (Exception ex)
                            {
                                tcs.SetException(ex);
                            }
                        });
                        return YZJBridge.Ok(reqId, await tcs.Task);
                    }
                    // ═══════════════════════════════════════════════════
                    //  Component & Property
                    case "get_component":
                    {
                        var tcs = new TaskCompletionSource<string>();
                        string spec = msg.message ?? "";
                        YZJBridge.PostToMainThread(() =>
                        {
                            try
                            {
                                tcs.SetResult(ComponentService.GetComponent(spec));
                            }
                            catch (Exception ex)
                            {
                                tcs.SetException(ex);
                            }
                        });
                        return YZJBridge.Ok(reqId, await tcs.Task);
                    }
                    case "set_property":
                    {
                        // message 格式: "目标名称|属性路径|值"
                        var tcs = new TaskCompletionSource<string>();
                        string spec = msg.message ?? "";
                        YZJBridge.PostToMainThread(() =>
                        {
                            try
                            {
                                tcs.SetResult(ComponentService.SetProperty(spec));
                            }
                            catch (Exception ex)
                            {
                                tcs.SetException(ex);
                            }
                        });
                        return YZJBridge.Ok(reqId, await tcs.Task);
                    }
                    // ═══════════════════════════════════════════════════
                    //  Prefab
                    case "get_prefab_structure":
                    {
                        var tcs = new TaskCompletionSource<string>();
                        string assetPath = msg.message ?? "";
                        YZJBridge.PostToMainThread(() =>
                        {
                            try
                            {
                                tcs.SetResult(ComponentService.GetPrefabStructure(assetPath));
                            }
                            catch (Exception ex)
                            {
                                tcs.SetException(ex);
                            }
                        });
                        return YZJBridge.Ok(reqId, await tcs.Task);
                    }
                    // ═══════════════════════════════════════════════════
                    //  请求统计 / 撤销
                    case "stats":
                        return YZJBridge.Ok(reqId, RequestLog.ToJson());
                    case "stats_reset":
                        RequestLog.Reset();
                        return YZJBridge.Ok(reqId, "{\"ok\":true}");
                    // 注意：这里刻意不提供“全局撤销 N 步”。Undo.PerformUndo() 不区分
                    // 操作是谁做的，会连带撤掉用户自己的编辑，甚至触发整图重导入；
                    // 实测在空组状态下还会把管线请求卡住。撤销改成两条精确路径：
                    //   1) Shader Graph 文件快照 → unity_shadergraph op=undo
                    //   2) Pipeline batch（transactional）自带事务撤销
                    // ═══════════════════════════════════════════════════
                    //  Shader Graph（op 见 ShaderGraphService）
                    case "shadergraph":
                    {
                        // ShaderGraph 的 GraphData / AssetDatabase 都只能在主线程碰
                        var tcs = new TaskCompletionSource<string>();
                        string payload = msg.message ?? "{}";
                        YZJBridge.PostToMainThread(() =>
                        {
                            try
                            {
                                tcs.SetResult(ShaderGraphService.Execute(payload));
                            }
                            catch (Exception ex)
                            {
                                tcs.SetException(ex);
                            }
                        });
                        return YZJBridge.Ok(reqId, await tcs.Task);
                    }
                    // ═══════════════════════════════════════════════════
                    //  物理查询 / 性能采样 / 资产缩略图 / 帧调试
                    case "physics":
                        return await RunOnMain(reqId, () => PhysicsService.Execute(msg.message));
                    case "profiler":
                        return await RunOnMain(reqId, () => ProfilerService.Execute(msg.message));
                    case "asset_preview":
                        return await RunOnMain(reqId, () => AssetPreviewService.Execute(msg.message));
                    case "frame_debugger":
                        return await RunOnMain(reqId, () => FrameDebuggerService.Execute(msg.message));
                    default:
                        return YZJBridge.Err(reqId, "unknown message type: " + msg.type);
                }
            }
            catch (Exception ex)
            {
                return YZJBridge.Err(reqId, ex.ToString());
            }
        }

        /// <summary>把同步服务丢到主线程执行，异常回传给调用方。</summary>
        static async Task<BridgeEnvelope> RunOnMain(string reqId, Func<string> fn)
        {
            var tcs = new TaskCompletionSource<string>();
            YZJBridge.PostToMainThread(
                delegate
                {
                    try { tcs.SetResult(fn()); }
                    catch (Exception ex) { tcs.SetException(ex); }
                }
            );
            return YZJBridge.Ok(reqId, await tcs.Task);
        }
    }
}
