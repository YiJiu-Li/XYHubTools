// ShaderLibraryPanel.cs — XY Shader 库面板（分组下载）
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace Framework.XYEditor.ShaderLibrary
{
    public class ShaderLibraryPanel : IXYPanel
    {
        // ── EditorPrefs 键 ────────────────────────────────────────────────
        private const string PREF_URL = "XYShaderLib_URL";
        private const string PREF_TOKEN = "XYShaderLib_Token";
        private const string PREF_USER = "XYShaderLib_User";
        private const string PREF_ROLE = "XYShaderLib_Role";
        private const string PREF_DOWNLOAD_DIR = "XYShaderLib_DownloadDir";

        // ── 连接 / 登录 ───────────────────────────────────────────────────
        private string _baseUrl = "http://310007.xyz:17851";
        private string _username = "";
        private string _password = "";
        private string _token = "";
        private string _currentUser = "";
        private string _currentRole = "";
        private bool _showPassword;
        private bool _showLoginBlock = true;

        // ── 下载 ──────────────────────────────────────────────────────────
        private List<DownloadGroup> _downloadGroups = new();
        private string _downloadTargetDir = "Assets/ShaderLibrary";
        private bool _preserveStructure = false;
        private bool _resolveDependencies = true;
        private Vector2 _downloadScroll;
        private Vector2 _downloadLogScroll;
        private List<string> _downloadLog = new();
        private HashSet<string> _allFilePaths = new();

        // ── 状态栏 ────────────────────────────────────────────────────────
        private string _statusMsg = "";
        private bool _statusError;
        private double _statusClearAt;

        private readonly Action _repaint;

        // ── 数据类型 ──────────────────────────────────────────────────────
        [Serializable]
        private class ShaderNode
        {
            public string key = "";
            public string type = "";
            public List<ShaderNode> children = new();
            public bool IsDir => type == "dir" || type == "directory" || type == "folder";
        }

        [Serializable]
        private class ShaderNodeList
        {
            public List<ShaderNode> items;
        }

        private class DownloadGroup
        {
            public string Name = "";
            public List<string> Files = new();
            public bool Selected;
            public bool Expanded;
        }

        // Unity 内置 include 白名单（跳过不在 Shader 库中的系统文件）
        private static readonly HashSet<string> BuiltinIncludes = new HashSet<string>(
            new[]
            {
                "UnityCG.cginc",
                "Lighting.cginc",
                "AutoLight.cginc",
                "HLSLSupport.cginc",
                "UnityShaderVariables.cginc",
                "UnityStandardBRDF.cginc",
                "UnityStandardUtils.cginc",
                "UnityGlobalIllumination.cginc",
                "UnityStandardCore.cginc",
                "UnityPBSLighting.cginc",
                "UnityDeferredLibrary.cginc",
                "UnityMetaPass.cginc",
                "UnityShaderUtilities.cginc",
                "UnityGBuffer.cginc",
                "UnityStandardInput.cginc",
                "UnityInstancing.cginc",
            },
            StringComparer.OrdinalIgnoreCase
        );

        // ═══════════════════════════════════════════════════════════════════
        //  构造 / 生命周期
        // ═══════════════════════════════════════════════════════════════════

        public ShaderLibraryPanel(Action repaint = null)
        {
            _repaint = repaint ?? (() => { });
        }

        public void Init()
        {
            _baseUrl = EditorPrefs.GetString(PREF_URL, "http://310007.xyz:17851");
            _token = EditorPrefs.GetString(PREF_TOKEN, "");
            _currentUser = EditorPrefs.GetString(PREF_USER, "");
            _currentRole = EditorPrefs.GetString(PREF_ROLE, "");
            _downloadTargetDir = EditorPrefs.GetString(PREF_DOWNLOAD_DIR, "Assets/ShaderLibrary");

            if (!string.IsNullOrEmpty(_token))
            {
                DoRefreshFilePaths();
                DoLoadMetadata();
            }
        }

        public void Cleanup()
        {
            EditorPrefs.SetString(PREF_URL, _baseUrl);
            EditorPrefs.SetString(PREF_DOWNLOAD_DIR, _downloadTargetDir);
        }

        // ═══════════════════════════════════════════════════════════════════
        //  主绘制
        // ═══════════════════════════════════════════════════════════════════

        public void Draw(float containerWidth)
        {
            PollStatusClear();

            // ── 顶部：连接 & 登录 ─────────────────────────────────────────
            DrawLoginHeader();

            if (!string.IsNullOrEmpty(_statusMsg))
                DrawStatusBar();

            if (!IsLoggedIn)
            {
                EditorGUILayout.HelpBox("请先登录以访问 Shader 库。", MessageType.Info);
                return;
            }

            GUILayout.Space(4);
            DrawDownloadTab(containerWidth);

            XYEditorGUI.DrawFooter(
                "作者: 依旧 | GitHub: https://github.com/YiJiu-Li",
                "https://github.com/YiJiu-Li"
            );
            EditorGUILayout.Space(10);
        }

        // ── 连接 & 登录 ───────────────────────────────────────────────────
        private void DrawLoginHeader()
        {
            XYEditorGUI.DrawBox(() =>
            {
                // 折叠开关
                GUILayout.BeginHorizontal();
                _showLoginBlock = EditorGUILayout.Foldout(
                    _showLoginBlock,
                    IsLoggedIn
                        ? $"🔗 {_baseUrl}  |  👤 {_currentUser}  ({_currentRole})"
                        : "🔗 连接 / 登录",
                    true,
                    EditorStyles.foldout
                );
                GUILayout.FlexibleSpace();
                if (
                    IsLoggedIn
                    && GUILayout.Button("注销", EditorStyles.miniButton, GUILayout.Width(40))
                )
                    DoLogout();
                GUILayout.EndHorizontal();

                if (!_showLoginBlock)
                    return;

                // 服务器地址
                GUILayout.BeginHorizontal();
                GUILayout.Label("服务器", GUILayout.Width(50));
                _baseUrl = EditorGUILayout.TextField(_baseUrl);
                if (GUILayout.Button("测试", EditorStyles.miniButton, GUILayout.Width(36)))
                    DoHealthCheck();
                GUILayout.EndHorizontal();

                if (IsLoggedIn)
                    return;

                // 用户名 / 密码
                GUILayout.BeginHorizontal();
                GUILayout.Label("用户名", GUILayout.Width(50));
                _username = EditorGUILayout.TextField(_username, GUILayout.ExpandWidth(true));
                GUILayout.EndHorizontal();

                GUILayout.BeginHorizontal();
                GUILayout.Label("密  码", GUILayout.Width(50));
                if (_showPassword)
                    _password = EditorGUILayout.TextField(_password);
                else
                    _password = EditorGUILayout.PasswordField(_password);
                _showPassword = GUILayout.Toggle(_showPassword, "👁", GUILayout.Width(28));
                GUILayout.EndHorizontal();

                GUILayout.BeginHorizontal();
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("登录", GUILayout.Width(64)))
                    DoLogin();
                GUILayout.EndHorizontal();
            });
        }

        private void DrawStatusBar()
        {
            Color prev = GUI.color;
            GUI.color = _statusError ? new Color(1f, 0.5f, 0.5f) : new Color(0.6f, 1f, 0.7f);
            GUILayout.Label(_statusMsg, EditorStyles.helpBox);
            GUI.color = prev;
        }

        // ═══════════════════════════════════════════════════════════════════
        //  API 操作
        // ═══════════════════════════════════════════════════════════════════

        private bool IsLoggedIn => !string.IsNullOrEmpty(_token);

        private void DoHealthCheck()
        {
            var r = ShaderLibraryApi.Health(_baseUrl);
            SetStatus(r.Success ? "✅ 服务器连接正常" : $"❌ {r.Error}", !r.Success);
        }

        private void DoLogin()
        {
            if (string.IsNullOrWhiteSpace(_username))
            {
                SetStatus("请输入用户名", true);
                return;
            }
            var r = ShaderLibraryApi.Login(_baseUrl, _username, _password);
            if (!r.Success)
            {
                SetStatus($"登录失败：{r.Error}", true);
                return;
            }
            // 从响应中提取 token（尝试多种字段名）
            string token =
                ShaderLibraryApi.ExtractString(r.Data, "access_token")
                ?? ShaderLibraryApi.ExtractString(r.Data, "token")
                ?? ShaderLibraryApi.ExtractString(r.Data, "key");
            if (string.IsNullOrEmpty(token))
            {
                // 如果响应本身就是裸 token（非 JSON）
                string trimmed = r.Data?.Trim().Trim('"');
                token = string.IsNullOrEmpty(trimmed) ? null : trimmed;
            }
            if (string.IsNullOrEmpty(token))
            {
                SetStatus($"登录响应解析失败：{r.Data}", true);
                return;
            }
            _token = token;
            _currentUser = ShaderLibraryApi.ExtractString(r.Data, "username") ?? _username;
            _currentRole = ShaderLibraryApi.ExtractString(r.Data, "role") ?? "";

            EditorPrefs.SetString(PREF_TOKEN, _token);
            EditorPrefs.SetString(PREF_USER, _currentUser);
            EditorPrefs.SetString(PREF_ROLE, _currentRole);

            _password = "";
            SetStatus($"✅ 已登录 {_currentUser}");
            DoRefreshFilePaths();
            DoLoadMetadata();
        }

        private void DoLogout()
        {
            ShaderLibraryApi.Logout(_baseUrl, _token);
            _token = _currentUser = _currentRole = "";
            EditorPrefs.DeleteKey(PREF_TOKEN);
            EditorPrefs.DeleteKey(PREF_USER);
            EditorPrefs.DeleteKey(PREF_ROLE);
            _allFilePaths.Clear();
            _downloadGroups.Clear();
            SetStatus("已注销");
        }

        private void DoRefreshFilePaths()
        {
            var r = ShaderLibraryApi.GetTree(_baseUrl, _token);
            if (!r.Success)
            {
                SetStatus($"获取文件列表失败：{r.Error}", true);
                return;
            }
            var roots = ParseTreeJson(r.Data);
            CollectAllFilePaths(roots);
            SetStatus($"已加载 {_allFilePaths.Count} 个文件路径");
        }

        private List<ShaderNode> ParseTreeJson(string json)
        {
            if (string.IsNullOrEmpty(json))
                return new List<ShaderNode>();
            json = json.Trim();
            try
            {
                if (json.StartsWith("["))
                {
                    var list = JsonUtility.FromJson<ShaderNodeList>("{\"items\":" + json + "}");
                    return list?.items ?? new List<ShaderNode>();
                }
                int start = json.IndexOf('[');
                int end = json.LastIndexOf(']');
                if (start >= 0 && end > start)
                {
                    string arr = json.Substring(start, end - start + 1);
                    var list = JsonUtility.FromJson<ShaderNodeList>("{\"items\":" + arr + "}");
                    return list?.items ?? new List<ShaderNode>();
                }
            }
            catch (Exception e)
            {
                SetStatus($"解析树失败：{e.Message}", true);
            }
            return new List<ShaderNode>();
        }

        private void SetStatus(string msg, bool isError = false)
        {
            _statusMsg = msg;
            _statusError = isError;
            _statusClearAt = EditorApplication.timeSinceStartup + 5.0;
            _repaint();
        }

        private void PollStatusClear()
        {
            if (
                !string.IsNullOrEmpty(_statusMsg)
                && EditorApplication.timeSinceStartup > _statusClearAt
            )
            {
                _statusMsg = "";
                _repaint();
            }
        }

        // ═══════════════════════════════════════════════════════════════════
        //  📦 下载选项卡
        // ═══════════════════════════════════════════════════════════════════

        private void DrawDownloadTab(float containerWidth)
        {
            GUILayout.BeginHorizontal(EditorStyles.toolbar);
            GUILayout.Label("分组下载", EditorStyles.toolbarButton);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("↻ 刷新分组", EditorStyles.toolbarButton, GUILayout.Width(72)))
                DoLoadMetadata();
            GUILayout.EndHorizontal();

            if (_downloadGroups.Count == 0)
            {
                GUILayout.Space(12);
                EditorGUILayout.HelpBox(
                    "尚未加载分组，请点击上方「↻ 刷新分组」，\n或先在 Web 界面创建分组后再刷新。",
                    MessageType.Info
                );
                return;
            }

            // ── 分组列表 ──────────────────────────────────────────────────
            XYEditorGUI.DrawSection(
                "选择分组",
                () =>
                {
                    _downloadScroll = EditorGUILayout.BeginScrollView(
                        _downloadScroll,
                        GUILayout.MaxHeight(260)
                    );
                    foreach (var g in _downloadGroups)
                    {
                        GUILayout.BeginVertical(EditorStyles.helpBox);
                        GUILayout.BeginHorizontal();
                        bool newSel = EditorGUILayout.Toggle(g.Selected, GUILayout.Width(18));
                        if (newSel != g.Selected)
                        {
                            g.Selected = newSel;
                            _repaint();
                        }
                        g.Expanded = EditorGUILayout.Foldout(
                            g.Expanded,
                            $"🏷 {g.Name}   ({g.Files.Count} 个文件)",
                            true
                        );
                        GUILayout.EndHorizontal();
                        if (g.Expanded)
                            foreach (var f in g.Files)
                                GUILayout.Label($"     📄 {f}", EditorStyles.miniLabel);
                        GUILayout.EndVertical();
                    }
                    EditorGUILayout.EndScrollView();
                }
            );

            // ── 下载设置 ──────────────────────────────────────────────────
            XYEditorGUI.DrawSection(
                "下载设置",
                () =>
                {
                    GUILayout.BeginHorizontal();
                    GUILayout.Label("目标目录", GUILayout.Width(60));
                    _downloadTargetDir = EditorGUILayout.TextField(_downloadTargetDir);
                    // 先把按钮结果存起来，End 之后再弹对话框，避免 OpenFolderPanel
                    // 内部抛出 ExitGUIException 时 EndHorizontal 来不及执行
                    bool browseClicked = GUILayout.Button(
                        "浏览",
                        EditorStyles.miniButton,
                        GUILayout.Width(40)
                    );
                    GUILayout.EndHorizontal();
                    if (browseClicked)
                    {
                        string chosen = EditorUtility.OpenFolderPanel(
                            "选择下载目标目录",
                            Application.dataPath,
                            ""
                        );
                        if (!string.IsNullOrEmpty(chosen))
                        {
                            string dataPath = Application.dataPath;
                            string projRoot = dataPath.Replace('\\', '/').Replace("/Assets", "");
                            string chosenFwd = chosen.Replace('\\', '/');
                            if (chosenFwd.StartsWith(projRoot))
                                chosen = chosenFwd.Substring(projRoot.Length).TrimStart('/');
                            _downloadTargetDir = chosen;
                            EditorPrefs.SetString(PREF_DOWNLOAD_DIR, chosen);
                        }
                    }
                    _preserveStructure = EditorGUILayout.Toggle("保留目录结构", _preserveStructure);
                    _resolveDependencies = EditorGUILayout.Toggle(
                        "解析 #include 依赖",
                        _resolveDependencies
                    );
                }
            );

            // ── 下载按钮 ──────────────────────────────────────────────────
            int selectedFileCount = 0;
            foreach (var g in _downloadGroups)
                if (g.Selected)
                    selectedFileCount += g.Files.Count;

            GUILayout.Space(4);
            GUI.enabled = selectedFileCount > 0 && !string.IsNullOrWhiteSpace(_downloadTargetDir);
            if (
                GUILayout.Button(
                    $"⬇  下载选中分组（{selectedFileCount} 个基础文件）",
                    GUILayout.Height(32)
                )
            )
                DoDownloadGroup();
            GUI.enabled = true;

            // ── 日志 ──────────────────────────────────────────────────────
            if (_downloadLog.Count > 0)
            {
                GUILayout.Space(4);
                GUILayout.BeginHorizontal();
                GUILayout.Label("下载日志", EditorStyles.miniBoldLabel);
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("清空", EditorStyles.miniButton, GUILayout.Width(36)))
                    _downloadLog.Clear();
                GUILayout.EndHorizontal();
                _downloadLogScroll = EditorGUILayout.BeginScrollView(
                    _downloadLogScroll,
                    GUILayout.MaxHeight(180)
                );
                foreach (var entry in _downloadLog)
                    GUILayout.Label(entry, EditorStyles.miniLabel);
                EditorGUILayout.EndScrollView();
            }
        }

        // ── 加载元数据（获取分组） ────────────────────────────────────────
        private void DoLoadMetadata()
        {
            var r = ShaderLibraryApi.GetMetadata(_baseUrl, _token);
            if (!r.Success)
            {
                SetStatus($"加载元数据失败：{r.Error}", true);
                return;
            }
            _downloadGroups = ParseGroupsFromMetadata(r.Data);
            SetStatus(
                _downloadGroups.Count > 0
                    ? $"✅ 已加载 {_downloadGroups.Count} 个分组"
                    : "元数据已加载（暂无分组）"
            );
        }

        // ── 执行下载 ─────────────────────────────────────────────────────
        private void DoDownloadGroup()
        {
            _downloadLog.Clear();

            // 收集选中分组的基础文件
            var fileSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var g in _downloadGroups)
                if (g.Selected)
                    foreach (var f in g.Files)
                        fileSet.Add(f);

            if (fileSet.Count == 0)
            {
                SetStatus("没有选中的文件", true);
                return;
            }

            // BFS：下载内容并解析 #include 依赖
            var contentCache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var queue = new Queue<string>(fileSet);
            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            while (queue.Count > 0)
            {
                string path = queue.Dequeue();
                if (visited.Contains(path))
                    continue;
                visited.Add(path);

                SetStatus($"正在获取：{path}");
                var r = ShaderLibraryApi.GetContent(_baseUrl, _token, path);
                if (!r.Success)
                {
                    // .meta 文件在服务器上不存在时静默跳过（非致命）
                    if (!path.EndsWith(".meta", StringComparison.OrdinalIgnoreCase))
                        _downloadLog.Add($"❌ 获取失败：{path}（{r.Error}）");
                    continue;
                }
                string content = ShaderLibraryApi.ExtractString(r.Data, "content") ?? r.Data;
                contentCache[path] = content;

                bool isBase = fileSet.Contains(path);
                _downloadLog.Add(isBase ? $"📄 {path}" : $"   ↳ 📎 {path}（依赖）");

                // 非 .meta 文件：同时把对应的 .meta 加入下载队列（用于 GUID 映射）
                if (!path.EndsWith(".meta", StringComparison.OrdinalIgnoreCase))
                {
                    string metaPath = path + ".meta";
                    if (!visited.Contains(metaPath))
                        queue.Enqueue(metaPath);
                }

                if (_resolveDependencies)
                    foreach (string inc in ExtractIncludes(content))
                    {
                        string resolved = ResolveToKnownPath(path, inc);
                        if (resolved != null && !visited.Contains(resolved))
                            queue.Enqueue(resolved);
                    }
            }

            // 计算绝对目标路径
            string absTarget = _downloadTargetDir;
            if (!Path.IsPathRooted(absTarget))
                absTarget = Path.Combine(
                    Application
                        .dataPath.Replace("/Assets", "")
                        .Replace('/', Path.DirectorySeparatorChar),
                    absTarget.Replace('/', Path.DirectorySeparatorChar)
                );

            // ── 阶段 1：从服务端 .meta 提取 serverGuid → 主体文件路径 ──────
            // 例：serverGuid "42ad40a7..." → "Shaders/SubGraph.shadersubgraph"
            var serverGuidToServerPath = new Dictionary<string, string>(
                StringComparer.OrdinalIgnoreCase
            );
            foreach (var kvp in contentCache)
            {
                if (!kvp.Key.EndsWith(".meta", StringComparison.OrdinalIgnoreCase))
                    continue;
                string guid = ExtractMetaGuid(kvp.Value);
                if (!string.IsNullOrEmpty(guid))
                    serverGuidToServerPath[guid] = kvp.Key.Substring(0, kvp.Key.Length - 5);
            }

            // ── 阶段 2：只写非 .meta 文件，让 Unity 自己生成本地 .meta ─────
            int success = 0,
                skipped = 0,
                failed = 0;
            bool anyWritten = false;
            foreach (var kvp in contentCache)
            {
                if (kvp.Key.EndsWith(".meta", StringComparison.OrdinalIgnoreCase))
                    continue;
                string localRel = _preserveStructure ? kvp.Key : Path.GetFileName(kvp.Key);
                string localAbs = Path.Combine(
                    absTarget,
                    localRel.Replace('/', Path.DirectorySeparatorChar)
                );
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(localAbs));
                    if (File.Exists(localAbs))
                    {
                        string existing = File.ReadAllText(localAbs, System.Text.Encoding.UTF8);
                        if (existing == kvp.Value)
                        {
                            skipped++;
                            continue;
                        }
                    }
                    File.WriteAllText(localAbs, kvp.Value, System.Text.Encoding.UTF8);
                    success++;
                    anyWritten = true;
                }
                catch (Exception ex)
                {
                    _downloadLog.Add($"❌ 写入失败：{kvp.Key}（{ex.Message}）");
                    failed++;
                }
            }

            // ── 阶段 3：Refresh，让 Unity 为新文件生成本地 GUID ────────────
            string targetFwd = absTarget.Replace('\\', '/');
            string dataFwd = Application.dataPath.Replace('\\', '/');
            bool inAssets = targetFwd.StartsWith(dataFwd);
            if (inAssets && anyWritten)
                AssetDatabase.Refresh();

            // ── 阶段 4：读取本地 .meta，建立 serverGuid → localGuid 映射 ──
            var guidRemap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var kv in serverGuidToServerPath)
            {
                string serverGuid = kv.Key;
                string serverFilePath = kv.Value;
                string localRel = _preserveStructure
                    ? serverFilePath
                    : Path.GetFileName(serverFilePath);
                string localMetaAbs =
                    Path.Combine(absTarget, localRel.Replace('/', Path.DirectorySeparatorChar))
                    + ".meta";
                if (!File.Exists(localMetaAbs))
                    continue;
                string localGuid = ExtractMetaGuid(
                    File.ReadAllText(localMetaAbs, System.Text.Encoding.UTF8)
                );
                if (string.IsNullOrEmpty(localGuid) || localGuid == serverGuid)
                    continue;
                guidRemap[serverGuid] = localGuid;
                _downloadLog.Add(
                    $"🔁 {Path.GetFileName(serverFilePath)}: {serverGuid} → {localGuid}"
                );
            }

            // ── 阶段 5：用本地 GUID 替换所有 shader 文件内的引用 ───────────
            int patched = 0;
            if (guidRemap.Count > 0)
            {
                foreach (var kvp in contentCache)
                {
                    if (kvp.Key.EndsWith(".meta", StringComparison.OrdinalIgnoreCase))
                        continue;
                    string patchedContent = PatchGuids(kvp.Value, guidRemap);
                    if (patchedContent == kvp.Value)
                        continue;
                    string localRel = _preserveStructure ? kvp.Key : Path.GetFileName(kvp.Key);
                    string localAbs = Path.Combine(
                        absTarget,
                        localRel.Replace('/', Path.DirectorySeparatorChar)
                    );
                    try
                    {
                        File.WriteAllText(localAbs, patchedContent, System.Text.Encoding.UTF8);
                        patched++;
                        _downloadLog.Add($"✏️ GUID 已修正：{kvp.Key}");
                    }
                    catch (Exception ex)
                    {
                        _downloadLog.Add($"❌ GUID 修正失败：{kvp.Key}（{ex.Message}）");
                    }
                }
                if (inAssets && patched > 0)
                    AssetDatabase.Refresh();
            }

            string summary =
                $"✅ 完成：{success} 更新，{skipped} 无变化，{failed} 失败"
                + (patched > 0 ? $"，{patched} 个文件 GUID 已修正" : "")
                + $"（共 {contentCache.Count} 个）";
            _downloadLog.Add("─────────────────────────────");
            _downloadLog.Add(summary);
            SetStatus(summary, failed > 0);
        }

        // ── #include 解析 ────────────────────────────────────────────────

        private static string ExtractMetaGuid(string metaContent)
        {
            var m = Regex.Match(
                metaContent,
                @"^guid:\s*([a-f0-9]{32})",
                RegexOptions.Multiline | RegexOptions.IgnoreCase
            );
            return m.Success ? m.Groups[1].Value : null;
        }

        /// <summary>将文件内容中所有 serverGuid 替换为对应的 localGuid。</summary>
        private static string PatchGuids(string content, Dictionary<string, string> remap)
        {
            foreach (var kv in remap)
                content = content.Replace(kv.Key, kv.Value);
            return content;
        }

        // ── #include 解析 ────────────────────────────────────────────────
        private static List<string> ExtractIncludes(string content)
        {
            var list = new List<string>();
            if (string.IsNullOrEmpty(content))
                return list;
            var matches = Regex.Matches(content, @"#include\s+""([^""]+)""");
            foreach (Match m in matches)
            {
                string inc = m.Groups[1].Value;
                if (!BuiltinIncludes.Contains(Path.GetFileName(inc)))
                    list.Add(inc);
            }
            return list;
        }

        /// <summary>将 include 路径解析为已知文件树中的路径，优先精确匹配，次之文件名模糊匹配。</summary>
        private string ResolveToKnownPath(string containingPath, string includePath)
        {
            includePath = includePath.Replace('\\', '/');
            string dir = "";
            int slash = containingPath.Replace('\\', '/').LastIndexOf('/');
            if (slash >= 0)
                dir = containingPath.Substring(0, slash);

            // 1. 相对路径归一化
            string combined = string.IsNullOrEmpty(dir) ? includePath : dir + "/" + includePath;
            combined = NormalizePath(combined);
            if (_allFilePaths.Contains(combined))
                return combined;

            // 2. 直接路径
            if (_allFilePaths.Contains(includePath))
                return includePath;

            // 3. 按文件名模糊匹配
            string fileName = Path.GetFileName(includePath);
            foreach (string p in _allFilePaths)
                if (
                    string.Equals(Path.GetFileName(p), fileName, StringComparison.OrdinalIgnoreCase)
                )
                    return p;

            return null;
        }

        private static string NormalizePath(string path)
        {
            var parts = path.Replace('\\', '/').Split('/');
            var stack = new List<string>();
            foreach (string p in parts)
            {
                if (p == "..")
                {
                    if (stack.Count > 0)
                        stack.RemoveAt(stack.Count - 1);
                }
                else if (p != ".")
                    stack.Add(p);
            }
            return string.Join("/", stack);
        }

        // ── 文件路径收集（供依赖解析使用） ───────────────────────────────
        private void CollectAllFilePaths(List<ShaderNode> nodes)
        {
            _allFilePaths.Clear();
            foreach (var n in nodes)
                CollectPathsRecursive(n);
        }

        private void CollectPathsRecursive(ShaderNode n)
        {
            if (!n.IsDir)
                _allFilePaths.Add(n.key);
            if (n.children != null)
                foreach (var c in n.children)
                    CollectPathsRecursive(c);
        }

        // ── 元数据 JSON 解析（手写精简解析器，无需 Newtonsoft） ──────────
        // groups 结构：{ "groupId": {"id":"...","name":"...","color":"...","files":["path",...]} }
        private static List<DownloadGroup> ParseGroupsFromMetadata(string json)
        {
            var result = new List<DownloadGroup>();
            if (string.IsNullOrEmpty(json))
                return result;

            int groupsOpen = FindJsonValueStart(json, "groups");
            if (groupsOpen < 0 || json[groupsOpen] != '{')
                return result;
            int groupsClose = FindMatchingBrace(json, groupsOpen);
            if (groupsClose < 0)
                return result;

            string groupsJson = json.Substring(groupsOpen + 1, groupsClose - groupsOpen - 1);
            int pos = 0;
            while (pos < groupsJson.Length)
            {
                int qStart = groupsJson.IndexOf('"', pos);
                if (qStart < 0)
                    break;
                int qEnd = FindStringEnd(groupsJson, qStart + 1);
                if (qEnd < 0)
                    break;
                int colon = groupsJson.IndexOf(':', qEnd + 1);
                if (colon < 0)
                    break;
                int objOpen = groupsJson.IndexOf('{', colon + 1);
                if (objOpen < 0)
                    break;
                int objClose = FindMatchingBrace(groupsJson, objOpen);
                if (objClose < 0)
                    break;

                string entry = groupsJson.Substring(objOpen, objClose - objOpen + 1);
                result.Add(
                    new DownloadGroup
                    {
                        Name = ShaderLibraryApi.ExtractString(entry, "name") ?? "未命名",
                        Files = ExtractStringArray(entry, "files"),
                    }
                );
                pos = objClose + 1;
            }
            return result;
        }

        /// <summary>返回 json 中 key 对应值的起始 '{' 或 '[' 位置。</summary>
        private static int FindJsonValueStart(string json, string key)
        {
            string pat = $"\"{key}\"";
            int ki = json.IndexOf(pat, StringComparison.Ordinal);
            if (ki < 0)
                return -1;
            int colon = json.IndexOf(':', ki + pat.Length);
            if (colon < 0)
                return -1;
            for (int i = colon + 1; i < json.Length; i++)
            {
                char c = json[i];
                if (c == '{' || c == '[')
                    return i;
                if (c != ' ' && c != '\t' && c != '\n' && c != '\r')
                    return -1;
            }
            return -1;
        }

        /// <summary>找到 json[startPos] 处的 { 或 [ 对应的闭合位置。</summary>
        private static int FindMatchingBrace(string json, int startPos)
        {
            if (startPos < 0 || startPos >= json.Length)
                return -1;
            char open = json[startPos];
            char close = open == '{' ? '}' : ']';
            int depth = 0;
            bool inStr = false;
            for (int i = startPos; i < json.Length; i++)
            {
                char c = json[i];
                if (inStr)
                {
                    if (c == '\\')
                        i++;
                    else if (c == '"')
                        inStr = false;
                }
                else if (c == '"')
                    inStr = true;
                else if (c == open)
                    depth++;
                else if (c == close)
                {
                    depth--;
                    if (depth == 0)
                        return i;
                }
            }
            return -1;
        }

        /// <summary>从 startAfterQuote 位置找到 JSON 字符串的结尾引号位置。</summary>
        private static int FindStringEnd(string s, int startAfterQuote)
        {
            for (int i = startAfterQuote; i < s.Length; i++)
            {
                if (s[i] == '\\')
                {
                    i++;
                    continue;
                }
                if (s[i] == '"')
                    return i;
            }
            return -1;
        }

        /// <summary>从 JSON 对象中提取 key 对应的字符串数组。</summary>
        private static List<string> ExtractStringArray(string json, string key)
        {
            var result = new List<string>();
            int arrOpen = FindJsonValueStart(json, key);
            if (arrOpen < 0 || json[arrOpen] != '[')
                return result;
            int arrClose = FindMatchingBrace(json, arrOpen);
            if (arrClose < 0)
                return result;
            string arrContent = json.Substring(arrOpen + 1, arrClose - arrOpen - 1);
            var matches = Regex.Matches(arrContent, @"""((?:[^""\\]|\\.)*)""");
            foreach (Match m in matches)
                result.Add(
                    m.Groups[1]
                        .Value.Replace("\\\"", "\"")
                        .Replace("\\\\", "\\")
                        .Replace("\\/", "/")
                );
            return result;
        }
    }
}
