// ═══════════════════════════════════════════════════════════════
//  FrameDebuggerService — 帧调试摘要
//
//  消息：type = "frame_debugger"，message = 扁平 JSON：
//      {"op":"summary"} / {"op":"events","limit":50} / {"op":"details","index":3}
//
//  关键事实（Unity 2022.3 实测）：
//    · FrameDebuggerUtility 是 internal，只能反射
//    · 必须先打开 Frame Debugger 窗口，否则 SetEnabled(true) 之后
//      GetFrameEvents() 一直是空数组（不渲染就没有帧可捕获）
//    · 开启后编辑器会停在那一帧，读完必须 SetEnabled(false)，否则画面冻住
// ═══════════════════════════════════════════════════════════════

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Framework.XYEditor.Bridge
{
    internal static class FrameDebuggerApi
    {
        const BindingFlags B = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;

        static bool _probed;
        static bool _available;
        static string _reason = "";

        internal static Type Utility, DataType, EventTypeType;
        internal static MethodInfo SetEnabled, GetEvents, GetName, GetData, GetCauses, IsLocalEnabled, ToggleWindow;
        internal static FieldInfo FType, FObject;
        internal static Dictionary<string, FieldInfo> DataFields = new Dictionary<string, FieldInfo>();

        internal static bool Available { get { Probe(); return _available; } }
        internal static string Reason { get { Probe(); return _reason; } }

        internal static void Probe()
        {
            if (_probed) return;
            _probed = true;
            try
            {
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    Utility = Utility ?? asm.GetType("UnityEditorInternal.FrameDebuggerInternal.FrameDebuggerUtility");
                    DataType = DataType ?? asm.GetType("UnityEditorInternal.FrameDebuggerInternal.FrameDebuggerEventData");
                    EventTypeType = EventTypeType ?? asm.GetType("UnityEditorInternal.FrameDebuggerInternal.FrameEventType");
                }
                if (Utility == null || DataType == null)
                {
                    _reason = "未找到 FrameDebuggerUtility（该 Unity 版本不支持）";
                    return;
                }

                SetEnabled = Utility.GetMethod("SetEnabled", B);
                GetEvents = Utility.GetMethod("GetFrameEvents", B);
                GetName = Utility.GetMethod("GetFrameEventInfoName", B);
                GetData = Utility.GetMethod("GetFrameEventData", B);
                GetCauses = Utility.GetMethod("GetBatchBreakCauseStrings", B);

                // UnityEngine.FrameDebugger.IsLocalEnabled 在运行时程序集里，且不一定是 public
                var runtimeType = Type.GetType("UnityEngine.FrameDebugger, UnityEngine.CoreModule");
                if (runtimeType != null)
                    IsLocalEnabled = runtimeType.GetMethod("IsLocalEnabled",
                        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);

                // 真正可靠的开/关入口是窗口自己的静态方法：它会处理等待计数、
                // 切换捕获状态并触发重绘；直接调 FrameDebuggerUtility.SetEnabled
                // 会被窗口下一帧的 UI 同步覆盖掉（实测 events 一直是 0）。
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    var winType = asm.GetType("UnityEditor.FrameDebuggerWindow");
                    if (winType == null) continue;
                    ToggleWindow = winType.GetMethod("OpenWindowAndToggleEnabled",
                        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                    if (ToggleWindow != null) break;
                }

                var evType = Utility.GetMethod("GetFrameEvents", B).ReturnType.GetElementType();
                FType = evType.GetField("m_Type", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                FObject = evType.GetField("m_Obj", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

                foreach (var f in DataType.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
                    DataFields[f.Name] = f;

                _available = SetEnabled != null && GetEvents != null && GetName != null && GetData != null;
                if (!_available) _reason = "FrameDebuggerUtility 成员缺失";
            }
            catch (Exception ex)
            {
                _available = false;
                _reason = ex.Message;
            }
        }

        internal static object ReadData(int index)
        {
            var data = Activator.CreateInstance(DataType, true);
            GetData.Invoke(null, new object[] { index, data });
            return data;
        }

        internal static T Get<T>(object data, string field, T fallback)
        {
            FieldInfo f;
            if (data == null || !DataFields.TryGetValue(field, out f)) return fallback;
            try
            {
                object v = f.GetValue(data);
                return v == null ? fallback : (T)v;
            }
            catch { return fallback; }
        }

        internal static string[] Causes()
        {
            try { return (string[])GetCauses.Invoke(null, null); }
            catch { return Array.Empty<string>(); }
        }
    }

    public static class FrameDebuggerService
    {
        public static string Execute(string json)
        {
            Dictionary<string, object> a;
            try { a = FlatJson.Parse(json ?? "{}"); }
            catch (Exception ex) { return XyJ.Fail("参数不是合法 JSON: " + ex.Message); }

            if (!FrameDebuggerApi.Available) return XyJ.Fail("Frame Debugger 不可用: " + FrameDebuggerApi.Reason);

            string op = XyJ.Str(a, "op").Trim().ToLowerInvariant();
            try
            {
                switch (op)
                {
                    case "": case "status": return Status();
                    case "enable": return Enable(true);
                    case "disable": return Enable(false);
                    case "events": return Events(a);
                    case "details": return Details(a);
                    case "summary": return Summary(a);
                    default:
                        return XyJ.Fail("未知 op: " + op + "（可用: status/enable/disable/events/details/summary）");
                }
            }
            catch (Exception ex)
            {
                return XyJ.Fail(op + " 失败: " + ex.Message);
            }
        }

        // ───────────── 开关 ─────────────

        static bool IsEnabled()
        {
            try
            {
                if (FrameDebuggerApi.IsLocalEnabled != null)
                    return (bool)FrameDebuggerApi.IsLocalEnabled.Invoke(null, null);
            }
            catch { }
            return false;
        }

        static bool WindowOpen()
        {
            foreach (var w in Resources.FindObjectsOfTypeAll<EditorWindow>())
                if (w != null && w.GetType().Name == "FrameDebuggerWindow") return true;
            return false;
        }

        static void EnsureWindow()
        {
            if (WindowOpen()) return;
            // 不打开窗口就不会渲染捕获，GetFrameEvents 恒为空
            EditorApplication.ExecuteMenuItem("Window/Analysis/Frame Debugger");
        }

        static string Enable(bool on)
        {
            EnsureWindow();
            bool cur = IsEnabled();

            // Toggle 是“翻转”，所以只在状态不一致时调用；已经开着又要刷新时连翻两次
            if (FrameDebuggerApi.ToggleWindow != null)
            {
                if (cur != on) FrameDebuggerApi.ToggleWindow.Invoke(null, null);
                else if (on)
                {
                    FrameDebuggerApi.ToggleWindow.Invoke(null, null);
                    FrameDebuggerApi.ToggleWindow.Invoke(null, null);
                }
            }
            else
            {
                FrameDebuggerApi.SetEnabled.Invoke(null, new object[] { on, 0 });
            }

            return "{\"ok\":true,\"enabled\":" + XyJ.B(IsEnabled()) +
                   ",\"windowOpen\":" + XyJ.B(WindowOpen()) +
                   ",\"eventCount\":" + EventCount() + "}";
        }

        static int EventCount()
        {
            try
            {
                var ev = FrameDebuggerApi.GetEvents.Invoke(null, null) as Array;
                return ev == null ? 0 : ev.Length;
            }
            catch { return 0; }
        }

        static string Status()
        {
            return "{\"ok\":true,\"enabled\":" + XyJ.B(IsEnabled()) +
                   ",\"windowOpen\":" + XyJ.B(WindowOpen()) +
                   ",\"eventCount\":" + EventCount() + "}";
        }

        // ───────────── events ─────────────

        static string Events(Dictionary<string, object> a)
        {
            var ev = FrameDebuggerApi.GetEvents.Invoke(null, null) as Array;
            int total = ev == null ? 0 : ev.Length;
            int offset = Mathf.Max(0, XyJ.Int(a, "offset", 0));
            int limit = Mathf.Clamp(XyJ.Int(a, "limit", 100), 1, 1000);

            var sb = new StringBuilder(1024);
            sb.Append("{\"ok\":true,\"total\":").Append(total)
              .Append(",\"offset\":").Append(offset)
              .Append(",\"events\":[");

            int emitted = 0;
            for (int i = offset; i < total && emitted < limit; i++)
            {
                var item = ev.GetValue(i);
                object type = FrameDebuggerApi.FType == null ? null : FrameDebuggerApi.FType.GetValue(item);
                var obj = FrameDebuggerApi.FObject == null ? null : FrameDebuggerApi.FObject.GetValue(item) as UnityEngine.Object;
                var data = SafeData(i);

                if (emitted > 0) sb.Append(',');
                sb.Append("{\"index\":").Append(i)
                  .Append(",\"type\":").Append(XyJ.Q(type == null ? "" : type.ToString()))
                  .Append(",\"name\":").Append(XyJ.Q(SafeName(i)))
                  .Append(",\"object\":").Append(XyJ.Q(obj == null ? "" : obj.name))
                  .Append(",\"drawCalls\":").Append(FrameDebuggerApi.Get<int>(data, "m_DrawCallCount", 0))
                  .Append(",\"vertices\":").Append(FrameDebuggerApi.Get<int>(data, "m_VertexCount", 0))
                  .Append(",\"indices\":").Append(FrameDebuggerApi.Get<int>(data, "m_IndexCount", 0))
                  .Append(",\"shader\":").Append(XyJ.Q(FrameDebuggerApi.Get<string>(data, "m_RealShaderName", "")))
                  .Append(",\"pass\":").Append(XyJ.Q(FrameDebuggerApi.Get<string>(data, "m_PassName", "")))
                  .Append(",\"renderTarget\":").Append(XyJ.Q(FrameDebuggerApi.Get<string>(data, "m_RenderTargetName", "")))
                  .Append('}');
                emitted++;
            }
            sb.Append("],\"returned\":").Append(emitted).Append('}');
            return sb.ToString();
        }

        static string SafeName(int index)
        {
            try { return FrameDebuggerApi.GetName.Invoke(null, new object[] { index }) as string ?? ""; }
            catch { return ""; }
        }

        static object SafeData(int index)
        {
            try { return FrameDebuggerApi.ReadData(index); }
            catch { return null; }
        }

        // ───────────── details ─────────────

        static string Details(Dictionary<string, object> a)
        {
            int index = XyJ.Int(a, "index", -1);
            int total = EventCount();
            if (index < 0 || index >= total) return XyJ.Fail("index 越界（0.." + (total - 1) + "）");

            var data = SafeData(index);
            if (data == null) return XyJ.Fail("读取事件详情失败");

            var sb = new StringBuilder(768);
            sb.Append("{\"ok\":true,\"index\":").Append(index)
              .Append(",\"name\":").Append(XyJ.Q(SafeName(index)))
              .Append(",\"drawCalls\":").Append(FrameDebuggerApi.Get<int>(data, "m_DrawCallCount", 0))
              .Append(",\"vertices\":").Append(FrameDebuggerApi.Get<int>(data, "m_VertexCount", 0))
              .Append(",\"indices\":").Append(FrameDebuggerApi.Get<int>(data, "m_IndexCount", 0))
              .Append(",\"instances\":").Append(FrameDebuggerApi.Get<int>(data, "m_InstanceCount", 0))
              .Append(",\"shader\":").Append(XyJ.Q(FrameDebuggerApi.Get<string>(data, "m_RealShaderName", "")))
              .Append(",\"originalShader\":").Append(XyJ.Q(FrameDebuggerApi.Get<string>(data, "m_OriginalShaderName", "")))
              .Append(",\"pass\":").Append(XyJ.Q(FrameDebuggerApi.Get<string>(data, "m_PassName", "")))
              .Append(",\"lightMode\":").Append(XyJ.Q(FrameDebuggerApi.Get<string>(data, "m_PassLightMode", "")))
              .Append(",\"renderTarget\":").Append(XyJ.Q(FrameDebuggerApi.Get<string>(data, "m_RenderTargetName", "")))
              .Append(",\"renderTargetSize\":").Append(XyJ.Q(
                    FrameDebuggerApi.Get<int>(data, "m_RenderTargetWidth", 0) + "x" +
                    FrameDebuggerApi.Get<int>(data, "m_RenderTargetHeight", 0)))
              .Append(",\"keywords\":").Append(XyJ.Q(FrameDebuggerApi.Get<string>(data, "shaderKeywords", "")))
              .Append(",\"mesh\":").Append(XyJ.Q(MeshName(data)))
              .Append('}');
            return sb.ToString();
        }

        static string MeshName(object data)
        {
            try
            {
                var mesh = FrameDebuggerApi.Get<Mesh>(data, "m_Mesh", null);
                return mesh == null ? "" : mesh.name;
            }
            catch { return ""; }
        }

        // ───────────── summary ─────────────

        static string Summary(Dictionary<string, object> a)
        {
            int total = EventCount();
            if (total == 0)
            {
                return "{\"ok\":true,\"pending\":true,\"enabled\":" + XyJ.B(IsEnabled()) +
                       ",\"windowOpen\":" + XyJ.B(WindowOpen()) +
                       ",\"hint\":" + XyJ.Q("还没有捕获到帧：需要 Game/Scene 视图在渲染。op=enable 后稍等再试") + "}";
            }

            int drawEvents = 0, drawCalls = 0;
            long vertices = 0, indices = 0, instances = 0;
            var shaderCount = new Dictionary<string, int>();
            var shaderVerts = new Dictionary<string, long>();
            var passCount = new Dictionary<string, int>();
            var rtCount = new Dictionary<string, int>();
            var causeCount = new Dictionary<int, int>();

            var causes = FrameDebuggerApi.Causes();

            for (int i = 0; i < total; i++)
            {
                var data = SafeData(i);
                if (data == null) continue;

                int dc = FrameDebuggerApi.Get<int>(data, "m_DrawCallCount", 0);
                int v = FrameDebuggerApi.Get<int>(data, "m_VertexCount", 0);
                int idx = FrameDebuggerApi.Get<int>(data, "m_IndexCount", 0);
                int inst = FrameDebuggerApi.Get<int>(data, "m_InstanceCount", 0);

                if (dc > 0)
                {
                    drawEvents++;
                    drawCalls += dc;
                    vertices += v;
                    indices += idx;
                    instances += inst;

                    string sh = FrameDebuggerApi.Get<string>(data, "m_RealShaderName", "(unknown)");
                    string pass = FrameDebuggerApi.Get<string>(data, "m_PassName", "");
                    string rt = FrameDebuggerApi.Get<string>(data, "m_RenderTargetName", "");

                    Bump(shaderCount, sh);
                    Add(shaderVerts, sh, v);
                    if (!string.IsNullOrEmpty(pass)) Bump(passCount, sh + "/" + pass);
                    if (!string.IsNullOrEmpty(rt)) Bump(rtCount, rt);

                    int cause = FrameDebuggerApi.Get<int>(data, "m_BatchBreakCause", 0);
                    if (cause > 0) BumpInt(causeCount, cause);
                }
            }

            var sb = new StringBuilder(1600);
            sb.Append("{\"ok\":true,\"pending\":false")
              .Append(",\"totalEvents\":").Append(total)
              .Append(",\"drawEvents\":").Append(drawEvents)
              .Append(",\"drawCalls\":").Append(drawCalls)
              .Append(",\"vertices\":").Append(vertices)
              .Append(",\"indices\":").Append(indices)
              .Append(",\"instances\":").Append(instances)
              .Append(",\"uniqueShaders\":").Append(shaderCount.Count)
              .Append(",\"uniqueRenderTargets\":").Append(rtCount.Count);

            sb.Append(",\"topShaders\":");
            TopBy(shaderCount, 12, sb, shaderVerts);

            sb.Append(",\"topPasses\":");
            TopBy(passCount, 10, sb, null);

            sb.Append(",\"renderTargets\":");
            TopBy(rtCount, 8, sb, null);

            sb.Append(",\"batchBreakCauses\":[");
            int ci = 0;
            foreach (var kv in causeCount.OrderByDescending(x => x.Value))
            {
                if (ci++ >= 8) break;
                if (ci > 1) sb.Append(',');
                string reason = kv.Key < causes.Length ? causes[kv.Key] : "unknown";
                sb.Append("{\"reason\":").Append(XyJ.Q(reason)).Append(",\"count\":").Append(kv.Value).Append('}');
            }
            sb.Append(']');
            sb.Append('}');
            return sb.ToString();
        }

        static void Bump(Dictionary<string, int> d, string key)
        {
            int v;
            d.TryGetValue(key, out v);
            d[key] = v + 1;
        }

        static void BumpInt(Dictionary<int, int> d, int key)
        {
            int v;
            d.TryGetValue(key, out v);
            d[key] = v + 1;
        }

        static void Add(Dictionary<string, long> d, string key, long value)
        {
            long v;
            d.TryGetValue(key, out v);
            d[key] = v + value;
        }

        static void TopBy(Dictionary<string, int> counts, int limit, StringBuilder sb, Dictionary<string, long> verts)
        {
            sb.Append('[');
            int n = 0;
            foreach (var kv in counts.OrderByDescending(x => x.Value))
            {
                if (n++ >= limit) break;
                if (n > 1) sb.Append(',');
                sb.Append("{\"name\":").Append(XyJ.Q(kv.Key)).Append(",\"count\":").Append(kv.Value);
                if (verts != null)
                {
                    long v;
                    verts.TryGetValue(kv.Key, out v);
                    sb.Append(",\"vertices\":").Append(v);
                }
                sb.Append('}');
            }
            sb.Append(']');
        }
    }
}
