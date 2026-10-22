// ═══════════════════════════════════════════════════════════════
//  ShaderGraphApi — Shader Graph 编辑器 API 的反射封装
//
//  GraphData / MultiJson / FileUtilities 等类型在 Unity.ShaderGraph.Editor
//  程序集里都是 internal 的，所以这里全部用反射访问，不引用 ShaderGraph 程序集，
//  也就不会给 Bridge 增加编译依赖。
//
//  所有写操作都走 GraphData 模型（而不是对 .shadergraph 文本做正则替换）：
//  序列化由 ShaderGraph 自己完成，写出来的图一定是合法的。
//  反射缺少成员时 Available=false，调用方直接报错，不会写出半成品。
// ═══════════════════════════════════════════════════════════════

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace Framework.XYEditor.Bridge
{
    internal sealed class SGSlotInfo
    {
        public int id;
        public string name;
        public bool isInput;
        public string type;
        public bool connected;
    }

    internal sealed class SGNodeInfo
    {
        public string id;
        public string type;   // 类型全名，可直接回传给 add_node / resolve
        public string name;   // 节点显示名（如 "Add"、"Property"）
        public float x;
        public float y;
        public readonly List<SGSlotInfo> slots = new List<SGSlotInfo>();
    }

    internal sealed class SGEdgeInfo
    {
        public string outNode;
        public string inNode;
        public int outSlot;
        public int inSlot;
    }

    internal static class ShaderGraphApi
    {
        const BindingFlags INST = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
        const BindingFlags STAT = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;

        static bool _probed;
        static bool _available;
        static string _reason = "";
        static string _missing = "";

        internal static bool Available { get { Probe(); return _available; } }
        internal static string Reason { get { Probe(); return _reason; } }
        internal static bool UrpAvailable { get { Probe(); return _urpTarget != null && _trySetSubTarget != null; } }

        // ── ShaderGraph 侧类型 ──
        static Assembly _sgAsm;
        static Type _graphData, _target, _blockDesc, _absNode, _slotRef, _materialSlot, _messageMgr,
            _multiJson, _fileUtil, _jsonObject, _categoryData, _propertyNode, _abstractProperty;
        static Type _urpTarget, _litSub, _unlitSub, _spriteLitSub, _spriteUnlitSub;

        // ── 成员 ──
        static PropertyInfo _messageMgrProp, _objectIdProp, _drawStateProp, _drawPosProp, _edgesProp,
            _graphPathProp, _graphPropsProp, _propNodeProp, _slotNodeProp, _slotIdProp;
        static MethodInfo _addContexts, _initOutputs, _getActiveBlocks, _addRemoveBlocks, _onEnable,
            _addNode, _getNodeFromId, _connect, _removeEdge, _removeNode, _getNodesGeneric,
            _getSlotsGeneric, _addCategory, _categoryDefault, _trySetSubTarget, _addGraphInput,
            _serialize, _deserialize, _writeToDisk, _writeGraphToDisk;
        static ConstructorInfo _slotRefCtor;

        internal static void Probe()
        {
            if (_probed) return;
            _probed = true;
            try
            {
                Init();
                _available = true;
            }
            catch (Exception ex)
            {
                _available = false;
                _reason = ex.Message + (string.IsNullOrEmpty(_missing) ? "" : " | 缺失成员: " + _missing);
            }
        }

        static Assembly FindAsm(string name)
        {
            foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
                if (a.GetName().Name == name) return a;
            return null;
        }

        static Type Need(Assembly asm, string fullName, string label)
        {
            var t = asm == null ? null : asm.GetType(fullName);
            if (t == null) Missing(label);
            return t;
        }

        static void Missing(string name)
        {
            _missing = string.IsNullOrEmpty(_missing) ? name : _missing + ", " + name;
        }

        static void Init()
        {
            _sgAsm = FindAsm("Unity.ShaderGraph.Editor");
            if (_sgAsm == null)
                throw new InvalidOperationException("未找到 Unity.ShaderGraph.Editor 程序集：请确认工程已安装 com.unity.shadergraph（URP/HDRP 会自动带上）。");

            _graphData = Need(_sgAsm, "UnityEditor.ShaderGraph.GraphData", "GraphData");
            _target = Need(_sgAsm, "UnityEditor.ShaderGraph.Target", "Target");
            _blockDesc = Need(_sgAsm, "UnityEditor.ShaderGraph.BlockFieldDescriptor", "BlockFieldDescriptor");
            _absNode = Need(_sgAsm, "UnityEditor.ShaderGraph.AbstractMaterialNode", "AbstractMaterialNode");
            _materialSlot = Need(_sgAsm, "UnityEditor.ShaderGraph.MaterialSlot", "MaterialSlot");
            _slotRef = Need(_sgAsm, "UnityEditor.Graphing.SlotReference", "SlotReference");
            _multiJson = Need(_sgAsm, "UnityEditor.ShaderGraph.Serialization.MultiJson", "MultiJson");
            _fileUtil = Need(_sgAsm, "UnityEditor.ShaderGraph.FileUtilities", "FileUtilities");
            _jsonObject = Need(_sgAsm, "UnityEditor.ShaderGraph.Serialization.JsonObject", "JsonObject");
            _categoryData = Need(_sgAsm, "UnityEditor.ShaderGraph.CategoryData", "CategoryData");
            _messageMgr = Need(_sgAsm, "UnityEditor.Graphing.Util.MessageManager", "MessageManager");
            _propertyNode = _sgAsm.GetType("UnityEditor.ShaderGraph.PropertyNode");
            _abstractProperty = _sgAsm.GetType("UnityEditor.ShaderGraph.Internal.AbstractShaderProperty");

            if (_graphData == null || _absNode == null || _multiJson == null || _fileUtil == null)
                throw new InvalidOperationException("ShaderGraph 核心类型解析失败。");

            _messageMgrProp = _graphData.GetProperty("messageManager", INST);
            _edgesProp = _graphData.GetProperty("edges", INST);
            _graphPathProp = _graphData.GetProperty("path", INST);
            _graphPropsProp = _graphData.GetProperty("properties", INST);
            _objectIdProp = _jsonObject.GetProperty("objectId", INST);
            _drawStateProp = _absNode.GetProperty("drawState", INST);
            _drawPosProp = _drawStateProp == null ? null : _drawStateProp.PropertyType.GetProperty("position", INST);
            _propNodeProp = _propertyNode == null ? null : _propertyNode.GetProperty("property", INST);

            if (_objectIdProp == null) Missing("JsonObject.objectId");
            if (_drawStateProp == null || _drawPosProp == null) Missing("AbstractMaterialNode.drawState.position");
            if (_edgesProp == null) Missing("GraphData.edges");

            _addContexts = _graphData.GetMethod("AddContexts", INST, null, Type.EmptyTypes, null);
            if (_addContexts == null) Missing("GraphData.AddContexts");

            _initOutputs = _graphData.GetMethod(
                "InitializeOutputs", INST, null,
                new[] { _target == null ? typeof(object) : _target.MakeArrayType(), _blockDesc == null ? typeof(object) : _blockDesc.MakeArrayType() }, null);
            if (_initOutputs == null) Missing("GraphData.InitializeOutputs");

            _getActiveBlocks = _graphData.GetMethod("GetActiveBlocksForAllActiveTargets", INST);
            _addRemoveBlocks = _graphData.GetMethod("AddRemoveBlocksFromActiveList", INST);
            _onEnable = _graphData.GetMethod("OnEnable", INST, null, Type.EmptyTypes, null);
            _addCategory = _graphData.GetMethod("AddCategory", INST);

            // AddNode 有两代签名：1 参（14.x 实测）与 2 参（带 preview 偏好）
            _addNode = _absNode == null ? null
                : (_graphData.GetMethod("AddNode", INST, null, new[] { _absNode }, null)
                   ?? _graphData.GetMethod("AddNode", INST, null, new[] { _absNode, typeof(bool) }, null));
            if (_addNode == null) Missing("GraphData.AddNode");

            // GetNodeFromId 有泛型与非泛型两个重载，只能显式挑非泛型的 string 版本
            _getNodeFromId = _graphData.GetMethods(INST).FirstOrDefault(m =>
                m.Name == "GetNodeFromId" && !m.IsGenericMethod &&
                m.GetParameters().Length == 1 && m.GetParameters()[0].ParameterType == typeof(string));
            if (_getNodeFromId == null) Missing("GraphData.GetNodeFromId(string)");

            _connect = _slotRef == null ? null
                : _graphData.GetMethod("Connect", INST, null, new[] { _slotRef, _slotRef }, null);
            if (_connect == null) Missing("GraphData.Connect");

            _removeEdge = _graphData.GetMethods(INST).FirstOrDefault(m => m.Name == "RemoveEdge" && m.GetParameters().Length == 1);
            _removeNode = _absNode == null ? null
                : _graphData.GetMethods(INST).FirstOrDefault(m =>
                    m.Name == "RemoveNode" && m.GetParameters().Length == 1 && m.GetParameters()[0].ParameterType == _absNode);
            _getNodesGeneric = _graphData.GetMethods(INST).FirstOrDefault(m =>
                m.Name == "GetNodes" && m.IsGenericMethod && m.GetParameters().Length == 0);
            _getSlotsGeneric = _absNode == null ? null
                : _absNode.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                    .FirstOrDefault(m => m.Name == "GetSlots" && m.IsGenericMethod && m.GetParameters().Length == 1);

            _addGraphInput = _graphData.GetMethod("AddGraphInput", INST);

            if (_removeEdge == null) Missing("GraphData.RemoveEdge");
            if (_removeNode == null) Missing("GraphData.RemoveNode");
            if (_getNodesGeneric == null) Missing("GraphData.GetNodes<T>");
            if (_getSlotsGeneric == null || _materialSlot == null) Missing("AbstractMaterialNode.GetSlots<T>");

            _slotRefCtor = _slotRef == null || _absNode == null ? null
                : _slotRef.GetConstructor(new[] { _absNode, typeof(int) });
            if (_slotRefCtor == null) Missing("SlotReference(node, slotId)");
            if (_slotRef != null)
            {
                _slotNodeProp = _slotRef.GetProperty("node", INST);
                _slotIdProp = _slotRef.GetProperty("slotId", INST);
            }

            _serialize = _multiJson.GetMethod("Serialize", STAT, null, new[] { _jsonObject }, null);
            _deserialize = _multiJson.GetMethods(STAT).FirstOrDefault(m =>
                m.Name == "Deserialize" && m.IsGenericMethod && m.GetParameters().Length == 4);
            _writeToDisk = _fileUtil.GetMethod("WriteToDisk", STAT, null, new[] { typeof(string), typeof(string) }, null);
            _writeGraphToDisk = _graphData == null ? null
                : _fileUtil.GetMethod("WriteShaderGraphToDisk", STAT, null, new[] { typeof(string), _graphData }, null);

            if (_serialize == null) Missing("MultiJson.Serialize");
            if (_deserialize == null) Missing("MultiJson.Deserialize<T>(4 参)");
            if (_writeToDisk == null) Missing("FileUtilities.WriteToDisk");
            if (_writeGraphToDisk == null) Missing("FileUtilities.WriteShaderGraphToDisk");

            _categoryDefault = _categoryData == null ? null
                : _categoryData.GetMethod("DefaultCategory", STAT);

            // URP 目标（可选）：只影响 create 的模板，不影响已有图的编辑
            var urpAsm = FindAsm("Unity.RenderPipelines.Universal.Editor");
            if (urpAsm != null)
            {
                _urpTarget = urpAsm.GetType("UnityEditor.Rendering.Universal.ShaderGraph.UniversalTarget");
                _litSub = urpAsm.GetType("UnityEditor.Rendering.Universal.ShaderGraph.UniversalLitSubTarget");
                _unlitSub = urpAsm.GetType("UnityEditor.Rendering.Universal.ShaderGraph.UniversalUnlitSubTarget");
                _spriteLitSub = urpAsm.GetType("UnityEditor.Rendering.Universal.ShaderGraph.UniversalSpriteLitSubTarget");
                _spriteUnlitSub = urpAsm.GetType("UnityEditor.Rendering.Universal.ShaderGraph.UniversalSpriteUnlitSubTarget");
                _trySetSubTarget = _urpTarget == null ? null
                    : _urpTarget.GetMethod("TrySetActiveSubTarget", INST, null, new[] { typeof(Type) }, null);
            }
        }

        // ─────────────────────────────────────────────────────────
        //  创建 / 载入 / 保存
        // ─────────────────────────────────────────────────────────

        /// <summary>模板名 → URP SubTarget 类型；null 表示 blank（无 target）。</summary>
        internal static Type ResolveTemplate(string template)
        {
            Probe();
            switch ((template ?? "").Trim().ToLowerInvariant())
            {
                case "":
                case "lit":
                case "urp_lit": return _litSub;
                case "unlit":
                case "urp_unlit": return _unlitSub;
                case "sprite_lit":
                case "urp_sprite_lit": return _spriteLitSub;
                case "sprite_unlit":
                case "urp_sprite_unlit": return _spriteUnlitSub;
                case "blank":
                case "empty": return null;
                default: return _litSub;
            }
        }

        internal static bool IsKnownTemplate(string template)
        {
            switch ((template ?? "").Trim().ToLowerInvariant())
            {
                case "":
                case "lit":
                case "urp_lit":
                case "unlit":
                case "urp_unlit":
                case "sprite_lit":
                case "urp_sprite_lit":
                case "sprite_unlit":
                case "urp_sprite_unlit":
                case "blank":
                case "empty":
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>按模板创建一张新图并写盘。返回 null 表示成功，否则是错误文本。</summary>
        internal static string CreateGraph(string assetPath, string fullPath, string template)
        {
            if (!Available) return "ShaderGraph 反射层不可用: " + Reason;

            string t = (template ?? "lit").Trim().ToLowerInvariant();
            Type subTarget = ResolveTemplate(t);
            bool wantsUrp = !(t == "blank" || t == "empty");
            if (wantsUrp && !UrpAvailable)
                return "工程里没有 URP Shader Graph 编辑器程序集，只能创建 template=blank 的空图。";

            var graph = NewGraph();
            _addContexts.Invoke(graph, null);

            var targets = Array.CreateInstance(_target, wantsUrp ? 1 : 0);
            if (wantsUrp)
            {
                var target = Activator.CreateInstance(_urpTarget);
                _trySetSubTarget.Invoke(target, new object[] { subTarget });
                targets.SetValue(target, 0);
            }

            var blocks = BuildTemplateBlocks(t);
            _initOutputs.Invoke(graph, new object[] { targets, blocks });
            // 模板没覆盖到的（如 sprite 模板）补全 target 自己认为激活的 block
            if (wantsUrp && (_getActiveBlocks != null && _addRemoveBlocks != null))
            {
                var active = _getActiveBlocks.Invoke(graph, null);
                _addRemoveBlocks.Invoke(graph, new object[] { active });
            }

            AddDefaultCategory(graph);
            if (_graphPathProp != null) _graphPathProp.SetValue(graph, "Shader Graphs");

            string text = _writeGraphToDisk.Invoke(null, new object[] { assetPath, graph }) as string;
            if (string.IsNullOrEmpty(text)) return "写盘失败: " + assetPath;

            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport);
            return null;
        }

        static void AddDefaultCategory(object graph)
        {
            if (_addCategory == null || _categoryDefault == null) return;
            try
            {
                var cat = _categoryDefault.Invoke(null, new object[] { null });
                if (cat != null) _addCategory.Invoke(graph, new object[] { cat });
            }
            catch { /* 分类只是编辑器显示用，失败不影响图 */ }
        }

        /// <summary>按官方模板给出一组 Block 描述符；取不到就返回空数组，交给激活列表兜底。</summary>
        static Array BuildTemplateBlocks(string template)
        {
            string[] names;
            switch (template)
            {
                case "unlit":
                case "urp_unlit":
                    names = new[] { "Vertex/P", "Vertex/N", "Vertex/T", "Surface/C" };
                    break;
                case "sprite_lit":
                case "urp_sprite_lit":
                case "sprite_unlit":
                case "urp_sprite_unlit":
                    return Array.CreateInstance(_blockDesc, 0);
                default:
                    names = new[]
                    {
                        "Vertex/P", "Vertex/N", "Vertex/T",
                        "Surface/C", "Surface/NTS", "Surface/M", "Surface/S", "Surface/E", "Surface/O",
                    };
                    break;
            }

            var list = new List<object>();
            var blockFields = _sgAsm.GetType("UnityEditor.ShaderGraph.BlockFields");
            var vd = blockFields == null ? null : blockFields.GetNestedType("VertexDescription", BindingFlags.Public | BindingFlags.NonPublic);
            var sd = blockFields == null ? null : blockFields.GetNestedType("SurfaceDescription", BindingFlags.Public | BindingFlags.NonPublic);
            foreach (var n in names)
            {
                string[] p = n.Split('/');
                Type owner = p[0] == "Vertex" ? vd : sd;
                string field = p[1] == "P" ? "Position"
                    : p[1] == "N" ? "Normal"
                    : p[1] == "T" ? "Tangent"
                    : p[1] == "C" ? "BaseColor"
                    : p[1] == "NTS" ? "NormalTS"
                    : p[1] == "M" ? "Metallic"
                    : p[1] == "S" ? "Smoothness"
                    : p[1] == "E" ? "Emission"
                    : p[1] == "O" ? "Occlusion" : p[1];
                var fi = owner == null ? null : owner.GetField(field, BindingFlags.Public | BindingFlags.Static);
                if (fi != null) list.Add(fi.GetValue(null));
            }
            var arr = Array.CreateInstance(_blockDesc, list.Count);
            for (int i = 0; i < list.Count; i++) arr.SetValue(list[i], i);
            return arr;
        }

        internal static object NewGraph()
        {
            var graph = Activator.CreateInstance(_graphData);
            if (_messageMgrProp != null && _messageMgr != null)
                _messageMgrProp.SetValue(graph, Activator.CreateInstance(_messageMgr));
            return graph;
        }

        internal static object LoadGraph(string fullPath)
        {
            if (!Available) throw new InvalidOperationException("ShaderGraph 反射层不可用: " + Reason);
            string text = File.ReadAllText(fullPath);
            var graph = NewGraph();
            _deserialize.MakeGenericMethod(_graphData)
                .Invoke(null, new object[] { graph, text, null, false });
            if (_onEnable != null) _onEnable.Invoke(graph, null);
            return graph;
        }

        internal static void SaveGraph(string assetPath, object graph)
        {
            var text = _writeGraphToDisk.Invoke(null, new object[] { assetPath, graph }) as string;
            if (string.IsNullOrEmpty(text))
                throw new IOException("写盘失败: " + assetPath);
            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport);
        }

        // ─────────────────────────────────────────────────────────
        //  节点 / 连线
        // ─────────────────────────────────────────────────────────

        internal static object GetNode(object graph, string nodeId)
        {
            if (string.IsNullOrEmpty(nodeId)) return null;
            return _getNodeFromId.Invoke(graph, new object[] { nodeId });
        }

        internal static string NodeId(object node) => (string)_objectIdProp.GetValue(node);

        internal static string AddNode(object graph, Type nodeType, float x, float y, string propertyId)
        {
            var node = Activator.CreateInstance(nodeType, true);
            if (_drawStateProp != null && _drawPosProp != null)
            {
                var draw = _drawStateProp.GetValue(node);
                _drawPosProp.SetValue(draw, new Rect(x, y, 208, 300));
                _drawStateProp.SetValue(node, draw); // DrawState 是 struct，必须写回
            }

            _addNode.Invoke(graph, _addNode.GetParameters().Length == 2
                ? new object[] { node, true }
                : new object[] { node });

            // Property 节点必须绑定黑板属性：未绑定的 Property 节点序列化后没有输出槽，
            // 下次导入会在 PropertyNode.AddOutputSlot 里抛异常，整张图报废。
            if (_propertyNode != null && _propertyNode.IsInstanceOfType(node))
            {
                if (_propNodeProp == null || _graphPropsProp == null)
                    throw new InvalidOperationException("当前 ShaderGraph 版本没暴露 PropertyNode.property，无法安全创建 Property 节点。");
                if (string.IsNullOrEmpty(propertyId))
                    throw new InvalidOperationException("Property 节点必须指定 propertyId（黑板属性的 objectId）。");
                var prop = FindProperty(graph, propertyId)
                    ?? throw new InvalidOperationException("图上没有 objectId=" + propertyId + " 的黑板属性。");
                _propNodeProp.SetValue(node, prop);
            }

            return NodeId(node);
        }

        internal static void RemoveNode(object graph, object node)
        {
            if (_removeNode == null) throw new InvalidOperationException("当前 ShaderGraph 版本没有 GraphData.RemoveNode。");
            _removeNode.Invoke(graph, new object[] { node });
        }

        /// <summary>挪动节点画布位置（只影响显示，不改图逻辑）。</summary>
        internal static bool MoveNode(object node, float x, float y)
        {
            if (_drawStateProp == null || _drawPosProp == null) return false;
            var draw = _drawStateProp.GetValue(node);
            _drawPosProp.SetValue(draw, new Rect(x, y, 208, 300));
            _drawStateProp.SetValue(node, draw);
            return true;
        }

        internal static string Connect(object graph, object outNode, int outSlot, object inNode, int inSlot)
        {
            if (_connect == null || _slotRefCtor == null)
                return "当前 ShaderGraph 版本没有 GraphData.Connect。";
            var a = _slotRefCtor.Invoke(new object[] { outNode, outSlot });
            var b = _slotRefCtor.Invoke(new object[] { inNode, inSlot });
            var edge = _connect.Invoke(graph, new object[] { a, b });
            return edge == null ? "ShaderGraph 拒绝连接（槽位类型不兼容或会形成环）。" : null;
        }

        internal static int Disconnect(object graph, string outNodeId, int outSlot, string inNodeId, int inSlot)
        {
            if (_removeEdge == null || _edgesProp == null) return 0;
            var doomed = new List<object>();
            foreach (var edge in (IEnumerable)_edgesProp.GetValue(graph))
            {
                var e = ReadEdge(edge);
                if (outNodeId != null && e.outNode != outNodeId) continue;
                if (inNodeId != null && e.inNode != inNodeId) continue;
                if (outSlot >= 0 && e.outSlot != outSlot) continue;
                if (inSlot >= 0 && e.inSlot != inSlot) continue;
                doomed.Add(edge);
            }
            foreach (var e in doomed) _removeEdge.Invoke(graph, new object[] { e });
            return doomed.Count;
        }

        static SGEdgeInfo ReadEdge(object edge)
        {
            var t = edge.GetType();
            var o = t.GetProperty("outputSlot", INST)?.GetValue(edge);
            var i = t.GetProperty("inputSlot", INST)?.GetValue(edge);
            return new SGEdgeInfo
            {
                outNode = SlotNodeId(o),
                outSlot = SlotId(o),
                inNode = SlotNodeId(i),
                inSlot = SlotId(i),
            };
        }

        static string SlotNodeId(object slotRef)
        {
            if (slotRef == null || _slotNodeProp == null) return "";
            var node = _slotNodeProp.GetValue(slotRef);
            return node == null ? "" : NodeId(node);
        }

        static int SlotId(object slotRef)
        {
            if (slotRef == null || _slotIdProp == null) return -1;
            return (int)_slotIdProp.GetValue(slotRef);
        }

        // ─────────────────────────────────────────────────────────
        //  读取
        // ─────────────────────────────────────────────────────────

        static List<object> AllNodes(object graph)
        {
            var list = new List<object>();
            var e = (IEnumerable)_getNodesGeneric.MakeGenericMethod(_absNode).Invoke(graph, null);
            foreach (var n in e) list.Add(n);
            return list;
        }

        static List<object> SlotsOf(object node)
        {
            var listType = typeof(List<>).MakeGenericType(_materialSlot);
            var list = (IList)Activator.CreateInstance(listType);
            _getSlotsGeneric.MakeGenericMethod(_materialSlot).Invoke(node, new object[] { list });
            var result = new List<object>();
            foreach (var s in list) result.Add(s);
            return result;
        }

        internal static List<SGNodeInfo> GetNodes(object graph)
        {
            var result = new List<SGNodeInfo>();
            foreach (var node in AllNodes(graph))
            {
                var info = new SGNodeInfo { id = NodeId(node), type = node.GetType().Name };
                var nameProp = node.GetType().GetProperty("name", BindingFlags.Public | BindingFlags.Instance);
                info.name = nameProp == null ? info.type : (nameProp.GetValue(node) as string ?? info.type);
                if (_drawStateProp != null && _drawPosProp != null)
                {
                    var draw = _drawStateProp.GetValue(node);
                    var rect = (Rect)_drawPosProp.GetValue(draw);
                    info.x = rect.x;
                    info.y = rect.y;
                }
                foreach (var slot in SlotsOf(node))
                {
                    var st = slot.GetType();
                    var si = new SGSlotInfo
                    {
                        id = (int)st.GetProperty("id", INST).GetValue(slot),
                        isInput = (bool)st.GetProperty("isInputSlot", INST).GetValue(slot),
                        connected = (bool)st.GetProperty("isConnected", INST).GetValue(slot),
                        type = st.GetProperty("concreteValueType", INST).GetValue(slot).ToString(),
                    };
                    si.name = RawSlotName(slot, st);
                    info.slots.Add(si);
                }
                result.Add(info);
            }
            result.Sort((a, b) => string.CompareOrdinal(a.id, b.id));
            return result;
        }

        static string RawSlotName(object slot, Type t)
        {
            var m = t.GetMethod("RawDisplayName", BindingFlags.Public | BindingFlags.Instance);
            if (m != null)
            {
                try { return m.Invoke(slot, null) as string ?? ""; } catch { }
            }
            var p = t.GetProperty("displayName", INST);
            if (p != null)
            {
                try { return p.GetValue(slot) as string ?? ""; } catch { }
            }
            return "";
        }

        internal static List<SGEdgeInfo> GetEdges(object graph)
        {
            var result = new List<SGEdgeInfo>();
            foreach (var edge in (IEnumerable)_edgesProp.GetValue(graph))
                result.Add(ReadEdge(edge));
            return result;
        }

        /// <summary>按槽位 id 或槽位显示名（大小写/空格不敏感）找槽；找不到返回 int.MinValue。</summary>
        internal static int FindSlot(object node, bool wantInput, int slotId, string slotName)
        {
            var slots = SlotsOf(node);
            foreach (var slot in slots)
            {
                var t = slot.GetType();
                if ((bool)t.GetProperty("isInputSlot", INST).GetValue(slot) != wantInput) continue;
                int id = (int)t.GetProperty("id", INST).GetValue(slot);
                if (slotId != int.MinValue) { if (id == slotId) return id; }
                else if (!string.IsNullOrEmpty(slotName))
                {
                    string raw = Normalize(RawSlotName(slot, t));
                    string want = Normalize(slotName);
                    if (raw == want || raw.StartsWith(want, StringComparison.Ordinal)) return id;
                }
                else return id; // 未指定：取该方向第一个槽
            }
            return int.MinValue;
        }

        static string Normalize(string s) => (s ?? "").Replace(" ", "").Replace("_", "").ToLowerInvariant();

        // ─────────────────────────────────────────────────────────
        //  黑板属性 / 节点类型
        // ─────────────────────────────────────────────────────────

        internal sealed class SGPropertyInfo
        {
            public string id;
            public string name;
            public string type;
        }

        static object FindProperty(object graph, string propertyId)
        {
            foreach (var p in AllProperties(graph))
                if ((string)_objectIdProp.GetValue(p) == propertyId) return p;
            return null;
        }

        /// <summary>设置黑板属性默认值。返回 null 表示成功。</summary>
        internal static string SetPropertyValue(object graph, string propertyId, string value)
        {
            var prop = FindProperty(graph, propertyId);
            if (prop == null) return "图上没有 objectId=" + propertyId + " 的黑板属性";
            var t = prop.GetType();
            var vp = t.GetProperty("value", INST);
            if (vp == null || !vp.CanWrite) return t.Name + " 没有可写的 value。";

            object converted;
            if (!TryConvert(value, vp.PropertyType, out converted))
                return "值 '" + value + "' 无法转换为 " + vp.PropertyType.Name + "（属性 " + t.Name + "）";
            vp.SetValue(prop, converted);
            return null;
        }

        static IEnumerable<object> AllProperties(object graph)
        {
            if (_graphPropsProp == null) yield break;
            var e = _graphPropsProp.GetValue(graph) as IEnumerable;
            if (e == null) yield break;
            foreach (var p in e) if (p != null) yield return p;
        }

        internal static List<SGPropertyInfo> GetProperties(object graph)
        {
            var list = new List<SGPropertyInfo>();
            foreach (var p in AllProperties(graph))
            {
                var t = p.GetType();
                var dn = t.GetProperty("displayName", BindingFlags.Public | BindingFlags.Instance);
                var nm = t.GetProperty("name", BindingFlags.Public | BindingFlags.Instance);
                list.Add(new SGPropertyInfo
                {
                    id = (string)_objectIdProp.GetValue(p),
                    name = (dn != null ? dn.GetValue(p) as string : null) ?? (nm != null ? nm.GetValue(p) as string : null) ?? t.Name,
                    type = t.Name.Replace("ShaderProperty", ""),
                });
            }
            return list;
        }

        /// <summary>往黑板加一个属性。返回 null 表示成功。</summary>
        internal static string AddProperty(object graph, string name, string type, out string id, out string finalName)
        {
            id = null;
            finalName = null;
            var propType = ResolvePropertyType(type);
            if (propType == null)
                return "未知属性类型: " + type + "（可用: float/vector2/vector3/vector4/color/bool/texture2d）";
            if (_addGraphInput == null)
                return "当前 ShaderGraph 版本没有 GraphData.AddGraphInput。";

            var prop = Activator.CreateInstance(propType, true);
            var dn = propType.GetProperty("displayName", INST) ?? propType.GetProperty("displayName", BindingFlags.Public | BindingFlags.Instance);
            if (dn != null && !string.IsNullOrEmpty(name)) dn.SetValue(prop, name);

            // AddGraphInput 会自己清洗显示名与引用名（重名/非法字符）
            _addGraphInput.Invoke(graph, new object[] { prop, -1 });

            id = (string)_objectIdProp.GetValue(prop);
            finalName = dn != null ? (dn.GetValue(prop) as string) : name;
            return null;
        }

        static Type ResolvePropertyType(string type)
        {
            string simple;
            switch ((type ?? "float").Trim().ToLowerInvariant())
            {
                case "float": case "vector1": case "scalar": simple = "Vector1ShaderProperty"; break;
                case "vector2": simple = "Vector2ShaderProperty"; break;
                case "vector3": simple = "Vector3ShaderProperty"; break;
                case "vector4": simple = "Vector4ShaderProperty"; break;
                case "color": case "colour": simple = "ColorShaderProperty"; break;
                case "bool": case "boolean": simple = "BooleanShaderProperty"; break;
                case "texture2d": case "texture": simple = "Texture2DShaderProperty"; break;
                case "texture3d": simple = "Texture3DShaderProperty"; break;
                case "cubemap": simple = "CubemapShaderProperty"; break;
                default: return null;
            }
            var t = _sgAsm.GetType("UnityEditor.ShaderGraph.Internal." + simple);
            return t != null && _abstractProperty != null && _abstractProperty.IsAssignableFrom(t) ? t : null;
        }

        static List<Type> _nodeTypes;

        internal static List<Type> NodeTypes()
        {
            if (_nodeTypes != null) return _nodeTypes;
            _nodeTypes = new List<Type>();
            if (_absNode == null) return _nodeTypes;
            Type[] all;
            try { all = _sgAsm.GetTypes(); }
            catch (ReflectionTypeLoadException e) { all = e.Types.Where(t => t != null).ToArray(); }
            foreach (var t in all)
            {
                if (t == null || t.IsAbstract || t.IsInterface || t.IsGenericTypeDefinition) continue;
                if (!_absNode.IsAssignableFrom(t)) continue;
                if (t.Name.StartsWith("<")) continue;
                _nodeTypes.Add(t);
            }
            _nodeTypes.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));
            return _nodeTypes;
        }

        internal static Type ResolveNodeType(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            name = name.Trim();
            var exact = _sgAsm.GetType(name) ?? _sgAsm.GetType("UnityEditor.ShaderGraph." + name);
            if (exact == null) exact = _sgAsm.GetType("UnityEditor.ShaderGraph." + name + "Node");
            if (exact != null && _absNode.IsAssignableFrom(exact) && !exact.IsAbstract) return exact;

            foreach (var t in NodeTypes())
            {
                if (string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase)) return t;
                if (string.Equals(t.Name, name + "Node", StringComparison.OrdinalIgnoreCase)) return t;
                if (string.Equals(t.FullName, name, StringComparison.OrdinalIgnoreCase)) return t;
            }
            return null;
        }

        internal static string NodeTitle(Type t)
        {
            try
            {
                var attr = t.GetCustomAttributes(false)
                    .FirstOrDefault(a => a.GetType().Name == "TitleAttribute" || a.GetType().Name == "Title");
                if (attr != null)
                {
                    var at = attr.GetType();
                    // 14.x：public string[] title（分类路径，如 ["Math","Basic"]）
                    var fi = at.GetField("title", INST);
                    var pi = at.GetProperty("title", INST);
                    var v = fi != null ? fi.GetValue(attr) : (pi != null ? pi.GetValue(attr) : null);
                    var arr = v as string[];
                    if (arr != null && arr.Length > 0) return string.Join("/", arr);
                    if (v is string && !string.IsNullOrEmpty((string)v)) return (string)v;
                }
            }
            catch { }
            return t.Name;
        }

        // ─────────────────────────────────────────────────────────
        //  节点字段赋值（走真实对象，不做文本替换）
        // ─────────────────────────────────────────────────────────

        internal static string SetNodeField(object node, string field, string value)
        {
            var type = node.GetType();
            for (var t = type; t != null && t != typeof(object); t = t.BaseType)
            {
                var fi = t.GetField(field, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                         ?? t.GetField(field, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.IgnoreCase);
                if (fi != null)
                {
                    object converted;
                    if (!TryConvert(value, fi.FieldType, out converted))
                        return "值 '" + value + "' 无法转换为 " + fi.FieldType.Name;
                    fi.SetValue(node, converted);
                    return null;
                }
                var pi = t.GetProperty(field, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (pi != null && pi.CanWrite)
                {
                    object converted;
                    if (!TryConvert(value, pi.PropertyType, out converted))
                        return "值 '" + value + "' 无法转换为 " + pi.PropertyType.Name;
                    pi.SetValue(node, converted);
                    return null;
                }
            }
            return "节点 " + type.Name + " 上没有可写字段/属性 '" + field + "'。";
        }

        internal static bool TryConvert(string value, Type to, out object result)
        {
            result = null;
            string v = (value ?? "").Trim().Trim('(', ')', '[', ']');
            try
            {
                if (to == typeof(string)) { result = value ?? ""; return true; }
                if (to == typeof(float)) { result = float.Parse(v, System.Globalization.CultureInfo.InvariantCulture); return true; }
                if (to == typeof(int)) { result = int.Parse(v, System.Globalization.CultureInfo.InvariantCulture); return true; }
                if (to == typeof(bool)) { result = v == "1" || v.Equals("true", StringComparison.OrdinalIgnoreCase); return true; }
                if (to.IsEnum) { result = Enum.Parse(to, v, true); return true; }

                var parts = v.Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(s => float.Parse(s, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
                if (to == typeof(Vector2) && parts.Length >= 2) { result = new Vector2(parts[0], parts[1]); return true; }
                if (to == typeof(Vector3) && parts.Length >= 3) { result = new Vector3(parts[0], parts[1], parts[2]); return true; }
                if (to == typeof(Vector4) && parts.Length >= 4) { result = new Vector4(parts[0], parts[1], parts[2], parts[3]); return true; }
                if (to == typeof(Color) && parts.Length >= 3)
                {
                    result = new Color(parts[0], parts[1], parts[2], parts.Length >= 4 ? parts[3] : 1f);
                    return true;
                }
            }
            catch { }
            return false;
        }
    }
}
