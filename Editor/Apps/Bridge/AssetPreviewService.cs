// ═══════════════════════════════════════════════════════════════
//  AssetPreviewService — 资产缩略图
//
//  消息：type = "asset_preview"，message = 扁平 JSON：
//      {"op":"get","path":"Assets/...","width":256}
//  返回：{"ok":true,"path":"<绝对路径>.png","kind":"material", ...}
//        首次调用材质/预制体时可能返回 {"ok":true,"pending":true}，
//        客户端稍后重试即可（AssetPreview 是异步渲染的）。
//
//  为什么不用 PreviewRenderUtility：它走内置管线，在 URP 工程里渲染出来
//  几乎全黑（实测球体只有一圈暗边）。AssetPreview 走的是项目当前 SRP，
//  和 Project 窗口里看到的一致。
// ═══════════════════════════════════════════════════════════════

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Framework.XYEditor.Bridge
{
    public static class AssetPreviewService
    {
        static string OutDir
        {
            get
            {
                string root = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
                return Path.Combine(root, "Library", "XYPreviews");
            }
        }

        public static string Execute(string json)
        {
            Dictionary<string, object> a;
            try { a = FlatJson.Parse(json ?? "{}"); }
            catch (Exception ex) { return XyJ.Fail("参数不是合法 JSON: " + ex.Message); }

            string op = XyJ.Str(a, "op").Trim().ToLowerInvariant();
            try
            {
                switch (op)
                {
                    case "": case "get": return Get(a);
                    case "batch": return Batch(a);
                    default:
                        return XyJ.Fail("未知 op: " + op + "（可用: get/batch）");
                }
            }
            catch (Exception ex)
            {
                return XyJ.Fail(op + " 失败: " + ex.Message);
            }
        }

        // ───────────── get ─────────────

        static string Get(Dictionary<string, object> a)
        {
            string assetPath = XyJ.Str(a, "path").Replace('\\', '/').Trim();
            if (string.IsNullOrEmpty(assetPath)) return XyJ.Fail("缺少 path");
            if (!assetPath.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
                return XyJ.Fail("path 必须以 Assets/ 开头: " + assetPath);

            int w = Mathf.Clamp(XyJ.Int(a, "width", 256), 32, 1024);
            int h = Mathf.Clamp(XyJ.Int(a, "height", w), 32, 1024);
            bool transparent = XyJ.Bool(a, "transparent", false);
            Color bg = transparent ? new Color(0.16f, 0.16f, 0.16f, 0f) : new Color(0.16f, 0.16f, 0.16f, 1f);

            var asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(assetPath);
            if (asset == null) return XyJ.Fail("资产不存在或无法加载: " + assetPath);

            string kind;
            Texture source = null;

            if (asset is Texture2D)
            {
                kind = "texture";
                source = asset as Texture2D;
            }
            else if (asset is Sprite)
            {
                kind = "sprite";
                source = (asset as Sprite).texture;
            }
            else if (asset is Material)
            {
                kind = "material";
                source = AssetPreview.GetAssetPreview(asset);
            }
            else if (asset is GameObject)
            {
                kind = "prefab";
                source = AssetPreview.GetAssetPreview(asset);
            }
            else
            {
                kind = "icon";
                source = AssetPreview.GetMiniThumbnail(asset);
            }

            if (source == null)
            {
                // 异步预览还没生成，让客户端稍后重试
                return "{\"ok\":true,\"pending\":true,\"assetPath\":" + XyJ.Q(assetPath) +
                       ",\"kind\":" + XyJ.Q(kind) +
                       ",\"hint\":" + XyJ.Q("预览还在生成，稍后重试即可") + "}";
            }

            var tex = Scale(source, w, h, bg);
            if (tex == null) return XyJ.Fail("无法为该资产生成缩略图: " + assetPath);

            byte[] png;
            try { png = tex.EncodeToPNG(); }
            finally { UnityEngine.Object.DestroyImmediate(tex); }

            if (png == null || png.Length == 0) return XyJ.Fail("PNG 编码失败");

            Directory.CreateDirectory(OutDir);
            string file = Path.Combine(OutDir, FileName(assetPath, w, h));
            File.WriteAllBytes(file, png);

            return "{\"ok\":true,\"pending\":false,\"path\":" + XyJ.Q(file) +
                   ",\"assetPath\":" + XyJ.Q(assetPath) +
                   ",\"kind\":" + XyJ.Q(kind) +
                   ",\"width\":" + w + ",\"height\":" + h +
                   ",\"sourceSize\":\"" + source.width + "x" + source.height + "\"" +
                   ",\"bytes\":" + png.Length + "}";
        }

        // ───────────── batch ─────────────

        static string Batch(Dictionary<string, object> a)
        {
            string list = XyJ.Str(a, "paths");
            if (string.IsNullOrEmpty(list)) return XyJ.Fail("缺少 paths（逗号或换行分隔）");

            var parts = list.Split(new[] { ',', '\n', ';' }, StringSplitOptions.RemoveEmptyEntries);
            int max = Mathf.Clamp(XyJ.Int(a, "maxResults", 20), 1, 100);

            var sb = new StringBuilder(512);
            sb.Append("{\"ok\":true,\"items\":[");
            int count = 0, pending = 0;
            foreach (var raw in parts)
            {
                if (count >= max) break;
                string p = raw.Trim();
                if (p.Length == 0) continue;

                var sub = new Dictionary<string, object>(a) { ["path"] = p, ["op"] = "get" };
                var parsed = FlatJson.Parse(Get(sub));
                if (XyJ.Bool(parsed, "pending", false)) { pending++; continue; }
                if (!XyJ.Bool(parsed, "ok", false)) continue;

                if (count > 0) sb.Append(',');
                sb.Append("{\"assetPath\":").Append(XyJ.Q(p))
                  .Append(",\"path\":").Append(XyJ.Q(XyJ.Str(parsed, "path")))
                  .Append(",\"kind\":").Append(XyJ.Q(XyJ.Str(parsed, "kind"))).Append('}');
                count++;
            }
            sb.Append("],\"count\":").Append(count).Append(",\"pending\":").Append(pending).Append('}');
            return sb.ToString();
        }

        // ───────────── 缩放/采样 ─────────────

        static Texture2D Scale(Texture src, int w, int h, Color bg)
        {
            if (src == null) return null;
            var rt = RenderTexture.GetTemporary(w, h, 24, RenderTextureFormat.ARGB32);
            var prev = RenderTexture.active;
            try
            {
                RenderTexture.active = rt;
                GL.Clear(true, true, bg);
                Graphics.Blit(src, rt);
                var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
                tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                tex.Apply();
                return tex;
            }
            finally
            {
                RenderTexture.active = prev;
                RenderTexture.ReleaseTemporary(rt);
            }
        }

        static string FileName(string assetPath, int w, int h)
        {
            string safe = assetPath.Replace("Assets/", "").Replace('/', '_').Replace('\\', '_').Replace(':', '_');
            return safe + "_" + w + "x" + h + ".png";
        }
    }
}
