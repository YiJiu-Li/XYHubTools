// ═══════════════════════════════════════════════════════════════
//  BridgeStatusTool — MCP Bridge 状态面板（嵌入 XY Editor Hub）
// ═══════════════════════════════════════════════════════════════

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Framework.XYEditor.Bridge
{
    [XYTool(order: 100, tags: new[] { "MCP", "Bridge", "AI", "管道", "连接" })]
    public class BridgeStatusTool : IXYEditorTool
    {
        public string ToolName => "MCP Bridge";
        public string Category => "服务";
        public string Description => "AI MCP 桥接 — 管道转发 + 可选 SSE 服务管理";
        public string DocumentPath => null;
        public Texture2D Icon => EditorGUIUtility.FindTexture("d_NetworkAnimator Icon");

        private Vector2 _scroll;
        private readonly List<string> _statusLog = new List<string>(64);
        private bool _showLog = true;

        // ── 21 工具自检结果（保留最近一次） ──
        private readonly List<BridgeSelfTest.Result> _testResults =
            new List<BridgeSelfTest.Result>();
        private bool _showTests = false;
        private Vector2 _testScroll;
        private string _testFilter = "all"; // all / core / assets / scene / events
        private string _testFilterStatus = "all"; // all / ok / fail

        // ── 缓存的 GUIStyle ──
        private GUIStyle _titleStyle;
        private GUIStyle _subTitleStyle;
        private GUIStyle _statusStyle;
        private GUIStyle _infoStyle;
        private GUIStyle _logStyle;
        private GUIStyle _btnStyle;
        private GUIStyle _sectionStyle;
        private GUIStyle _cardOnStyle;
        private GUIStyle _cardOffStyle;
        private GUIStyle _testRowStyle;
        private GUIStyle _testHeaderStyle;

        private void EnsureStyles()
        {
            if (_titleStyle == null)
            {
                _titleStyle = new GUIStyle(EditorStyles.boldLabel)
                {
                    fontSize = 15,
                    normal = { textColor = new Color(0.9f, 0.9f, 0.9f) },
                };
                _subTitleStyle = new GUIStyle(EditorStyles.boldLabel)
                {
                    fontSize = 12,
                    normal = { textColor = new Color(0.75f, 0.75f, 0.75f) },
                };
                _statusStyle = new GUIStyle(EditorStyles.boldLabel)
                {
                    fontSize = 13,
                    normal = { textColor = Color.white },
                };
                _infoStyle = new GUIStyle(EditorStyles.miniLabel)
                {
                    normal = { textColor = new Color(0.55f, 0.55f, 0.55f) },
                };
                _logStyle = new GUIStyle(EditorStyles.miniLabel)
                {
                    normal = { textColor = new Color(0.65f, 0.65f, 0.65f) },
                };
                _btnStyle = new GUIStyle(GUI.skin.button) { fontSize = 11, fixedHeight = 24 };
                _sectionStyle = new GUIStyle(EditorStyles.foldout) { fontStyle = FontStyle.Bold };
                _cardOnStyle = new GUIStyle(EditorStyles.helpBox)
                {
                    normal = { background = MakeTex(1, 1, new Color(0.08f, 0.22f, 0.08f, 0.6f)) },
                };
                _cardOffStyle = new GUIStyle(EditorStyles.helpBox)
                {
                    normal = { background = MakeTex(1, 1, new Color(0.18f, 0.18f, 0.18f, 0.5f)) },
                };
                _testRowStyle = new GUIStyle(EditorStyles.helpBox)
                {
                    padding = new RectOffset(6, 6, 4, 4),
                    margin = new RectOffset(0, 0, 2, 2),
                };
                _testHeaderStyle = new GUIStyle(EditorStyles.miniBoldLabel)
                {
                    normal = { textColor = new Color(0.85f, 0.85f, 0.85f) },
                };
            }
        }

        private static Texture2D MakeTex(int w, int h, Color c)
        {
            var t = new Texture2D(w, h);
            t.SetPixel(0, 0, c);
            t.Apply();
            return t;
        }

        public void OnEnable()
        {
            _scroll = Vector2.zero;
            _statusLog.Clear();
            _titleStyle = null;
        }

        public void OnDisable() { }

        public void OnGUI(Rect rect)
        {
            EnsureStyles();
            float pad = 16f;
            float w = rect.width - pad * 2;
            float y = 14f;
            float totalH = 1120f;

            _scroll = GUI.BeginScrollView(rect, _scroll, new Rect(0, 0, rect.width - 16, totalH));

            // ── 标题 ──
            GUI.Label(new Rect(pad, y, w, 26), "MCP Bridge 服务状态", _titleStyle);
            y += 32;
            EditorGUI.DrawRect(new Rect(pad, y, w, 1), new Color(0.35f, 0.35f, 0.35f, 0.5f));
            y += 12;

            // ── 管道状态卡片 ──
            DrawStatusCard(pad, ref y, w);
            y += 8;

            // ── 操作按钮 ──
            DrawControls(pad, ref y, w);
            y += 8;

            // ── 分割 + MCP 组标题 ──
            EditorGUI.DrawRect(new Rect(pad, y, w, 1), new Color(0.35f, 0.35f, 0.35f, 0.5f));
            y += 10;
            GUI.Label(
                new Rect(pad, y, w, 22),
                "可选 SSE 服务（VS Code 使用；Codex stdio 可保持停止）",
                _subTitleStyle
            );
            y += 26;

            // ── MCP 分组卡片 ──
            foreach (var group in BridgeSetup.AllGroups)
            {
                DrawGroupCard(pad, ref y, w, group);
                y += 6;
            }

            // ── 提示 + Reload VS Code ──
            float hintY = y;
            var hintStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                normal = { textColor = new Color(0.5f, 0.5f, 0.2f) },
                wordWrap = true,
            };
            string hint =
                "提示：下方 SSE 服务显示已停止是正常状态；只有 VS Code 需要通过 .vscode/mcp.json 常驻连接时才需要启动。";
            float hintH = hintStyle.CalcHeight(new GUIContent(hint), w - 180);
            GUI.Label(new Rect(pad, hintY, w - 180, hintH), hint, hintStyle);

            // Reload VS Code 按钮
            var reloadStyle = new GUIStyle(GUI.skin.button)
            {
                fontSize = 11,
                fixedHeight = 22,
                normal = { textColor = new Color(0.8f, 0.7f, 0.2f) },
            };
            if (
                GUI.Button(
                    new Rect(pad + w - 170, hintY, 170, 22),
                    "Reload VS Code 生效",
                    reloadStyle
                )
            )
            {
                ReloadVSCode();
            }
            y += Math.Max(hintH, 24) + 8;

            // ── 21 工具自检面板 ──
            y += 4;
            EditorGUI.DrawRect(new Rect(pad, y, w, 1), new Color(0.35f, 0.35f, 0.35f, 0.5f));
            y += 10;
            DrawTestPanel(pad, ref y, w);
            y += 6;

            // ── 日志 ──
            y += 4;
            EditorGUI.DrawRect(new Rect(pad, y, w, 1), new Color(0.35f, 0.35f, 0.35f, 0.5f));
            y += 10;
            DrawPipeInfo(pad, ref y, w);
            y += 6;
            if (GUI.Button(new Rect(pad, y, 100, 22), "清空日志", _btnStyle))
                _statusLog.Clear();

            GUI.EndScrollView();
        }

        // ═══════════════════════════════════════════════════════════
        //  管道状态卡片
        // ═══════════════════════════════════════════════════════════

        private void DrawStatusCard(float x, ref float y, float w)
        {
            float cardH = 74f;
            var boxRect = new Rect(x, y, w, cardH);
            EditorGUI.DrawRect(boxRect, new Color(0.16f, 0.16f, 0.17f, 0.85f));
            EditorGUI.DrawRect(new Rect(x, y, w, 1), new Color(0.3f, 0.3f, 0.3f, 0.5f));
            EditorGUI.DrawRect(new Rect(x, y + cardH - 1, w, 1), new Color(0.3f, 0.3f, 0.3f, 0.5f));

            bool isRunning = YZJBridge.IsPipeServerRunning;
            var dotColor = isRunning
                ? new Color(0.15f, 0.85f, 0.25f)
                : new Color(0.7f, 0.65f, 0.15f);
            EditorGUI.DrawRect(new Rect(x + 10, y + 10, 10, 10), dotColor);

            GUI.Label(
                new Rect(x + 28, y + 6, w - 40, 22),
                isRunning ? "● 管道服务运行中" : "○ 管道服务已停止",
                _statusStyle
            );

            string pipeName = YZJBridge.PipeDisplayName;
            GUI.Label(
                new Rect(x + 28, y + 28, w - 40, 18),
                "管道名: " + (string.IsNullOrEmpty(pipeName) ? "—" : pipeName),
                _infoStyle
            );

            bool connected = YZJBridge.IsPipeConnected;
            GUI.Label(
                new Rect(x + w - 190, y + 30, 180, 18),
                "管道请求: " + (connected ? "处理中" : "空闲"),
                _infoStyle
            );
            GUI.Label(
                new Rect(x + 28, y + 48, w - 40, 18),
                "说明: Codex stdio 通常是短连接，显示空闲属于正常状态。",
                _infoStyle
            );

            y += cardH;
        }

        // ═══════════════════════════════════════════════════════════
        //  操作按钮
        // ═══════════════════════════════════════════════════════════

        private void DrawControls(float x, ref float y, float w)
        {
            bool isRunning = YZJBridge.IsPipeServerRunning;
            float btnW = 128f;
            float btnH = 26f;
            float gap = 8f;
            var old = GUI.enabled;

            GUI.Label(new Rect(x, y + 5, 84, 18), "管道服务", _testHeaderStyle);
            float bx = x + 84;
            GUI.enabled = !isRunning;
            if (GUI.Button(new Rect(bx, y, btnW, btnH), "启动管道", _btnStyle))
            {
                YZJBridge.Start();
                AddLog("Bridge 已启动");
            }
            bx += btnW + gap;

            GUI.enabled = isRunning;
            if (GUI.Button(new Rect(bx, y, btnW, btnH), "停止管道", _btnStyle))
            {
                YZJBridge.Stop();
                AddLog("Bridge 已停止");
            }
            bx += btnW + gap;

            GUI.enabled = true;
            if (GUI.Button(new Rect(bx, y, btnW, btnH), "重启管道", _btnStyle))
            {
                YZJBridge.Stop();
                YZJBridge.Start();
                AddLog("Bridge 已重启");
            }
            bx += btnW + gap;

            if (GUI.Button(new Rect(bx, y, btnW + 20, btnH), "安装/更新配置", _btnStyle))
            {
                BridgeSetup.SetupAll();
                AddLog("MCP 一键安装已执行");
            }

            GUI.enabled = old;
            y += btnH + 8;

            // ── MCP 进程控制 ─────────────────────────────────
            GUI.Label(new Rect(x, y + 5, 84, 18), "SSE 服务", _testHeaderStyle);
            bx = x + 84;
            if (GUI.Button(new Rect(bx, y, btnW, btnH), "启动全部 SSE", _btnStyle))
            {
                foreach (var g in BridgeSetup.AllGroups)
                    BridgeSetup.StartMcpGroup(g);
                AddLog("全部 SSE MCP 服务已启动");
            }
            bx += btnW + gap;
            if (GUI.Button(new Rect(bx, y, btnW, btnH), "停止全部 SSE", _btnStyle))
            {
                BridgeSetup.StopAllMcpGroups();
                AddLog("全部 SSE MCP 服务已停止");
            }
            y += btnH;
        }

        // ═══════════════════════════════════════════════════════════
        //  单组 MCP 卡片
        // ═══════════════════════════════════════════════════════════

        private void DrawGroupCard(float x, ref float y, float w, string group)
        {
            bool running = BridgeSetup.IsMcpGroupRunning(group);
            int port = BridgeSetup.GroupPorts.ContainsKey(group)
                ? BridgeSetup.GroupPorts[group]
                : 0;
            float cardH = 44f;

            GUI.Box(new Rect(x, y, w, cardH), "", running ? _cardOnStyle : _cardOffStyle);

            // 左侧状态点
            EditorGUI.DrawRect(
                new Rect(x + 8, y + 16, 8, 8),
                running ? new Color(0.15f, 0.85f, 0.25f) : new Color(0.45f, 0.45f, 0.45f)
            );

            // 组名 + 描述 + 端口
            string label = GetGroupLabel(group);
            string desc = GetGroupDesc(group) + "  [:" + port + "]";
            GUI.Label(new Rect(x + 22, y + 4, w - 200, 18), label, _statusStyle);
            GUI.Label(new Rect(x + 22, y + 22, w - 200, 16), desc, _infoStyle);

            // 运行状态文字
            var runColorStyle = new GUIStyle(_infoStyle)
            {
                normal =
                {
                    textColor = running ? new Color(0.2f, 0.9f, 0.3f) : new Color(0.5f, 0.5f, 0.5f),
                },
            };
            GUI.Label(
                new Rect(x + w - 188, y + 14, 80, 18),
                running ? "● 运行中" : "○ 未启动",
                runColorStyle
            );

            // 启动 / 停止 按鈕
            float btnX = x + w - 100;
            float btnY = y + 10;
            if (running)
            {
                if (GUI.Button(new Rect(btnX, btnY, 90, 24), "■ 停止", _btnStyle))
                {
                    BridgeSetup.StopMcpGroup(group);
                    AddLog("已停止: " + label);
                }
            }
            else
            {
                if (GUI.Button(new Rect(btnX, btnY, 90, 24), "▶ 启动", _btnStyle))
                {
                    BridgeSetup.StartMcpGroup(group);
                    AddLog("已启动: " + label + " 端口:" + port);
                }
            }

            y += cardH;
        }

        private static string GetGroupLabel(string g) =>
            g switch
            {
                "core" => "xybridge-core",
                "assets" => "xybridge-assets",
                "scene" => "xybridge-scene",
                "events" => "xybridge-events",
                _ => g,
            };

        private static string GetGroupDesc(string g) =>
            g switch
            {
                "core" => "ping / status / log / console",
                "assets" => "select / find / import / refresh / prefab",
                "scene" => "execute_code / selection / scene / play / screenshot / component",
                "events" => "subscribe / poll / screenshot",
                _ => "",
            };

        // ═══════════════════════════════════════════════════════════
        //  状态日志
        // ═══════════════════════════════════════════════════════════

        private void DrawPipeInfo(float x, ref float y, float w)
        {
            _showLog = EditorGUI.Foldout(
                new Rect(x, y, w, 20),
                _showLog,
                "📋 操作日志 (" + _statusLog.Count + " 条)",
                true,
                _sectionStyle
            );
            y += 24;
            if (!_showLog)
                return;

            int n = Math.Min(_statusLog.Count, 20);
            float boxH = Math.Max(60, n * 18 + 16);
            EditorGUI.DrawRect(new Rect(x, y, w, boxH), new Color(0.12f, 0.12f, 0.13f, 0.7f));

            float ly = y + 6;
            for (int i = Math.Max(0, _statusLog.Count - n); i < _statusLog.Count; i++)
            {
                GUI.Label(new Rect(x + 8, ly, w - 16, 18), _statusLog[i], _logStyle);
                ly += 18;
            }
            y += boxH;
        }

        private void AddLog(string msg)
        {
            string ts = DateTime.Now.ToString("HH:mm:ss");
            _statusLog.Add("[" + ts + "] " + msg);
            if (_statusLog.Count > 100)
                _statusLog.RemoveAt(0);
        }

        // ═══════════════════════════════════════════════════════════
        //  21 工具自检面板
        // ═══════════════════════════════════════════════════════════

        private void RunAllTests()
        {
            _testResults.Clear();
            foreach (var c in BridgeSelfTest.AllCases)
                _testResults.Add(BridgeSelfTest.RunOne(c));

            int passed = 0;
            foreach (var r in _testResults)
                if (r.Ok)
                    passed++;
            string msg = $"[Run All 21] {passed}/{_testResults.Count} passed";
            AddLog(msg);
            UnityEngine.Debug.Log("[BridgeSelfTest] " + msg);
            _showTests = true;
            RequestPanelRepaint();
        }

        private void RunSingleTest(int idx)
        {
            var c = BridgeSelfTest.AllCases.Find(x => x.Idx == idx);
            if (c == null)
            {
                AddLog("test idx not found: " + idx);
                return;
            }

            var result = BridgeSelfTest.RunOne(c);
            // 覆盖已有结果
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

            string msg =
                $"[#{idx:00} {c.Name}] {(result.Ok ? "OK" : "FAIL")} {result.Detail} ({result.ElapsedMs:0}ms)";
            AddLog(msg);
            UnityEngine.Debug.Log("[BridgeSelfTest] " + msg);
            RequestPanelRepaint();
        }

        private void DrawTestPanel(float x, ref float y, float w)
        {
            // 标题 + 折叠
            int passed = 0,
                total = _testResults.Count;
            foreach (var r in _testResults)
                if (r.Ok)
                    passed++;
            string title = $"21 工具自检 ({passed}/{total})";
            _showTests = EditorGUI.Foldout(
                new Rect(x, y, w, 20),
                _showTests,
                title,
                true,
                _sectionStyle
            );
            y += 24;
            if (!_showTests)
                return;

            // 工具栏
            float barH = 24f;
            float bx = x;
            float bH = 22f;
            if (GUI.Button(new Rect(bx, y, 90, bH), "运行全部", _btnStyle))
                RunAllTests();
            bx += 96;
            if (GUI.Button(new Rect(bx, y, 80, bH), "清空结果", _btnStyle))
                _testResults.Clear();
            bx += 86;

            // 组筛选
            GUI.Label(new Rect(bx, y + 3, 28, 18), "组:", _testHeaderStyle);
            bx += 28;
            DrawFilterButton(bx, y, 44, bH, "all", "全部");
            bx += 48;
            DrawFilterButton(bx, y, 50, bH, "core", "core");
            bx += 54;
            DrawFilterButton(bx, y, 56, bH, "assets", "assets");
            bx += 60;
            DrawFilterButton(bx, y, 54, bH, "scene", "scene");
            bx += 58;
            DrawFilterButton(bx, y, 56, bH, "events", "events");
            bx += 60;

            // 状态筛选
            GUI.Label(new Rect(bx, y + 3, 42, 18), "状态:", _testHeaderStyle);
            bx += 42;
            DrawStatusFilterButton(bx, y, 44, bH, "all", "全部");
            bx += 48;
            DrawStatusFilterButton(bx, y, 50, bH, "ok", "成功");
            bx += 54;
            DrawStatusFilterButton(bx, y, 50, bH, "fail", "失败");
            y += barH + 6;

            // 列表区
            const float rowH = 28f;
            int visibleCount = 0;
            for (int i = 0; i < _testResults.Count; i++)
            {
                var r = _testResults[i];
                if (_testFilter != "all" && r.Group != _testFilter)
                    continue;
                if (_testFilterStatus == "ok" && !r.Ok)
                    continue;
                if (_testFilterStatus == "fail" && r.Ok)
                    continue;
                visibleCount++;
            }
            if (visibleCount > 0)
            {
                float listH = Math.Min(visibleCount, 10) * rowH + 8;
                EditorGUI.DrawRect(new Rect(x, y, w, listH), new Color(0.12f, 0.12f, 0.13f, 0.5f));
                _testScroll = GUI.BeginScrollView(
                    new Rect(x, y, w, listH),
                    _testScroll,
                    new Rect(0, 0, w - 16, visibleCount * rowH + 8)
                );

                float ly = 4f;
                for (int i = 0; i < _testResults.Count; i++)
                {
                    var r = _testResults[i];
                    if (_testFilter != "all" && r.Group != _testFilter)
                        continue;
                    if (_testFilterStatus == "ok" && !r.Ok)
                        continue;
                    if (_testFilterStatus == "fail" && r.Ok)
                        continue;
                    DrawTestRow(x + 4, ly, w - 24, rowH - 4, r);
                    ly += rowH;
                }
                GUI.EndScrollView();
                y += listH + 6;
            }
            else if (_testResults.Count > 0)
            {
                GUI.Label(
                    new Rect(x, y, w, 18),
                    "当前筛选下没有测试结果。",
                    _infoStyle
                );
                y += 20;
            }

            if (_testResults.Count == 0)
            {
                GUI.Label(
                    new Rect(x, y, w, 18),
                    "尚未运行。可运行全部，或在下方逐个测试：",
                    _infoStyle
                );
                y += 20;

                float runListH = Math.Min(BridgeSelfTest.AllCases.Count, 12) * rowH + 8;
                _testScroll = GUI.BeginScrollView(
                    new Rect(x, y, w, runListH),
                    _testScroll,
                    new Rect(0, 0, w - 16, BridgeSelfTest.AllCases.Count * rowH + 8)
                );
                float ry = 4f;
                foreach (var c in BridgeSelfTest.AllCases)
                {
                    DrawTestCaseRow(x + 4, ry, w - 24, rowH - 4, c);
                    ry += rowH;
                }
                GUI.EndScrollView();
                y += runListH;
            }
        }

        private void DrawFilterButton(float x, float y, float w, float h, string key, string label)
        {
            bool active = _testFilter == key;
            var style = new GUIStyle(GUI.skin.button) { fontSize = 10, fixedHeight = h };
            if (active)
            {
                style.normal.textColor = new Color(0.3f, 0.9f, 0.4f);
                style.fontStyle = FontStyle.Bold;
            }
            if (GUI.Button(new Rect(x, y, w, h), label, style))
            {
                _testFilter = key;
                RequestPanelRepaint();
            }
        }

        private void DrawStatusFilterButton(
            float x,
            float y,
            float w,
            float h,
            string key,
            string label
        )
        {
            bool active = _testFilterStatus == key;
            var style = new GUIStyle(GUI.skin.button) { fontSize = 10, fixedHeight = h };
            if (active)
            {
                style.normal.textColor = new Color(0.3f, 0.9f, 0.4f);
                style.fontStyle = FontStyle.Bold;
            }
            if (GUI.Button(new Rect(x, y, w, h), label, style))
            {
                _testFilterStatus = key;
                RequestPanelRepaint();
            }
        }

        private void DrawTestRow(float x, float y, float w, float h, BridgeSelfTest.Result r)
        {
            var bg = r.Ok
                ? new Color(0.10f, 0.22f, 0.10f, 0.55f)
                : new Color(0.30f, 0.10f, 0.10f, 0.55f);
            EditorGUI.DrawRect(new Rect(x, y, w, h), bg);

            // 状态图标
            var markStyle = new GUIStyle(EditorStyles.boldLabel) { fontSize = 13 };
            markStyle.normal.textColor = r.Ok
                ? new Color(0.35f, 0.95f, 0.45f)
                : new Color(0.95f, 0.35f, 0.35f);
            GUI.Label(new Rect(x + 4, y + 2, 20, h - 2), r.Ok ? "✓" : "✗", markStyle);

            // 编号
            GUI.Label(
                new Rect(x + 24, y + 2, 28, h - 2),
                "[" + r.Idx.ToString("00") + "]",
                _testHeaderStyle
            );

            // 名字
            var nameStyle = new GUIStyle(EditorStyles.label) { fontSize = 11 };
            GUI.Label(new Rect(x + 52, y + 2, 220, h - 2), r.Name, nameStyle);

            // 耗时
            GUI.Label(
                new Rect(x + 270, y + 2, 50, h - 2),
                r.ElapsedMs.ToString("0") + "ms",
                _infoStyle
            );

            // 详情（截断）
            string det = r.Detail ?? "";
            if (det.Length > 50)
                det = det.Substring(0, 50) + "...";
            GUI.Label(new Rect(x + 320, y + 2, w - 320 - 60, h - 2), det, _infoStyle);

            // 重跑按钮
            if (GUI.Button(new Rect(x + w - 50, y + 2, 50, h - 2), "重跑", _btnStyle))
                RunSingleTest(r.Idx);
        }

        private void DrawTestCaseRow(float x, float y, float w, float h, BridgeSelfTest.TestCase c)
        {
            EditorGUI.DrawRect(new Rect(x, y, w, h), new Color(0.14f, 0.14f, 0.15f, 0.55f));

            // 编号
            GUI.Label(
                new Rect(x + 4, y + 2, 28, h - 2),
                "[" + c.Idx.ToString("00") + "]",
                _testHeaderStyle
            );

            // 名字
            var nameStyle = new GUIStyle(EditorStyles.label) { fontSize = 11 };
            GUI.Label(new Rect(x + 32, y + 2, 220, h - 2), c.Name, nameStyle);

            // 组标签
            var grpStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                normal = { textColor = GroupColor(c.Group) },
            };
            GUI.Label(new Rect(x + 250, y + 4, 50, h - 2), c.Group, grpStyle);

            // 提示
            string hint = c.Hint ?? "";
            if (hint.Length > 30)
                hint = hint.Substring(0, 30) + "...";
            GUI.Label(new Rect(x + 300, y + 2, w - 300 - 50, h - 2), hint, _infoStyle);

            // 跑
            if (GUI.Button(new Rect(x + w - 50, y + 2, 50, h - 2), "▶ 跑", _btnStyle))
                RunSingleTest(c.Idx);
        }

        private static Color GroupColor(string g)
        {
            if (g == "core")
                return new Color(0.6f, 0.85f, 1f);
            if (g == "assets")
                return new Color(1f, 0.85f, 0.5f);
            if (g == "scene")
                return new Color(0.7f, 1f, 0.7f);
            if (g == "events")
                return new Color(0.9f, 0.6f, 0.9f);
            return Color.white;
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

        // ═══════════════════════════════════════════════════════════
        //  Reload VS Code
        // ═══════════════════════════════════════════════════════════

        private void ReloadVSCode()
        {
            // VS Code CLI 不支持 reload 已有窗口，只能引导用户手动操作
            bool ok = EditorUtility.DisplayDialog(
                "如何让 mcp.json 生效",
                "mcp.json 已保存完毕。\n\n要启动 / 重启 MCP 服务器，请在 VS Code 中：\n\n"
                    + "  Ctrl+Shift+P  →  Developer: Reload Window\n\n"
                    + "（点「聚焦 VS Code」可将 VS Code 窗口带到前台）",
                "聚焦 VS Code",
                "关闭"
            );

            if (ok)
            {
                // 用 --reuse-window 聚焦已有窗口，不新建
                try
                {
                    string projectRoot = System.IO.Path.GetDirectoryName(
                        UnityEngine.Application.dataPath
                    );
                    var psi = new ProcessStartInfo
                    {
                        FileName = "cmd.exe",
                        Arguments = "/c code --reuse-window \"" + projectRoot + "\"",
                        UseShellExecute = false,
                        CreateNoWindow = true,
                    };
                    Process.Start(psi);
                    AddLog("已聚焦 VS Code，请按 Ctrl+Shift+P → Developer: Reload Window");
                }
                catch (Exception ex)
                {
                    AddLog("聚焦失败: " + ex.Message);
                }
            }
        }
    }
}
