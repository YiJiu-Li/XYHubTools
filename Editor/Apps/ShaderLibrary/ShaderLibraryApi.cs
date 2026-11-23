// ShaderLibraryApi.cs — XY Asset Library HTTP 客户端（类名保留兼容旧代码）
// 对接 http://310007.xyz:17851 接口，所有调用均为同步阻塞式（适合 Editor 工具）。

using System;
using System.IO;
using System.Net;
using System.Text;

namespace Framework.XYEditor.ShaderLibrary
{
    /// <summary>
    /// XY Asset Library REST API 封装。
    /// 所有方法均返回 <see cref="ApiResult"/>，不抛异常。
    /// </summary>
    public static class ShaderLibraryApi
    {
        // ── 结果结构 ──────────────────────────────────────────────────────
        public struct ApiResult
        {
            public bool Success;
            public string Data; // 响应体原文
            public string Error; // 错误描述（Success=false 时有效）
        }

        public struct BinaryApiResult
        {
            public bool Success;
            public byte[] Data;
            public string ContentType;
            public string Error;
        }

        // ═══════════════════════════════════════════════════════════════════
        //  核心 HTTP（使用 HttpWebRequest，完全同步，兼容 Unity Mono）
        // ═══════════════════════════════════════════════════════════════════

        private static ApiResult Request(string url, string method, string body, string token)
        {
            try
            {
                var req = (HttpWebRequest)WebRequest.Create(url);
                req.Method = method;
                req.ContentType = "application/json";
                req.Accept = "application/json";
                req.Timeout = 15000;

                if (!string.IsNullOrEmpty(token))
                    req.Headers["authorization"] = "Bearer " + token;

                if (body != null)
                {
                    byte[] bytes = Encoding.UTF8.GetBytes(body);
                    req.ContentLength = bytes.Length;
                    using var ws = req.GetRequestStream();
                    ws.Write(bytes, 0, bytes.Length);
                }
                else
                {
                    req.ContentLength = 0;
                }

                using var resp = (HttpWebResponse)req.GetResponse();
                using var reader = new StreamReader(resp.GetResponseStream(), Encoding.UTF8);
                return new ApiResult { Success = true, Data = reader.ReadToEnd() };
            }
            catch (WebException ex) when (ex.Response is HttpWebResponse errResp)
            {
                try
                {
                    using var reader = new StreamReader(errResp.GetResponseStream(), Encoding.UTF8);
                    return new ApiResult
                    {
                        Success = false,
                        Error = $"HTTP {(int)errResp.StatusCode}: {reader.ReadToEnd()}",
                    };
                }
                catch
                {
                    return new ApiResult { Success = false, Error = ex.Message };
                }
            }
            catch (Exception e)
            {
                return new ApiResult { Success = false, Error = e.Message };
            }
        }

        private static ApiResult Get(string url, string token = null) =>
            Request(url, "GET", null, token);

        private static ApiResult Post(string url, string body, string token = null) =>
            Request(url, "POST", body, token);

        private static BinaryApiResult GetBytes(string url, string token = null)
        {
            try
            {
                var req = (HttpWebRequest)WebRequest.Create(url);
                req.Method = "GET";
                req.Accept = "application/octet-stream";
                req.Timeout = 30000;

                if (!string.IsNullOrEmpty(token))
                    req.Headers["authorization"] = "Bearer " + token;

                using var resp = (HttpWebResponse)req.GetResponse();
                using var stream = resp.GetResponseStream();
                using var memory = new MemoryStream();
                stream.CopyTo(memory);
                return new BinaryApiResult
                {
                    Success = true,
                    Data = memory.ToArray(),
                    ContentType = resp.ContentType,
                };
            }
            catch (WebException ex) when (ex.Response is HttpWebResponse errResp)
            {
                try
                {
                    using var reader = new StreamReader(errResp.GetResponseStream(), Encoding.UTF8);
                    return new BinaryApiResult
                    {
                        Success = false,
                        Error = $"HTTP {(int)errResp.StatusCode}: {reader.ReadToEnd()}",
                    };
                }
                catch
                {
                    return new BinaryApiResult { Success = false, Error = ex.Message };
                }
            }
            catch (Exception e)
            {
                return new BinaryApiResult { Success = false, Error = e.Message };
            }
        }

