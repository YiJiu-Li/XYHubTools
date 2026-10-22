// ═══════════════════════════════════════════════════════════════
//  ShaderGraphService — Shader Graph 操作的协议入口
//
//  管道消息：type = "shadergraph"，message = 一个扁平 JSON 对象，形如
//      {"op":"add_node","path":"Assets/Shaders/A.shadergraph","nodeType":"Add","x":200,"y":0}
//
//  返回：单个 JSON 文本，恒带 "ok" 字段；失败时带 "error"。
//  所有操作都在主线程执行（MessageDispatcher 里 PostToMainThread）。
// ═══════════════════════════════════════════════════════════════

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Framework.XYEditor.Bridge
{
    public static class ShaderGraphService
    {
        const string VERSION = "1.0";

        // ═══════════════ 入口 ═══════════════
        public static string Execute(string json)
        {
            Dictionary<string, object> args;
            try { args = FlatJson.Parse(json ?? "{}"); }
            catch (Exception ex) { return Fail("参数不是合法 JSON: " + ex.Message); }

            string op = Str(args, "op").Trim().ToLowerInvariant();
            try
            {
                switch (op)
                {
                    case "": case "status": return Status();
                    case "list": return List(args);
                    case "info": return Info(args);
                    case "nodes": return Nodes(args);
                    case "edges": return Edges(args);
                    case "properties": return Properties(args);
                    case "add_property": return AddProperty(args);
                    case "set_property_default": return SetPropertyDefault(args);
                    case "create": return Create(args);
                    case "open": return Open(args);
                    case "close": return CloseWindow(args);
                    case "undo": return Undo(args);
                    case "add_node": return AddNode(args);
                    case "remove_node": return RemoveNode(args);
                    case "move_node": return MoveNode(args);
                    case "connect": return Connect(args);
                    case "disconnect": return Disconnect(args);
                    case "set_value": return SetValue(args);
                    case "node_types": return NodeTypes(args);
                    default:
                        return Fail("未知 op: " + op + "（可用: status/list/info/nodes/edges/properties/add_property/set_property_default/create/open/close/undo/add_node/remove_node/move_node/connect/disconnect/set_value/node_types）");
                }
            }
            catch (Exception ex)
            {
                return Fail(op + " 失败: " + Unwrap(ex));
            }
        }

        static string Unwrap(Exception ex)
        {
            while (ex is System.Reflection.TargetInvocationException && ex.InnerException != null)
                ex = ex.InnerException;
            return ex.Message;
        }

        // ═══════════════ status ═══════════════
        static string Status()
        {
            bool api = ShaderGraphApi.Available;
            var sb = new StringBuilder(512);
            sb.Append("{\"ok\":true,\"version\":").Append(Q(VERSION));
            sb.Append(",\"shaderGraphInstalled\":").Append(B(IsPackageInstalled()));
            sb.Append(",\"apiAvailable\":").Append(B(api));
            sb.Append(",\"urpAvailable\":").Append(B(ShaderGraphApi.UrpAvailable));
            if (!api) sb.Append(",\"reason\":").Append(Q(ShaderGraphApi.Reason));
            sb.Append(",\"templates\":[\"lit\",\"unlit\",\"sprite_lit\",\"sprite_unlit\",\"blank\"]");
            sb.Append(",\"ops\":[\"status\",\"list\",\"info\",\"nodes\",\"edges\",\"properties\",\"add_property\",\"set_property_default\",\"create\",\"open\",\"close\",\"undo\",\"add_node\",\"remove_node\",\"move_node\",\"connect\",\"disconnect\",\"set_value\",\"node_types\"]}");
            return sb.ToString();
        }

        static bool IsPackageInstalled()
        {
            foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
                if (a.GetName().Name == "Unity.ShaderGraph.Editor") return true;
            return false;
        }

        // ═══════════════ list ═══════════════
        static string List(Dictionary<string, object> args)
        {
            string filter = Str(args, "filter");
            int max = Int(args, "max", 200);
            var guids = AssetDatabase.FindAssets("t:Shader");
            var sb = new StringBuilder(1024);
            sb.Append("{\"ok\":true,\"graphs\":[");
            int count = 0;
            foreach (var g in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(g);
                if (!path.EndsWith(".shadergraph", StringComparison.OrdinalIgnoreCase)) continue;
                if (!string.IsNullOrEmpty(filter) &&
                    path.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0) continue;
                if (count >= max) break;
                if (count > 0) sb.Append(',');
                sb.Append("{\"path\":").Append(Q(path))
                  .Append(",\"name\":").Append(Q(Path.GetFileNameWithoutExtension(path))).Append('}');
                count++;
            }
            sb.Append("],\"count\":").Append(count).Append('}');
            return sb.ToString();
        }

        // ═══════════════ info ═══════════════
        static string Info(Dictionary<string, object> args)
        {
            if (!ShaderGraphApi.Available) return Fail(ShaderGraphApi.Reason);

            string assetPath = Str(args, "path");
            string full, err;
            if (!TryFullPath(assetPath, out full, out err)) return Fail(err);
            if (!File.Exists(full)) return Fail("文件不存在: " + assetPath);

            object graph;
            try { graph = ShaderGraphApi.LoadGraph(full); }
            catch (Exception ex) { return Fail("载入失败: " + Unwrap(ex)); }

            var nodes = ShaderGraphApi.GetNodes(graph);
            var edges = ShaderGraphApi.GetEdges(graph);
            var shader = AssetDatabase.LoadAssetAtPath<Shader>(assetPath);

            var sb = new StringBuilder(1024);
            sb.Append("{\"ok\":true,\"path\":").Append(Q(assetPath));
            sb.Append(",\"nodeCount\":").Append(nodes.Count);
            sb.Append(",\"edgeCount\":").Append(edges.Count);
            sb.Append(",\"shaderName\":").Append(Q(shader == null ? "" : shader.name));
            sb.Append(",\"isSupported\":").Append(B(shader != null && shader.isSupported));
            sb.Append(",\"passCount\":").Append(shader == null ? 0 : shader.passCount);

            sb.Append(",\"properties\":[");
            if (shader != null)
            {
                int pc = shader.GetPropertyCount();
                for (int i = 0; i < pc; i++)
                {
                    if (i > 0) sb.Append(',');
                    sb.Append("{\"name\":").Append(Q(shader.GetPropertyName(i)))
                      .Append(",\"type\":").Append(Q(shader.GetPropertyType(i).ToString())).Append('}');
                }
            }
            sb.Append("],\"blackboard\":[");
            var props = ShaderGraphApi.GetProperties(graph);
            for (int i = 0; i < props.Count; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append("{\"objectId\":").Append(Q(props[i].id))
                  .Append(",\"name\":").Append(Q(props[i].name))
                  .Append(",\"type\":").Append(Q(props[i].type)).Append('}');
            }
            sb.Append("]}");
            return sb.ToString();
        }

        // ═══════════════ nodes ═══════════════
        static string Nodes(Dictionary<string, object> args)
        {
            if (!ShaderGraphApi.Available) return Fail(ShaderGraphApi.Reason);
            object graph;
            string err;
            if (!LoadForRead(args, out graph, out err)) return err;

            var nodes = ShaderGraphApi.GetNodes(graph);
            bool withSlots = Bool(args, "withSlots", true);
            var sb = new StringBuilder(2048);
            sb.Append("{\"ok\":true,\"count\":").Append(nodes.Count).Append(",\"nodes\":[");
            for (int i = 0; i < nodes.Count; i++)
            {
                var n = nodes[i];
                if (i > 0) sb.Append(',');
                sb.Append("{\"id\":").Append(Q(n.id))
                  .Append(",\"type\":").Append(Q(n.type))
                  .Append(",\"name\":").Append(Q(n.name))
                  .Append(",\"x\":").Append(F(n.x))
                  .Append(",\"y\":").Append(F(n.y));
                if (withSlots)
                {
                    sb.Append(",\"slots\":[");
                    for (int s = 0; s < n.slots.Count; s++)
                    {
                        var sl = n.slots[s];
                        if (s > 0) sb.Append(',');
                        sb.Append("{\"id\":").Append(sl.id)
                          .Append(",\"name\":").Append(Q(sl.name))
                          .Append(",\"isInput\":").Append(B(sl.isInput))
                          .Append(",\"type\":").Append(Q(sl.type))
                          .Append(",\"connected\":").Append(B(sl.connected)).Append('}');
                    }
                    sb.Append(']');
                }
                sb.Append('}');
            }
            sb.Append("]}");
            return sb.ToString();
        }

        // ═══════════════ edges ═══════════════
        static string Edges(Dictionary<string, object> args)
        {
            if (!ShaderGraphApi.Available) return Fail(ShaderGraphApi.Reason);
            object graph;
            string err;
            if (!LoadForRead(args, out graph, out err)) return err;

            var edges = ShaderGraphApi.GetEdges(graph);
            var sb = new StringBuilder(1024);
            sb.Append("{\"ok\":true,\"count\":").Append(edges.Count).Append(",\"edges\":[");
            for (int i = 0; i < edges.Count; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append("{\"outputNodeId\":").Append(Q(edges[i].outNode))
                  .Append(",\"outputSlotId\":").Append(edges[i].outSlot)
                  .Append(",\"inputNodeId\":").Append(Q(edges[i].inNode))
                  .Append(",\"inputSlotId\":").Append(edges[i].inSlot).Append('}');
            }
            sb.Append("]}");
            return sb.ToString();
        }

        // ═══════════════ properties ═══════════════
        static string Properties(Dictionary<string, object> args)
        {
            if (!ShaderGraphApi.Available) return Fail(ShaderGraphApi.Reason);
            object graph;
            string err;
            if (!LoadForRead(args, out graph, out err)) return err;

            var props = ShaderGraphApi.GetProperties(graph);
            var sb = new StringBuilder(512);
            sb.Append("{\"ok\":true,\"count\":").Append(props.Count).Append(",\"properties\":[");
            for (int i = 0; i < props.Count; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append("{\"objectId\":").Append(Q(props[i].id))
                  .Append(",\"name\":").Append(Q(props[i].name))
                  .Append(",\"type\":").Append(Q(props[i].type)).Append('}');
            }
            sb.Append("]}");
            return sb.ToString();
        }

        // ═══════════════ add_property ═══════════════
        static string AddProperty(Dictionary<string, object> args)
        {
            if (!ShaderGraphApi.Available) return Fail(ShaderGraphApi.Reason);

            object graph;
            string full, err;
            if (!LoadForWrite(args, out graph, out full, out err)) return err;

            string name = Str(args, "name");
            string type = Str(args, "type");
            if (string.IsNullOrEmpty(type)) type = "float";

            string id, finalName;
            string addErr = ShaderGraphApi.AddProperty(graph, name, type, out id, out finalName);
            if (addErr != null) return Fail(addErr);

            ShaderGraphApi.SaveGraph(Str(args, "path"), graph);
            return "{\"ok\":true,\"propertyId\":" + Q(id) +
                   ",\"name\":" + Q(finalName) + ",\"type\":" + Q(type) + "}";
        }

        // ═══════════════ set_property_default ═══════════════
        static string SetPropertyDefault(Dictionary<string, object> args)
        {
            if (!ShaderGraphApi.Available) return Fail(ShaderGraphApi.Reason);

            object graph;
            string full, err;
            if (!LoadForWrite(args, out graph, out full, out err)) return err;

            string propertyId = Str(args, "propertyId");
            if (string.IsNullOrEmpty(propertyId)) return Fail("缺少 propertyId（op=properties 取）");
            string value = Str(args, "value");

            string setErr = ShaderGraphApi.SetPropertyValue(graph, propertyId, value);
            if (setErr != null) return Fail(setErr);

            ShaderGraphApi.SaveGraph(Str(args, "path"), graph);
            return "{\"ok\":true,\"propertyId\":" + Q(propertyId) + ",\"value\":" + Q(value) + "}";
        }

        // ═══════════════ 快照 / 撤销 ═══════════════
        //  Unity 的 Undo 系统不覆盖“直接写文件”，.shadergraph 就是这么写的，
        //  所以每次改动前把原文件复制到 Library/XYUndo，op=undo 时还原上一份。

        const int MaxSnapshotsPerAsset = 20;

        static string UndoDir
        {
            get
            {
                string root = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
                return Path.Combine(root, "Library", "XYUndo");
            }
        }

        static string SnapshotKey(string assetPath)
        {
            return assetPath.Replace("Assets/", "").Replace('/', '_').Replace('\\', '_').Replace(':', '_');
        }

        /// <summary>改动前留一份原文件。返回快照路径，失败返回 null（不阻断主流程）。</summary>
        static string Snapshot(string assetPath, string fullPath)
        {
            try
            {
                if (!File.Exists(fullPath)) return null;
                Directory.CreateDirectory(UndoDir);

                string key = SnapshotKey(assetPath);
                string file = Path.Combine(
                    UndoDir,
                    key + "__" + DateTime.Now.Ticks.ToString(CultureInfo.InvariantCulture) + ".snap");
                File.Copy(fullPath, file, true);

                // 同一个图最多留 N 份，超出的删最旧的
                var all = Snapshots(assetPath);
                for (int i = 0; i < all.Count - MaxSnapshotsPerAsset; i++)
                {
                    try { File.Delete(all[i]); } catch { }
                }
                return file;
            }
            catch { return null; }
        }

        static List<string> Snapshots(string assetPath)
        {
            var list = new List<string>();
            try
            {
                if (!Directory.Exists(UndoDir)) return list;
                string prefix = SnapshotKey(assetPath) + "__";
                foreach (var f in Directory.GetFiles(UndoDir, prefix + "*.snap"))
                    list.Add(f);
                list.Sort(StringComparer.Ordinal);
            }
            catch { }
            return list;
        }

        /// <summary>回滚到上一份快照（再调一次就回到更早的一份）。</summary>
        static string Undo(Dictionary<string, object> args)
        {
            string assetPath = Str(args, "path");
            string full, err;
            if (!TryFullPath(assetPath, out full, out err)) return Fail(err);

            var snaps = Snapshots(assetPath);
            if (snaps.Count == 0)
                return Fail("没有可回滚的快照: " + assetPath + "（快照只覆盖本 Bridge 的写操作）");

            string snap = snaps[snaps.Count - 1];
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(full));
                File.Copy(snap, full, true);
                AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport);
                File.Delete(snap);
            }
            catch (Exception ex)
            {
                return Fail("回滚失败: " + ex.Message);
            }

            return "{\"ok\":true,\"path\":" + Q(assetPath) +
                   ",\"restoredFrom\":" + Q(Path.GetFileName(snap)) +
                   ",\"remaining\":" + (snaps.Count - 1) + "}";
        }

        // ═══════════════ create ═══════════════
        static string Create(Dictionary<string, object> args)
        {
            if (!ShaderGraphApi.Available) return Fail(ShaderGraphApi.Reason);

            string assetPath = Str(args, "path").Replace('\\', '/').Trim();
            if (string.IsNullOrEmpty(assetPath)) return Fail("缺少 path（如 Assets/Shaders/My.shadergraph）");
            if (!assetPath.EndsWith(".shadergraph", StringComparison.OrdinalIgnoreCase))
                assetPath += ".shadergraph";

            string full, err;
            if (!TryFullPath(assetPath, out full, out err)) return Fail(err);
            if (File.Exists(full) && !Bool(args, "overwrite", false))
                return Fail("已存在同名资源: " + assetPath + "（需要覆盖请传 overwrite=true）");
            if (File.Exists(full) && FindGraphWindow(assetPath) != null && !Bool(args, "allowOpenWindow", false))
                return Fail("目标图正在 Shader Graph 窗口中打开，覆盖写盘会触发窗口重载/弹框。请先 op=close 关闭窗口。");

            string template = Str(args, "template");
            if (string.IsNullOrEmpty(template)) template = "lit";
            if (!ShaderGraphApi.IsKnownTemplate(template))
                return Fail("未知模板: " + template + "（可用: lit/unlit/sprite_lit/sprite_unlit/blank）");

            try
            {
                string dir = Path.GetDirectoryName(full);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
            }
            catch (Exception ex) { return Fail("创建目录失败: " + ex.Message); }

            if (File.Exists(full)) Snapshot(assetPath, full); // 覆盖前留底
            string createErr = ShaderGraphApi.CreateGraph(assetPath, full, template);
            if (createErr != null) return Fail(createErr);

            var shader = AssetDatabase.LoadAssetAtPath<Shader>(assetPath);
            return "{\"ok\":true,\"path\":" + Q(assetPath) +
                   ",\"template\":" + Q(template) +
                   ",\"imported\":" + B(shader != null) +
                   (shader == null ? ",\"warning\":\"资源已写盘但未能作为 Shader 导入，请查看 Console\"}" : "}");
        }

        // ═══════════════ open ═══════════════
        static string Open(Dictionary<string, object> args)
        {
            string assetPath = Str(args, "path");
            var asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(assetPath);
            if (asset == null) return Fail("资源不存在: " + assetPath);
            AssetDatabase.OpenAsset(asset);
            return "{\"ok\":true,\"path\":" + Q(assetPath) + "}";
        }

        // ═══════════════ close ═══════════════
        static string CloseWindow(Dictionary<string, object> args)
        {
            string assetPath = Str(args, "path");
            if (string.IsNullOrEmpty(assetPath)) return Fail("缺少 path");
            var win = FindGraphWindow(assetPath);
            if (win == null) return "{\"ok\":true,\"closed\":false,\"note\":\"该图当前没有打开窗口\"}";

            const System.Reflection.BindingFlags F =
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;

            bool saveWindow = Bool(args, "saveWindow", false);
            try
            {
                if (saveWindow)
                {
                    var save = win.GetType().GetMethod("SaveAsset", F);
                    if (save != null) save.Invoke(win, null);
                }
                else
                {
                    // 窗口经常因为内部归一化被标脏，Close() 会弹模态框把主线程卡死。
                    // 默认以磁盘为准（丢弃窗口内未保存状态），要保留窗口改动就传 saveWindow=true。
                    var dirtyProp = win.GetType().GetProperty("hasUnsavedChanges", F);
                    if (dirtyProp != null && dirtyProp.CanWrite) dirtyProp.SetValue(win, false);
                }
            }
            catch { }

            win.Close();
            return "{\"ok\":true,\"closed\":true,\"savedWindow\":" + B(saveWindow) + "}";
        }

        // ═══════════════ add_node ═══════════════
        static string AddNode(Dictionary<string, object> args)
        {
            if (!ShaderGraphApi.Available) return Fail(ShaderGraphApi.Reason);

            object graph;
            string full, err;
            if (!LoadForWrite(args, out graph, out full, out err)) return err;

            string nodeType = Str(args, "nodeType");
            var type = ShaderGraphApi.ResolveNodeType(nodeType);
            if (type == null) return Fail("未知节点类型: " + nodeType + "（用 op=node_types 查询）");

            float x = Float(args, "x", 0f), y = Float(args, "y", 0f);
            string propertyId = Str(args, "propertyId");
            if (string.IsNullOrEmpty(propertyId)) propertyId = Str(args, "property");

            string nodeId = ShaderGraphApi.AddNode(graph, type, x, y, propertyId);
            ShaderGraphApi.SaveGraph(Str(args, "path"), graph);

            return "{\"ok\":true,\"nodeId\":" + Q(nodeId) +
                   ",\"nodeType\":" + Q(type.Name) +
                   ",\"x\":" + F(x) + ",\"y\":" + F(y) + "}";
        }

        // ═══════════════ remove_node ═══════════════
        static string RemoveNode(Dictionary<string, object> args)
        {
            if (!ShaderGraphApi.Available) return Fail(ShaderGraphApi.Reason);

            object graph;
            string full, err;
            if (!LoadForWrite(args, out graph, out full, out err)) return err;

            string nodeId = Str(args, "nodeId");
            var node = ShaderGraphApi.GetNode(graph, nodeId);
            if (node == null) return Fail("节点不存在: " + nodeId);

            ShaderGraphApi.RemoveNode(graph, node);
            ShaderGraphApi.SaveGraph(Str(args, "path"), graph);
            return "{\"ok\":true,\"removedNodeId\":" + Q(nodeId) + "}";
        }

        // ═══════════════ move_node ═══════════════
        static string MoveNode(Dictionary<string, object> args)
        {
            if (!ShaderGraphApi.Available) return Fail(ShaderGraphApi.Reason);

            object graph;
            string full, err;
            if (!LoadForWrite(args, out graph, out full, out err)) return err;

            string nodeId = Str(args, "nodeId");
            var node = ShaderGraphApi.GetNode(graph, nodeId);
            if (node == null) return Fail("节点不存在: " + nodeId);

            float x = Float(args, "x", 0f), y = Float(args, "y", 0f);
            if (!ShaderGraphApi.MoveNode(node, x, y))
                return Fail("当前 ShaderGraph 版本无法设置节点位置。");

            ShaderGraphApi.SaveGraph(Str(args, "path"), graph);
            return "{\"ok\":true,\"nodeId\":" + Q(nodeId) + ",\"x\":" + F(x) + ",\"y\":" + F(y) + "}";
        }

        // ═══════════════ connect ═══════════════
        static string Connect(Dictionary<string, object> args)
        {
            if (!ShaderGraphApi.Available) return Fail(ShaderGraphApi.Reason);

            object graph;
            string full, err;
            if (!LoadForWrite(args, out graph, out full, out err)) return err;

            var outNode = ShaderGraphApi.GetNode(graph, Str(args, "outputNodeId"));
            if (outNode == null) return Fail("输出节点不存在: " + Str(args, "outputNodeId"));
            var inNode = ShaderGraphApi.GetNode(graph, Str(args, "inputNodeId"));
            if (inNode == null) return Fail("输入节点不存在: " + Str(args, "inputNodeId"));

            int outSlot = ResolveSlot(outNode, false, args, "outputSlotId", "outputSlot");
            if (outSlot == int.MinValue) return Fail("输出节点上找不到指定输出槽（可传 outputSlotId 或 outputSlot 名称）");
            int inSlot = ResolveSlot(inNode, true, args, "inputSlotId", "inputSlot");
            if (inSlot == int.MinValue) return Fail("输入节点上找不到指定输入槽（可传 inputSlotId 或 inputSlot 名称）");

            string connectErr = ShaderGraphApi.Connect(graph, outNode, outSlot, inNode, inSlot);
            if (connectErr != null) return Fail(connectErr);

            ShaderGraphApi.SaveGraph(Str(args, "path"), graph);
            return "{\"ok\":true,\"outputNodeId\":" + Q(Str(args, "outputNodeId")) +
                   ",\"outputSlotId\":" + outSlot +
                   ",\"inputNodeId\":" + Q(Str(args, "inputNodeId")) +
                   ",\"inputSlotId\":" + inSlot + "}";
        }

        static int ResolveSlot(object node, bool wantInput, Dictionary<string, object> args, string idKey, string nameKey)
        {
            bool hasId = args.ContainsKey(idKey) && args[idKey] != null;
            int slotId = hasId ? Int(args, idKey, int.MinValue) : int.MinValue;
            string slotName = hasId ? null : Str(args, nameKey);
            return ShaderGraphApi.FindSlot(node, wantInput, slotId, slotName);
        }

        // ═══════════════ disconnect ═══════════════
        static string Disconnect(Dictionary<string, object> args)
        {
            if (!ShaderGraphApi.Available) return Fail(ShaderGraphApi.Reason);

            object graph;
            string full, err;
            if (!LoadForWrite(args, out graph, out full, out err)) return err;

            string outId = Str(args, "outputNodeId");
            string inId = Str(args, "inputNodeId");
            if (string.IsNullOrEmpty(outId) && string.IsNullOrEmpty(inId))
                return Fail("至少指定 outputNodeId 或 inputNodeId");

            int outSlot = args.ContainsKey("outputSlotId") ? Int(args, "outputSlotId", -1) : -1;
            int inSlot = args.ContainsKey("inputSlotId") ? Int(args, "inputSlotId", -1) : -1;

            int removed = ShaderGraphApi.Disconnect(
                graph,
                string.IsNullOrEmpty(outId) ? null : outId, outSlot,
                string.IsNullOrEmpty(inId) ? null : inId, inSlot);

            if (removed == 0) return Fail("没有匹配的连线（检查节点 id 与槽位 id）");

            ShaderGraphApi.SaveGraph(Str(args, "path"), graph);
            return "{\"ok\":true,\"removedEdges\":" + removed + "}";
        }

        // ═══════════════ set_value ═══════════════
        static string SetValue(Dictionary<string, object> args)
        {
            if (!ShaderGraphApi.Available) return Fail(ShaderGraphApi.Reason);

            object graph;
            string full, err;
            if (!LoadForWrite(args, out graph, out full, out err)) return err;

            string nodeId = Str(args, "nodeId");
            var node = ShaderGraphApi.GetNode(graph, nodeId);
            if (node == null) return Fail("节点不存在: " + nodeId);

            string field = Str(args, "field");
            if (string.IsNullOrEmpty(field)) field = "m_Value";
            string value = Str(args, "value");

            string setErr = ShaderGraphApi.SetNodeField(node, field, value);
            if (setErr != null) return Fail(setErr);

            ShaderGraphApi.SaveGraph(Str(args, "path"), graph);
            return "{\"ok\":true,\"nodeId\":" + Q(nodeId) +
                   ",\"field\":" + Q(field) + ",\"value\":" + Q(value) + "}";
        }

        // ═══════════════ node_types ═══════════════
        static string NodeTypes(Dictionary<string, object> args)
        {
            if (!ShaderGraphApi.Available) return Fail(ShaderGraphApi.Reason);
            string filter = Str(args, "filter").ToLowerInvariant();
            int max = Int(args, "max", 200);

            var sb = new StringBuilder(4096);
            sb.Append("{\"ok\":true,\"types\":[");
            int count = 0;
            foreach (var t in ShaderGraphApi.NodeTypes())
            {
                if (!string.IsNullOrEmpty(filter) &&
                    t.Name.ToLowerInvariant().IndexOf(filter, StringComparison.Ordinal) < 0 &&
                    (t.FullName ?? "").ToLowerInvariant().IndexOf(filter, StringComparison.Ordinal) < 0) continue;
                if (count >= max) break;
                if (count > 0) sb.Append(',');
                sb.Append("{\"name\":").Append(Q(t.Name))
                  .Append(",\"fullName\":").Append(Q(t.FullName))
                  .Append(",\"title\":").Append(Q(ShaderGraphApi.NodeTitle(t))).Append('}');
                count++;
            }
            sb.Append("],\"count\":").Append(count).Append(",\"truncated\":").Append(B(count >= max)).Append('}');
            return sb.ToString();
        }

        // ═══════════════ 公共工具 ═══════════════

        static bool LoadForRead(Dictionary<string, object> args, out object graph, out string errorJson)
        {
            graph = null;
            errorJson = null;
            string assetPath = Str(args, "path");
            string full, err;
            if (!TryFullPath(assetPath, out full, out err)) { errorJson = Fail(err); return false; }
            if (!File.Exists(full)) { errorJson = Fail("文件不存在: " + assetPath); return false; }
            try { graph = ShaderGraphApi.LoadGraph(full); }
            catch (Exception ex) { errorJson = Fail("载入失败: " + Unwrap(ex)); return false; }
            return true;
        }

        static bool LoadForWrite(Dictionary<string, object> args, out object graph, out string full, out string errorJson)
        {
            graph = null;
            full = null;
            errorJson = null;
            string assetPath = Str(args, "path");
            if (!TryFullPath(assetPath, out full, out string err)) { errorJson = Fail(err); return false; }
            if (!File.Exists(full)) { errorJson = Fail("文件不存在: " + assetPath); return false; }

            // 图正在 Shader Graph 窗口里打开时，写盘会触发窗口的「磁盘文件已变」重载流程，
            // 严重时会弹模态框把编辑器主线程卡住（自动化场景下等于挂死）。
            // 因此默认拒绝，要求先关窗口；确实要写可传 allowOpenWindow=true。
            if (!Bool(args, "allowOpenWindow", false))
            {
                var win = FindGraphWindow(assetPath);
                if (win != null)
                {
                    errorJson = Fail(
                        "图正在 Shader Graph 窗口中打开，直接写盘会导致窗口重载甚至弹框卡住主线程。" +
                        "请先关闭该窗口（或用 op=close 关闭），或显式传 allowOpenWindow=true。");
                    return false;
                }
            }

            if (!assetPath.EndsWith(".shadergraph", StringComparison.OrdinalIgnoreCase))
                assetPath = assetPath.Substring(0, assetPath.Length - ".shadergraph".Length) + ".shadergraph";

            Snapshot(assetPath, full); // 改动前留底，供 op=undo 回滚
            try { graph = ShaderGraphApi.LoadGraph(full); }
            catch (Exception ex) { errorJson = Fail("载入失败: " + Unwrap(ex)); return false; }
            return true;
        }

        /// <summary>找到正在显示指定 .shadergraph 的 Shader Graph 窗口；没有则返回 null。</summary>
        static EditorWindow FindGraphWindow(string assetPath)
        {
            try
            {
                string normalized = (assetPath ?? "").Replace('\\', '/');
                string stem = Path.GetFileNameWithoutExtension(normalized);

                foreach (var w in Resources.FindObjectsOfTypeAll<EditorWindow>())
                {
                    if (w == null || w.GetType().Name != "MaterialGraphEditWindow") continue;
                    if (WindowAssetPath(w) == normalized) return w;
                    // 退路：窗口标题就是图名（不同版本持有 asset 的字段名不一致）
                    string title = w.titleContent != null ? w.titleContent.text : "";
                    if (!string.IsNullOrEmpty(stem) && string.Equals(title, stem, StringComparison.OrdinalIgnoreCase))
                        return w;
                }
            }
            catch { }
            return null;
        }

        static string WindowAssetPath(EditorWindow w)
        {
            try
            {
                const System.Reflection.BindingFlags F =
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
                var t = w.GetType();

                object obj = null;
                var fi = t.GetField("m_CurrentAsset", F);
                if (fi != null) obj = fi.GetValue(w);
                if (obj == null)
                {
                    var pi = t.GetProperty("asset", F);
                    if (pi != null) obj = pi.GetValue(w);
                }

                var shader = obj as Shader;
                if (shader == null && obj != null)
                {
                    // 有些版本存的是 GraphObject：从它身上再取 asset
                    var gp = obj.GetType().GetProperty("asset", F);
                    if (gp != null) shader = gp.GetValue(obj) as Shader;
                    if (shader == null)
                    {
                        var gf = obj.GetType().GetField("asset", F);
                        if (gf != null) shader = gf.GetValue(obj) as Shader;
                    }
                }
                return shader == null ? null : AssetDatabase.GetAssetPath(shader).Replace('\\', '/');
            }
            catch { return null; }
        }

        static bool TryFullPath(string assetPath, out string full, out string error)
        {
            full = null;
            error = null;
            string p = (assetPath ?? "").Replace('\\', '/').Trim();
            if (p.Length == 0) { error = "缺少 path"; return false; }
            if (!p.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
            { error = "path 必须以 Assets/ 开头: " + p; return false; }
            if (p.Contains("..")) { error = "path 不能包含 '..'"; return false; }

            string root = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            full = Path.GetFullPath(Path.Combine(root, p));
            if (!full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) &&
                !full.StartsWith(root + "/", StringComparison.OrdinalIgnoreCase))
            { error = "path 越出工程目录"; return false; }
            return true;
        }

        static string Str(Dictionary<string, object> a, string key)
        {
            object v;
            if (a == null || !a.TryGetValue(key, out v) || v == null) return "";
            return v as string ?? Convert.ToString(v, CultureInfo.InvariantCulture);
        }

        static int Int(Dictionary<string, object> a, string key, int fallback)
        {
            object v;
            if (a == null || !a.TryGetValue(key, out v) || v == null) return fallback;
            if (v is double) return (int)(double)v;
            if (v is bool) return (bool)v ? 1 : 0;
            int r;
            return int.TryParse(Convert.ToString(v, CultureInfo.InvariantCulture), out r) ? r : fallback;
        }

        static float Float(Dictionary<string, object> a, string key, float fallback)
        {
            object v;
            if (a == null || !a.TryGetValue(key, out v) || v == null) return fallback;
            if (v is double) return (float)(double)v;
            float r;
            return float.TryParse(Convert.ToString(v, CultureInfo.InvariantCulture), NumberStyles.Float, CultureInfo.InvariantCulture, out r) ? r : fallback;
        }

        static bool Bool(Dictionary<string, object> a, string key, bool fallback)
        {
            object v;
            if (a == null || !a.TryGetValue(key, out v) || v == null) return fallback;
            if (v is bool) return (bool)v;
            string s = Convert.ToString(v, CultureInfo.InvariantCulture);
            return s == "1" || string.Equals(s, "true", StringComparison.OrdinalIgnoreCase);
        }

        internal static string Fail(string msg)
        {
            return "{\"ok\":false,\"error\":" + Q(msg ?? "") + "}";
        }

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

        internal static string B(bool b) { return b ? "true" : "false"; }

        internal static string F(float f) { return f.ToString("0.######", CultureInfo.InvariantCulture); }
    }

    /// <summary>
    /// 极简扁平 JSON 解析：只处理一层对象，值为 string/number/bool/null。
    /// 嵌套对象/数组按原文保留（本服务用不到，但不会解析失败）。
    /// </summary>
    internal static class FlatJson
    {
        internal static Dictionary<string, object> Parse(string text)
        {
            var result = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrEmpty(text)) return result;

            int i = 0;
            SkipWs(text, ref i);
            if (i >= text.Length || text[i] != '{') throw new FormatException("需要 JSON 对象");
            i++;
            while (true)
            {
                SkipWs(text, ref i);
                if (i >= text.Length) throw new FormatException("未闭合的对象");
                if (text[i] == '}') { i++; break; }

                string key = ReadString(text, ref i);
                SkipWs(text, ref i);
                if (i >= text.Length || text[i] != ':') throw new FormatException("缺少 ':'");
                i++;
                SkipWs(text, ref i);

                object value;
                char c = i < text.Length ? text[i] : '\0';
                if (c == '"') value = ReadString(text, ref i);
                else if (c == '{' || c == '[') value = ReadRaw(text, ref i);
                else value = ReadLiteral(text, ref i);

                result[key] = value;

                SkipWs(text, ref i);
                if (i < text.Length && text[i] == ',') { i++; continue; }
                if (i < text.Length && text[i] == '}') { i++; break; }
                throw new FormatException("对象语法错误（位置 " + i + "）");
            }
            return result;
        }

        static void SkipWs(string s, ref int i)
        {
            while (i < s.Length && (s[i] == ' ' || s[i] == '\t' || s[i] == '\n' || s[i] == '\r')) i++;
        }

        static string ReadString(string s, ref int i)
        {
            if (s[i] != '"') throw new FormatException("需要字符串");
            i++;
            var sb = new StringBuilder();
            while (i < s.Length)
            {
                char c = s[i++];
                if (c == '"') return sb.ToString();
                if (c != '\\') { sb.Append(c); continue; }
                if (i >= s.Length) break;
                char e = s[i++];
                switch (e)
                {
                    case '"': sb.Append('"'); break;
                    case '\\': sb.Append('\\'); break;
                    case '/': sb.Append('/'); break;
                    case 'b': sb.Append('\b'); break;
                    case 'f': sb.Append('\f'); break;
                    case 'n': sb.Append('\n'); break;
                    case 'r': sb.Append('\r'); break;
                    case 't': sb.Append('\t'); break;
                    case 'u':
                        if (i + 4 <= s.Length)
                        {
                            sb.Append((char)Convert.ToInt32(s.Substring(i, 4), 16));
                            i += 4;
                        }
                        break;
                    default: sb.Append(e); break;
                }
            }
            throw new FormatException("字符串未闭合");
        }

        static object ReadLiteral(string s, ref int i)
        {
            int start = i;
            while (i < s.Length && s[i] != ',' && s[i] != '}' && s[i] != ']') i++;
            string raw = s.Substring(start, i - start).Trim();
            if (raw == "true") return true;
            if (raw == "false") return false;
            if (raw == "null" || raw.Length == 0) return null;
            double d;
            if (double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out d)) return d;
            return raw;
        }

        static object ReadRaw(string s, ref int i)
        {
            int start = i;
            char open = s[i];
            char close = open == '{' ? '}' : ']';
            int depth = 0;
            bool inStr = false;
            while (i < s.Length)
            {
                char c = s[i];
                if (inStr)
                {
                    if (c == '\\') { i += 2; continue; }
                    if (c == '"') inStr = false;
                }
                else
                {
                    if (c == '"') inStr = true;
                    else if (c == open) depth++;
                    else if (c == close)
                    {
                        depth--;
                        if (depth == 0) { i++; return s.Substring(start, i - start); }
                    }
                }
                i++;
            }
            throw new FormatException("嵌套值未闭合");
        }
    }
}
