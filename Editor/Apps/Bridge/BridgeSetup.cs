// ═══════════════════════════════════════════════════════════════
//  BridgeSetup — MCP Bridge 安装与进程管理（由 XYHub 面板调用）
//  将 Python MCP Server 和 VS Code 配置复制到项目根目录
// ═══════════════════════════════════════════════════════════════

using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEngine;

namespace Framework.XYEditor.Bridge
{
    public static class BridgeSetup
    {
        /// <summary>
        /// MCP~ 目录路径。
        /// 优先从 UPM 包路径解析（发布后），失败则回退到 Assets/XYHubTools/MCP~/（本地开发）。
        /// </summary>
        private static string McpSourceDir
        {
            get
            {
                // 优先从 UPM 包路径解析（发布后），失败回退 Assets 本地路径
                var info = UnityEditor.PackageManager.PackageInfo.FindForPackageName(
                    "com.yzj.xyhubtools"
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

            string msg = "";
            if (pyOk)
                msg += "✅ xy_mcp_server.py 已复制到项目根目录\n";
            else
                msg += "⚠ xy_mcp_server.py 复制失败（可能已存在）\n";

            if (vscOk)
                msg += "✅ .vscode/mcp.json 已创建\n";
            else
                msg += "⚠ .vscode/mcp.json 创建失败\n";

            msg += "\n下一步:\n";
            msg += "1. 安装 Python 依赖: pip install pywin32\n";
            msg +=
                "2. 在 Bridge 面板点 ▶ 启动 对应 MCP 服务\n3. VS Code 将自动连接（首次配置需 Reload Window 一次）";

            EditorUtility.DisplayDialog("XY Bridge — 安装完成", msg, "确定");
        }

        public static void CopyPythonServerOnly()
        {
            CopyPythonServer();
        }

        public static void CreateVscodeConfigOnly()
        {
            CreateVscodeConfig();
        }

        // ═══════════════════════════════════════════════════════════
        //  实现
        // ═══════════════════════════════════════════════════════════

        private static bool CopyPythonServer()
        {
            try
            {
                string src = Path.Combine(McpSourceDir, "xy_mcp_server.py");
                string dst = Path.Combine(ProjectRoot, "xy_mcp_server.py");

                if (!File.Exists(src))
                {
                    UnityEngine.Debug.LogError("[XY Bridge] 源文件不存在: " + src);
                    return false;
                }

                File.Copy(src, dst, overwrite: true);
                UnityEngine.Debug.Log("[XY Bridge] 已复制: " + dst);
                AssetDatabase.Refresh();
                return true;
            }
            catch (System.Exception ex)
            {
                UnityEngine.Debug.LogError("[XY Bridge] 复制失败: " + ex.Message);
                return false;
            }
        }

        private static bool CreateVscodeConfig()
        {
            // 将全部 MCP 分组写入 mcp.json（SSE 格式）
            return WriteMcpJsonWithGroups(new List<string>(AllGroups));
        }

        // ═══════════════════════════════════════════════════════════
        //  MCP 组管理（core / assets / scene / events）
        // ═══════════════════════════════════════════════════════════

        internal static readonly string[] AllGroups = { "core", "assets", "scene", "events" };

        /// <summary>各组 SSE 服务端口：core=3100 assets=3101 scene=3102 events=3103</summary>
        internal static readonly Dictionary<string, int> GroupPorts = new Dictionary<string, int>
        {
            { "core", 3100 },
            { "assets", 3101 },
            { "scene", 3102 },
            { "events", 3103 },
        };

        internal static string McpJsonPath => Path.Combine(ProjectRoot, ".vscode", "mcp.json");

        // ── 进程管理 ─────────────────────────────────────────────────
        private static readonly Dictionary<string, Process> _mcpProcesses =
            new Dictionary<string, Process>();

        [InitializeOnLoadMethod]
        private static void OnLoad()
        {
            // 域重载后重新关联仍在运行的 MCP 进程（域重载不杀进程）
            foreach (var g in AllGroups)
            {
                int pid = EditorPrefs.GetInt("XYBridge_PID_" + g, -1);
                if (pid <= 0)
                    continue;
                try
                {
                    var p = Process.GetProcessById(pid);
                    if (!p.HasExited)
                        _mcpProcesses[g] = p;
                    else
                        EditorPrefs.DeleteKey("XYBridge_PID_" + g);
                }
                catch
                {
                    EditorPrefs.DeleteKey("XYBridge_PID_" + g);
                }
            }
            EditorApplication.quitting += StopAllMcpGroups;
        }

        internal static bool IsMcpGroupRunning(string group)
        {
            if (!_mcpProcesses.TryGetValue(group, out var p))
                return false;
            try
            {
                return !p.HasExited;
            }
            catch
            {
                return false;
            }
        }

        internal static void StartMcpGroup(string group)
        {
            StopMcpGroup(group);
            if (!GroupPorts.TryGetValue(group, out int port))
                return;
            string python = FindPython();
            string script = Path.Combine(ProjectRoot, "xy_mcp_server.py");
            var psi = new ProcessStartInfo
            {
                FileName = python,
                Arguments =
                    "\"" + script + "\" --group=" + group + " --transport=sse --port=" + port,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            try
            {
                var proc = Process.Start(psi);
                if (proc != null)
                {
                    _mcpProcesses[group] = proc;
                    EditorPrefs.SetInt("XYBridge_PID_" + group, proc.Id);
                    UnityEngine.Debug.Log(
                        "[XY Bridge] 已启动 xybridge-"
                            + group
                            + " (PID="
                            + proc.Id
                            + ", 端口="
                            + port
                            + ")"
                    );
                }
            }
            catch (System.Exception ex)
            {
                UnityEngine.Debug.LogError("[XY Bridge] 启动失败 " + group + ": " + ex.Message);
            }
        }

        internal static void StopMcpGroup(string group)
        {
            if (_mcpProcesses.TryGetValue(group, out var proc))
            {
                try
                {
                    if (!proc.HasExited)
                        proc.Kill();
                }
                catch { }
                _mcpProcesses.Remove(group);
            }
            EditorPrefs.DeleteKey("XYBridge_PID_" + group);
        }

        internal static void StopAllMcpGroups()
        {
            foreach (var g in AllGroups)
                StopMcpGroup(g);
        }

        private static bool WriteMcpJsonWithGroups(List<string> groups)
        {
            try
            {
                string vscDir = Path.Combine(ProjectRoot, ".vscode");
                Directory.CreateDirectory(vscDir);
                string vscMcpPath = Path.Combine(vscDir, "mcp.json");

                var sb = new System.Text.StringBuilder();
                sb.AppendLine("{");
                sb.AppendLine("  \"servers\": {");

                for (int i = 0; i < groups.Count; i++)
                {
                    string g = groups[i];
                    int p = GroupPorts.ContainsKey(g) ? GroupPorts[g] : 3100;
                    sb.AppendLine("    \"xybridge-" + g + "\": {");
                    sb.AppendLine("      \"type\": \"sse\",");
                    sb.AppendLine("      \"url\": \"http://127.0.0.1:" + p + "/sse\"");
                    sb.Append("    }");
                    if (i < groups.Count - 1)
                        sb.AppendLine(",");
                    else
                        sb.AppendLine();
                }

                sb.AppendLine("  }");
                sb.AppendLine("}");

                File.WriteAllText(vscMcpPath, sb.ToString());
                UnityEngine.Debug.Log(
                    "[XY Bridge] mcp.json 更新 (SSE): " + string.Join(", ", groups)
                );
                return true;
            }
            catch (System.Exception ex)
            {
                UnityEngine.Debug.LogError("[XY Bridge] 写入失败: " + ex.Message);
                return false;
            }
        }

        private static string FindPython()
        {
            // 1) 环境变量 PYTHON_EXE
            string env = System.Environment.GetEnvironmentVariable("PYTHON_EXE");
            if (IsValidPython(env))
                return env;

            // 2) 通过 py.exe 启动器找到真实路径（最可靠）
            string pyPath = FindPythonViaPyLauncher();
            if (IsValidPython(pyPath))
                return pyPath;

            // 3) PATH 中搜索（排除 WindowsApps 占位符）
            string pathEnv = System.Environment.GetEnvironmentVariable("PATH") ?? "";
            foreach (string dir in pathEnv.Split(';'))
            {
                string d = dir.Trim();
                if (string.IsNullOrEmpty(d) || d.Contains("WindowsApps"))
                    continue;
                string candidate = Path.Combine(d, "python.exe");
                if (IsValidPython(candidate))
                    return candidate;
                candidate = Path.Combine(d, "python3.exe");
                if (IsValidPython(candidate))
                    return candidate;
            }

            // 4) 常见安装路径（兜底）
            var localAppData = System.Environment.GetFolderPath(
                System.Environment.SpecialFolder.LocalApplicationData
            );
            string[] commonPaths =
            {
                localAppData + @"\Programs\Python\Python313\python.exe",
                localAppData + @"\Programs\Python\Python312\python.exe",
                localAppData + @"\Programs\Python\Python311\python.exe",
                localAppData + @"\Programs\Python\Python310\python.exe",
                localAppData + @"\Programs\Python\Python39\python.exe",
                @"C:\Python313\python.exe",
                @"C:\Python312\python.exe",
                @"C:\Python311\python.exe",
                @"C:\Python310\python.exe",
                @"C:\Python39\python.exe",
                @"C:\Program Files\Python313\python.exe",
                @"C:\Program Files\Python312\python.exe",
                @"C:\Program Files\Python311\python.exe",
            };
            foreach (string p in commonPaths)
            {
                if (IsValidPython(p))
                    return p;
            }

            // 5) 最后兜底：直接写 "python"，让系统 PATH 查找
            return @"python";
        }

        /// <summary>通过 py.exe 启动器获取真实 Python 路径</summary>
        private static string FindPythonViaPyLauncher()
        {
            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "py",
                    Arguments = "-c \"import sys; print(sys.executable)\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                };
                using (var proc = System.Diagnostics.Process.Start(psi))
                {
                    if (proc == null)
                        return null;
                    string output = proc.StandardOutput.ReadToEnd().Trim();
                    proc.WaitForExit(5000);
                    if (!string.IsNullOrEmpty(output) && File.Exists(output))
                        return output;
                }
            }
            catch { }
            return null;
        }

        /// <summary>检查 python.exe 是否真实存在（Windows Store 占位符约 0 字节）</summary>
        private static bool IsValidPython(string path)
        {
            if (string.IsNullOrEmpty(path))
                return false;
            try
            {
                var fi = new FileInfo(path);
                return fi.Exists && fi.Length > 1024;
            }
            catch
            {
                return false;
            }
        }
    }
}
