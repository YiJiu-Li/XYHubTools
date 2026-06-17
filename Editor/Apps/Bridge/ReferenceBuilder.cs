// ═══════════════════════════════════════════════════════════════
//  ReferenceBuilder — Roslyn MetadataReference 收集管道
// ═══════════════════════════════════════════════════════════════

using System;
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
        /// <summary>构建完整 MetadataReference 列表</summary>
        internal static List<MetadataReference> Build(
            Assembly bridgeAssembly,
            Action<string> reportStage = null,
            CancellationToken ct = default
        )
        {
            var references = new List<MetadataReference>(384);
            var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            CompilationService.ThrowIfCanceled(ct);
            CompilationService.Report(reportStage, "Adding core compiler references");
            TryAddRef(references, paths, SafeLocation(typeof(object).Assembly));
            TryAddRef(references, paths, SafeLocation(typeof(Enumerable).Assembly));
            TryAddRef(references, paths, SafeLocation(typeof(Debug).Assembly));
            TryAddRef(references, paths, SafeLocation(typeof(Editor).Assembly));
            TryAddRef(references, paths, SafeLocation(bridgeAssembly));

            AddSystemDirs(references, paths, reportStage, ct);
            AddPrecompiled(references, paths, reportStage, ct);
            AddCompilationAsms(references, paths, AssembliesType.Editor, reportStage, ct);
            AddCompilationAsms(
                references,
                paths,
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
                    TryAddRef(references, paths, SafeLocation(asm));
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch { }
            }

            AddScriptAsmsDir(references, paths, reportStage, ct);
            return references;
        }

        internal static void AddSystemDirs(
            List<MetadataReference> refs,
            HashSet<string> paths,
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
                        TryAddRef(refs, paths, dll);
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
            List<MetadataReference> refs,
            HashSet<string> paths,
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
                    TryAddRef(refs, paths, p);
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch { }
        }

        internal static void AddCompilationAsms(
            List<MetadataReference> refs,
            HashSet<string> paths,
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
                TryAddRef(refs, paths, asm.outputPath);
                if (asm.allReferences == null)
                    continue;
                foreach (var r in asm.allReferences)
                {
                    CompilationService.ThrowIfCanceled(ct);
                    TryAddRef(refs, paths, r);
                }
            }
        }

        internal static void AddScriptAsmsDir(
            List<MetadataReference> refs,
            HashSet<string> paths,
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
                    TryAddRef(refs, paths, dll);
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

        internal static void TryAddRef(
            List<MetadataReference> refs,
            HashSet<string> paths,
            string path
        )
        {
            if (string.IsNullOrEmpty(path))
                return;
            try
            {
                if (!Path.IsPathRooted(path))
                    path = Path.GetFullPath(path);
            }
            catch
            {
                return;
            }

            if (!File.Exists(path))
                return;
            var normalized = path.Replace('\\', '/');
            if (normalized.IndexOf("/NetStandard/", StringComparison.OrdinalIgnoreCase) >= 0)
                return;
            if (!paths.Add(path))
                return;

            try
            {
                var an = AssemblyName.GetAssemblyName(path);
                var tokenBytes = an.GetPublicKeyToken();
                var token =
                    tokenBytes != null && tokenBytes.Length > 0
                        ? BitConverter.ToString(tokenBytes).Replace("-", "").ToLowerInvariant()
                        : "null";
                if (!paths.Add("__identity__:" + an.Name + ":" + token))
                    return;
            }
            catch
            {
                var fn = Path.GetFileNameWithoutExtension(path);
                if (
                    !string.IsNullOrEmpty(fn) && !paths.Add("__filename__:" + fn.ToLowerInvariant())
                )
                    return;
            }

            try
            {
                refs.Add(MetadataReference.CreateFromFile(path));
            }
            catch { }
        }
    }
}
