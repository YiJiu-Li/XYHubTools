// ═══════════════════════════════════════════════════════════════
//  BridgeStatusTool — MCP Bridge 状态面板（嵌入 XY Editor Hub）
//
//  布局遵循框架约定：
//    · GUILayout 自动布局（与 XYEditorToolBase 的做法一致，不再手算 Rect）
//    · 样式走 BridgeStyles 缓存（皮肤切换自动重建），不在 OnGUI 里 new GUIStyle
//    · 通用样式复用 XYEditorStyles
//  工具名与描述由 Hub 顶栏统一显示，面板内不再重复画标题。
// ═══════════════════════════════════════════════════════════════

using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Framework.XYEditor.Bridge
{
    [XYTool(order: 100, tags: new[] { "MCP", "Bridge", "AI", "管道", "连接" })]
    public class BridgeStatusTool : IXYEditorTool
    {
        public string ToolName => "MCP Bridge";
        public string Category => "服务";
        public string Description => "AI MCP 桥接 — Unity 管道服务 + Codex stdio 配置";
        public string DocumentPath => null;
        public Texture2D Icon => EditorGUIUtility.FindTexture("d_NetworkAnimator Icon");

        // ── SessionState 键 ───────────────────────────────────────────────
        private const string SessionKeyResults = "XYBridge_SelfTestResults";
        private const string SessionKeyIncludeDestructive = "XYBridge_SelfTestIncludeDestructive";
        private const string SessionKeyShowTests = "XYBridge_ShowTests";
        private const string SessionKeyShowLog = "XYBridge_ShowLog";

        // ── 尺寸 ──────────────────────────────────────────────────────────
        private const float RowH = 26f;
        private const float ChipH = 20f;
        private const float BtnH = 24f;

        // ── 状态 ──────────────────────────────────────────────────────────
        private Vector2 _scroll;
        private readonly List<string> _statusLog = new List<string>(64);
        private bool _showLog = true;

        // 自检结果持久化到 SessionState：破坏性用例可能触发域重载，
        // 本工具实例会被 Hub 反射重建，只放实例字段的话结果会丢。
        private readonly List<BridgeSelfTest.Result> _testResults =
            new List<BridgeSelfTest.Result>();
        private bool _showTests = true;
        private Vector2 _testScroll;
        private string _testFilter = "all"; // all / core / assets / scene / events / shader / inspect / pipeline
        private string _testFilterStatus = "all"; // all / ok / fail
        private bool _includeDestructive;

        [Serializable]
        private class ResultStore
        {
            public List<BridgeSelfTest.Result> items = new List<BridgeSelfTest.Result>();
        }

        // ═══════════════════════════════════════════════════════════════
        //  生命周期
        // ═══════════════════════════════════════════════════════════════

        public void OnEnable()
        {
            _scroll = Vector2.zero;
            _statusLog.Clear();
            LoadTestResults();
            _showTests = SessionState.GetBool(SessionKeyShowTests, true);
            _showLog = SessionState.GetBool(SessionKeyShowLog, true);
            _includeDestructive = SessionState.GetBool(SessionKeyIncludeDestructive, false);
        }

        public void OnDisable() { }

        // ═══════════════════════════════════════════════════════════════
        //  结果持久化
        // ═══════════════════════════════════════════════════════════════

        private void LoadTestResults()
        {
            _testResults.Clear();
            try
            {
                string json = SessionState.GetString(SessionKeyResults, "");
                if (string.IsNullOrEmpty(json))
                    return;
                var store = JsonUtility.FromJson<ResultStore>(json);
                if (store != null && store.items != null)
                    _testResults.AddRange(store.items);
            }
            catch { }
        }

        private void SaveTestResults()
        {
            try
            {
                SessionState.SetString(
                    SessionKeyResults,
                    JsonUtility.ToJson(new ResultStore { items = _testResults })
                );
            }
            catch { }
        }

        // ═══════════════════════════════════════════════════════════════
        //  主绘制
        // ═══════════════════════════════════════════════════════════════

        private readonly PipelineCommandBrowser _commandBrowser = new PipelineCommandBrowser();

        public void OnGUI(Rect rect)
        {
            BridgeStyles.EnsureInited();

            GUILayout.BeginArea(rect);
            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            GUILayout.Space(12);

            DrawStatusCard();
            GUILayout.Space(10);
            DrawControls();
            DrawDivider();
            _commandBrowser.Draw();
            DrawDivider();
            DrawTestPanel();
            DrawDivider();
            DrawLogPanel();

            GUILayout.Space(16);
            EditorGUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        // ═══════════════════════════════════════════════════════════════
        //  管道状态卡片
        // ═══════════════════════════════════════════════════════════════

        private void DrawStatusCard()
        {
            bool running = YZJBridge.IsPipeServerRunning;
            bool connected = YZJBridge.IsPipeConnected;

            EditorGUILayout.BeginVertical(BridgeStyles.Card);

            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label(
                    running ? "● 管道服务运行中" : "○ 管道服务已停止",
                    running ? BridgeStyles.StatusOnStyle : BridgeStyles.StatusOffStyle
                );
                GUILayout.FlexibleSpace();
                GUILayout.Label(
                    connected ? "请求处理中" : "空闲",
                    BridgeStyles.MutedRight,
                    GUILayout.Width(84)
                );
            }

            GUILayout.Space(6);

            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label("管道名", BridgeStyles.SectionLabel, GUILayout.Width(46));
                string pipe = YZJBridge.PipeDisplayName;
                GUILayout.Label(string.IsNullOrEmpty(pipe) ? "—" : pipe, BridgeStyles.Mono);
            }

            GUILayout.Space(3);

            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label("Hub 脚本", BridgeStyles.SectionLabel, GUILayout.Width(46));
                bool installed = System.IO.File.Exists(BridgeSetup.StableScript);
                GUILayout.Label(
                    (installed ? "" : "（未安装）") + BridgeSetup.StableScript,
                    installed ? BridgeStyles.Mono : BridgeStyles.Muted
                );
            }

            EditorGUILayout.LabelField("脚本检查", BridgeSetup.ScriptStatus, BridgeStyles.MutedWrap);
            EditorGUILayout.LabelField("运行版本", "通过 MCP unity_status 查看；更新脚本后需重新连接 MCP。", BridgeStyles.MutedWrap);
            GUILayout.Space(3);

            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label("Pipeline", BridgeStyles.SectionLabel, GUILayout.Width(60));
                GUILayout.Label(XY.PipelineBootstrap.PipelineInstaller.Status, BridgeStyles.MutedWrap);
                if (GUILayout.Button("检查 / 重试", BridgeStyles.Button, GUILayout.Width(90)))
                    XY.PipelineBootstrap.PipelineInstaller.Retry();
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label("说明", BridgeStyles.SectionLabel, GUILayout.Width(46));
                GUILayout.Label(
                    "Hub 脚本自动发现所有 Unity 实例，MCP 配置只需写一次；多开时用 unity_use_project 切换。",
                    BridgeStyles.MutedWrap
                );
            }

            EditorGUILayout.EndVertical();
        }

        // ═══════════════════════════════════════════════════════════════
        //  操作按钮
        // ═══════════════════════════════════════════════════════════════

        private void DrawControls()
        {
            bool running = YZJBridge.IsPipeServerRunning;

            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label("管道服务", BridgeStyles.SectionLabel, GUILayout.Width(46));

                using (new EditorGUI.DisabledScope(running))
                {
                    if (
                        GUILayout.Button(
                            "启动",
                            BridgeStyles.Button,
                            GUILayout.Height(BtnH),
                            GUILayout.Width(64)
                        )
                    )
                    {
                        YZJBridge.Start();
                        AddLog("Bridge 已启动");
                    }
                }

                using (new EditorGUI.DisabledScope(!running))
                {
                    if (
                        GUILayout.Button(
                            "停止",
                            BridgeStyles.Button,
                            GUILayout.Height(BtnH),
                            GUILayout.Width(64)
                        )
                    )
                    {
                        YZJBridge.Stop();
                        AddLog("Bridge 已停止");
                    }
                }

                if (
                    GUILayout.Button(
                        "重启",
                        BridgeStyles.Button,
                        GUILayout.Height(BtnH),
                        GUILayout.Width(64)
                    )
                )
                {
                    YZJBridge.Stop();
                    YZJBridge.Start();
                    AddLog("Bridge 已重启");
                }

                GUILayout.FlexibleSpace();

                if (
                    GUILayout.Button(
                        "安装 / 更新 MCP（一次性）",
                        BridgeStyles.Button,
                        GUILayout.Height(BtnH),
                        GUILayout.Width(150)
                    )
                )
                {
                    BridgeSetup.SetupAll();
                    AddLog("MCP 配置已更新（单条 " + BridgeSetup.StableServerName + "）");
                    AddLog("Hub 脚本: " + BridgeSetup.StableScript);
                }
            }
        }

        // ═══════════════════════════════════════════════════════════════
        //  自检
        // ═══════════════════════════════════════════════════════════════

        private bool _testsRunning;

        private async void RunAllTests()
        {
            if (_testsRunning) return;
            _testsRunning = true;
            _testResults.Clear();
            SaveTestResults();

            int skipped = 0;
            foreach (var c in BridgeSelfTest.AllCases)
            {
                // 破坏性用例默认跳过：它们会重新导入资源 / 刷新 AssetDatabase，
                // 可能触发域重载打断本轮自检，反而看不到结果
                if (c.Destructive && !_includeDestructive)
                {
                    skipped++;
                    continue;
                }
                _testResults.Add(await BridgeSelfTest.RunOneAsync(c));
                // 每个用例后立即落盘，万一中途域重载也能保住已完成的部分
                SaveTestResults();
            }

            _testsRunning = false;
            int passed = 0;
            foreach (var r in _testResults)
                if (r.Ok)
                    passed++;
            string msg = $"[Run All] {passed}/{_testResults.Count} passed";
            if (skipped > 0)
                msg += $"，跳过 {skipped} 个破坏性用例";
            AddLog(msg);
            UnityEngine.Debug.Log("[BridgeSelfTest] " + msg);
            _showTests = true;
            RequestPanelRepaint();
        }

        private async void RunSingleTest(int idx)
        {
            var c = BridgeSelfTest.AllCases.Find(x => x.Idx == idx);
            if (c == null)
            {
                AddLog("test idx not found: " + idx);
                return;
            }

            if (_testsRunning) return;
            _testsRunning = true;
            var result = await BridgeSelfTest.RunOneAsync(c);
            _testsRunning = false;
            bool replaced = false;
            for (int i = 0; i < _testResults.Count; i++)
            {
                if (_testResults[i].Idx == idx)
                {
                    _testResults[i] = result;
                    replaced = true;
                    break;
                }
            }
            if (!replaced)
                _testResults.Add(result);

            SaveTestResults();

            string msg =
                $"[#{idx:00} {c.Name}] {(result.Ok ? "OK" : "FAIL")} {result.Detail} ({result.ElapsedMs:0}ms)";
            AddLog(msg);
            UnityEngine.Debug.Log("[BridgeSelfTest] " + msg);
            RequestPanelRepaint();
        }

        private void DrawTestPanel()
        {
            int passed = 0;
            foreach (var r in _testResults)
                if (r.Ok)
                    passed++;
            int total = _testResults.Count;

            bool expanded = EditorGUILayout.Foldout(
                _showTests,
                $"工具自检  ({passed}/{total} 通过)",
                true,
                BridgeStyles.Foldout
            );
            if (expanded != _showTests)
            {
                _showTests = expanded;
                SessionState.SetBool(SessionKeyShowTests, _showTests);
            }
            if (!_showTests)
                return;

            GUILayout.Space(6);

            // ── 动作行 ──
            using (new EditorGUI.DisabledScope(_testsRunning))
            using (new EditorGUILayout.HorizontalScope())
            {
                if (
                    GUILayout.Button(
                        "运行全部",
                        BridgeStyles.Button,
                        GUILayout.Height(BtnH),
                        GUILayout.Width(80)
                    )
                )
                    RunAllTests();

                if (
                    GUILayout.Button(
                        "清空结果",
                        BridgeStyles.Button,
                        GUILayout.Height(BtnH),
                        GUILayout.Width(80)
                    )
                )
                {
                    _testResults.Clear();
                    SaveTestResults();
                }

                GUILayout.Space(10);

                bool include = GUILayout.Toggle(
                    _includeDestructive,
                    " 含破坏性用例",
                    BridgeStyles.TinyButton,
                    GUILayout.Height(ChipH),
                    GUILayout.Width(118)
                );
                if (include != _includeDestructive)
                {
                    _includeDestructive = include;
                    SessionState.SetBool(SessionKeyIncludeDestructive, _includeDestructive);
                }

                GUILayout.FlexibleSpace();

                int destructive = CountDestructiveCases();
                if (destructive > 0)
                    GUILayout.Label(
                        $"{destructive} 个破坏性用例默认跳过",
                        BridgeStyles.MutedRight,
                        GUILayout.Width(160)
                    );
            }

            GUILayout.Space(6);

            // ── 筛选行 ──
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label("组", BridgeStyles.SectionLabel, GUILayout.Width(18));
                DrawChip(ref _testFilter, "all", "全部", 44);
                DrawChip(ref _testFilter, "core", "core", 46);
                DrawChip(ref _testFilter, "assets", "assets", 52);
                DrawChip(ref _testFilter, "scene", "scene", 50);
                DrawChip(ref _testFilter, "events", "events", 52);
                DrawChip(ref _testFilter, "shader", "shader", 56);
                DrawChip(ref _testFilter, "inspect", "inspect", 58);
                DrawChip(ref _testFilter, "pipeline", "Pipeline", 65);

                GUILayout.Space(14);

                GUILayout.Label("状态", BridgeStyles.SectionLabel, GUILayout.Width(30));
                DrawChip(ref _testFilterStatus, "all", "全部", 44);
                DrawChip(ref _testFilterStatus, "ok", "成功", 44);
                DrawChip(ref _testFilterStatus, "fail", "失败", 44);
            }

            GUILayout.Space(6);

            // Always enumerate definitions: saved results must not hide newly added cases.
            if (_testsRunning)
                GUILayout.Label("正在自检，请稍候…（HTTP 请求异步执行）", BridgeStyles.Muted);
            _testScroll = EditorGUILayout.BeginScrollView(_testScroll, GUILayout.Height(320));
            int visible = 0;
            foreach (var c in BridgeSelfTest.AllCases)
            {
                if (_testFilter != "all" && c.Group != _testFilter) continue;
                var result = _testResults.Find(r => r.Idx == c.Idx);
                if (result != null && !PassesFilter(result)) continue;
                if (result == null && _testFilterStatus != "all") continue;
                visible++;
                using (new EditorGUI.DisabledScope(_testsRunning))
                {
                    if (result == null) DrawCaseRow(c);
                    else DrawResultRow(result);
                }
            }
            if (visible == 0) GUILayout.Label("当前筛选下没有用例。", BridgeStyles.Muted);
            EditorGUILayout.EndScrollView();
        }

        private bool PassesFilter(BridgeSelfTest.Result r)
        {
            if (_testFilter != "all" && r.Group != _testFilter)
                return false;
            if (_testFilterStatus == "ok" && !r.Ok)
                return false;
            if (_testFilterStatus == "fail" && r.Ok)
                return false;
            return true;
        }

        private static int CountDestructiveCases()
        {
            int n = 0;
            foreach (var c in BridgeSelfTest.AllCases)
                if (c.Destructive)
                    n++;
            return n;
        }

        private void DrawResultRow(BridgeSelfTest.Result r)
        {
            using (
                new EditorGUILayout.HorizontalScope(r.Ok ? BridgeStyles.RowOk : BridgeStyles.RowFail)
            )
            {
                GUILayout.Label(
                    r.Ok ? "✓" : "✗",
                    r.Ok ? BridgeStyles.MarkPass : BridgeStyles.MarkFail,
                    GUILayout.Width(16)
                );
                GUILayout.Label($"[{r.Idx:00}]", BridgeStyles.Muted, GUILayout.Width(30));
                GUILayout.Label(r.Name, BridgeStyles.RowName, GUILayout.Width(200));
                GUILayout.Label($"{r.ElapsedMs:0} ms", BridgeStyles.Muted, GUILayout.Width(52));

                string det = r.Detail ?? "";
                if (det.Length > 60)
                    det = det.Substring(0, 60) + "…";
                GUILayout.Label(det, BridgeStyles.Muted);

                if (GUILayout.Button("重跑", BridgeStyles.TinyButton, GUILayout.Width(40)))
                    RunSingleTest(r.Idx);
            }
        }

        private void DrawCaseRow(BridgeSelfTest.TestCase c)
        {
            using (new EditorGUILayout.HorizontalScope(BridgeStyles.RowIdle))
            {
                GUILayout.Label($"[{c.Idx:00}]", BridgeStyles.Muted, GUILayout.Width(30));
                GUILayout.Label(c.Name, BridgeStyles.RowName, GUILayout.Width(200));
                GUILayout.Label(c.Group, BridgeStyles.GroupOf(c.Group), GUILayout.Width(65));

                string hint = c.Hint ?? "";
                if (hint.Length > 34)
                    hint = hint.Substring(0, 34) + "…";
                GUILayout.Label(hint, BridgeStyles.Muted);

                if (c.Destructive)
                    GUILayout.Label("破坏性", BridgeStyles.MarkWarn, GUILayout.Width(44));

                if (GUILayout.Button("跑", BridgeStyles.TinyButton, GUILayout.Width(30)))
                    RunSingleTest(c.Idx);
            }
        }

        private void DrawChip(ref string target, string key, string label, float width)
        {
            bool active = target == key;
            if (
                GUILayout.Button(
                    label,
                    active ? BridgeStyles.ChipActive : BridgeStyles.Chip,
                    GUILayout.Height(ChipH),
                    GUILayout.Width(width)
                )
            )
            {
                target = key;
                RequestPanelRepaint();
            }
        }

        // ═══════════════════════════════════════════════════════════════
        //  日志
        // ═══════════════════════════════════════════════════════════════

        private void DrawLogPanel()
        {
            bool expanded = EditorGUILayout.Foldout(
                _showLog,
                $"操作日志  ({_statusLog.Count})",
                true,
                BridgeStyles.Foldout
            );
            if (expanded != _showLog)
            {
                _showLog = expanded;
                SessionState.SetBool(SessionKeyShowLog, _showLog);
            }
            if (!_showLog)
                return;

            GUILayout.Space(6);

            EditorGUILayout.BeginVertical(BridgeStyles.LogBox);
            int n = Math.Min(_statusLog.Count, 20);
            if (n == 0)
            {
                GUILayout.Label("暂无日志。", XYEditorStyles.LogEntry);
            }
            else
            {
                for (int i = Math.Max(0, _statusLog.Count - n); i < _statusLog.Count; i++)
                    GUILayout.Label(_statusLog[i], XYEditorStyles.LogEntry);
            }
            EditorGUILayout.EndVertical();

            GUILayout.Space(6);
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("清空日志", BridgeStyles.Button, GUILayout.Width(80)))
                    _statusLog.Clear();
            }
        }

        private void AddLog(string msg)
        {
            string ts = DateTime.Now.ToString("HH:mm:ss");
            _statusLog.Add("[" + ts + "] " + msg);
            if (_statusLog.Count > 100)
                _statusLog.RemoveAt(0);
        }

        // ═══════════════════════════════════════════════════════════════
        //  工具
        // ═══════════════════════════════════════════════════════════════

        private static void DrawDivider()
        {
            GUILayout.Space(8);
            Rect r = EditorGUILayout.GetControlRect(false, 1f);
            EditorGUI.DrawRect(r, BridgeStyles.Divider);
            GUILayout.Space(8);
        }

        /// <summary>
        /// 工具是 IXYEditorTool（不是 EditorWindow），没有 Repaint()。
        /// 借助 SceneView.RepaintAll 强制所有视图下一帧重画，Hub 的 OnGUI 会跟着刷新。
        /// </summary>
        private static void RequestPanelRepaint()
        {
            try
            {
                SceneView.RepaintAll();
            }
            catch { }
            EditorApplication.delayCall += () =>
            {
                try
                {
                    SceneView.RepaintAll();
                }
                catch { }
            };
        }
    }
}
