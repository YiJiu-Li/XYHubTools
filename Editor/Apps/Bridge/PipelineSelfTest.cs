using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace Framework.XYEditor.Bridge
{
    // Real loopback requests, without blocking the editor thread needed by /api/exec.
    internal static class PipelineSelfTest
    {
        [Serializable] private class Descriptor { public int port; public string evalToken; public string projectPath; }
        [Serializable] private class Reply { public bool success; public string error; public string errorDetails; public Command[] commands; public Eval result; }
        [Serializable] private class Command { public string name; }
        [Serializable] private class Eval { public bool success; public int result; }

        internal static IEnumerable<BridgeSelfTest.TestCase> Cases()
        {
            yield return Case(24, "pipeline_discovery", "读取当前工程端口与认证信息", async () =>
            {
                var d = await Discover();
                return "127.0.0.1:" + d.port;
            });
            yield return Case(25, "unity_pipeline", "HTTP 结构化调用 editor_status", async () =>
            {
                await Request("/api/exec", "{\"command\":\"editor_status\",\"parameters\":{}}", true);
                return "editor_status HTTP OK";
            });
            yield return Case(26, "unity_pipeline_commands", "HTTP 获取实时命令列表", async () =>
            {
                var reply = await Request("/api/commands", null, false);
                if (reply.commands == null || !Array.Exists(reply.commands, c => c.name == "editor_status"))
                    throw new Exception("命令列表缺少 editor_status");
                return reply.commands.Length + " commands";
            });
            yield return Case(27, "unity_cli", "HTTP commandLine 执行 eval：1+1=2", async () =>
            {
                var reply = await Request("/api/exec", "{\"commandLine\":\"eval \\\"return 1+1;\\\"\"}", true);
                if (reply.result == null || !reply.result.success || reply.result.result != 2)
                    throw new Exception("eval 未返回 2");
                return "eval = 2（无需 CLI）";
            });
        }

        private static BridgeSelfTest.TestCase Case(int id, string name, string hint, Func<Task<string>> run)
        {
            return new BridgeSelfTest.TestCase { Idx = id, Name = name, Group = "pipeline", Hint = hint, RunAsync = run };
        }

        private static async Task<Descriptor> Discover()
        {
            var root = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            var file = Path.Combine(root, "Library", "Pipeline", ".unity-pipeline-port");
            for (int attempt = 0; ; attempt++)
            {
                try
                {
                    var d = JsonUtility.FromJson<Descriptor>(File.ReadAllText(file));
                    if (d == null || d.port < 1 || d.port > 65535 || string.IsNullOrEmpty(d.evalToken) ||
                        !string.Equals(Path.GetFullPath(d.projectPath), root, StringComparison.OrdinalIgnoreCase))
                        throw new Exception("Pipeline 服务信息无效或工程不匹配");
                    return d;
                }
                catch when (attempt < 2) { await Task.Delay(50); }
            }
        }

        private static async Task<Reply> Request(string endpoint, string body, bool requireSuccess)
        {
            var json = await RequestJson(endpoint, body);
            var reply = JsonUtility.FromJson<Reply>(json);
            if (reply == null || !string.IsNullOrEmpty(reply.error) || (requireSuccess && !reply.success))
                throw new Exception(reply == null ? "响应无效" : reply.error + " " + reply.errorDetails);
            return reply;
        }

        internal static async Task<string> RequestJson(string endpoint, string body = null)
        {
            var d = await Discover();
            using (var request = new UnityWebRequest("http://127.0.0.1:" + d.port + endpoint, body == null ? "GET" : "POST"))
            {
                request.downloadHandler = new DownloadHandlerBuffer();
                request.redirectLimit = 0;
                request.timeout = 15;
                request.SetRequestHeader("Authorization", "Bearer " + d.evalToken);
                if (body != null)
                {
                    request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body));
                    request.SetRequestHeader("Content-Type", "application/json");
                }
                var operation = request.SendWebRequest();
                while (!operation.isDone) await Task.Delay(20);
                if (request.result != UnityWebRequest.Result.Success)
                    throw new Exception("Pipeline HTTP " + request.responseCode + ": " + request.error);
                return request.downloadHandler.text;
            }
        }
    }
}
