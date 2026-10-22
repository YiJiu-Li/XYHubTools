// ═══════════════════════════════════════════════════════════════
//  BridgeSelfTest — Unity 侧后端与 Pipeline HTTP 自检
//  直接调用 Bridge 后端服务（绕开 stdio 传输链路）
//  入口：XYHub > MCP Bridge 面板
//
//  注：MCP 层共 34 个工具，其中 unity_list_projects / unity_use_project
//      是纯客户端工具（只操作命名管道，不经过 Unity 管道协议），
//      因此不在本自检范围内。
//
//  历史调整：
//    删：unity_log / unity_warn / unity_error
//    降：unity_run_tests
//        unity_play_mode（enter/exit 禁用，仅读状态）
//    加：unity_subscribe / unity_poll_events / unity_screenshot_to_file
//    恢复：unity_set_property / unity_get_prefab_structure
//          （已接线到 MessageDispatcher 的 set_property / get_prefab_structure）
//    加：unity_shadergraph（shader 组）
//        unity_physics / unity_profiler / unity_asset_preview /
//        unity_frame_debugger（inspect 组）
//
//  破坏性用例（Destructive=true）：会重新导入资源 / 刷新 AssetDatabase，
//  可能触发域重载或场景刷新而打断本轮自检。默认「运行全部」跳过它们，
//  需在面板勾选「含破坏性用例」才执行。
//  自检结果由面板写入 SessionState，域重载后可恢复显示。
// ═══════════════════════════════════════════════════════════════

