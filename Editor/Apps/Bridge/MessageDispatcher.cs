// ═══════════════════════════════════════════════════════════════
//  MessageDispatcher — 消息路由与处理（11 种消息类型）
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
        internal static async Task<PipeEnvelope> HandleAsync(PipeEnvelope msg)
        {
            string reqId = msg.id;
            try
            {
                switch (msg.type)
                {
                    case "ping":
                        return YZJBridge.Ok(reqId, "pong");
                    case "status":
                    {
                        string s = YZJBridge.IsPlaying
                            ? (YZJBridge.IsPaused ? "playing_paused" : "playing")
                            : "editing";
                        string scene = YZJBridge.ActiveScenePath ?? "";
                        return YZJBridge.Ok(
                            reqId,
                            s + (string.IsNullOrEmpty(scene) ? "" : " " + scene)
                        );
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
                        var tcs = new TaskCompletionSource<PipeEnvelope>();
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
                        var tcs = new TaskCompletionSource<PipeEnvelope>();
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
                                            + sel.name.Replace("\\", "\\\\").Replace("\"", "\\\"")
                                            + "\",\"type\":\""
                                            + sel.GetType().Name
                                            + "\",\"path\":\""
                                            + (AssetDatabase.GetAssetPath(sel) ?? "")
                                                .Replace("\\", "\\\\")
                                                .Replace("\"", "\\\"")
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
                                        .Append(p.Replace("\\", "\\\\").Replace("\"", "\\\""))
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
                    // ═══════════════════════════════════════════════════
                    default:
                        return YZJBridge.Err(reqId, "unknown message type: " + msg.type);
                }
            }
            catch (Exception ex)
            {
                return YZJBridge.Err(reqId, ex.ToString());
            }
        }
    }
}
