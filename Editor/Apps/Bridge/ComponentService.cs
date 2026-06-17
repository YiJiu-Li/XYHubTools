// ═══════════════════════════════════════════════════════════════
//  ComponentService — 组件读写 + Prefab 结构分析
// ═══════════════════════════════════════════════════════════════

using System;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Framework.XYEditor.Bridge
{
    internal static class ComponentService
    {
        // ═══════════════════════════════════════════════════════════
        //  get_component — 读取 GameObject 组件属性
        //  message 格式: "目标名称|可选组件类型"
        // ═══════════════════════════════════════════════════════════

        internal static string GetComponent(string targetAndType)
        {
            var parts = (targetAndType ?? "").Split('|');
            string targetName = parts.Length > 0 ? parts[0].Trim() : "";
            string typeName = parts.Length > 1 ? parts[1].Trim() : "";

            var go = FindGameObject(targetName);
            if (go == null)
                return "{\"error\":\"GameObject not found: "
                    + targetName.Replace("\"", "\\\"")
                    + "\"}";

            Component[] components;
            if (!string.IsNullOrEmpty(typeName))
            {
                var wantedType = ResolveComponentType(typeName);
                var comp = wantedType != null
                    ? go.GetComponent(wantedType)
                    : go.GetComponent(typeName);
                if (comp == null)
                    return "{\"error\":\"Component not found: "
                        + typeName.Replace("\"", "\\\"")
                        + "\"}";
                components = new[] { comp };
            }
            else
            {
                components = go.GetComponents<Component>();
            }

            var result = new StringBuilder();
            result.Append("{\"gameObject\":\"");
            result.Append(go.name);
            result.Append("\",\"instanceId\":");
            result.Append(go.GetInstanceID());
            result.Append(",\"components\":[");

            bool first = true;
            foreach (var c in components)
            {
                if (c == null)
                    continue;
                if (!first)
                    result.Append(',');
                first = false;
                AppendComponentJson(result, c);
            }
            result.Append("]}");
            return result.ToString();
        }

        private static void AppendComponentJson(StringBuilder sb, Component comp)
        {
            var so = new SerializedObject(comp);
            sb.Append("{\"type\":\"");
            sb.Append(comp.GetType().FullName);
            sb.Append("\",\"properties\":{");

            var prop = so.GetIterator();
            bool firstProp = true;
            bool enterChildren = true;
            while (prop.NextVisible(enterChildren))
            {
                enterChildren = false;
                // Skip script reference and m_ObjectHideFlags
                if (prop.name == "m_Script" || prop.name == "m_ObjectHideFlags")
                    continue;
                if (prop.depth > 2)
                    continue;

                if (!firstProp)
                    sb.Append(',');
                firstProp = false;

                sb.Append('"');
                sb.Append(prop.name);
                sb.Append("\":");
                AppendSerializedValue(sb, prop);
            }
            sb.Append("}}");
        }

        private static void AppendSerializedValue(StringBuilder sb, SerializedProperty prop)
        {
            try
            {
                switch (prop.propertyType)
                {
                    case SerializedPropertyType.Boolean:
                        sb.Append(prop.boolValue ? "true" : "false");
                        break;
                    case SerializedPropertyType.Integer:
                    case SerializedPropertyType.Enum:
                        sb.Append(prop.intValue);
                        break;
                    case SerializedPropertyType.Float:
                        sb.Append(
                            prop.floatValue.ToString(
                                System.Globalization.CultureInfo.InvariantCulture
                            )
                        );
                        break;
                    case SerializedPropertyType.String:
                        sb.Append('"');
                        sb.Append(EscapeJson(prop.stringValue ?? ""));
                        sb.Append('"');
                        break;
                    case SerializedPropertyType.Vector2:
                        var v2 = prop.vector2Value;
                        sb.Append("{\"x\":")
                            .Append(
                                v2.x.ToString(System.Globalization.CultureInfo.InvariantCulture)
                            )
                            .Append(",\"y\":")
                            .Append(
                                v2.y.ToString(System.Globalization.CultureInfo.InvariantCulture)
                            )
                            .Append('}');
                        break;
                    case SerializedPropertyType.Vector3:
                        var v3 = prop.vector3Value;
                        sb.Append("{\"x\":")
                            .Append(
                                v3.x.ToString(System.Globalization.CultureInfo.InvariantCulture)
                            )
                            .Append(",\"y\":")
                            .Append(
                                v3.y.ToString(System.Globalization.CultureInfo.InvariantCulture)
                            )
                            .Append(",\"z\":")
                            .Append(
                                v3.z.ToString(System.Globalization.CultureInfo.InvariantCulture)
                            )
                            .Append('}');
                        break;
                    case SerializedPropertyType.Vector4:
                        var v4 = prop.vector4Value;
                        sb.Append("{\"x\":")
                            .Append(
                                v4.x.ToString(System.Globalization.CultureInfo.InvariantCulture)
                            )
                            .Append(",\"y\":")
                            .Append(
                                v4.y.ToString(System.Globalization.CultureInfo.InvariantCulture)
                            )
                            .Append(",\"z\":")
                            .Append(
                                v4.z.ToString(System.Globalization.CultureInfo.InvariantCulture)
                            )
                            .Append(",\"w\":")
                            .Append(
                                v4.w.ToString(System.Globalization.CultureInfo.InvariantCulture)
                            )
                            .Append('}');
                        break;
                    case SerializedPropertyType.Quaternion:
                        var q = prop.quaternionValue;
                        sb.Append("{\"x\":")
                            .Append(q.x.ToString(System.Globalization.CultureInfo.InvariantCulture))
                            .Append(",\"y\":")
                            .Append(q.y.ToString(System.Globalization.CultureInfo.InvariantCulture))
                            .Append(",\"z\":")
                            .Append(q.z.ToString(System.Globalization.CultureInfo.InvariantCulture))
                            .Append(",\"w\":")
                            .Append(q.w.ToString(System.Globalization.CultureInfo.InvariantCulture))
                            .Append('}');
                        break;
                    case SerializedPropertyType.Color:
                        var c = prop.colorValue;
                        sb.Append("{\"r\":")
                            .Append(c.r.ToString(System.Globalization.CultureInfo.InvariantCulture))
                            .Append(",\"g\":")
                            .Append(c.g.ToString(System.Globalization.CultureInfo.InvariantCulture))
                            .Append(",\"b\":")
                            .Append(c.b.ToString(System.Globalization.CultureInfo.InvariantCulture))
                            .Append(",\"a\":")
                            .Append(c.a.ToString(System.Globalization.CultureInfo.InvariantCulture))
                            .Append('}');
                        break;
                    case SerializedPropertyType.ObjectReference:
                        if (prop.objectReferenceValue != null)
                        {
                            sb.Append("{\"name\":\"");
                            sb.Append(EscapeJson(prop.objectReferenceValue.name));
                            sb.Append("\",\"type\":\"");
                            sb.Append(prop.objectReferenceValue.GetType().Name);
                            sb.Append("\",\"instanceId\":");
                            sb.Append(prop.objectReferenceInstanceIDValue);
                            sb.Append('}');
                        }
                        else
                        {
                            sb.Append("null");
                        }
                        break;
                    case SerializedPropertyType.AnimationCurve:
                        sb.Append("\"<AnimationCurve>\"");
                        break;
                    default:
                        sb.Append("\"<");
                        sb.Append(prop.propertyType.ToString());
                        sb.Append(">\"");
                        break;
                }
            }
            catch
            {
                sb.Append("null");
            }
        }

        // ═══════════════════════════════════════════════════════════
        //  set_property — 修改序列化字段
        //  message 格式: "目标名称|属性路径|值"
        // ═══════════════════════════════════════════════════════════

        internal static string SetProperty(string spec)
        {
            var parts = (spec ?? "").Split('|');
            if (parts.Length < 3)
                return "{\"error\":\"usage: targetName|propertyPath|value\"}";

            string targetName = parts[0].Trim();
            string propPath = parts[1].Trim();
            string valueStr = parts[2].Trim();

            var go = FindGameObject(targetName);
            if (go == null)
                return "{\"error\":\"GameObject not found: "
                    + targetName.Replace("\"", "\\\"")
                    + "\"}";

            // 解析 "ComponentType.field..." 前缀，缩小查找范围
            Type wantedType = null;
            string fieldPath = propPath;
            int dot = propPath.IndexOf('.');
            if (dot > 0)
            {
                string typeName = propPath.Substring(0, dot).Trim();
                fieldPath = propPath.Substring(dot + 1).Trim();
                wantedType = ResolveComponentType(typeName);
                if (wantedType == null)
                    return "{\"error\":\"component type not found: "
                        + typeName.Replace("\"", "\\\"")
                        + "\"}";
            }

            var components = go.GetComponents<Component>();
            foreach (var c in components)
            {
                if (c == null)
                    continue;
                if (wantedType != null && !wantedType.IsInstanceOfType(c))
                    continue;

                var so = new SerializedObject(c);
                var prop = ResolveSerializedProperty(so, fieldPath);
                if (prop == null)
                    continue;

                Undo.RecordObject(c, "XY Bridge set_property");
                try
                {
                    SetSerializedValue(prop, valueStr);
                    so.ApplyModifiedProperties();
                    return "{\"ok\":true,\"target\":\""
                        + EscapeJson(go.name)
                        + "\",\"component\":\""
                        + EscapeJson(c.GetType().Name)
                        + "\",\"property\":\""
                        + EscapeJson(propPath)
                        + "\"}";
                }
                catch (Exception ex)
                {
                    return "{\"error\":\"" + ex.Message.Replace("\"", "\\\"") + "\"}";
                }
            }
            return "{\"error\":\"property not found: " + propPath.Replace("\"", "\\\"") + "\"}";
        }

        // ── 辅助：把 "Transform" / "UnityEngine.Transform" 解析成 Type ──
        private static Type ResolveComponentType(string name)
        {
            // 1) 直接在所有已加载程序集里找
            foreach (var asm in System.AppDomain.CurrentDomain.GetAssemblies())
            {
                Type t = asm.GetType(name, false);
                if (t != null)
                    return t;
            }
            // 2) 仅写短名时补常见命名空间
            if (!name.Contains("."))
            {
                string[] prefixes =
                {
                    "UnityEngine.",
                    "UnityEngine.UI.",
                    "UnityEditor.",
                    "UnityEngine.Rendering.Universal.",
                };
                foreach (var p in prefixes)
                {
                    foreach (var asm in System.AppDomain.CurrentDomain.GetAssemblies())
                    {
                        Type t = asm.GetType(p + name, false);
                        if (t != null)
                            return t;
                    }
                }
            }
            return null;
        }

        // ── 辅助：把用户写的友好字段名 (localPosition) 映射到序列化字段 (m_LocalPosition) ──
        private static SerializedProperty ResolveSerializedProperty(
            SerializedObject so,
            string userPath
        )
        {
            if (string.IsNullOrEmpty(userPath))
                return null;

            // 1) 原样查找
            var p = so.FindProperty(userPath);
            if (p != null)
                return p;

            // 2) 友好名 → 序列化名（首段加 m_ 前缀）
            //    localPosition.x          -> m_LocalPosition.x
            //    localRotation.w          -> m_LocalRotation.w
            //    localScale.x             -> m_LocalScale.x
            int dot = userPath.IndexOf('.');
            string first = dot > 0 ? userPath.Substring(0, dot) : userPath;
            string rest = dot > 0 ? userPath.Substring(dot) : "";
            if (char.IsLower(first[0]))
            {
                string mapped = "m_" + char.ToUpper(first[0]) + first.Substring(1);
                string altPath = mapped + rest;
                p = so.FindProperty(altPath);
                if (p != null)
                    return p;
            }

            // 3) 整个路径加 m_ 前缀
            if (char.IsLower(userPath[0]))
            {
                string mappedAll = "m_" + char.ToUpper(userPath[0]) + userPath.Substring(1);
                p = so.FindProperty(mappedAll);
                if (p != null)
                    return p;
            }

            return null;
        }

        private static void SetSerializedValue(SerializedProperty prop, string value)
        {
            switch (prop.propertyType)
            {
                case SerializedPropertyType.Boolean:
                    prop.boolValue = bool.Parse(value);
                    break;
                case SerializedPropertyType.Integer:
                    prop.intValue = int.Parse(value);
                    break;
                case SerializedPropertyType.Float:
                    prop.floatValue = float.Parse(
                        value,
                        System.Globalization.CultureInfo.InvariantCulture
                    );
                    break;
                case SerializedPropertyType.String:
                    prop.stringValue = value;
                    break;
                case SerializedPropertyType.Enum:
                    prop.intValue = int.Parse(value);
                    break;
                case SerializedPropertyType.ObjectReference:
                    if (value == "null" || value == "")
                    {
                        prop.objectReferenceValue = null;
                    }
                    else
                    {
                        // Try by name first
                        var obj = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(value);
                        if (obj == null)
                        {
                            // Try by asset name
                            var guids = AssetDatabase.FindAssets(value);
                            if (guids.Length > 0)
                                obj = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(
                                    AssetDatabase.GUIDToAssetPath(guids[0])
                                );
                        }
                        prop.objectReferenceValue = obj;
                    }
                    break;
                case SerializedPropertyType.Vector2:
                    var v2parts = value.Split(',');
                    prop.vector2Value = new Vector2(
                        float.Parse(
                            v2parts[0].Trim(),
                            System.Globalization.CultureInfo.InvariantCulture
                        ),
                        float.Parse(
                            v2parts[1].Trim(),
                            System.Globalization.CultureInfo.InvariantCulture
                        )
                    );
                    break;
                case SerializedPropertyType.Vector3:
                    var v3parts = value.Split(',');
                    prop.vector3Value = new Vector3(
                        float.Parse(
                            v3parts[0].Trim(),
                            System.Globalization.CultureInfo.InvariantCulture
                        ),
                        float.Parse(
                            v3parts[1].Trim(),
                            System.Globalization.CultureInfo.InvariantCulture
                        ),
                        float.Parse(
                            v3parts[2].Trim(),
                            System.Globalization.CultureInfo.InvariantCulture
                        )
                    );
                    break;
                case SerializedPropertyType.Color:
                    ColorUtility.TryParseHtmlString(value, out var color);
                    prop.colorValue = color;
                    break;
                default:
                    throw new NotSupportedException("Unsupported type: " + prop.propertyType);
            }
        }

        // ═══════════════════════════════════════════════════════════
        //  get_prefab_structure — 读取 Prefab 内部结构
        //  message: Prefab 资源路径
        // ═══════════════════════════════════════════════════════════

        internal static string GetPrefabStructure(string assetPath)
        {
            if (string.IsNullOrWhiteSpace(assetPath))
                return "{\"error\":\"asset path required\"}";

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
            if (prefab == null)
            {
                var guids = AssetDatabase.FindAssets(assetPath);
                if (guids.Length > 0 && AssetDatabase.GUIDToAssetPath(guids[0]).EndsWith(".prefab"))
                    prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                        AssetDatabase.GUIDToAssetPath(guids[0])
                    );
            }
            if (prefab == null)
                return "{\"error\":\"prefab not found: " + assetPath.Replace("\"", "\\\"") + "\"}";

            var sb = new StringBuilder();
            sb.Append("{\"name\":\"");
            sb.Append(EscapeJson(prefab.name));
            sb.Append("\",\"path\":\"");
            sb.Append(EscapeJson(assetPath));
            sb.Append("\",\"children\":[");
            AppendChildInfo(sb, prefab.transform, true);
            sb.Append("]}");
            return sb.ToString();
        }

        private static void AppendChildInfo(StringBuilder sb, Transform parent, bool isFirst)
        {
            for (int i = 0; i < parent.childCount; i++)
            {
                if (i > 0 || !isFirst)
                    sb.Append(',');
                var child = parent.GetChild(i);
                var comps = child.GetComponents<Component>();
                sb.Append("{\"name\":\"");
                sb.Append(EscapeJson(child.name));
                sb.Append("\",\"components\":[");
                bool firstComp = true;
                foreach (var c in comps)
                {
                    if (c == null)
                        continue;
                    if (!firstComp)
                        sb.Append(',');
                    firstComp = false;
                    sb.Append('"');
                    sb.Append(EscapeJson(c.GetType().Name));
                    sb.Append('"');
                }
                sb.Append("],\"children\":[");
                AppendChildInfo(sb, child, true);
                sb.Append("]}");
            }
        }

        // ═══════════════════════════════════════════════════════════
        //  工具
        // ═══════════════════════════════════════════════════════════

        private static GameObject FindGameObject(string target)
        {
            if (string.IsNullOrEmpty(target))
                return null;

            // Try by name
            var go = GameObject.Find(target);
            if (go != null)
                return go;

            // Try Selection
            if (Selection.activeGameObject != null)
            {
                if (Selection.activeGameObject.name == target)
                    return Selection.activeGameObject;
                // Try child
                var t = Selection.activeGameObject.transform.Find(target);
                if (t != null)
                    return t.gameObject;
            }

            // Try by instance ID
            if (int.TryParse(target, out int instanceId))
            {
                var obj = EditorUtility.InstanceIDToObject(instanceId);
                if (obj is GameObject g)
                    return g;
            }

            return null;
        }

        private static string EscapeJson(string s)
        {
            if (string.IsNullOrEmpty(s))
                return "";
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"");
        }
    }
}
