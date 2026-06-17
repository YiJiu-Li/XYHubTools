// ═══════════════════════════════════════════════════════════════
//  SnippetCompiler — 代码片段 Roslyn 编译流水线
// ═══════════════════════════════════════════════════════════════

using System;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;
using Assembly = System.Reflection.Assembly;

namespace Framework.XYEditor.Bridge
{
    internal static class SnippetCompiler
    {
        // ───────────────── 编译选项 ─────────────────
        internal static readonly CSharpParseOptions ParseOptions = new CSharpParseOptions(
            kind: SourceCodeKind.Regular,
            documentationMode: DocumentationMode.None,
            languageVersion: LanguageVersion.CSharp9,
            preprocessorSymbols: CompilationService.BuildPreprocessorSymbols()
        );

        internal static readonly CSharpCompilationOptions CompilationOptions =
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                optimizationLevel: OptimizationLevel.Release,
                allowUnsafe: false,
                assemblyIdentityComparer: DesktopAssemblyIdentityComparer.Default
            );

        private static int _assemblyCounter;
        internal static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false);

        // ═══════════════════════════════════════════════════════════════
        //  公开编译入口
        // ═══════════════════════════════════════════════════════════════

        internal static CompiledAsyncSnippet Compile(string code)
        {
            string usings,
                body;
            CompilationService.SplitUsings(code, out usings, out body);

            CompiledAsyncSnippet snippet;
            string primaryError;
            if (TryCompile(body, usings, false, out snippet, out primaryError))
                return snippet;

            string fallbackError;
            if (TryCompile(body, usings, true, out snippet, out fallbackError))
                return snippet;

            var sb = new StringBuilder();
            if (!string.IsNullOrEmpty(primaryError))
                sb.Append(primaryError);
            if (
                !string.IsNullOrEmpty(fallbackError)
                && !string.Equals(primaryError, fallbackError, StringComparison.Ordinal)
            )
            {
                if (sb.Length > 0)
                    sb.Append("\n\nexpression fallback:\n");
                sb.Append(fallbackError);
            }
            throw new Exception(
                sb.Length > 0 ? sb.ToString() : "unknown async compilation failure"
            );
        }

        internal static bool TryCompile(
            string bodyCode,
            string leadingUsings,
            bool expressionMode,
            out CompiledAsyncSnippet snippet,
            out string error
        )
        {
            snippet = null;
            error = null;
            const string hostTypeName = "__YZJAsyncSnippetHost";
            const string fullTypeName =
                "Framework.XYEditor.Bridge.RuntimeSnippets.__YZJAsyncSnippetHost";

            string source = BuildSource(hostTypeName, leadingUsings, bodyCode, expressionMode);
            SyntaxTree syntaxTree;
            try
            {
                syntaxTree = CSharpSyntaxTree.ParseText(
                    source,
                    ParseOptions,
                    path: "YZJRuntimeAsyncSnippet.cs",
                    encoding: Utf8NoBom
                );
            }
            catch (Exception ex)
            {
                error = "parse failed: " + ex;
                return false;
            }

            string assemblyName =
                "__YZJRuntimeAsync_" + Interlocked.Increment(ref _assemblyCounter).ToString("X8");
            var compilation = CSharpCompilation.Create(
                assemblyName,
                syntaxTrees: new[] { syntaxTree },
                references: CompilationService.EnsureReferences(),
                options: CompilationOptions
            );

            using (var peStream = new MemoryStream(16 * 1024))
            {
                EmitResult emitResult;
                try
                {
                    emitResult = compilation.Emit(peStream);
                }
                catch (Exception ex)
                {
                    error = "emit failed: " + ex;
                    return false;
                }

                if (!emitResult.Success)
                {
                    error = CompilationService.FormatErrors(emitResult.Diagnostics);
                    return false;
                }

                try
                {
                    byte[] bytes = peStream.ToArray();
                    Assembly assembly = Assembly.Load(bytes);
                    Type hostType = assembly.GetType(fullTypeName, true);
                    MethodInfo m = hostType.GetMethod(
                        "ExecuteAsync",
                        BindingFlags.Public | BindingFlags.Static
                    );
                    if (m == null)
                    {
                        error = "missing ExecuteAsync method";
                        return false;
                    }
                    var executor =
                        (Func<ScriptGlobals, ExecuteCodeContext, CancellationToken, Task<object>>)
                            Delegate.CreateDelegate(
                                typeof(Func<
                                    ScriptGlobals,
                                    ExecuteCodeContext,
                                    CancellationToken,
                                    Task<object>
                                >),
                                m
                            );
                    snippet = new CompiledAsyncSnippet(executor);
                    return true;
                }
                catch (Exception ex)
                {
                    error = "assembly load failed: " + ex;
                    return false;
                }
            }
        }

        // ═══════════════════════════════════════════════════════════════
        //  代码片段宿主源码生成
        // ═══════════════════════════════════════════════════════════════

        internal static string BuildSource(
            string hostTypeName,
            string leadingUsings,
            string bodyCode,
            bool expressionMode
        )
        {
            var sb = new StringBuilder(4096);
            sb.AppendLine("using System;");
            sb.AppendLine("using System.IO;");
            sb.AppendLine("using System.Text;");
            sb.AppendLine("using System.Linq;");
            sb.AppendLine("using System.Reflection;");
            sb.AppendLine("using System.Threading;");
            sb.AppendLine("using System.Threading.Tasks;");
            sb.AppendLine("using System.Collections;");
            sb.AppendLine("using System.Collections.Generic;");
            sb.AppendLine("using UnityEngine;");
            sb.AppendLine("using UnityEngine.SceneManagement;");
            sb.AppendLine("using UnityEngine.UI;");
            sb.AppendLine("using UnityEditor;");
            sb.AppendLine("using UnityEditor.SceneManagement;");
            sb.AppendLine("using UnityEditor.Animations;");
            sb.AppendLine("using static UnityEngine.Object;");
            sb.AppendLine("using Object = UnityEngine.Object;");
            if (!string.IsNullOrWhiteSpace(leadingUsings))
                sb.AppendLine(leadingUsings);

            sb.AppendLine("namespace Framework.XYEditor.Bridge.RuntimeSnippets");
            sb.AppendLine("{");
            sb.Append("    public static class ").AppendLine(hostTypeName);
            sb.AppendLine("    {");
            sb.AppendLine(
                "        public static async global::System.Threading.Tasks.Task<object> ExecuteAsync("
                    + "global::Framework.XYEditor.Bridge.ScriptGlobals globals, "
                    + "global::Framework.XYEditor.Bridge.ExecuteCodeContext ctx, "
                    + "global::System.Threading.CancellationToken cancellationToken)"
            );
            sb.AppendLine("        {");
            sb.AppendLine(
                "            var print = new global::System.Action<object>(globals.print);"
            );
            sb.AppendLine(
                "            var printJson = new global::System.Action<object>(globals.printJson);"
            );
            sb.AppendLine("            var clear = new global::System.Action(globals.clear);");
            sb.AppendLine("            var ct = cancellationToken;");
            sb.AppendLine("            ctx.ThrowIfCancellationRequested();");
            sb.AppendLine("            #line 1");

            if (expressionMode)
            {
                if (string.IsNullOrWhiteSpace(bodyCode))
                    sb.AppendLine("            return null;");
                else
                {
                    sb.Append("            return (object)(");
                    sb.Append(bodyCode);
                    sb.AppendLine(");");
                }
            }
            else
            {
                if (!string.IsNullOrWhiteSpace(bodyCode))
                    sb.AppendLine(bodyCode);
                sb.AppendLine("            return null;");
            }
            sb.AppendLine("            #line default");
            sb.AppendLine("        }");
            sb.AppendLine("    }");
            sb.AppendLine("}");
            return sb.ToString();
        }
    }
}
