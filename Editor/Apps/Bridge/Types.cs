// ═══════════════════════════════════════════════════════════════
//  共享类型定义 — PipeEnvelope / ScriptGlobals / ProgressSnapshot
// ═══════════════════════════════════════════════════════════════

using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Framework.XYEditor.Bridge
{
    /// <summary>Pipe 消息信封</summary>
    [Serializable]
    internal class PipeEnvelope
    {
        public string id = "";
        public string reply_to = "";
        public string type = "";
        public bool ok;
        public string message = "";
        public string error = "";
        public int processId;
        public string processPath = "";
    }

    /// <summary>代码执行进度快照</summary>
    [Serializable]
    internal class ExecuteCodeProgressSnapshot
    {
        public bool active;
        public string title;
        public string info;
        public float progress;
        public int revision;
        public string source;
    }

    /// <summary>暴露给用户代码片段的全局函数</summary>
    public sealed class ScriptGlobals
    {
        private readonly StringBuilder _output = new StringBuilder(256);
        private readonly Action _touchActivity;

        public ScriptGlobals()
            : this(null) { }

        public ScriptGlobals(Action touchActivity)
        {
            _touchActivity = touchActivity;
        }

        private void TouchActivity()
        {
            try
            {
                if (_touchActivity != null)
                    _touchActivity();
            }
            catch { }
        }

        public void print(object obj)
        {
            TouchActivity();
            _output.AppendLine(obj != null ? obj.ToString() : "null");
        }

        public void printJson(object obj)
        {
            TouchActivity();
            _output.AppendLine(ToJson(obj));
        }

        public void clear()
        {
            _output.Length = 0;
        }

        public string GetOutput()
        {
            return _output.ToString();
        }

        private static string ToJson(object value)
        {
            var sb = new StringBuilder(256);
            var seen = new HashSet<int>();
            AppendJsonValue(sb, value, seen);
            return sb.ToString();
        }

        private static void AppendJsonValue(StringBuilder sb, object value, HashSet<int> seen)
        {
            if (value == null)
            {
                sb.Append("null");
                return;
            }

            if (value is string s)
            {
                AppendEscapedString(sb, s);
                return;
            }

            if (value is char ch)
            {
                AppendEscapedString(sb, ch.ToString());
                return;
            }

            if (value is bool b)
            {
                sb.Append(b ? "true" : "false");
                return;
            }

            if (value is Enum)
            {
                AppendEscapedString(sb, value.ToString());
                return;
            }

            if (value is UnityEngine.Object uObj)
            {
                try
                {
                    sb.Append(EditorJsonUtility.ToJson(uObj, true));
                }
                catch
                {
                    AppendEscapedString(sb, uObj.name ?? uObj.GetType().Name);
                }
                return;
            }

            var type = value.GetType();
            if (IsSimpleNumber(type))
            {
                AppendNumber(sb, value);
                return;
            }

            if (value is DateTime dt)
            {
                AppendEscapedString(sb, dt.ToString("o", CultureInfo.InvariantCulture));
                return;
            }

            if (value is IDictionary dict)
            {
                int id = RuntimeHelpers.GetHashCode(value);
                if (!seen.Add(id))
                {
                    AppendEscapedString(sb, "<cycle>");
                    return;
                }
                sb.Append('{');
                bool first = true;
                foreach (DictionaryEntry entry in dict)
                {
                    if (!first)
                        sb.Append(',');
                    first = false;
                    AppendEscapedString(sb, entry.Key != null ? entry.Key.ToString() : "");
                    sb.Append(':');
                    AppendJsonValue(sb, entry.Value, seen);
                }
                sb.Append('}');
                seen.Remove(id);
                return;
            }

            if (value is IEnumerable enumerable)
            {
                int id = RuntimeHelpers.GetHashCode(value);
                if (!seen.Add(id))
                {
                    AppendEscapedString(sb, "<cycle>");
                    return;
                }
                sb.Append('[');
                bool first = true;
                foreach (var item in enumerable)
                {
                    if (!first)
                        sb.Append(',');
                    first = false;
                    AppendJsonValue(sb, item, seen);
                }
                sb.Append(']');
                seen.Remove(id);
                return;
            }

            int objId = RuntimeHelpers.GetHashCode(value);
            if (!seen.Add(objId))
            {
                AppendEscapedString(sb, "<cycle>");
                return;
            }

            sb.Append('{');
            bool firstMember = true;
            foreach (var field in type.GetFields(BindingFlags.Instance | BindingFlags.Public))
            {
                if (!firstMember)
                    sb.Append(',');
                firstMember = false;
                AppendEscapedString(sb, field.Name);
                sb.Append(':');
                AppendJsonValue(sb, field.GetValue(value), seen);
            }

            foreach (var prop in type.GetProperties(BindingFlags.Instance | BindingFlags.Public))
            {
                if (!prop.CanRead || prop.GetIndexParameters().Length > 0)
                    continue;
                object propValue;
                try
                {
                    propValue = prop.GetValue(value, null);
                }
                catch
                {
                    continue;
                }
                if (!firstMember)
                    sb.Append(',');
                firstMember = false;
                AppendEscapedString(sb, prop.Name);
                sb.Append(':');
                AppendJsonValue(sb, propValue, seen);
            }
            sb.Append('}');
            seen.Remove(objId);
        }

        private static bool IsSimpleNumber(Type type)
        {
            return type == typeof(byte)
                || type == typeof(sbyte)
                || type == typeof(short)
                || type == typeof(ushort)
                || type == typeof(int)
                || type == typeof(uint)
                || type == typeof(long)
                || type == typeof(ulong)
                || type == typeof(float)
                || type == typeof(double)
                || type == typeof(decimal);
        }

        private static void AppendNumber(StringBuilder sb, object value)
        {
            if (value is IFormattable formattable)
                sb.Append(formattable.ToString(null, CultureInfo.InvariantCulture));
            else
                sb.Append(value.ToString());
        }

        private static void AppendEscapedString(StringBuilder sb, string s)
        {
            sb.Append('"');
            if (!string.IsNullOrEmpty(s))
            {
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
                        case '\b':
                            sb.Append("\\b");
                            break;
                        case '\f':
                            sb.Append("\\f");
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
                            if (c < 32)
                                sb.Append("\\u").Append(((int)c).ToString("x4"));
                            else
                                sb.Append(c);
                            break;
                    }
                }
            }
            sb.Append('"');
        }
    }
}
