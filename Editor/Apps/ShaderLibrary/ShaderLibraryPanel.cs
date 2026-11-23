// ShaderLibraryPanel.cs — XY 资源库面板（分组下载）
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
        // ── EditorPrefs 键（沿用旧名，保留已有服务器、登录与目录设置） ──
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
        private string _downloadTargetDir = "Assets/ResourceLibrary";
        private bool _preserveStructure = true;
        private bool _resolveDependencies = true;
        private Vector2 _downloadScroll;
        private Vector2 _downloadLogScroll;
        private List<string> _downloadLog = new();
        private HashSet<string> _allFilePaths = new();
        private HashSet<string> _textFilePaths = new(StringComparer.OrdinalIgnoreCase);

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
            public string kind = "";
            public List<ShaderNode> children = new();
            public bool IsDir => type == "dir" || type == "directory" || type == "folder";
            public bool IsText =>
                kind == "text" || key.EndsWith(".meta", StringComparison.OrdinalIgnoreCase);
        }

        [Serializable]
        private class ShaderNodeList
        {
            public List<ShaderNode> items = new();
        }

        private class DownloadGroup
        {
            public string Name = "";
            public List<string> Files = new();
            public bool Selected;
            public bool Expanded;
        }

        private class GuidCandidate
        {
            public string ServerPath = "";
            public string LocalGuid = "";
        }

        // 文本资源可选解析 #include；Unity 内置 include 不属于远程资源库，直接跳过。
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
            _downloadTargetDir = EditorPrefs.GetString(PREF_DOWNLOAD_DIR, "Assets/ResourceLibrary");

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
                EditorGUILayout.HelpBox("请先登录以访问资源库。", MessageType.Info);
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
                        "解析文本 #include 依赖",
                        _resolveDependencies
                    );
                    EditorGUILayout.HelpBox(
                        "所有文件类型都会按原始字节下载；#include 解析只作用于服务器标记为文本的资源。",
                        MessageType.None
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

            // BFS：所有资源按原始字节下载；文本资源可继续解析 #include 依赖。
            var contentCache = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
            var queue = new Queue<string>(fileSet);
            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            while (queue.Count > 0)
            {
                string path = queue.Dequeue();
                if (visited.Contains(path))
                    continue;
                visited.Add(path);

                SetStatus($"正在获取：{path}");
                var r = ShaderLibraryApi.GetRaw(_baseUrl, _token, path);
                if (!r.Success)
                {
                    // .meta 文件在服务器上不存在时静默跳过（非致命）
                    if (!path.EndsWith(".meta", StringComparison.OrdinalIgnoreCase))
                        _downloadLog.Add($"❌ 获取失败：{path}（{r.Error}）");
                    continue;
                }
                byte[] content = r.Data ?? Array.Empty<byte>();
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

                if (_resolveDependencies && TryDecodeText(path, content, out string text))
                    foreach (string inc in ExtractIncludes(text))
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

            // ── 阶段 1：为每个目标路径预先确定本地 GUID ───────────────────
            // 同一个 serverGuid 可能出现在多个库路径，不能用单值字典覆盖。
            var guidCandidates = new Dictionary<string, List<GuidCandidate>>(
                StringComparer.OrdinalIgnoreCase
            );
            foreach (var kvp in contentCache)
            {
                if (!kvp.Key.EndsWith(".meta", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (!TryDecodeText(kvp.Key, kvp.Value, out string metaContent))
                    continue;
                string serverGuid = ExtractMetaGuid(metaContent);
                if (string.IsNullOrEmpty(serverGuid))
                    continue;

                string serverPath = kvp.Key.Substring(0, kvp.Key.Length - 5);
                string localRel = _preserveStructure
                    ? serverPath
                    : Path.GetFileName(serverPath);
                string localAbs = Path.Combine(
                    absTarget,
                    localRel.Replace('/', Path.DirectorySeparatorChar)
                );
                string localMetaAbs = localAbs + ".meta";
                string localGuid = null;
                if (File.Exists(localMetaAbs))
                    localGuid = ExtractMetaGuid(
                        File.ReadAllText(localMetaAbs, System.Text.Encoding.UTF8)
                    );

                string projectRelative = FileUtil.GetProjectRelativePath(
                    localAbs.Replace('\\', '/')
                );
                string currentOwner = string.IsNullOrEmpty(localGuid)
                    ? ""
                    : AssetDatabase.GUIDToAssetPath(localGuid);
                if (
                    !string.IsNullOrEmpty(currentOwner)
                    && !string.Equals(
                        currentOwner,
                        projectRelative,
                        StringComparison.OrdinalIgnoreCase
                    )
                )
                {
                    _downloadLog.Add(
                        $"⚠️ 目标 .meta 的 GUID 已属于其他资源，重新生成：{serverPath}"
                    );
                    localGuid = null;
                }

                if (string.IsNullOrEmpty(localGuid))
                    localGuid = System.Guid.NewGuid().ToString("N");

                if (!guidCandidates.TryGetValue(serverGuid, out var candidates))
                {
                    candidates = new List<GuidCandidate>();
                    guidCandidates[serverGuid] = candidates;
                }
                candidates.Add(
                    new GuidCandidate { ServerPath = serverPath, LocalGuid = localGuid }
                );
            }

            // ── 阶段 2：首次导入前修正引用，并写入带本地 GUID 的 .meta ─────
            int success = 0,
                skipped = 0,
                failed = 0;
            int patched = 0,
                metaWritten = 0;
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
                    byte[] outputBytes = kvp.Value;
                    if (TryDecodeText(kvp.Key, kvp.Value, out string originalContent))
                    {
                        string patchedContent = PatchGuidsForPath(
                            originalContent,
                            kvp.Key,
                            guidCandidates,
                            out var ambiguousGuids
                        );
                        if (patchedContent != originalContent)
                        {
                            outputBytes = new System.Text.UTF8Encoding(false).GetBytes(
                                patchedContent
                            );
                            patched++;
                        }
                        foreach (string ambiguousGuid in ambiguousGuids)
                            _downloadLog.Add(
                                $"⚠️ 重复服务器 GUID 已按最近目录映射：{kvp.Key} ({ambiguousGuid})"
                            );
                    }

                    bool assetChanged = true;
                    if (File.Exists(localAbs))
                    {
                        byte[] existing = File.ReadAllBytes(localAbs);
                        assetChanged = !BytesEqual(existing, outputBytes);
                    }
                    if (assetChanged)
                    {
                        File.WriteAllBytes(localAbs, outputBytes);
                        success++;
                        anyWritten = true;
                    }
                    else
                        skipped++;

                    string serverMetaPath = kvp.Key + ".meta";
                    if (
                        contentCache.TryGetValue(serverMetaPath, out byte[] serverMetaBytes)
                        && TryDecodeText(serverMetaPath, serverMetaBytes, out string serverMeta)
                    )
                    {
                        string serverGuid = ExtractMetaGuid(serverMeta);
                        GuidCandidate candidate = FindCandidateForPath(
                            guidCandidates,
                            serverGuid,
                            kvp.Key
                        );
                        if (candidate != null)
                        {
                            string patchedMeta = PatchGuidsForPath(
                                serverMeta,
                                kvp.Key,
                                guidCandidates,
                                out var metaAmbiguousGuids
                            );
                            foreach (string ambiguousGuid in metaAmbiguousGuids)
                                _downloadLog.Add(
                                    $"⚠️ .meta 重复服务器 GUID 已按最近目录映射：{kvp.Key} ({ambiguousGuid})"
                                );
                            string localMeta = ReplaceMetaGuid(
                                patchedMeta,
                                candidate.LocalGuid
                            );
                            string localMetaAbs = localAbs + ".meta";
                            byte[] localMetaBytes = new System.Text.UTF8Encoding(false).GetBytes(
                                localMeta
                            );
                            bool metaChanged =
                                !File.Exists(localMetaAbs)
                                || !BytesEqual(File.ReadAllBytes(localMetaAbs), localMetaBytes);
                            if (metaChanged)
                            {
                                File.WriteAllBytes(localMetaAbs, localMetaBytes);
                                metaWritten++;
                                anyWritten = true;
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    _downloadLog.Add($"❌ 写入失败：{kvp.Key}（{ex.Message}）");
                    failed++;
                }
            }

            // ── 阶段 3：文件与 .meta 全部准备完毕后再让 Unity 导入 ───────
            string targetFwd = absTarget.Replace('\\', '/');
            string dataFwd = Application.dataPath.Replace('\\', '/');
            bool inAssets = targetFwd.StartsWith(dataFwd);
            if (inAssets && anyWritten)
                AssetDatabase.Refresh();

            string summary =
                $"✅ 完成：{success} 更新，{skipped} 无变化，{failed} 失败"
                + (patched > 0 ? $"，{patched} 个文件 GUID 已修正" : "")
                + (metaWritten > 0 ? $"，{metaWritten} 个 .meta 已本地化" : "")
                + $"（共 {contentCache.Count} 个）";
            _downloadLog.Add("─────────────────────────────");
            _downloadLog.Add(summary);
            SetStatus(summary, failed > 0);
        }

        // ── 内容类型与文本处理 ──────────────────────────────────────────

        private bool TryDecodeText(string path, byte[] bytes, out string text)
        {
            text = null;
            if (
                !_textFilePaths.Contains(path)
                && !path.EndsWith(".meta", StringComparison.OrdinalIgnoreCase)
            )
                return false;
            try
            {
                text = new System.Text.UTF8Encoding(false, true).GetString(
                    bytes ?? Array.Empty<byte>()
                );
                if (text.Length > 0 && text[0] == '\uFEFF')
                    text = text.Substring(1);
                return true;
            }
            catch (System.Text.DecoderFallbackException)
            {
                return false;
            }
        }

        private static bool BytesEqual(byte[] left, byte[] right)
        {
            if (ReferenceEquals(left, right))
                return true;
            if (left == null || right == null || left.Length != right.Length)
                return false;
            for (int i = 0; i < left.Length; i++)
                if (left[i] != right[i])
                    return false;
            return true;
        }

        // ── #include 与 GUID 解析 ────────────────────────────────────────

        private static string ExtractMetaGuid(string metaContent)
        {
            var m = Regex.Match(
                metaContent,
                @"^guid:\s*([a-f0-9]{32})",
                RegexOptions.Multiline | RegexOptions.IgnoreCase
            );
            return m.Success ? m.Groups[1].Value : null;
        }

        private static string ReplaceMetaGuid(string metaContent, string localGuid)
        {
            return Regex.Replace(
                metaContent,
                @"^guid:\s*[a-f0-9]{32}\s*$",
                "guid: " + localGuid,
                RegexOptions.Multiline | RegexOptions.IgnoreCase
            );
        }

        private static GuidCandidate FindCandidateForPath(
            Dictionary<string, List<GuidCandidate>> candidatesByGuid,
            string serverGuid,
            string sourcePath
        )
        {
            if (
                string.IsNullOrEmpty(serverGuid)
                || !candidatesByGuid.TryGetValue(serverGuid, out var candidates)
                || candidates.Count == 0
            )
                return null;

            GuidCandidate best = candidates[0];
            int bestScore = CommonDirectoryPrefixLength(sourcePath, best.ServerPath);
            for (int i = 1; i < candidates.Count; i++)
            {
                int score = CommonDirectoryPrefixLength(sourcePath, candidates[i].ServerPath);
                if (
                    score > bestScore
                    || (
                        score == bestScore
                        && string.Compare(
                            candidates[i].ServerPath,
                            best.ServerPath,
                            StringComparison.OrdinalIgnoreCase
                        ) < 0
                    )
                )
                {
                    best = candidates[i];
                    bestScore = score;
                }
            }
            return best;
        }

        private static string PatchGuidsForPath(
            string content,
            string sourcePath,
            Dictionary<string, List<GuidCandidate>> candidatesByGuid,
            out List<string> ambiguousGuids
        )
        {
            ambiguousGuids = new List<string>();
            foreach (var kv in candidatesByGuid)
            {
                GuidCandidate candidate = FindCandidateForPath(
                    candidatesByGuid,
                    kv.Key,
                    sourcePath
                );
                if (candidate == null)
                    continue;
                string pattern =
                    @"(?<![a-f0-9])" + Regex.Escape(kv.Key) + @"(?![a-f0-9])";
                if (!Regex.IsMatch(content, pattern, RegexOptions.IgnoreCase))
                    continue;
                if (kv.Value.Count > 1)
                    ambiguousGuids.Add(kv.Key);
                content = Regex.Replace(
                    content,
                    pattern,
                    candidate.LocalGuid,
                    RegexOptions.IgnoreCase
                );
            }
            return content;
        }

        private static int CommonDirectoryPrefixLength(string leftPath, string rightPath)
        {
            string leftDir = (Path.GetDirectoryName(leftPath) ?? "").Replace('\\', '/');
            string rightDir = (Path.GetDirectoryName(rightPath) ?? "").Replace('\\', '/');
            string[] left = leftDir.Split('/');
            string[] right = rightDir.Split('/');
            int count = Math.Min(left.Length, right.Length);
            int matched = 0;
            while (
                matched < count
                && string.Equals(left[matched], right[matched], StringComparison.OrdinalIgnoreCase)
            )
                matched++;
            return matched;
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
            _textFilePaths.Clear();
            foreach (var n in nodes)
                CollectPathsRecursive(n);
        }

        private void CollectPathsRecursive(ShaderNode n)
        {
            if (!n.IsDir)
            {
                _allFilePaths.Add(n.key);
                if (n.IsText)
                    _textFilePaths.Add(n.key);
            }
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
