// ═══════════════════════════════════════════════════════════════
//  BridgeJson — 新增服务共用的小工具（JSON 输出 / 参数取值 / 场景路径）
//  FlatJson 解析器在 ShaderGraphService.cs 里，同程序集可直接复用。
// ═══════════════════════════════════════════════════════════════

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

namespace Framework.XYEditor.Bridge
{
    internal static class XyJ
    {
        internal static string Q(string s)
        {
            if (s == null) return "\"\"";
            var sb = new StringBuilder(s.Length + 8);
            sb.Append('"');
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
            return sb.ToString();
        }

        internal static string B(bool b) => b ? "true" : "false";

        internal static string F(float f) => f.ToString("0.######", CultureInfo.InvariantCulture);

        internal static string D(double d) => d.ToString("0.######", CultureInfo.InvariantCulture);

        internal static string Fail(string msg) => "{\"ok\":false,\"error\":" + Q(msg ?? "") + "}";

        internal static string Str(Dictionary<string, object> a, string key)
        {
            object v;
            if (a == null || !a.TryGetValue(key, out v) || v == null) return "";
            return v as string ?? Convert.ToString(v, CultureInfo.InvariantCulture);
        }

        internal static int Int(Dictionary<string, object> a, string key, int fallback)
        {
            object v;
            if (a == null || !a.TryGetValue(key, out v) || v == null) return fallback;
            if (v is double) return (int)(double)v;
            if (v is bool) return (bool)v ? 1 : 0;
            int r;
            return int.TryParse(Convert.ToString(v, CultureInfo.InvariantCulture), out r) ? r : fallback;
        }

        internal static float Float(Dictionary<string, object> a, string key, float fallback)
        {
            object v;
            if (a == null || !a.TryGetValue(key, out v) || v == null) return fallback;
            if (v is double) return (float)(double)v;
            float r;
            return float.TryParse(Convert.ToString(v, CultureInfo.InvariantCulture),
                NumberStyles.Float, CultureInfo.InvariantCulture, out r) ? r : fallback;
        }

        internal static bool Bool(Dictionary<string, object> a, string key, bool fallback)
        {
            object v;
            if (a == null || !a.TryGetValue(key, out v) || v == null) return fallback;
            if (v is bool) return (bool)v;
            string s = Convert.ToString(v, CultureInfo.InvariantCulture);
            return s == "1" || string.Equals(s, "true", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>解析 "1,2,3" / "(1, 2, 3)" 形式的向量。</summary>
        internal static bool TryVec3(string text, out Vector3 v)
        {
            v = Vector3.zero;
            if (string.IsNullOrEmpty(text)) return false;
            var parts = text.Trim().Trim('(', ')', '[', ']')
                .Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 3) return false;
            float x, y, z;
            if (!float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out x)) return false;
            if (!float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out y)) return false;
            if (!float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out z)) return false;
            v = new Vector3(x, y, z);
            return true;
        }

        internal static bool TryQuat(string text, out Quaternion q)
        {
            q = Quaternion.identity;
            if (string.IsNullOrEmpty(text)) return false;
            var parts = text.Trim().Trim('(', ')', '[', ']')
                .Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 4) return false;
            float x, y, z, w;
            if (!float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out x)) return false;
            if (!float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out y)) return false;
            if (!float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out z)) return false;
            if (!float.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out w)) return false;
            q = new Quaternion(x, y, z, w);
            return true;
        }

        /// <summary>layerMask 参数：接受 "Default"、"Default,Water" 或整型位掩码。</summary>
        internal static int LayerMask(Dictionary<string, object> a, int fallback)
        {
            string raw = Str(a, "layerMask");
            if (string.IsNullOrEmpty(raw)) return fallback;

            int mask;
            if (int.TryParse(raw, out mask)) return mask;

            int result = 0;
            foreach (var name in raw.Split(new[] { ',', '|' }, StringSplitOptions.RemoveEmptyEntries))
            {
                int layer = UnityEngine.LayerMask.NameToLayer(name.Trim());
                if (layer >= 0) result |= 1 << layer;
            }
            return result == 0 ? fallback : result;
        }

        internal static QueryTriggerInteraction Triggers(Dictionary<string, object> a)
        {
            string raw = Str(a, "queryTriggers");
            if (string.Equals(raw, "true", StringComparison.OrdinalIgnoreCase) || raw == "1")
                return QueryTriggerInteraction.Collide;
            object v;
            if (a != null && a.TryGetValue("queryTriggers", out v) && v is bool && (bool)v)
                return QueryTriggerInteraction.Collide;
            return QueryTriggerInteraction.Ignore;
        }
    }

    internal static class XyGo
    {
        /// <summary>层级路径：Player/Body/Head</summary>
        internal static string Path(Transform t)
        {
            if (t == null) return "";
            var stack = new List<string>(8);
            for (var cur = t; cur != null; cur = cur.parent) stack.Add(cur.name);
            stack.Reverse();
            return string.Join("/", stack);
        }

        internal static string Describe(Collider c)
        {
            if (c == null) return "{\"collider\":null}";
            var go = c.gameObject;
            var rb = c.attachedRigidbody;
            return "{\"gameObject\":" + XyJ.Q(go.name) +
                   ",\"path\":" + XyJ.Q(Path(go.transform)) +
                   ",\"collider\":" + XyJ.Q(c.GetType().Name) +
                   ",\"layer\":" + XyJ.Q(UnityEngine.LayerMask.LayerToName(go.layer)) +
                   ",\"tag\":" + XyJ.Q(SafeTag(go)) +
                   ",\"instanceId\":" + go.GetInstanceID() +
                   ",\"isTrigger\":" + XyJ.B(c.isTrigger) +
                   ",\"rigidbody\":" + XyJ.Q(rb == null ? "" : rb.GetType().Name) + "}";
        }

        static string SafeTag(GameObject go)
        {
            try { return go.tag; } catch { return ""; }
        }
    }
}
