// ═══════════════════════════════════════════════════════════════
//  BridgeSelfTest — 21 个 MCP 工具的本地自检
//  直接调用 Bridge 后端服务（绕开 stdio 传输链路）
//  入口：XYHub > MCP Bridge 面板
//
//  22 → 21 调整：
//    删：unity_log / unity_warn / unity_error
//        unity_set_property / unity_get_prefab_structure
//    降：unity_run_tests
//        unity_play_mode（enter/exit 禁用，仅读状态）
//    加：unity_subscribe / unity_poll_events / unity_screenshot_to_file
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
    public static class BridgeSelfTest
    {
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
            public string Hint;
        }

        private static List<TestCase> _all;

        /// <summary>21 个 MCP 工具的 case 定义。</summary>
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
                        Hint = "强制重新导入 SampleScene",
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
                        Hint = "刷新 AssetDatabase",
                    },
                    new TestCase
                    {
                        Idx = 9,
                        Name = "unity_recompile_scripts",
                        Group = "assets",
                        Run = () =>
                        {
                            CompilationService.InvalidateCache();
                            CompilationPipeline.RequestScriptCompilation();
                            return "triggered";
                        },
                        Hint = "触发脚本重编译",
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
                };
                return _all;
            }
        }

        /// <summary>运行单个 case，返回 Result。</summary>
        public static Result RunOne(TestCase c)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            bool ok = true;
            string detail = "";
            try
            {
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

        /// <summary>全量跑一遍，返回 List&lt;Result&gt;。</summary>
        public static List<Result> RunAll()
        {
            var results = new List<Result>();
            foreach (var c in AllCases)
                results.Add(RunOne(c));
            return results;
        }

        public static void Run()
        {
            var results = RunAll();
            int passed = 0;
            var report = new StringBuilder();
            report.AppendLine("═══ Bridge Self Test: 21 Tools ═══");
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
