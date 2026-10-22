// ═══════════════════════════════════════════════════════════════
//  RequestLog — 请求级日志与统计
//
//  每个管道请求记一条：type / 是否成功 / 耗时 / 客户端标识 / 撤销组。
//  只保留最近 100 条（环形队列），stats 消息把它吐成 JSON，供 unity_status 显示。
//  多客户端同时连时，靠 client 字段区分“谁在改”。
// ═══════════════════════════════════════════════════════════════

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Framework.XYEditor.Bridge
{
    internal static class RequestLog
    {
        const int MaxEntries = 100;

        internal sealed class Entry
        {
            public string type;
            public bool ok;
            public double ms;
            public string client;
            public string at;
        }

        static readonly Queue<Entry> _entries = new Queue<Entry>(MaxEntries);
        static readonly object _lock = new object();
        static readonly HashSet<string> _clients = new HashSet<string>();

        static long _total;
        static long _fail;
        static double _sumMs;
        static double _lastMs;
        static string _lastType = "";
        static string _firstAt = "";

        internal static void Record(string type, bool ok, double ms, string client)
        {
            lock (_lock)
            {
                _total++;
                if (!ok) _fail++;
                _sumMs += ms;
                _lastMs = ms;
                _lastType = type ?? "";
                if (string.IsNullOrEmpty(_firstAt)) _firstAt = Now();
                if (!string.IsNullOrEmpty(client)) _clients.Add(client);

                _entries.Enqueue(new Entry
                {
                    type = type ?? "",
                    ok = ok,
                    ms = ms,
                    client = client ?? "",
                    at = Now(),
                });
                while (_entries.Count > MaxEntries) _entries.Dequeue();
            }
        }

        static string Now() => DateTime.Now.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture);

        internal static void Reset()
        {
            lock (_lock)
            {
                _entries.Clear();
                _clients.Clear();
                _total = 0;
                _fail = 0;
                _sumMs = 0;
                _lastMs = 0;
                _lastType = "";
                _firstAt = "";
            }
        }

        /// <summary>JSON：总计数 + 最近 N 条。</summary>
        internal static string ToJson(int recent = 15)
        {
            lock (_lock)
            {
                var sb = new StringBuilder(1024);
                sb.Append("{\"ok\":true");
                sb.Append(",\"total\":").Append(_total);
                sb.Append(",\"fail\":").Append(_fail);
                sb.Append(",\"avgMs\":").Append(F(_total > 0 ? _sumMs / _total : 0));
                sb.Append(",\"lastMs\":").Append(F(_lastMs));
                sb.Append(",\"lastType\":").Append(XyJ.Q(_lastType));
                sb.Append(",\"since\":").Append(XyJ.Q(_firstAt));
                sb.Append(",\"clientCount\":").Append(_clients.Count);
                sb.Append(",\"clients\":[");
                int ci = 0;
                foreach (var c in _clients)
                {
                    if (ci++ > 0) sb.Append(',');
                    sb.Append(XyJ.Q(c));
                }
                sb.Append("],\"recent\":[");

                int skip = Math.Max(0, _entries.Count - Math.Max(1, recent));
                int i = 0;
                foreach (var e in _entries)
                {
                    if (i++ < skip) continue;
                    if (i - 1 > skip) sb.Append(',');
                    sb.Append("{\"type\":").Append(XyJ.Q(e.type))
                      .Append(",\"ok\":").Append(XyJ.B(e.ok))
                      .Append(",\"ms\":").Append(F(e.ms))
                      .Append(",\"client\":").Append(XyJ.Q(e.client))
                      .Append(",\"at\":").Append(XyJ.Q(e.at)).Append('}');
                }
                sb.Append("]}");
                return sb.ToString();
            }
        }

        static string F(double d) => d.ToString("0.##", CultureInfo.InvariantCulture);
    }
}
