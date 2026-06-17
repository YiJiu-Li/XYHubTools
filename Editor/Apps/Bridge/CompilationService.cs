// ═══════════════════════════════════════════════════════════════
//  Roslyn 编译辅助 — MetadataReference 收集、缓存、诊断格式化
// ═══════════════════════════════════════════════════════════════

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using UnityEditor;
using UnityEngine;
using Assembly = System.Reflection.Assembly;

namespace Framework.XYEditor.Bridge
{
    /// <summary>
    /// Roslyn 编译基础设施。
    /// 负责收集和管理 MetadataReference 缓存、动态编译的诊断格式化。
    /// </summary>
    internal static class CompilationService
    {
        internal static readonly object CompileCacheLock = new object();
        internal static List<MetadataReference> CachedReferences;
        internal static bool ReferencesReady;

        /// <summary>失效编译缓存（脚本重编译后调用）</summary>
        internal static void InvalidateCache()
        {
            lock (CompileCacheLock)
            {
                ReferencesReady = false;
                CachedReferences = null;
            }
        }

        /// <summary>确保 MetadataReference 缓存就绪</summary>
        internal static async Task<string> EnsureReadyAsync(
            Action<Action> postToMainThread,
            Action<string> reportStage,
            int executeTimeoutMs,
            CancellationToken cancellationToken
        )
        {
            ThrowIfCanceled(cancellationToken);
            Report(reportStage, "Checking compiler cache");
            lock (CompileCacheLock)
            {
                if (ReferencesReady && CachedReferences != null)
                {
                    Report(reportStage, "Compiler cache ready");
                    return null;
                }
            }

            var tcs = new TaskCompletionSource<string>();
            Report(reportStage, "Waiting for Unity main thread");
            postToMainThread(() =>
            {
                try
                {
                    ThrowIfCanceled(cancellationToken);
                    EnsureReferences(reportStage, cancellationToken);
                    tcs.TrySetResult(null);
                }
                catch (OperationCanceledException)
                {
                    tcs.TrySetResult("execute_code canceled");
                }
                catch (Exception ex)
                {
                    tcs.TrySetResult("prepare execute_code failed: " + ex.Message);
                }
            });

            var delayTask = Task.Delay(executeTimeoutMs, cancellationToken);
            var completed = await Task.WhenAny(tcs.Task, delayTask);
            if (cancellationToken.IsCancellationRequested)
                return "execute_code canceled";
            if (completed != tcs.Task)
                return "prepare execute_code timed out";
            return tcs.Task.Result;
        }

        internal static void Report(Action<string> reportStage, string stage)
        {
            if (reportStage == null || string.IsNullOrEmpty(stage))
                return;
            try
            {
                reportStage(stage);
            }
            catch { }
        }

        internal static void ThrowIfCanceled(CancellationToken ct)
        {
            if (ct.IsCancellationRequested)
                throw new OperationCanceledException(ct);
        }

        /// <summary>获取或构建 MetadataReference 列表</summary>
        internal static List<MetadataReference> EnsureReferences(
            Action<string> reportStage = null,
            CancellationToken ct = default
        )
        {
            ThrowIfCanceled(ct);
            Report(reportStage, "Locking compiler reference cache");
            lock (CompileCacheLock)
            {
                ThrowIfCanceled(ct);
                if (ReferencesReady && CachedReferences != null)
                {
                    Report(reportStage, "Compiler reference cache ready");
                    return CachedReferences;
                }
                CachedReferences = BuildReferences(typeof(YZJBridge).Assembly, reportStage, ct);
                ReferencesReady = true;
                Report(reportStage, "Compiler reference cache ready");
                return CachedReferences;
            }
        }

        /// <summary>构建完整 MetadataReference 列表</summary>
        internal static List<MetadataReference> BuildReferences(
            Assembly bridgeAssembly,
            Action<string> reportStage = null,
            CancellationToken ct = default
        ) => ReferenceBuilder.Build(bridgeAssembly, reportStage, ct);

        /// <summary>分离代码开头的 using 块和实际代码体</summary>
        internal static void SplitUsings(string code, out string usings, out string body)
        {
            if (string.IsNullOrEmpty(code))
            {
                usings = "";
                body = "";
                return;
            }
            var normalized = code.Replace("\r\n", "\n");
            var lines = normalized.Split('\n');
            var u = new StringBuilder();
            var b = new StringBuilder();
            var inUsings = true;

            foreach (var line in lines)
            {
                var t = line.Trim();
                if (inUsings)
                {
                    if (string.IsNullOrEmpty(t))
                    {
                        if (u.Length > 0)
                            u.AppendLine(line);
                        else
                            b.AppendLine(line);
                        continue;
                    }
                    if (
                        t.StartsWith("using ", StringComparison.Ordinal)
                        && t.EndsWith(";", StringComparison.Ordinal)
                    )
                    {
                        u.AppendLine(line);
                        continue;
                    }
                    inUsings = false;
                }
                b.AppendLine(line);
            }
            usings = u.ToString().TrimEnd();
            body = b.ToString().TrimEnd();
        }

