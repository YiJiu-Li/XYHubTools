// ═══════════════════════════════════════════════════════════════
//  共享类型定义 — PipeEnvelope / ScriptGlobals / ProgressSnapshot
// ═══════════════════════════════════════════════════════════════

using System;
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
            if (obj == null)
            {
                _output.AppendLine("null");
                return;
            }
            try
            {
                string json;
                if (obj is UnityEngine.Object uObj)
                    json = EditorJsonUtility.ToJson(uObj, true);
                else
                    json = JsonUtility.ToJson(obj, true);
                _output.AppendLine(json);
            }
            catch (Exception ex)
            {
                _output
                    .Append("[printJson error: ")
                    .Append(ex.Message)
                    .Append("] ")
                    .AppendLine(obj.ToString());
            }
        }

        public void clear()
        {
            _output.Length = 0;
        }

        public string GetOutput()
        {
            return _output.ToString();
        }
    }
}
