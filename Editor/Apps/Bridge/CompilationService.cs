// ═══════════════════════════════════════════════════════════════
//  Roslyn 编译辅助 — MetadataReference 收集、缓存、诊断格式化
//
//  主线程边界（重要）：
//    只有「收集 DLL 路径」和「读工程 define」必须回主线程（Unity API）。
//    MetadataReference.CreateFromFile 是纯 Roslyn 操作，放到后台跑，
//    否则数百个 DLL 的解析会把编辑器 UI 冻住。
// ═══════════════════════════════════════════════════════════════

using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
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
            // 不清 ReferenceBuilder 的实例缓存：它的 key 带文件写入时间，
            // 重编译后变了的 DLL 会自动拿到新实例，没变的继续复用已解析的元数据。
        }

        /// <summary>
        /// 确保 MetadataReference 缓存就绪。
        ///
        /// 分两段，边界就是主线程：
        ///   ① 主线程 post：只收集 DLL 路径（要调 Unity API）
        ///   ② 后台 Task.Run：把路径构建成 MetadataReference（耗时大头，不占主线程）
        /// </summary>
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

            // ① 主线程：只收路径
            var pathsTcs = new TaskCompletionSource<List<string>>();
            Report(reportStage, "Waiting for Unity main thread");
            postToMainThread(() =>
            {
                try
                {
                    ThrowIfCanceled(cancellationToken);
                    pathsTcs.TrySetResult(
                        ReferenceBuilder.CollectPaths(
                            typeof(YZJBridge).Assembly,
                            reportStage,
                            cancellationToken
                        )
                    );
                }
                catch (OperationCanceledException)
                {
                    pathsTcs.TrySetCanceled();
                }
                catch (Exception ex)
                {
                    pathsTcs.TrySetException(ex);
                }
            });

            List<string> paths = null;
            using (var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                var delayTask = Task.Delay(executeTimeoutMs, timeoutCts.Token);
                var completed = await Task.WhenAny(pathsTcs.Task, delayTask);
                // 谁先完成都取消定时器，避免正常路径下白留一个 executeTimeoutMs 的计时器
                timeoutCts.Cancel();

                if (cancellationToken.IsCancellationRequested)
                    return "execute_code canceled";
                if (completed != pathsTcs.Task)
                    return "prepare execute_code timed out";

                try
                {
                    paths = await pathsTcs.Task;
                }
                catch (OperationCanceledException)
                {
                    return "execute_code canceled";
                }
                catch (Exception ex)
                {
                    return "prepare execute_code failed: " + ex.Message;
                }
            }

            if (paths == null)
                return "execute_code canceled";

            // ② 后台：路径 → MetadataReference
            Report(reportStage, "Building metadata references");
            List<MetadataReference> built;
            try
            {
                built = await Task.Run(
                    () => ReferenceBuilder.BuildFromPaths(paths, reportStage, cancellationToken),
                    cancellationToken
                );
            }
            catch (OperationCanceledException)
            {
                return "execute_code canceled";
            }
            catch (Exception ex)
            {
                return "prepare execute_code failed: " + ex.Message;
            }

            lock (CompileCacheLock)
            {
                CachedReferences = built;
                ReferencesReady = true;
            }
            Report(reportStage, "Compiler reference cache ready");
            return null;
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

        /// <summary>
        /// 获取或构建 MetadataReference 列表。
        /// 未命中缓存时同步构建 —— 内部会调 Unity API 收集路径，
        /// <b>因此调用方应在主线程</b>（execute_code 的异步路径走 EnsureReadyAsync，
        /// 那条路已经保证在主线程收集完才调用这里）。
        /// </summary>
        internal static List<MetadataReference> EnsureReferences(
            Action<string> reportStage = null,
            CancellationToken ct = default
        )
        {
            ThrowIfCanceled(ct);
            lock (CompileCacheLock)
            {
                ThrowIfCanceled(ct);
                if (ReferencesReady && CachedReferences != null)
                {
                    Report(reportStage, "Compiler reference cache ready");
                    return CachedReferences;
                }
            }

            // 构建放在锁外：别让一次慢构建把其它调用方一起堵住
            var built = BuildReferences(typeof(YZJBridge).Assembly, reportStage, ct);
            lock (CompileCacheLock)
            {
                CachedReferences = built;
                ReferencesReady = true;
            }
            return built;
        }

        /// <summary>构建完整 MetadataReference 列表（主线程同步版）</summary>
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

        // ═══════════════════════════════════════════════════════════════
        //  预处理器符号
        // ═══════════════════════════════════════════════════════════════

        private static string[] s_LastProjectDefines = new string[0];

        /// <summary>
        /// 工程当前的预处理器符号快照。
        ///
        /// 用 <see cref="EditorUserBuildSettings.activeScriptCompilationDefines"/> —— 这是 Unity
        /// 已经算好的完整集合（UNITY_2022_3_OR_NEWER、平台符号、工程自定义 Scripting Define
        /// Symbols 全都在里面），比手搓 #if 列表 + PlayerSettings.GetScriptingDefineSymbolsForGroup
        /// 更全、也不会因为组选错而漏。官方 com.unity.pipeline 的 RoslynCompilationService
        /// 用的是同一套办法。
        ///
        /// 主线程调用时刷新快照；非主线程调用返回上次快照（Editor API 只能在主线程读）。
        /// </summary>
        internal static string[] ProjectDefines()
        {
            try
            {
                var live = EditorUserBuildSettings.activeScriptCompilationDefines;
                if (live != null && live.Length > 0)
                    s_LastProjectDefines = live;
            }
            catch
            {
                // 非主线程 / 编辑器未就绪：沿用上次快照
            }
            return s_LastProjectDefines;
        }

        /// <summary>
        /// 主线程预热：在 <c>[InitializeOnLoad]</c> 时调一次，避免后台编译先跑、
        /// 读到空符号集导致 #if UNITY_EDITOR 等区域解析到错误分支。
        /// </summary>
        internal static void SnapshotProjectDefines() => ProjectDefines();

        /// <summary>
        /// 构造片段编译用的 ParseOptions。<b>每次编译现取</b>，符号永远是最新的 ——
        /// 不要放进静态只读字段（那样改 define / 切构建目标后不会刷新）。
        /// </summary>
        internal static CSharpParseOptions ProjectParseOptions(string[] extraSymbols = null)
        {
            var symbols = ProjectDefines();
            if (extraSymbols != null && extraSymbols.Length > 0)
            {
                var merged = new HashSet<string>(symbols ?? new string[0], StringComparer.Ordinal);
                foreach (var s in extraSymbols)
                    if (!string.IsNullOrEmpty(s))
                        merged.Add(s);
                symbols = new List<string>(merged).ToArray();
            }
            return new CSharpParseOptions(
                kind: SourceCodeKind.Regular,
                documentationMode: DocumentationMode.None,
                languageVersion: LanguageVersion.CSharp9,
                preprocessorSymbols: symbols
            );
        }
    }
}
