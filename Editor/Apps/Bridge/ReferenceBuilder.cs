// ═══════════════════════════════════════════════════════════════
//  ReferenceBuilder — Roslyn MetadataReference 收集管道
//
//  拆成两段，边界就是主线程：
//    CollectPaths   —— 主线程。只产出 DLL 路径字符串，因为这里要调 Unity API
//                      （CompilationPipeline / PlayerSettings / Application.dataPath）。
//    BuildFromPaths —— 任意线程。纯 Roslyn + 文件 IO，不需要主线程。
//                      这是耗时大头（数百次 MetadataReference.CreateFromFile），
//                      放在后台跑，编辑器 UI 不再被冻住。
// ═══════════════════════════════════════════════════════════════

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using Microsoft.CodeAnalysis;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;
using Assembly = System.Reflection.Assembly;

namespace Framework.XYEditor.Bridge
{
    /// <summary>
    /// MetadataReference 收集管道。
    /// 从 Unity 系统目录、预编译程序集、编译管线收集所有 DLL 引用。
    /// </summary>
    internal static class ReferenceBuilder
    {
        /// <summary>
        /// 按「路径 + 最后写入时间」缓存 MetadataReference 实例。
        ///
        /// 复用同一批实例能让 Roslyn 复用已解析的元数据，而不是每次编译重读全部 DLL
        /// （官方 com.unity.pipeline 的 RoslynCompilationService 注释实测：重读 ~190 个 DLL
        /// 每次约 830 ms）。带上写入时间做 key，脚本重编译后 ScriptAssemblies 下的 DLL 会
        /// 自动拿到新实例，而没变的系统/包 DLL 继续复用 —— 既正确又省事。
        /// </summary>
        private static readonly ConcurrentDictionary<string, MetadataReference> s_ReferenceCache =
            new ConcurrentDictionary<string, MetadataReference>(StringComparer.OrdinalIgnoreCase);

        /// <summary>缓存条目上限，超过就整体丢弃，避免长期运行下无界增长。</summary>
        private const int ReferenceCacheLimit = 4096;

        // ═══════════════════════════════════════════════════════════════
        //  第一段：主线程 —— 只收路径
        // ═══════════════════════════════════════════════════════════════

        /// <summary>
        /// 收集候选 DLL 路径。<b>必须在主线程调用</b>（内部使用 Unity Editor API）。
        /// 返回的列表可能含重复项与不存在的路径，由 <see cref="BuildFromPaths"/> 过滤。
        /// </summary>
        internal static List<string> CollectPaths(
            Assembly bridgeAssembly,
            Action<string> reportStage = null,
            CancellationToken ct = default
        )
        {
            var paths = new List<string>(512);
            // 只做原始字符串去重，避免同一路径被六个来源反复追加导致列表膨胀
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            CompilationService.ThrowIfCanceled(ct);
            CompilationService.Report(reportStage, "Adding core compiler references");
            AddPath(paths, seen, SafeLocation(typeof(object).Assembly));
            AddPath(paths, seen, SafeLocation(typeof(Enumerable).Assembly));
            AddPath(paths, seen, SafeLocation(typeof(Debug).Assembly));
            AddPath(paths, seen, SafeLocation(typeof(Editor).Assembly));
            AddPath(paths, seen, SafeLocation(bridgeAssembly));

            AddSystemDirs(paths, seen, reportStage, ct);
            AddPrecompiled(paths, seen, reportStage, ct);
            AddCompilationAsms(paths, seen, AssembliesType.Editor, reportStage, ct);
            AddCompilationAsms(
                paths,
                seen,
                AssembliesType.PlayerWithoutTestAssemblies,
                reportStage,
                ct
            );

            CompilationService.Report(reportStage, "Adding loaded AppDomain assemblies");
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    CompilationService.ThrowIfCanceled(ct);
                    if (asm == null || asm.IsDynamic)
                        continue;
                    AddPath(paths, seen, SafeLocation(asm));
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch { }
            }

            AddScriptAsmsDir(paths, seen, reportStage, ct);
            return paths;
        }

