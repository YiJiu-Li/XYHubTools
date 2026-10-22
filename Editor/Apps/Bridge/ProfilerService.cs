// ═══════════════════════════════════════════════════════════════
//  ProfilerService — 性能采样
//
//  消息：type = "profiler"，message = 扁平 JSON：
//      {"op":"start"}  → {"op":"stats"} → {"op":"stop"}
//
//  关键事实（Unity 2022.3 实测）：
//    · ProfilerDriver.enabled 只管“显示”，真正采编辑器帧要 profileEditor = true
//    · lastFrameIndex == -1 表示还没有任何帧，需要等编辑器跑几帧
//    · 计数器按区域读写：GetFormattedCounterValue(frame, area, "Batches")
//    · GetOverviewText(area, frame) 就是 Profiler Overview 面板那段文字
// ═══════════════════════════════════════════════════════════════

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.Profiling;

namespace Framework.XYEditor.Bridge
{
    public static class ProfilerService
    {
        // 区域 → 计数器名（名字取自 Profiler Overview 面板，缺失的会被跳过）
        static readonly Dictionary<ProfilerArea, string[]> Counters = new Dictionary<ProfilerArea, string[]>
        {
            { ProfilerArea.CPU, new[] { "CPU Total Frame Time" } },
            { ProfilerArea.Rendering, new[] { "Batches", "SetPass Calls", "Triangles", "Vertices", "Shadow Casters" } },
            { ProfilerArea.Memory, new[]
                {
                    "Total Used Memory", "Total Reserved Memory", "Texture Memory", "Mesh Memory",
                    "Material Count", "Object Count", "GC Used Memory", "GC Allocated In Frame",
                }
            },
        };

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
                    case "": case "status": return Status();
                    case "start": return Start(a);
                    case "stop": return Stop();
                    case "stats": return Stats(a);
                    case "overview": return Overview(a);
                    case "save": return Save(a);
                    default:
                        return XyJ.Fail("未知 op: " + op + "（可用: status/start/stop/stats/overview/save）");
                }
            }
            catch (Exception ex)
            {
                return XyJ.Fail(op + " 失败: " + ex.Message);
            }
        }

        // ───────────── status ─────────────

        static string Status()
        {
            int last = ProfilerDriver.lastFrameIndex;
            int first = ProfilerDriver.firstFrameIndex;
            var sb = new StringBuilder(512);
            sb.Append("{\"ok\":true")
              .Append(",\"enabled\":").Append(XyJ.B(ProfilerDriver.enabled))
              .Append(",\"profileEditor\":").Append(XyJ.B(ProfilerDriver.profileEditor))
              .Append(",\"deepProfiling\":").Append(XyJ.B(ProfilerDriver.deepProfiling))
              .Append(",\"profileGPU\":").Append(XyJ.B(ProfilerDriver.profileGPU))
              .Append(",\"firstFrame\":").Append(first)
              .Append(",\"lastFrame\":").Append(last)
              .Append(",\"frames\":").Append(last >= 0 && first >= 0 ? (last - first + 1) : 0)
              .Append(",\"usedHeapBytes\":").Append(ProfilerDriver.usedHeapSize)
              .Append(",\"objectCount\":").Append(ProfilerDriver.objectCount)
              .Append(",\"memory\":").Append(MemoryJson())
              .Append('}');
            return sb.ToString();
        }

        static string MemoryJson()
        {
            return "{\"totalAllocated\":" + Profiler.GetTotalAllocatedMemoryLong() +
                   ",\"totalReserved\":" + Profiler.GetTotalReservedMemoryLong() +
                   ",\"monoUsed\":" + Profiler.GetMonoUsedSizeLong() +
                   ",\"monoHeap\":" + Profiler.GetMonoHeapSizeLong() +
                   ",\"gfxDriver\":" + Profiler.GetAllocatedMemoryForGraphicsDriver() +
                   ",\"tempAllocator\":" + Profiler.GetTempAllocatorSize() + "}";
        }

        // ───────────── start / stop ─────────────

        static string Start(Dictionary<string, object> a)
        {
            // 关键：不打开 profileEditor 就永远拿不到编辑器帧
            ProfilerDriver.profileEditor = true;
            if (XyJ.Bool(a, "deep", false)) ProfilerDriver.deepProfiling = true;
            if (XyJ.Bool(a, "gpu", false)) ProfilerDriver.profileGPU = true;
            if (XyJ.Bool(a, "reset", false)) ProfilerDriver.ResetHistory();
            ProfilerDriver.enabled = true;

            var sb = new StringBuilder(320);
            sb.Append("{\"ok\":true,\"enabled\":true")
              .Append(",\"lastFrame\":").Append(ProfilerDriver.lastFrameIndex)
              .Append(",\"note\":").Append(XyJ.Q("采样已开启；编辑器需要渲染几帧后 op=stats 才有数据"))
              .Append('}');
            return sb.ToString();
        }

        static string Stop()
        {
            ProfilerDriver.enabled = false;
            return "{\"ok\":true,\"enabled\":false,\"lastFrame\":" + ProfilerDriver.lastFrameIndex + "}";
        }

        // ───────────── stats ─────────────

        static string Stats(Dictionary<string, object> a)
        {
            int last = ProfilerDriver.lastFrameIndex;
            if (last < 0)
            {
                return "{\"ok\":true,\"pending\":true,\"enabled\":" + XyJ.B(ProfilerDriver.enabled) +
                       ",\"hint\":" + XyJ.Q("还没有采到帧：先 op=start，等编辑器渲染几帧再试") + "}";
            }

            int first = ProfilerDriver.firstFrameIndex;
            int span = Mathf.Clamp(XyJ.Int(a, "frames", 1), 1, 600);
            int from = Mathf.Max(first, last - span + 1);

            var sb = new StringBuilder(768);
            sb.Append("{\"ok\":true,\"pending\":false")
              .Append(",\"frame\":").Append(last)
              .Append(",\"framesFrom\":").Append(from)
              .Append(",\"frameCount\":").Append(last - first + 1);

            // 计数器（字符串，含单位）
            sb.Append(",\"counters\":{");
            bool firstEntry = true;
            foreach (var kv in Counters)
            {
                foreach (var name in kv.Value)
                {
                    string v = SafeCounter(last, kv.Key, name);
                    if (string.IsNullOrEmpty(v)) continue;
                    if (!firstEntry) sb.Append(',');
                    firstEntry = false;
                    sb.Append(XyJ.Q(kv.Key + "/" + name)).Append(':').Append(XyJ.Q(v));
                }
            }
            sb.Append('}');

            sb.Append(",\"memory\":").Append(MemoryJson());
            sb.Append(",\"camera\":").Append(CameraJson());
            sb.Append('}');
            return sb.ToString();
        }

        static string SafeCounter(int frame, ProfilerArea area, string name)
        {
            try { return ProfilerDriver.GetFormattedCounterValue(frame, area, name); }
            catch { return null; }
        }

        static string CameraJson()
        {
            var cam = Camera.main;
            if (cam == null) return "null";
            return "{\"name\":" + XyJ.Q(cam.name) +
                   ",\"path\":" + XyJ.Q(XyGo.Path(cam.transform)) +
                   ",\"pixelSize\":\"" + cam.pixelWidth + "x" + cam.pixelHeight + "\"" +
                   ",\"fov\":" + XyJ.F(cam.fieldOfView) +
                   ",\"farClip\":" + XyJ.F(cam.farClipPlane) +
                   ",\"cullingMask\":" + cam.cullingMask + "}";
        }

        // ───────────── overview ─────────────

        static string Overview(Dictionary<string, object> a)
        {
            int last = ProfilerDriver.lastFrameIndex;
            if (last < 0) return XyJ.Fail("还没有采到帧：先 op=start 并等待几帧");

            string areaName = XyJ.Str(a, "area");
            if (string.IsNullOrEmpty(areaName)) areaName = "CPU";

            ProfilerArea area;
            try { area = (ProfilerArea)Enum.Parse(typeof(ProfilerArea), areaName, true); }
            catch { return XyJ.Fail("未知 area: " + areaName + "（如 CPU/Rendering/Memory/GPU）"); }

            string text = ProfilerDriver.GetOverviewText(area, last) ?? "";
            return "{\"ok\":true,\"area\":" + XyJ.Q(area.ToString()) +
                   ",\"frame\":" + last + ",\"text\":" + XyJ.Q(text) + "}";
        }

        // ───────────── save ─────────────

        static string Save(Dictionary<string, object> a)
        {
            string path = XyJ.Str(a, "path");
            if (string.IsNullOrEmpty(path)) return XyJ.Fail("缺少 path（.raw 文件保存路径）");
            if (!path.EndsWith(".raw", StringComparison.OrdinalIgnoreCase)) path += ".raw";

            string root = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string full = Path.IsPathRooted(path) ? path : Path.Combine(root, path);
            full = Path.GetFullPath(full);

            string dir = Path.GetDirectoryName(full);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);

            if (ProfilerDriver.lastFrameIndex < 0)
                return XyJ.Fail("还没有采到帧，无法保存");

            ProfilerDriver.SaveProfile(full);
            if (!File.Exists(full)) return XyJ.Fail("保存失败，未生成文件: " + full);

            return "{\"ok\":true,\"path\":" + XyJ.Q(full) +
                   ",\"sizeBytes\":" + new FileInfo(full).Length + "}";
        }
    }
}