        // ═══════════════════════════════════════════════════════════════════
        //  Auth
        // ═══════════════════════════════════════════════════════════════════

        public static ApiResult Login(string baseUrl, string username, string password) =>
            Post(
                $"{baseUrl}/api/auth/login",
                $"{{\"username\":{Str(username)},\"password\":{Str(password)}}}"
            );

        public static ApiResult Logout(string baseUrl, string token) =>
            Post($"{baseUrl}/api/auth/logout", "{}", token);

        // ═══════════════════════════════════════════════════════════════════
        //  Assets
        // ═══════════════════════════════════════════════════════════════════

        public static ApiResult GetTree(string baseUrl, string token) =>
            Get($"{baseUrl}/api/assets/tree", token);

        public static ApiResult GetContent(string baseUrl, string token, string path) =>
            Get($"{baseUrl}/api/assets/content?path={Uri.EscapeDataString(path)}", token);

        /// <summary>按原始字节下载资源，不对扩展名或内容类型做限制。</summary>
        public static BinaryApiResult GetRaw(string baseUrl, string token, string path) =>
            GetBytes(
                $"{baseUrl}/api/assets/raw?path={Uri.EscapeDataString(path)}&download=true",
                token
            );

        public static ApiResult GetMetadata(string baseUrl, string token) =>
            Get($"{baseUrl}/api/assets/metadata", token);

        // ═══════════════════════════════════════════════════════════════════
        //  Health
        // ═══════════════════════════════════════════════════════════════════

        public static ApiResult Health(string baseUrl) => Get($"{baseUrl}/api/health");

        // ═══════════════════════════════════════════════════════════════════
        //  JSON 工具
        // ═══════════════════════════════════════════════════════════════════

        /// <summary>将字符串值编码为 JSON 字符串字面量（含双引号）。</summary>
        public static string Str(string s)
        {
            if (s == null)
                return "null";
            var sb = new StringBuilder(s.Length + 2);
            sb.Append('"');
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"':
                        sb.Append("\\\"");
                        break;
                    case '\\':
                        sb.Append("\\\\");
                        break;
                    case '\n':
                        sb.Append("\\n");
                        break;
                    case '\r':
                        sb.Append("\\r");
                        break;
                    case '\t':
                        sb.Append("\\t");
                        break;
                    default:
                        if (c < 0x20)
                            sb.Append($"\\u{(int)c:x4}");
                        else
                            sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
            return sb.ToString();
        }

        /// <summary>从 JSON 对象字符串中提取指定 key 的字符串值（浅层，不跨嵌套）。</summary>
        public static string ExtractString(string json, string key)
        {
            if (string.IsNullOrEmpty(json))
                return null;
            string search = $"\"{key}\"";
            int ki = json.IndexOf(search, StringComparison.Ordinal);
            if (ki < 0)
                return null;
            ki = json.IndexOf(':', ki + search.Length);
            if (ki < 0)
                return null;
            ki++;
            while (
                ki < json.Length
                && (json[ki] == ' ' || json[ki] == '\t' || json[ki] == '\n' || json[ki] == '\r')
            )
                ki++;
            if (ki >= json.Length || json[ki] != '"')
                return null;
            ki++; // skip opening quote
            var sb = new StringBuilder();
            while (ki < json.Length)
            {
                char c = json[ki++];
                if (c == '"')
                    break;
                if (c == '\\' && ki < json.Length)
                {
                    char esc = json[ki++];
                    switch (esc)
                    {
                        case '"':
                            sb.Append('"');
                            break;
                        case '\\':
                            sb.Append('\\');
                            break;
                        case 'n':
                            sb.Append('\n');
                            break;
                        case 'r':
                            sb.Append('\r');
                            break;
                        case 't':
                            sb.Append('\t');
                            break;
                        default:
                            sb.Append(esc);
                            break;
                    }
                }
                else
                    sb.Append(c);
            }
            return sb.ToString();
        }
    }
}
