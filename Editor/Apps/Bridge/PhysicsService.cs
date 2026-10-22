// ═══════════════════════════════════════════════════════════════
//  PhysicsService — 物理查询（射线 / 球形 / 盒形 / 重叠）
//
//  消息：type = "physics"，message = 扁平 JSON：
//      {"op":"raycast","origin":"0,1,0","direction":"0,-1,0","maxDistance":100}
//  返回：{"ok":true,"hit":true,"gameObject":"...","path":"...","point":"..", ...}
//
//  纯读操作，不改场景，不需要 Undo。
// ═══════════════════════════════════════════════════════════════

using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace Framework.XYEditor.Bridge
{
    public static class PhysicsService
    {
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
                    case "": case "raycast": return Raycast(a, false);
                    case "raycast_all": return Raycast(a, true);
                    case "spherecast": return SphereCast(a, false);
                    case "spherecast_all": return SphereCast(a, true);
                    case "boxcast": return BoxCast(a);
                    case "linecast": return LineCast(a);
                    case "overlap_sphere": return OverlapSphere(a);
                    case "overlap_box": return OverlapBox(a);
                    default:
                        return XyJ.Fail("未知 op: " + op +
                            "（可用: raycast/raycast_all/spherecast/spherecast_all/boxcast/linecast/overlap_sphere/overlap_box）");
                }
            }
            catch (Exception ex)
            {
                return XyJ.Fail(op + " 失败: " + ex.Message);
            }
        }

        // ───────────── 射线 ─────────────

        static string Raycast(Dictionary<string, object> a, bool all)
        {
            Vector3 origin, dir;
            if (!OriginDir(a, out origin, out dir)) return XyJ.Fail("需要 origin 与 direction（格式 \"x,y,z\"）");

            float maxDist = XyJ.Float(a, "maxDistance", 1000f);
            int mask = XyJ.LayerMask(a, Physics.DefaultRaycastLayers);
            var qti = XyJ.Triggers(a);

            if (!all)
            {
                RaycastHit hit;
                bool ok = Physics.Raycast(origin, dir, out hit, maxDist, mask, qti);
                return HitResult("raycast", ok, hit);
            }

            int max = Mathf.Clamp(XyJ.Int(a, "maxResults", 20), 1, 200);
            var hits = Physics.RaycastAll(origin, dir, maxDist, mask, qti);
            Array.Sort(hits, (x, y) => x.distance.CompareTo(y.distance));
            return HitList("raycast_all", hits, max);
        }

        static string SphereCast(Dictionary<string, object> a, bool all)
        {
            Vector3 origin, dir;
            if (!OriginDir(a, out origin, out dir)) return XyJ.Fail("需要 origin 与 direction");
            float radius = XyJ.Float(a, "radius", 0.5f);
            if (radius <= 0f) return XyJ.Fail("radius 必须 > 0");

            float maxDist = XyJ.Float(a, "maxDistance", 1000f);
            int mask = XyJ.LayerMask(a, Physics.DefaultRaycastLayers);
            var qti = XyJ.Triggers(a);

            if (!all)
            {
                RaycastHit hit;
                bool ok = Physics.SphereCast(origin, radius, dir, out hit, maxDist, mask, qti);
                return HitResult("spherecast", ok, hit);
            }

            int max = Mathf.Clamp(XyJ.Int(a, "maxResults", 20), 1, 200);
            var hits = Physics.SphereCastAll(origin, radius, dir, maxDist, mask, qti);
            Array.Sort(hits, (x, y) => x.distance.CompareTo(y.distance));
            return HitList("spherecast_all", hits, max);
        }

        static string BoxCast(Dictionary<string, object> a)
        {
            Vector3 origin, dir, half;
            if (!OriginDir(a, out origin, out dir)) return XyJ.Fail("需要 origin 与 direction");
            if (!XyJ.TryVec3(XyJ.Str(a, "halfExtents"), out half))
                return XyJ.Fail("需要 halfExtents（格式 \"0.5,0.5,0.5\"，是半尺寸）");

            Quaternion rot;
            if (!XyJ.TryQuat(XyJ.Str(a, "rotation"), out rot)) rot = Quaternion.identity;

            RaycastHit hit;
            bool ok = Physics.BoxCast(origin, half, dir, out hit, rot,
                XyJ.Float(a, "maxDistance", 1000f),
                XyJ.LayerMask(a, Physics.DefaultRaycastLayers),
                XyJ.Triggers(a));
            return HitResult("boxcast", ok, hit);
        }

        static string LineCast(Dictionary<string, object> a)
        {
            Vector3 from, to;
            if (!XyJ.TryVec3(XyJ.Str(a, "from"), out from)) return XyJ.Fail("需要 from（格式 \"x,y,z\"）");
            if (!XyJ.TryVec3(XyJ.Str(a, "to"), out to)) return XyJ.Fail("需要 to（格式 \"x,y,z\"）");

            RaycastHit hit;
            bool ok = Physics.Linecast(from, to, out hit,
                XyJ.LayerMask(a, Physics.DefaultRaycastLayers), XyJ.Triggers(a));
            return HitResult("linecast", ok, hit);
        }

        // ───────────── 重叠 ─────────────

        static string OverlapSphere(Dictionary<string, object> a)
        {
            Vector3 pos;
            if (!XyJ.TryVec3(XyJ.Str(a, "position"), out pos)) return XyJ.Fail("需要 position（格式 \"x,y,z\"）");
            float radius = XyJ.Float(a, "radius", 1f);
            if (radius <= 0f) return XyJ.Fail("radius 必须 > 0");

            var cols = Physics.OverlapSphere(pos, radius,
                XyJ.LayerMask(a, Physics.AllLayers), XyJ.Triggers(a));
            return ColliderList("overlap_sphere", pos, cols, a);
        }

        static string OverlapBox(Dictionary<string, object> a)
        {
            Vector3 pos, half;
            if (!XyJ.TryVec3(XyJ.Str(a, "position"), out pos)) return XyJ.Fail("需要 position");
            if (!XyJ.TryVec3(XyJ.Str(a, "halfExtents"), out half)) return XyJ.Fail("需要 halfExtents（半尺寸）");

            Quaternion rot;
            if (!XyJ.TryQuat(XyJ.Str(a, "rotation"), out rot)) rot = Quaternion.identity;

            var cols = Physics.OverlapBox(pos, half, rot,
                XyJ.LayerMask(a, Physics.AllLayers), XyJ.Triggers(a));
            return ColliderList("overlap_box", pos, cols, a);
        }

        // ───────────── 输出 ─────────────

        static bool OriginDir(Dictionary<string, object> a, out Vector3 origin, out Vector3 dir)
        {
            dir = Vector3.forward;
            bool hasOrigin = XyJ.TryVec3(XyJ.Str(a, "origin"), out origin);
            bool hasDir = XyJ.TryVec3(XyJ.Str(a, "direction"), out dir);
            if (!hasDir) dir = Vector3.forward;
            else dir = dir.normalized;
            return hasOrigin && hasDir;
        }

        static string HitResult(string op, bool hit, RaycastHit h)
        {
            var sb = new StringBuilder(256);
            sb.Append("{\"ok\":true,\"op\":").Append(XyJ.Q(op))
              .Append(",\"hit\":").Append(XyJ.B(hit));
            if (!hit) { sb.Append('}'); return sb.ToString(); }

            sb.Append(",\"distance\":").Append(XyJ.F(h.distance));
            sb.Append(",\"point\":").Append(XyJ.Q(Fmt(h.point)));
            sb.Append(",\"normal\":").Append(XyJ.Q(Fmt(h.normal)));
            sb.Append(",\"triangleIndex\":").Append(h.triangleIndex);
            sb.Append(",\"object\":").Append(XyGo.Describe(h.collider));
            sb.Append('}');
            return sb.ToString();
        }

        static string HitList(string op, RaycastHit[] hits, int max)
        {
            var sb = new StringBuilder(512);
            sb.Append("{\"ok\":true,\"op\":").Append(XyJ.Q(op))
              .Append(",\"count\":").Append(Mathf.Min(hits.Length, max))
              .Append(",\"totalFound\":").Append(hits.Length)
              .Append(",\"hits\":[");
            for (int i = 0; i < hits.Length && i < max; i++)
            {
                if (i > 0) sb.Append(',');
                var h = hits[i];
                sb.Append("{\"distance\":").Append(XyJ.F(h.distance))
                  .Append(",\"point\":").Append(XyJ.Q(Fmt(h.point)))
                  .Append(",\"normal\":").Append(XyJ.Q(Fmt(h.normal)))
                  .Append(",\"object\":").Append(XyGo.Describe(h.collider)).Append('}');
            }
            sb.Append("]}");
            return sb.ToString();
        }

        static string ColliderList(string op, Vector3 center, Collider[] cols, Dictionary<string, object> a)
        {
            int max = Mathf.Clamp(XyJ.Int(a, "maxResults", 50), 1, 500);
            var list = new List<Collider>(cols);
            list.Sort((x, y) =>
                Vector3.SqrMagnitude(x.bounds.ClosestPoint(center) - center)
                    .CompareTo(Vector3.SqrMagnitude(y.bounds.ClosestPoint(center) - center)));

            var sb = new StringBuilder(512);
            sb.Append("{\"ok\":true,\"op\":").Append(XyJ.Q(op))
              .Append(",\"count\":").Append(Mathf.Min(list.Count, max))
              .Append(",\"totalFound\":").Append(list.Count)
              .Append(",\"colliders\":[");
            for (int i = 0; i < list.Count && i < max; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append(XyGo.Describe(list[i]));
            }
            sb.Append("]}");
            return sb.ToString();
        }

        static string Fmt(Vector3 v)
        {
            return XyJ.F(v.x) + "," + XyJ.F(v.y) + "," + XyJ.F(v.z);
        }
    }
}
