// ═══════════════════════════════════════════════════════════════
//  BridgeSetup — MCP Bridge 安装配置（由 XYHub 面板调用）
//  将 Python MCP Server 复制到项目根目录，并写入 Codex stdio 配置
// ═══════════════════════════════════════════════════════════════

using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
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
            bool codexOk = CreateCodexConfig();

            string msg = "";
            if (pyOk)
                msg += "✅ xy_mcp_server.py 已复制到项目根目录\n";
            else
                msg += "⚠ xy_mcp_server.py 复制失败（可能已存在）\n";

            if (codexOk)
                msg += "✅ Codex 用户级 MCP 配置已写入\n";
            else
                msg += "⚠ Codex 用户级 MCP 配置写入失败\n";

            msg += "\n下一步:\n";
            msg += "1. 安装 Python 依赖: pip install pywin32\n";
            msg += "2. Codex 使用 stdio，重启 Codex 或新开项目线程后生效";

            EditorUtility.DisplayDialog("XY Bridge — 安装完成", msg, "确定");
        }

        public static void CopyPythonServerOnly()
        {
            CopyPythonServer();
        }

        public static void CreateCodexConfigOnly()
        {
            CreateCodexConfig();
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

        private static bool CreateCodexConfig()
        {
            try
            {
                string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                if (string.IsNullOrEmpty(userProfile))
                    return false;

                string codexDir = Path.Combine(userProfile, ".codex");
                Directory.CreateDirectory(codexDir);

                string configPath = Path.Combine(codexDir, "config.toml");
                string serverName = GetCodexServerName();
                string block = BuildCodexServerBlock(serverName);
                string config = File.Exists(configPath)
                    ? File.ReadAllText(configPath, Encoding.UTF8)
                    : "";

                string pattern =
                    @"(?ms)^\[mcp_servers\." + Regex.Escape(serverName) + @"\]\s*.*?(?=^\[|\z)";
                if (Regex.IsMatch(config, pattern))
                    config = Regex.Replace(config, pattern, block.TrimEnd() + Environment.NewLine);
                else
                {
                    if (!config.EndsWith(Environment.NewLine) && config.Length > 0)
                        config += Environment.NewLine;
                    config += Environment.NewLine + block;
                }

                File.WriteAllText(configPath, config, new UTF8Encoding(false));
                UnityEngine.Debug.Log(
                    "[XY Bridge] Codex MCP 配置已更新: "
                        + serverName
                        + " -> "
                        + ProjectRoot
                );
                return true;
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogError("[XY Bridge] Codex 配置写入失败: " + ex.Message);
                return false;
            }
        }

        internal static string GetCodexServerName()
        {
            string projectName = new DirectoryInfo(ProjectRoot).Name;
            var sb = new StringBuilder("xybridge_");
            foreach (char c in projectName.ToLowerInvariant())
            {
                if ((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9'))
                    sb.Append(c);
                else
                    sb.Append('_');
            }
            return Regex.Replace(sb.ToString(), "_+", "_").TrimEnd('_');
        }

        private static string BuildCodexServerBlock(string serverName)
        {
            string root = EscapeTomlString(ProjectRoot);
            return
                "[mcp_servers."
                + serverName
                + "]"
                + Environment.NewLine
                + "command = \"python\""
                + Environment.NewLine
                + "args = [\"xy_mcp_server.py\", \"--transport=stdio\"]"
                + Environment.NewLine
                + "cwd = \""
                + root
                + "\""
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

        private static string EscapeTomlString(string value)
        {
            return (value ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"");
        }

    }
}