        internal static void AddSystemDirs(
            List<string> paths,
            HashSet<string> seen,
            Action<string> report,
            CancellationToken ct
        )
        {
            CompilationService.ThrowIfCanceled(ct);
            CompilationService.Report(report, "Adding Unity system assemblies");
            try
            {
                if (!TryGetApiLevel(out var level))
                    return;
                var dirs = CompilationPipeline.GetSystemAssemblyDirectories(level);
                if (dirs == null)
                    return;
                foreach (var dir in dirs)
                {
                    if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
                        continue;
                    string[] dlls;
                    try
                    {
                        dlls = Directory.GetFiles(dir, "*.dll", SearchOption.TopDirectoryOnly);
                    }
                    catch
                    {
                        continue;
                    }
                    foreach (var dll in dlls)
                    {
                        CompilationService.ThrowIfCanceled(ct);
                        AddPath(paths, seen, dll);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch { }
        }

        internal static bool TryGetApiLevel(out ApiCompatibilityLevel level)
        {
            level = default;
            try
            {
                level = PlayerSettings.GetApiCompatibilityLevel(
                    EditorUserBuildSettings.selectedBuildTargetGroup
                );
                return true;
            }
            catch
            {
                return false;
            }
        }

        internal static void AddPrecompiled(
            List<string> paths,
            HashSet<string> seen,
            Action<string> report,
            CancellationToken ct
        )
        {
            CompilationService.ThrowIfCanceled(ct);
            CompilationService.Report(report, "Adding precompiled assemblies");
            try
            {
                var precompiled = CompilationPipeline.GetPrecompiledAssemblyPaths(
                    CompilationPipeline.PrecompiledAssemblySources.All
                );
                if (precompiled == null)
                    return;
                foreach (var p in precompiled)
                {
                    CompilationService.ThrowIfCanceled(ct);
                    AddPath(paths, seen, p);
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch { }
        }

        internal static void AddCompilationAsms(
            List<string> paths,
            HashSet<string> seen,
            AssembliesType type,
            Action<string> report,
            CancellationToken ct
        )
        {
            CompilationService.ThrowIfCanceled(ct);
            CompilationService.Report(
                report,
                type == AssembliesType.Editor
                    ? "Adding editor compilation assemblies"
                    : "Adding player compilation assemblies"
            );

            UnityEditor.Compilation.Assembly[] assemblies = null;
            try
            {
                assemblies = CompilationPipeline.GetAssemblies(type);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                return;
            }

            if (assemblies == null)
                return;
            foreach (var asm in assemblies)
            {
                CompilationService.ThrowIfCanceled(ct);
                if (asm == null)
                    continue;
                AddPath(paths, seen, asm.outputPath);
                if (asm.allReferences == null)
                    continue;
                foreach (var r in asm.allReferences)
                {
                    CompilationService.ThrowIfCanceled(ct);
                    AddPath(paths, seen, r);
                }
            }
        }

        internal static void AddScriptAsmsDir(
            List<string> paths,
            HashSet<string> seen,
            Action<string> report,
            CancellationToken ct
        )
        {
            CompilationService.ThrowIfCanceled(ct);
            CompilationService.Report(report, "Adding ScriptAssemblies");
            try
            {
                var projectRoot = Path.GetDirectoryName(Application.dataPath);
                var dir = Path.Combine(projectRoot, "Library", "ScriptAssemblies");
                if (!Directory.Exists(dir))
                    return;
                string[] dlls;
                try
                {
                    dlls = Directory.GetFiles(dir, "*.dll", SearchOption.TopDirectoryOnly);
                }
                catch
                {
                    return;
                }
                foreach (var dll in dlls)
                {
                    CompilationService.ThrowIfCanceled(ct);
                    AddPath(paths, seen, dll);
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch { }
        }

        internal static string SafeLocation(Assembly asm)
        {
            try
            {
                if (asm == null || asm.IsDynamic)
                    return null;
                var loc = asm.Location;
                return string.IsNullOrEmpty(loc) ? null : loc;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>追加一个候选路径（只做字符串级去重，不碰文件系统）。</summary>
        internal static void AddPath(List<string> paths, HashSet<string> seen, string path)
        {
            if (string.IsNullOrEmpty(path))
                return;
            if (seen.Add(path))
                paths.Add(path);
        }

        // ═══════════════════════════════════════════════════════════════
        //  第二段：任意线程 —— 路径 → MetadataReference
        // ═══════════════════════════════════════════════════════════════

        /// <summary>
        /// 把候选路径列表构建成 MetadataReference 列表。
        /// <b>不调用任何 Unity API</b>，可安全地在后台线程执行 —— 耗时的大头就在这里，
        /// 放到后台可以避免冻结编辑器 UI。
        /// </summary>
        internal static List<MetadataReference> BuildFromPaths(
            List<string> candidates,
            Action<string> reportStage = null,
            CancellationToken ct = default
        )
        {
            var references = new List<MetadataReference>(candidates != null ? candidates.Count : 64);
            if (candidates == null)
                return references;

            // 同一程序集可能出现多个路径（六个来源重叠）。Roslyn 对「同一 identity 不同路径」
            // 会报 CS1704，所以这里保留 identity 去重 —— 它只是元数据读取，线程安全，
            // 放在后台不影响主线程。
            var identities = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var accepted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            CompilationService.Report(reportStage, "Building metadata references");
            int i = 0;
            foreach (var candidate in candidates)
            {
                if ((++i & 63) == 0)
                    CompilationService.ThrowIfCanceled(ct);

                string path = candidate;
                try
                {
                    if (!Path.IsPathRooted(path))
                        path = Path.GetFullPath(path);
                }
                catch
                {
                    continue;
                }

                if (!File.Exists(path))
                    continue;
                var normalized = path.Replace('\\', '/');
                if (normalized.IndexOf("/NetStandard/", StringComparison.OrdinalIgnoreCase) >= 0)
                    continue;
                if (!accepted.Add(path))
                    continue;

                try
                {
                    var an = AssemblyName.GetAssemblyName(path);
                    var tokenBytes = an.GetPublicKeyToken();
                    var token =
                        tokenBytes != null && tokenBytes.Length > 0
                            ? BitConverter.ToString(tokenBytes).Replace("-", "").ToLowerInvariant()
                            : "null";
                    if (!identities.Add(an.Name + ":" + token))
                        continue;
                }
                catch
                {
                    var fn = Path.GetFileNameWithoutExtension(path);
                    if (!string.IsNullOrEmpty(fn) && !identities.Add("__filename__:" + fn.ToLowerInvariant()))
                        continue;
                }

                var reference = GetCachedReference(path);
                if (reference != null)
                    references.Add(reference);
            }

            CompilationService.Report(reportStage, "Metadata references ready (" + references.Count + ")");
            return references;
        }

        /// <summary>
        /// 按「路径 + 最后写入时间」取（或建）MetadataReference 实例。
        /// 线程安全；写入时间保证重编译后的 DLL 不会命中旧元数据。
        /// </summary>
        internal static MetadataReference GetCachedReference(string path)
        {
            long ticks = 0;
            try
            {
                ticks = File.GetLastWriteTimeUtc(path).Ticks;
            }
            catch { }

            string key = ticks != 0 ? path + "|" + ticks : path;

            if (s_ReferenceCache.Count >= ReferenceCacheLimit)
                s_ReferenceCache.Clear();

            MetadataReference cached;
            if (s_ReferenceCache.TryGetValue(key, out cached))
                return cached;

            try
            {
                var created = MetadataReference.CreateFromFile(path);
                s_ReferenceCache[key] = created;
                return created;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>兼容入口：主线程收集路径后立即构建（同步版，供主线程调用方使用）。</summary>
        internal static List<MetadataReference> Build(
            Assembly bridgeAssembly,
            Action<string> reportStage = null,
            CancellationToken ct = default
        ) => BuildFromPaths(CollectPaths(bridgeAssembly, reportStage, ct), reportStage, ct);
    }
}