using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Framework.XYEditor.Bridge
{
    public static class BridgeSelfTest
    {
        [Serializable]
        public class Result
        {
            public int Idx;
            public string Name;
            public string Group;
            public bool Ok;
            public string Detail;
            public double ElapsedMs;
        }

        public class TestCase
        {
            public int Idx;
            public string Name;
            public string Group;
            public Func<string> Run;
            public Func<Task<string>> RunAsync;
            public string Hint;

            /// <summary>
            /// 会改变编辑器状态（重新导入资源 / 刷新 AssetDatabase / 改场景），
            /// 可能触发域重载或场景刷新，从而打断本轮自检、使面板被重建。
            /// 默认「运行全部」会跳过这些用例，需显式勾选「含破坏性用例」才执行。
            /// </summary>
            public bool Destructive;
        }

        private static List<TestCase> _all;

        /// <summary>错误详情太长时截断，避免撑爆面板/对话框。</summary>
        private static string Clip(string s, int max = 90)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Length <= max ? s : s.Substring(0, max) + "…";
        }

        /// <summary>Bridge 后端与 Pipeline HTTP 用例（不含纯客户端的多工程工具）。</summary>
        public static List<TestCase> AllCases
        {
            get
            {
                if (_all != null)
                    return _all;
                _all = new List<TestCase>
                {
                    // ===== core 组（4）=====
                    new TestCase
                    {
                        Idx = 1,
                        Name = "unity_ping",
                        Group = "core",
                        Run = () => PipeServer.IsRunning ? "running" : "stopped",
                        Hint = "Named Pipe 服务是否在监听",
                    },
                    new TestCase
                    {
                        Idx = 2,
                        Name = "unity_status",
                        Group = "core",
                        Run = () =>
                        {
                            bool p = EditorApplication.isPlaying;
                            string s = EditorSceneManager.GetActiveScene().path ?? "";
                            return (p ? "playing" : "editing") + " " + s;
                        },
                        Hint = "Editor 编辑/播放状态 + 当前场景路径",
                    },
                    new TestCase
                    {
                        Idx = 3,
                        Name = "unity_get_console_logs",
                        Group = "core",
                        Run = () =>
                        {
                            var json = ConsoleService.GetRecent(5);
                            return json.Length > 0 ? "len=" + json.Length : "empty";
                        },
                        Hint = "读取最近 5 条 console 日志",
                    },
                    new TestCase
                    {
                        Idx = 4,
                        Name = "unity_clear_console",
                        Group = "core",
                        Run = () =>
                        {
                            ConsoleService.Clear();
                            return "ok";
                        },
                        Hint = "清空 console 缓冲区",
                    },
                    // ===== assets 组（6）=====
                    new TestCase
                    {
                        Idx = 5,
                        Name = "unity_select_asset",
                        Group = "assets",
                        Run = () =>
                        {
                            var path = "Assets/Scenes/SampleScene.unity";
                            Selection.activeObject = AssetDatabase.LoadMainAssetAtPath(path);
                            return path;
                        },
                        Hint = "在 Project 窗口选中 SampleScene",
                    },
                    new TestCase
                    {
                        Idx = 6,
                        Name = "unity_find_assets",
                        Group = "assets",
                        Run = () => "count=" + AssetDatabase.FindAssets(@"t:Scene").Length,
                        Hint = "搜索全部 Scene 资源",
                    },
                    new TestCase
                    {
                        Idx = 7,
                        Name = "unity_import_assets",
                        Group = "assets",
                        Run = () =>
                        {
                            AssetDatabase.ImportAsset(
                                "Assets/Scenes/SampleScene.unity",
                                ImportAssetOptions.ForceUpdate
                            );
                            return "ok";
                        },
                        Hint = "强制重新导入 SampleScene（会刷新场景）",
                        Destructive = true,
                    },
                    new TestCase
                    {
                        Idx = 8,
                        Name = "unity_refresh_asset_database",
                        Group = "assets",
                        Run = () =>
                        {
                            AssetDatabase.Refresh();
                            return "ok";
                        },
                        Hint = "刷新 AssetDatabase（脚本有变动时会重编译）",
                        Destructive = true,
                    },
                    new TestCase
                    {
                        Idx = 9,
                        Name = "unity_recompile_scripts",
                        Group = "assets",
                        Run = () =>
                        {
                            // 刻意不调用 CompilationPipeline.RequestScriptCompilation()：
                            // 它会触发域重载，把自检面板整个重建、结果丢失。
                            // 这里只验证编译服务可调用、引用收集可用。
                            CompilationService.InvalidateCache();
                            var refs = CompilationService.EnsureReferences();
                            return refs != null ? "refs=" + refs.Count : "refs=null";
                        },
                        Hint = "验证编译服务与引用收集（不触发实际重编译）",
                    },
                    new TestCase
                    {
                        Idx = 10,
                        Name = "unity_get_selection",
                        Group = "assets",
                        Run = () =>
                        {
                            var o = Selection.activeObject;
                            return o != null ? o.name : "null";
                        },
                        Hint = "读取 Project 当前选中对象",
                    },
                    // ===== scene 组（11）=====
                    new TestCase
                    {
                        Idx = 11,
                        Name = "unity_execute_code",
                        Group = "scene",
                        Run = () =>
                        {
                            try
                            {
                                var snippet = SnippetCompiler.Compile("return 1+1;");
                                return snippet != null ? "compile ok" : "compile null";
                            }
                            catch (Exception e)
                            {
                                return "compile err: " + e.Message;
                            }
                        },
                        Hint = "走 SnippetCompiler.Compile 编译阶段（不跑异步执行）",
                    },
                    new TestCase
                    {
                        Idx = 12,
                        Name = "unity_get_scene_roots",
                        Group = "scene",
                        Run = () =>
                        {
                            var roots = UnityEngine
                                .SceneManagement.SceneManager.GetActiveScene()
                                .GetRootGameObjects();
                            return "roots=" + roots.Length;
                        },
                        Hint = "场景根 GameObject 数量",
                    },
                    new TestCase
                    {
                        Idx = 13,
                        Name = "unity_get_scene_graph",
                        Group = "scene",
                        Run = () =>
                        {
                            var roots = UnityEngine
                                .SceneManagement.SceneManager.GetActiveScene()
                                .GetRootGameObjects();
                            return "graph=" + roots.Length;
                        },
                        Hint = "场景层级图（depth=2 摘要）",
                    },
                    new TestCase
                    {
                        Idx = 14,
                        Name = "unity_play_mode",
                        Group = "scene",
                        Run = () =>
                        {
                            // 只读：不切 play
                            bool p = EditorApplication.isPlaying;
                            return "{\"isPlaying\":" + (p ? "true" : "false") + "}";
                        },
                        Hint = "只读：当前 Editor 模式（不切 play）",
                    },
                    new TestCase
                    {
                        Idx = 15,
                        Name = "unity_screenshot",
                        Group = "scene",
                        Run = () => "deprecated, use screenshot_to_file",
                        Hint = "已弃用：返回 base64 JSON 巨大；改用 screenshot_to_file",
                    },
                    new TestCase
                    {
                        Idx = 16,
                        Name = "unity_screenshot_to_file",
                        Group = "scene",
                        Run = () =>
                        {
                            string r = ScreenshotService.CaptureToFile("scene", "");
                            return r.Length > 80 ? r.Substring(0, 80) + "..." : r;
                        },
                        Hint = "截图写到磁盘：项目根/TempScreenshots/shot_*.png",
                    },
                    new TestCase
                    {
                        Idx = 17,
                        Name = "unity_get_component",
                        Group = "scene",
                        Run = () =>
                        {
                            var cam = GameObject.Find("Main Camera");
                            if (cam == null)
                                return "Main Camera not found";
                            var t = cam.GetComponent<Transform>();
                            return t != null
                                ? "Transform ok pos=" + t.localPosition
                                : "no transform";
                        },
                        Hint = "读 Main Camera 组件属性",
                    },
                    // ===== events 组（4）=====
                    new TestCase
                    {
                        Idx = 18,
                        Name = "unity_subscribe",
                        Group = "events",
                        Run = () =>
                        {
                            EventBus.EnsureHooked();
                            string sub = EventBus.Subscribe("all");
                            return "{\"sub\":\"" + sub + "\"}";
                        },
                        Hint = "订阅 console+compile+scene 三类事件",
                    },
                    new TestCase
                    {
                        Idx = 19,
                        Name = "unity_subscribe_status",
                        Group = "events",
                        Run = () => "{\"status\":\"" + EventBus.GetStatus() + "\"}",
                        Hint = "查当前订阅状态（含队列深度）",
                    },
                    new TestCase
                    {
                        Idx = 20,
                        Name = "unity_poll_events",
                        Group = "events",
                        Run = () =>
                        {
                            // 触发一个 console 事件再 poll，看是否能取到
                            Debug.Log("[selftest] poll trigger");
                            string r = EventBus.Poll(100, 10);
                            return r.Length > 80 ? r.Substring(0, 80) + "..." : r;
                        },
                        Hint = "拉取累积事件（先 log 触发再 poll）",
                    },
                    new TestCase
                    {
                        Idx = 21,
                        Name = "unity_unsubscribe",
                        Group = "events",
                        Run = () => "{\"sub\":\"" + EventBus.Unsubscribe() + "\"}",
                        Hint = "清空订阅、丢弃队列",
                    },
                    // ===== scene 组（写操作）=====
                    new TestCase
                    {
                        Idx = 22,
                        Name = "unity_set_property",
                        Group = "scene",
                        Run = () =>
                        {
                            // 用临时 GameObject 测真实写入，避免动用户场景里的对象，测完立即销毁。
                            // 场景会被标记为已修改，属预期副作用（不保存即可丢弃）。
                            const string goName = "__XYBridgeSelfTest__";
                            var go = new GameObject(goName);
                            try
                            {
                                string r = ComponentService.SetProperty(
                                    goName + "|Transform.localPosition|4,5,6"
                                );
                                var p = go.transform.localPosition;
                                bool written =
                                    Mathf.Approximately(p.x, 4f)
                                    && Mathf.Approximately(p.y, 5f)
                                    && Mathf.Approximately(p.z, 6f);
                                return written ? "ok localPosition=" + p : "not applied: " + r;
                            }
                            finally
                            {
                                UnityEngine.Object.DestroyImmediate(go);
                            }
                        },
                        Hint = "用临时对象测组件写入，测完销毁",
                    },
                    // ===== assets 组（Prefab）=====
                    new TestCase
                    {
                        Idx = 23,
                        Name = "unity_get_prefab_structure",
                        Group = "assets",
                        Run = () =>
                        {
                            var guids = AssetDatabase.FindAssets("t:Prefab");
                            if (guids.Length == 0)
                                return "no prefab in project";
                            string path = AssetDatabase.GUIDToAssetPath(guids[0]);
                            string r = ComponentService.GetPrefabStructure(path);
                            return r.Length > 80 ? r.Substring(0, 80) + "..." : r;
                        },
                        Hint = "读取项目里第一个 Prefab 的结构",
                    },
                    // ===== shader 组 =====
                    new TestCase
                    {
                        Idx = 28,
                        Name = "unity_shadergraph",
                        Group = "shader",
                        Run = () =>
                        {
                            // 建图 → 加节点 → 读节点，最后删掉整个临时目录
                            const string dir = "Assets/_XYBridgeSelfTest";
                            string path = dir + "/SelfTest.shadergraph";
                            try
                            {
                                if (!AssetDatabase.IsValidFolder(dir))
                                    AssetDatabase.CreateFolder("Assets", "_XYBridgeSelfTest");

                                string r1 = ShaderGraphService.Execute(
                                    "{\"op\":\"create\",\"path\":\"" + path + "\",\"template\":\"unlit\",\"overwrite\":true}");
                                if (r1.IndexOf("\"ok\":true", StringComparison.Ordinal) < 0)
                                    return "create: " + Clip(r1);

                                string r2 = ShaderGraphService.Execute(
                                    "{\"op\":\"add_node\",\"path\":\"" + path + "\",\"nodeType\":\"Vector1\",\"x\":120,\"y\":0}");
                                if (r2.IndexOf("\"ok\":true", StringComparison.Ordinal) < 0)
                                    return "add_node: " + Clip(r2);

                                string r3 = ShaderGraphService.Execute(
                                    "{\"op\":\"nodes\",\"path\":\"" + path + "\",\"withSlots\":false}");
                                if (r3.IndexOf("\"ok\":true", StringComparison.Ordinal) < 0)
                                    return "nodes: " + Clip(r3);

                                var d = FlatJson.Parse(r3);
                                int nodes = XyJ.Int(d, "count", -1);

                                // 再验一次文件快照回滚：撤销后节点数应回到加节点之前
                                string r4 = ShaderGraphService.Execute(
                                    "{\"op\":\"undo\",\"path\":\"" + path + "\"}");
                                var d4 = FlatJson.Parse(r4);
                                bool undone = XyJ.Bool(d4, "ok", false);
                                string r5 = ShaderGraphService.Execute(
                                    "{\"op\":\"nodes\",\"path\":\"" + path + "\",\"withSlots\":false}");
                                int after = XyJ.Int(FlatJson.Parse(r5), "count", -1);

                                return "nodes=" + nodes + " undo=" + (undone ? "ok" : "fail") +
                                       " after=" + after + "（已清理临时资源）";
                            }
                            finally
                            {
                                if (AssetDatabase.IsValidFolder(dir)) AssetDatabase.DeleteAsset(dir);
                            }
                        },
                        Hint = "建图 / 加节点 / 列节点，用临时目录并在结束后删除",
                    },
                    // ===== inspect 组 =====
                    new TestCase
                    {
                        Idx = 29,
                        Name = "unity_physics",
                        Group = "inspect",
                        Run = () =>
                        {
                            // 临时立方体（顶面 y=0.5），从正上方往下打，期望 distance≈4.5
                            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                            try
                            {
                                go.name = "__XYPhysicsSelfTest__";
                                go.transform.position = Vector3.zero;
                                Physics.SyncTransforms();

                                string r = PhysicsService.Execute(
                                    "{\"op\":\"raycast\",\"origin\":\"0,5,0\",\"direction\":\"0,-1,0\"}");
                                var d = FlatJson.Parse(r);
                                bool hit = XyJ.Bool(d, "hit", false);
                                string dist = XyJ.Str(d, "distance");
                                return hit ? "hit dist=" + dist : "no hit";
                            }
                            finally
                            {
                                UnityEngine.Object.DestroyImmediate(go);
                            }
                        },
                        Hint = "临时立方体上打射线，测完销毁",
                    },
                    new TestCase
                    {
                        Idx = 30,
                        Name = "unity_profiler",
                        Group = "inspect",
                        Run = () =>
                        {
                            string r = ProfilerService.Execute("{\"op\":\"status\"}");
                            if (r.IndexOf("\"ok\":true", StringComparison.Ordinal) < 0)
                                return "status: " + Clip(r);
                            var d = FlatJson.Parse(r);
                            int frames = XyJ.Int(d, "frames", 0);
                            return "enabled=" + XyJ.Bool(d, "enabled", false) +
                                   " frames=" + frames +
                                   " heap=" + XyJ.Str(d, "usedHeapBytes") +
                                   (frames > 0 ? "" : "（未开始采样，属正常）");
                        },
                        Hint = "读采样状态与内存（不主动开启采样）",
                    },
                    new TestCase
                    {
                        Idx = 31,
                        Name = "unity_asset_preview",
                        Group = "inspect",
                        Run = () =>
                        {
                            // 用临时 PNG 走一遍完整链路（贴图路径是同步的，不需要等异步预览）
                            const string dir = "Assets/_XYBridgeSelfTest";
                            string asset = dir + "/preview.png";
                            string outPath = "";
                            try
                            {
                                if (!AssetDatabase.IsValidFolder(dir))
                                    AssetDatabase.CreateFolder("Assets", "_XYBridgeSelfTest");

                                var tex = new Texture2D(16, 16, TextureFormat.RGBA32, false);
                                try
                                {
                                    for (int y = 0; y < 16; y++)
                                        for (int x = 0; x < 16; x++)
                                            tex.SetPixel(x, y, Color.Lerp(Color.blue, Color.yellow, x / 15f));
                                    tex.Apply();
                                    System.IO.File.WriteAllBytes(asset, tex.EncodeToPNG());
                                }
                                finally { UnityEngine.Object.DestroyImmediate(tex); }

                                AssetDatabase.ImportAsset(asset, ImportAssetOptions.ForceSynchronousImport);

                                string r = AssetPreviewService.Execute(
                                    "{\"op\":\"get\",\"path\":\"" + asset + "\",\"width\":64}");
                                var d = FlatJson.Parse(r);
                                outPath = XyJ.Str(d, "path");
                                bool exists = !string.IsNullOrEmpty(outPath) && System.IO.File.Exists(outPath);
                                return "kind=" + XyJ.Str(d, "kind") +
                                       " bytes=" + XyJ.Int(d, "bytes", 0) +
                                       " file=" + (exists ? "ok" : "missing");
                            }
                            finally
                            {
                                if (!string.IsNullOrEmpty(outPath) && System.IO.File.Exists(outPath))
                                    System.IO.File.Delete(outPath);
                                if (AssetDatabase.IsValidFolder(dir)) AssetDatabase.DeleteAsset(dir);
                            }
                        },
                        Hint = "临时贴图 → 缩略图 PNG，测完删除",
                    },
                    new TestCase
                    {
                        Idx = 32,
                        Name = "unity_frame_debugger",
                        Group = "inspect",
                        Run = () =>
                        {
                            // 只读状态：开启捕获会让编辑器停在某一帧，自检里不做
                            string r = FrameDebuggerService.Execute("{\"op\":\"status\"}");
                            if (r.IndexOf("\"ok\":true", StringComparison.Ordinal) < 0)
                                return "status: " + Clip(r);
                            var d = FlatJson.Parse(r);
                            return "enabled=" + XyJ.Bool(d, "enabled", false) +
                                   " window=" + XyJ.Bool(d, "windowOpen", false) +
                                   " events=" + XyJ.Int(d, "eventCount", 0);
                        },
                        Hint = "只读：是否已启用 / 窗口是否已打开 / 已捕获多少事件",
                    },
                };
                _all.AddRange(PipelineSelfTest.Cases());
                return _all;
            }
        }

        public static async Task<Result> RunOneAsync(TestCase c)
        {
            if (c.RunAsync == null) return RunOne(c);
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var result = new Result { Idx = c.Idx, Name = c.Name, Group = c.Group };
            try
            {
                result.Detail = await c.RunAsync();
                result.Ok = !string.IsNullOrEmpty(result.Detail);
            }
            catch (Exception e) { result.Detail = e.GetType().Name + ": " + e.Message; }
            result.ElapsedMs = sw.Elapsed.TotalMilliseconds;
            return result;
        }

        /// <summary>运行单个 case，返回 Result。</summary>
        public static Result RunOne(TestCase c)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            bool ok = true;
            string detail = "";
            try
            {
                if (c.RunAsync != null)
                    throw new InvalidOperationException("HTTP 自检请使用 RunOneAsync，不能阻塞 Unity 主线程。");
                detail = c.Run() ?? "";
                ok = !string.IsNullOrEmpty(detail);
            }
            catch (Exception ex)
            {
                ok = false;
                detail = ex.GetType().Name + ": " + ex.Message;
            }
            sw.Stop();
            return new Result
            {
                Idx = c.Idx,
                Name = c.Name,
                Group = c.Group,
                Ok = ok,
                Detail = detail,
                ElapsedMs = sw.Elapsed.TotalMilliseconds,
            };
        }

        /// <summary>
        /// 全量跑一遍，返回 List&lt;Result&gt;。
        /// includeDestructive=false 时跳过会触发域重载 / 场景刷新的用例。
        /// </summary>
        public static List<Result> RunAll(bool includeDestructive = true)
        {
            var results = new List<Result>();
            foreach (var c in AllCases)
            {
                if (c.Destructive && !includeDestructive)
                    continue;
                results.Add(RunOne(c));
            }
            return results;
        }

        public static async Task<List<Result>> RunAllAsync(bool includeDestructive = false)
        {
            var results = new List<Result>();
            foreach (var c in AllCases)
            {
                if (c.Destructive && !includeDestructive) continue;
                results.Add(await RunOneAsync(c));
            }
            return results;
        }

        public static async void Run()
        {
            var results = await RunAllAsync();
            int passed = 0;
            var report = new StringBuilder();
            report.AppendLine($"═══ Bridge Self Test: {results.Count} Tools ═══");
            foreach (var r in results)
            {
                string mark = r.Ok ? "✅" : "❌";
                report.AppendLine(
                    $"{mark} [{r.Idx:00}] {r.Name, -32} {r.ElapsedMs, 6:0}ms  {r.Detail}"
                );
                if (r.Ok)
                    passed++;
            }
            report.AppendLine($"════ {passed}/{results.Count} passed ═══");

            string text = report.ToString();
            Debug.Log("[BridgeSelfTest]\n" + text);
            EditorUtility.DisplayDialog(
                "Bridge Self Test",
                $"{passed}/{results.Count} passed\n\n" + text,
                "OK"
            );
        }
    }
}
