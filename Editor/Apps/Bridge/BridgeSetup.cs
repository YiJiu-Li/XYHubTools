// ═══════════════════════════════════════════════════════════════
//  BridgeSetup — MCP Bridge 安装配置（由 XYHub 面板调用）
//
//  Hub 模式：把 xy_mcp_server.py 安装到与工程无关的固定位置
//      ~/.xybridge/xy_mcp_server.py
//  该脚本会自动发现所有存活的 Unity 实例（见其头部说明），因此：
//    · MCP 配置只需写一次，之后导入任何新工程都不用再改
//    · 同时开着多个 Unity 时，用 unity_list_projects / unity_use_project 切换目标
//
//  写入的配置：
//    · Codex    ~/.codex/config.toml        （单条 [mcp_servers.xybridge]）
//    · VS Code  <工程>/.vscode/mcp.json     （单条，指向固定脚本）
//    · WorkBuddy ~/.workbuddy-ai/mcp.json   （单条，交由 python --install-config 合并）
//
//  脚本只用 Python 标准库（2.0 起不再需要 pywin32），
//  但解释器命令名按平台解析：Windows 是 python，macOS / Linux 通常是 python3。
// ═══════════════════════════════════════════════════════════════

using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Security.Cryptography;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Framework.XYEditor.Bridge
{
    public static class BridgeSetup
    {
        /// <summary>固定的 MCP server 名（不再按工程名区分，避免配置随工程增长）。</summary>
        public const string StableServerName = "xybridge";

        /// <summary>
        /// MCP~ 目录路径。
        /// 优先从 UPM 包路径解析（发布后），失败则回退到 Assets/XYHubTools/MCP~/（本地开发）。
        /// </summary>
        private static string McpSourceDir
        {
            get
            {
                // 找自己所在的包用 FindForAssembly：Unity 2021.3 没有
                // PackageInfo.FindForPackageName（2022.2 才加），会 CS0117。
                // 包嵌在 Assets 下时返回 null，由调用方回退到 dataPath。
                var info = UnityEditor.PackageManager.PackageInfo.FindForAssembly(
                    typeof(BridgeSetup).Assembly
                );
                if (info != null && !string.IsNullOrEmpty(info.resolvedPath))
                {
                    var upmPath = Path.Combine(info.resolvedPath, "MCP~");
                    if (Directory.Exists(upmPath))
                        return upmPath;
                }
                return Path.Combine(Application.dataPath, "XYHubTools", "MCP~");
            }
        }

        private static string ProjectRoot => Path.GetDirectoryName(Application.dataPath);

        /// <summary>规则包目录，解析方式与 McpSourceDir 一致。</summary>
        private static string RulesSourceDir
        {
            get
            {
                var info = UnityEditor.PackageManager.PackageInfo.FindForAssembly(
                    typeof(BridgeSetup).Assembly
                );
                if (info != null && !string.IsNullOrEmpty(info.resolvedPath))
                {
                    var upmPath = Path.Combine(info.resolvedPath, "Agent~", "rules");
                    if (Directory.Exists(upmPath))
                        return upmPath;
                }
                return Path.Combine(Application.dataPath, "XYHubTools", "Agent~", "rules");
            }
        }

        private static string UserProfile =>
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        /// <summary>Hub 脚本的固定安装目录（与任何工程无关）。</summary>
        internal static string StableDir => Path.Combine(UserProfile, ".xybridge");

        /// <summary>Hub 脚本的固定安装路径。</summary>
        internal static string StableScript => Path.Combine(StableDir, "xy_mcp_server.py");

        private static double _nextScriptCheck;
        private static string _scriptStatus;

        internal static string ScriptStatus
        {
            get
            {
                if (_scriptStatus != null && EditorApplication.timeSinceStartup < _nextScriptCheck)
                    return _scriptStatus;
                _nextScriptCheck = EditorApplication.timeSinceStartup + 2;
                try
                {
                    string source = Path.Combine(McpSourceDir, "xy_mcp_server.py");
                    if (!File.Exists(StableScript)) return _scriptStatus = "尚未安装 Hub 脚本";
                    string installedText = File.ReadAllText(StableScript);
                    _scriptStatus = "已安装 " + ScriptVersion(installedText) + " / " + ToolCount(installedText) + " 个工具";
                    _scriptStatus += File.Exists(source)
                        ? (ScriptHash(source) == ScriptHash(StableScript) ? " / 与安装源一致" : " / 与安装源不同，请检查后更新")
                        : " / 安装源不存在";
                    return _scriptStatus;
                }
                catch (Exception ex) { return _scriptStatus = "脚本检查失败: " + ex.Message; }
            }
        }

        private static string ScriptVersion(string text)
        {
            var match = Regex.Match(text, "(?m)^SERVER_VERSION\\s*=\\s*\"([^\"]+)\"");
            return match.Success ? match.Groups[1].Value : "0.0.0";
        }

        private static int ToolCount(string text) => Regex.Matches(text, "\"name\"\\s*:\\s*\"unity_[^\"]+\"").Count;

        private static string ScriptHash(string path)
        {
            using (var sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path))).Replace("-", "");
        }

        /// <summary>
        /// project 体检不连 Unity，规则包只能从固定位置取，所以安装时把 Agent~/rules 复制过去。
        /// 用哈希比对，内容没变就不写盘。
        /// </summary>
        private static void InstallRulePacks()
        {
            string source = RulesSourceDir;
            if (!Directory.Exists(source))
                return;
            string target = Path.Combine(StableDir, "rules");
            foreach (string packDir in Directory.GetDirectories(source))
            {
                string file = Path.Combine(packDir, "rules.json");
                if (!File.Exists(file))
                    continue;
                string destination = Path.Combine(
                    target,
                    Path.GetFileName(packDir),
                    "rules.json"
                );
                try
                {
                    if (File.Exists(destination) && ScriptHash(file) == ScriptHash(destination))
                        continue;
                    Directory.CreateDirectory(Path.GetDirectoryName(destination));
                    File.Copy(file, destination, true);
                }
                catch (Exception ex)
                {
                    Debug.LogWarning("[XY Bridge] 规则包安装失败 " + file + ": " + ex.Message);
                }
            }
        }

        private static void InstallScript(string source, string destination)
        {
            if (File.Exists(destination) && ScriptHash(source) == ScriptHash(destination)) return;
            string temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.Copy(source, temporary);
                if (File.Exists(destination))
                {
                    try
                    {
                        // 优先原子替换：中途失败不会留下半个脚本
                        File.Replace(temporary, destination, destination + ".previous");
                    }
                    catch (Exception ex)
                        when (ex is PlatformNotSupportedException
                            || ex is NotSupportedException
                            || ex is IOException
                            || ex is UnauthorizedAccessException)
                    {
                        // Mono 在 macOS / Linux 上对 File.Replace 支持不一致，
                        // 退回到「先备份再覆盖」：不是原子操作，但一定能装完
                        Debug.LogWarning(
                            "[XY Bridge] 原子替换不可用（" + ex.GetType().Name + "），改用备份+覆盖"
                        );
                        try
                        {
                            File.Copy(destination, destination + ".previous", true);
                        }
                        catch { }
                        File.Copy(temporary, destination, true);
                    }
                }
                else File.Move(temporary, destination);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }

        // ═══════════════════════════════════════════════════════════
        //  XYHub 面板入口
        // ═══════════════════════════════════════════════════════════

        public static void SetupAll()
        {
            if (!Directory.Exists(McpSourceDir))
            {
                var errorMsg =
                    "找不到 MCP~ 目录，请确认 XYHubTools 插件已正确放置。"
                    + "\n预期路径: "
                    + McpSourceDir;
                EditorUtility.DisplayDialog("XY Bridge", errorMsg, "确定");
                return;
            }

            bool pyOk = CopyPythonServer();
            bool vscOk = CreateVscodeConfig();
            bool codexOk = CreateCodexConfig();
            bool wbOk = EnsureWorkbuddyConfig();

            var msg = new StringBuilder();
            if (pyOk)
            {
                msg.AppendLine("✅ Hub 脚本已安装（配置只写一次，以后换工程无需再改）");
                msg.AppendLine("   " + StableScript);
            }
            else
                msg.AppendLine("⚠ Hub 脚本安装失败，请查看 Console");

            msg.AppendLine(vscOk ? "✅ VS Code 项目级配置已写入" : "⚠ VS Code 配置写入失败");
            msg.AppendLine(codexOk ? "✅ Codex 用户级配置已写入" : "⚠ Codex 配置写入失败");
            msg.AppendLine(
                wbOk
                    ? "✅ WorkBuddy 配置已写入（如未生效，去连接器管理页「信任」xybridge）"
                    : "⚠ WorkBuddy 配置未写入（需 python 在 PATH 中，或设置 XYBRIDGE_PYTHON）"
            );

            msg.AppendLine();
            msg.AppendLine("下一步:");
            msg.AppendLine("1. 无需额外 Python 依赖（2.0 起脚本只用标准库，不再需要 pywin32）");
            msg.AppendLine("2. Codex / VS Code 均使用 stdio，重启客户端或新开会话后生效");
            msg.AppendLine("3. 多开 Unity 时用 unity_list_projects 查看、unity_use_project 切换");
            msg.AppendLine("4. Pipeline 命令已改为 HTTP 直连，无需安装 Unity CLI");
            msg.AppendLine("   " + XY.PipelineBootstrap.PipelineInstaller.Status);

            EditorUtility.DisplayDialog("XY Bridge — 安装完成", msg.ToString(), "确定");
        }

        public static void CopyPythonServerOnly()
        {
            CopyPythonServer();
        }

        public static void CreateCodexConfigOnly()
        {
            CreateCodexConfig();
        }

        public static void CreateVscodeConfigOnly()
        {
            CreateVscodeConfig();
        }

        public static void EnsureWorkbuddyConfigOnly()
        {
            EnsureWorkbuddyConfig();
        }

        // ═══════════════════════════════════════════════════════════
        //  实现
        // ═══════════════════════════════════════════════════════════

        private static string _pythonCommand;

        /// <summary>
        /// 解析可用的 Python 解释器命令名。
        /// macOS / Linux 上 `python` 经常不存在或指向 Python 2，必须优先试 python3；
        /// Windows 上 `python` 可能是 Microsoft Store 的占位程序（运行会弹商店），
        /// 所以不能只看 PATH，要真的执行一次并确认能打印版本号。
        /// </summary>
        internal static string ResolvePythonCommand()
        {
            if (!string.IsNullOrEmpty(_pythonCommand))
                return _pythonCommand;

            string overrideValue = Environment.GetEnvironmentVariable("XYBRIDGE_PYTHON");
            if (!string.IsNullOrEmpty(overrideValue))
                return _pythonCommand = overrideValue;

            bool windows = Application.platform == RuntimePlatform.WindowsEditor;
            string[] candidates = windows
                ? new[] { "python", "python3" }
                : new[] { "python3", "python" };

            foreach (string candidate in candidates)
            {
                if (ProbePython(candidate, out string version))
                {
                    Debug.Log("[XY Bridge] 使用 Python 解释器: " + candidate + " (" + version + ")");
                    return _pythonCommand = candidate;
                }
            }

            // 都探测不到时退回平台默认值：让用户看到明确失败，而不是悄悄写一条跑不起来的配置
            Debug.LogWarning("[XY Bridge] 未探测到可用的 Python，配置将写入平台默认命令名。");
            return _pythonCommand = windows ? "python" : "python3";
        }

        private static bool ProbePython(string exe, out string version)
        {
            version = null;
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = exe,
                    Arguments = "-c \"import sys;print(sys.version.split()[0])\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                };
                using (var p = Process.Start(psi))
                {
                    if (p == null)
                        return false;
                    string stdout = p.StandardOutput.ReadToEnd().Trim();
                    p.StandardError.ReadToEnd();
                    if (!p.WaitForExit(5000))
                    {
                        try
                        {
                            p.Kill();
                        }
                        catch { }
                        return false;
                    }
                    // 版本号形如 3.11.4；解析不出来就当作不可用（挡住 Store 占位程序）
                    if (p.ExitCode != 0 || stdout.Length == 0)
                        return false;
                    if (stdout.Split('.')[0] != "3")
                        return false;
                    version = stdout;
                    return true;
                }
            }
            catch
            {
                return false;
            }
        }

        private static bool CopyPythonServer()
        {
            try
            {
                string src = Path.Combine(McpSourceDir, "xy_mcp_server.py");
                if (!File.Exists(src))
                {
                    Debug.LogError("[XY Bridge] 源文件不存在: " + src);
                    return false;
                }

                string sourceText = File.ReadAllText(src);
                if (ToolCount(sourceText) == 0) throw new InvalidDataException("安装源未定义 MCP 工具");
                if (File.Exists(StableScript))
                {
                    string installedText = File.ReadAllText(StableScript);
                    Version sourceVersion, installedVersion;
                    if ((Version.TryParse(ScriptVersion(sourceText), out sourceVersion)
                        && Version.TryParse(ScriptVersion(installedText), out installedVersion)
                        && sourceVersion < installedVersion) || ToolCount(sourceText) < ToolCount(installedText))
                        throw new InvalidDataException("安装源版本或工具数量低于已安装脚本，已阻止覆盖。请更新当前工程的 MCP~ 安装源。");
                }

                // 1) 固定位置：Hub 模式的主副本，与工程无关，所有配置都指向它
                Directory.CreateDirectory(StableDir);
                InstallScript(src, StableScript);
                Debug.Log("[XY Bridge] 已安装 Hub 脚本: " + StableScript);

                // 1b) 规则包：project 体检不连 Unity，规则只能从固定位置取
                InstallRulePacks();

                // 2) 工程根目录：保留给旧的「一工程一脚本」配置，新配置不再引用
                InstallScript(src, Path.Combine(ProjectRoot, "xy_mcp_server.py"));
                _nextScriptCheck = 0;

                AssetDatabase.Refresh();
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogError("[XY Bridge] 复制失败: " + ex.Message);
                return false;
            }
        }

        private static bool CreateCodexConfig()
        {
            try
            {
                if (string.IsNullOrEmpty(UserProfile))
                    return false;

                string codexDir = Path.Combine(UserProfile, ".codex");
                Directory.CreateDirectory(codexDir);

                string configPath = Path.Combine(codexDir, "config.toml");
                string config = File.Exists(configPath)
                    ? File.ReadAllText(configPath, Encoding.UTF8)
                    : "";

                // 清掉此前按工程生成的 [mcp_servers.xybridge_*]，只留一条固定条目
                config = RemoveManagedCodexServers(config);
                if (!config.EndsWith(Environment.NewLine) && config.Length > 0)
                    config += Environment.NewLine;
                config += Environment.NewLine + BuildCodexServerBlock(StableServerName);

                File.WriteAllText(configPath, config, new UTF8Encoding(false));
                Debug.Log(
                    "[XY Bridge] Codex MCP 配置已更新: "
                        + StableServerName
                        + " -> "
                        + StableScript
                );
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogError("[XY Bridge] Codex 配置写入失败: " + ex.Message);
                return false;
            }
        }

        private static bool CreateVscodeConfig()
        {
            try
            {
                string vscodeDir = Path.Combine(ProjectRoot, ".vscode");
                Directory.CreateDirectory(vscodeDir);

                string configPath = Path.Combine(vscodeDir, "mcp.json");
                if (File.Exists(configPath))
                {
                    string old = File.ReadAllText(configPath, Encoding.UTF8);
                    string backupPath = configPath + ".bak";
                    if (!File.Exists(backupPath))
                        File.WriteAllText(backupPath, old, new UTF8Encoding(false));
                }

                File.WriteAllText(
                    configPath,
                    BuildVscodeMcpJson(StableServerName),
                    new UTF8Encoding(false)
                );
                Debug.Log("[XY Bridge] VS Code MCP 配置已更新: " + configPath);
                AssetDatabase.Refresh();
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogError("[XY Bridge] VS Code 配置写入失败: " + ex.Message);
                return false;
            }
        }

        /// <summary>
        /// 写 WorkBuddy 的 ~/.workbuddy-ai/mcp.json。
        /// JSON 合并交给 Hub 脚本自己完成（python 有 json 模块，比在 C# 里手搓合并可靠）：
        ///     python &lt;StableScript&gt; --install-config
        /// 该调用是幂等的：会清掉旧的 xybridge_&lt;工程名&gt; 条目，只保留一条 xybridge。
        /// </summary>
        private static bool EnsureWorkbuddyConfig()
        {
            try
            {
                if (!File.Exists(StableScript))
                {
                    Debug.LogWarning("[XY Bridge] Hub 脚本尚未安装，跳过 WorkBuddy 配置");
                    return false;
                }

                string python = ResolvePythonCommand();

                var psi = new ProcessStartInfo
                {
                    FileName = python,
                    Arguments = "\"" + StableScript + "\" --install-config",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8,
                };

                using (var p = Process.Start(psi))
                {
                    string stdout = p.StandardOutput.ReadToEnd();
                    string stderr = p.StandardError.ReadToEnd();
                    p.WaitForExit(15000);

                    if (p.ExitCode == 0 && !string.IsNullOrWhiteSpace(stdout))
                    {
                        Debug.Log("[XY Bridge] WorkBuddy 配置:\n" + stdout.Trim());
                        return true;
                    }

                    Debug.LogWarning(
                        "[XY Bridge] WorkBuddy 配置写入失败（exit="
                            + p.ExitCode
                            + "）: "
                            + (stderr.Trim() + stdout.Trim())
                    );
                    return false;
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning(
                    "[XY Bridge] 未能调用 python 写 WorkBuddy 配置: "
                        + ex.Message
                        + "\n可设置环境变量 XYBRIDGE_PYTHON 指定解释器，或手动执行：\n"
                        + "python \""
                        + StableScript
                        + "\" --install-config"
                );
                return false;
            }
        }

        /// <summary>固定的 MCP server 名。</summary>
        internal static string GetCodexServerName()
        {
            return StableServerName;
        }

        private static string BuildCodexServerBlock(string serverName)
        {
            // 脚本用绝对路径，不再依赖 cwd —— 这样它和工程解耦，一份脚本服务所有工程
            string script = EscapeTomlString(StableScript);
            string python = EscapeTomlString(ResolvePythonCommand());
            return
                "[mcp_servers."
                + serverName
                + "]"
                + Environment.NewLine
                + "command = \""
                + python
                + "\""
                + Environment.NewLine
                + "args = [\""
                + script
                + "\", \"--transport=stdio\"]"
                + Environment.NewLine
                + "startup_timeout_sec = 30"
                + Environment.NewLine
                + "[mcp_servers."
                + serverName
                + ".env]"
                + Environment.NewLine
                + "PYTHONIOENCODING = \"utf-8\""
                + Environment.NewLine
                + "PYTHONUTF8 = \"1\""
                + Environment.NewLine;
        }

        private static string BuildVscodeMcpJson(string serverName)
        {
            string script = EscapeJsonString(StableScript);
            string python = EscapeJsonString(ResolvePythonCommand());
            return
                "{"
                + Environment.NewLine
                + "  \"servers\": {"
                + Environment.NewLine
                + "    \""
                + EscapeJsonString(serverName)
                + "\": {"
                + Environment.NewLine
                + "      \"type\": \"stdio\","
                + Environment.NewLine
                + "      \"command\": \""
                + python
                + "\","
                + Environment.NewLine
                + "      \"args\": [\""
                + script
                + "\", \"--transport=stdio\"],"
                + Environment.NewLine
                + "      \"env\": {"
                + Environment.NewLine
                + "        \"PYTHONIOENCODING\": \"utf-8\","
                + Environment.NewLine
                + "        \"PYTHONUTF8\": \"1\""
                + Environment.NewLine
                + "      }"
                + Environment.NewLine
                + "    }"
                + Environment.NewLine
                + "  }"
                + Environment.NewLine
                + "}"
                + Environment.NewLine;
        }

        /// <summary>
        /// 删除本工具此前写过的所有 [mcp_servers.xybridge*] 表（含 .env 子表）。
        /// 只匹配本工具的命名空间，不碰用户其它 mcp_servers。
        /// </summary>
        private static string RemoveManagedCodexServers(string config)
        {
            if (string.IsNullOrEmpty(config))
                return "";

            string[] lines = config.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            var sb = new StringBuilder();
            bool skipping = false;
            var managed = new Regex(
                @"^\[mcp_servers\.xybridge(_[A-Za-z0-9_-]+)?(\.env)?\]$",
                RegexOptions.Compiled
            );

            foreach (string line in lines)
            {
                string trimmed = line.Trim();
                bool isTable = trimmed.StartsWith("[") && trimmed.EndsWith("]");
                if (isTable)
                    skipping = managed.IsMatch(trimmed);

                if (!skipping)
                    sb.AppendLine(line);
            }

            return Regex
                .Replace(
                    sb.ToString(),
                    @"(\r?\n){3,}",
                    Environment.NewLine + Environment.NewLine
                )
                .TrimEnd();
        }

        private static string EscapeTomlString(string value)
        {
            return (value ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"");
        }

        private static string EscapeJsonString(string value)
        {
            return (value ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"");
        }
    }
}