        /// <summary>将 Roslyn 诊断错误格式化为可读文本</summary>
        internal static string FormatErrors(IEnumerable<Diagnostic> diagnostics)
        {
            if (diagnostics == null)
                return null;
            var sb = new StringBuilder();
            var hasError = false;

            foreach (var d in diagnostics)
            {
                if (d == null || d.Severity != DiagnosticSeverity.Error)
                    continue;
                if (!hasError)
                {
                    hasError = true;
                    sb.Append("compilation failed:\n");
                }
                int line = 0,
                    col = 0;
                try
                {
                    var span = d.Location.GetMappedLineSpan();
                    line = span.StartLinePosition.Line + 1;
                    col = span.StartLinePosition.Character + 1;
                }
                catch { }
                sb.Append("  ")
                    .Append(d.Id)
                    .Append(" at ")
                    .Append(line)
                    .Append(":")
                    .Append(col)
                    .Append(": ")
                    .Append(d.GetMessage())
                    .Append("\n");
            }
            return hasError ? sb.ToString() : null;
        }

        /// <summary>收集当前 Unity 项目的预处理器符号</summary>
        internal static string[] BuildPreprocessorSymbols()
        {
            var symbols = new HashSet<string>(StringComparer.Ordinal) { "UNITY_EDITOR" };
#if UNITY_EDITOR_WIN
            symbols.Add("UNITY_EDITOR_WIN");
            symbols.Add("UNITY_STANDALONE_WIN");
#endif
#if UNITY_EDITOR_OSX
            symbols.Add("UNITY_EDITOR_OSX");
            symbols.Add("UNITY_STANDALONE_OSX");
#endif
#if UNITY_EDITOR_LINUX
            symbols.Add("UNITY_EDITOR_LINUX");
            symbols.Add("UNITY_STANDALONE_LINUX");
#endif
            AddUnityVersionSymbols(symbols);
#if UNITY_2020
            symbols.Add("UNITY_2020");
#endif
#if UNITY_2021
            symbols.Add("UNITY_2021");
#endif
#if UNITY_2022
            symbols.Add("UNITY_2022");
#endif
#if UNITY_2023
            symbols.Add("UNITY_2023");
#endif
#if UNITY_6000_0_OR_NEWER
            symbols.Add("UNITY_6000_0_OR_NEWER");
#endif
#if UNITY_2020_3_OR_NEWER
            symbols.Add("UNITY_2020_3_OR_NEWER");
#endif
#if UNITY_2021_3_OR_NEWER
            symbols.Add("UNITY_2021_3_OR_NEWER");
#endif
#if UNITY_2022_3_OR_NEWER
            symbols.Add("UNITY_2022_3_OR_NEWER");
#endif
#if UNITY_2023_1_OR_NEWER
            symbols.Add("UNITY_2023_1_OR_NEWER");
#endif
#if ENABLE_INPUT_SYSTEM
            symbols.Add("ENABLE_INPUT_SYSTEM");
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
            symbols.Add("ENABLE_LEGACY_INPUT_MANAGER");
#endif

            try
            {
                var group = EditorUserBuildSettings.selectedBuildTargetGroup;
                var raw = PlayerSettings.GetScriptingDefineSymbolsForGroup(group);
                if (!string.IsNullOrEmpty(raw))
                    foreach (var s in raw.Split(';'))
                    {
                        var sym = s.Trim();
                        if (!string.IsNullOrEmpty(sym))
                            symbols.Add(sym);
                    }
            }
            catch { }

            var list = new List<string>(symbols);
            list.Sort(StringComparer.Ordinal);
            return list.ToArray();
        }

        private static void AddUnityVersionSymbols(HashSet<string> symbols)
        {
            var version = Application.unityVersion ?? "";
            var parts = version.Split('.');
            int major = parts.Length > 0 ? ReadInt(parts[0]) : -1;
            int minor = parts.Length > 1 ? ReadInt(parts[1]) : -1;
            int patch = parts.Length > 2 ? ReadInt(parts[2]) : -1;
            if (major <= 0)
                return;

            symbols.Add("UNITY_" + major.ToString(CultureInfo.InvariantCulture));
            if (minor >= 0)
            {
                var mm =
                    "UNITY_"
                    + major.ToString(CultureInfo.InvariantCulture)
                    + "_"
                    + minor.ToString(CultureInfo.InvariantCulture);
                symbols.Add(mm);
                symbols.Add(mm + "_OR_NEWER");
                int first = major >= 6000 ? 0 : 1;
                for (int i = first; i <= minor; i++)
                    symbols.Add(
                        "UNITY_"
                            + major.ToString(CultureInfo.InvariantCulture)
                            + "_"
                            + i.ToString(CultureInfo.InvariantCulture)
                            + "_OR_NEWER"
                    );
            }
            if (minor >= 0 && patch >= 0)
                symbols.Add(
                    "UNITY_"
                        + major.ToString(CultureInfo.InvariantCulture)
                        + "_"
                        + minor.ToString(CultureInfo.InvariantCulture)
                        + "_"
                        + patch.ToString(CultureInfo.InvariantCulture)
                );
        }

        private static int ReadInt(string value)
        {
            if (string.IsNullOrEmpty(value))
                return -1;
            int end = 0;
            while (end < value.Length && char.IsDigit(value[end]))
                end++;
            if (end == 0)
                return -1;
            return int.TryParse(
                value.Substring(0, end),
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var r
            )
                ? r
                : -1;
        }
    }
}
