using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Framework.XYEditor;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace YiJiu.UnityCopyApp
{
    /// <summary>
    /// 资源复制面板 —— 独立渲染逻辑，可嵌入任意 GUILayout.BeginArea 区域，
    /// 也可由 CopyAppWindow 作为独立窗口使用。
    /// </summary>
    public class CopyAppPanel : IXYPanel
    {
        // ── 枚举 ──────────────────────────────────────────────────────────
        private enum CopyMode
        {
            Directory,
            SingleFile,
        }

        // ── 路径 / 状态 ───────────────────────────────────────────────────
        private string sourcePath = "";
        private string destinationPath = "";
        private bool isProcessing = false;
        private float progress = 0f;
        private string currentStatus = "";
        private CancellationTokenSource cancellationTokenSource;
        private bool showDetailedLogs = false;
        private Vector2 logScrollPosition;
        private List<LogEntry> logEntries = new();
        private int maxLogEntries = 100;
        private CopyMode currentMode = CopyMode.Directory;
        private Object sourceAsset = null;
        private string singleFileDestinationPath = "";
        private bool includeCodes = false;
        private bool includePackages = false;

        // ── 重绘回调 ──────────────────────────────────────────────────────
        private readonly Action _repaint;

        // ── 日志条目 ──────────────────────────────────────────────────────
        private class LogEntry
        {
            public string message;
            public Color color = Color.white;
            public DateTime timestamp = DateTime.Now;
        }

        // ═══════════════════════════════════════════════════════════════════
        //  公共 API
        // ═══════════════════════════════════════════════════════════════════

        public CopyAppPanel(Action repaint = null)
        {
            _repaint = repaint ?? (() => { });
        }

        /// <summary>激活时调用：读取 EditorPrefs</summary>
        public void Init()
        {
            if (string.IsNullOrEmpty(sourcePath))
                sourcePath = EditorPrefs.GetString("UnityCopyApp_SourcePath", "");
            if (string.IsNullOrEmpty(destinationPath))
                destinationPath = EditorPrefs.GetString("UnityCopyApp_DestPath", "");
            if (string.IsNullOrEmpty(singleFileDestinationPath))
                singleFileDestinationPath = EditorPrefs.GetString(
                    "UnityCopyApp_SingleFileDestPath",
                    ""
                );
            includeCodes = EditorPrefs.GetBool("UnityCopyApp_IncludeCodes", false);
            includePackages = EditorPrefs.GetBool("UnityCopyApp_IncludePackages", false);
        }

        /// <summary>停用时调用：保存 EditorPrefs 并取消任务</summary>
        public void Cleanup()
        {
            EditorPrefs.SetString("UnityCopyApp_SourcePath", sourcePath);
            EditorPrefs.SetString("UnityCopyApp_DestPath", destinationPath);
            EditorPrefs.SetString("UnityCopyApp_SingleFileDestPath", singleFileDestinationPath);
            EditorPrefs.SetBool("UnityCopyApp_IncludeCodes", includeCodes);
            EditorPrefs.SetBool("UnityCopyApp_IncludePackages", includePackages);

            cancellationTokenSource?.Cancel();
            cancellationTokenSource = null;
        }

        /// <summary>
        /// 主绘制方法：在调用前需已进入 GUILayout.BeginArea(rect)。
        /// </summary>
        /// <param name="containerWidth">当前容器可用宽度（px），供内部布局参考</param>
        public void Draw(float containerWidth)
        {
            GUILayout.Label("资源复制工具", XYEditorStyles.CenteredBoldTitle);

            currentMode = (CopyMode)EditorGUILayout.EnumPopup("复制模式:", currentMode);

            EditorGUI.BeginDisabledGroup(isProcessing);
            if (currentMode == CopyMode.Directory)
                DrawDirectoryModeGUI();
            else
                DrawSingleFileModeGUI();
            EditorGUI.EndDisabledGroup();

            showDetailedLogs = EditorGUILayout.Toggle("显示详细日志", showDetailedLogs);

            GUILayout.Space(10);

            if (isProcessing)
            {
                XYEditorGUI.DrawProgressBar(progress, currentStatus);
                if (GUILayout.Button("取消操作"))
                    cancellationTokenSource?.Cancel();
            }
            else
            {
                if (GUILayout.Button("开始复制"))
                {
                    if (currentMode == CopyMode.Directory)
                    {
                        if (
                            string.IsNullOrEmpty(sourcePath)
                            || string.IsNullOrEmpty(destinationPath)
                        )
                        {
                            EditorUtility.DisplayDialog("错误", "请选择源目录和目标目录", "确定");
                            return;
                        }
                        if (!System.IO.Directory.Exists(sourcePath))
                        {
                            EditorUtility.DisplayDialog("错误", "源目录不存在", "确定");
                            return;
                        }
                        logEntries.Clear();
                        StartCopyOperation();
                    }
                    else
                    {
                        if (sourceAsset == null || string.IsNullOrEmpty(singleFileDestinationPath))
                        {
                            EditorUtility.DisplayDialog("错误", "请选择源文件和目标目录", "确定");
                            return;
                        }
                        string assetPath = AssetDatabase.GetAssetPath(sourceAsset);
                        if (string.IsNullOrEmpty(assetPath))
                        {
                            EditorUtility.DisplayDialog("错误", "无法获取所选资源的路径", "确定");
                            return;
                        }
                        logEntries.Clear();
                        StartSingleFileCopyOperation(assetPath);
                    }
                }
            }

            GUILayout.Space(10);
            DrawLogArea(containerWidth);

            XYEditorGUI.DrawFooter(
                "作者: 依旧 | GitHub: https://github.com/YiJiu-Li",
                "https://github.com/YiJiu-Li"
            );
        }

        // ═══════════════════════════════════════════════════════════════════
        //  绘制子区域
        // ═══════════════════════════════════════════════════════════════════

        private void DrawDirectoryModeGUI()
        {
            sourcePath = XYEditorGUI.FolderPathField("源目录:", sourcePath, "选择源目录");
            destinationPath = XYEditorGUI.FolderPathField(
                "目标目录:",
                destinationPath,
                "选择目标目录"
            );

            includeCodes = EditorGUILayout.Toggle("包含代码文件", includeCodes);

            XYEditorGUI.DrawSection(
                "代码文件过滤项（不勾选时将排除以下文件类型）：",
                () =>
                {
                    GUI.enabled = false;
                    foreach (var ext in GetCodeExtensions())
                        EditorGUILayout.Toggle(ext, true);
                    GUI.enabled = true;
                }
            );

            if (includeCodes)
                EditorGUILayout.HelpBox(
                    "包含代码文件选项将复制脚本(.cs)和其他代码相关文件。",
                    MessageType.Info
                );
        }

        private void DrawSingleFileModeGUI()
        {
            EditorGUILayout.BeginHorizontal();
            sourceAsset = EditorGUILayout.ObjectField(
                "源资源文件:",
                sourceAsset,
                typeof(Object),
                false
            );
            EditorGUILayout.EndHorizontal();

            singleFileDestinationPath = XYEditorGUI.FolderPathField(
                "目标目录:",
                singleFileDestinationPath,
                "选择目标目录"
            );

            XYEditorGUI.DrawSection(
                "复制选项：",
                () =>
                {
                    includeCodes = EditorGUILayout.Toggle("包含代码文件", includeCodes);
                    includePackages = EditorGUILayout.Toggle("包含Packages依赖", includePackages);
                    if (!includePackages)
                        EditorGUILayout.HelpBox(
                            "不包含Packages依赖选项将排除所有以 'Packages/' 开头的依赖文件。",
                            MessageType.Info
                        );
                }
            );

            XYEditorGUI.DrawSection(
                "代码文件过滤项（不勾选时将排除以下文件类型）：",
                () =>
                {
                    GUI.enabled = false;
                    foreach (var ext in GetCodeExtensions())
                        EditorGUILayout.Toggle(ext, true);
                    GUI.enabled = true;
                }
            );

            if (includeCodes)
                EditorGUILayout.HelpBox(
                    "包含代码文件选项将复制脚本(.cs)和其他代码相关文件。",
                    MessageType.Info
                );
        }

        private void DrawLogArea(float containerWidth)
        {
            GUILayout.Label("日志输出:", XYEditorStyles.LogTitle);

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            logScrollPosition = EditorGUILayout.BeginScrollView(
                logScrollPosition,
                GUILayout.Height(200)
            );

            foreach (var log in logEntries)
            {
                Rect logRect = EditorGUILayout.GetControlRect(
                    false,
                    XYEditorStyles.LogEntry.CalcHeight(
                        new GUIContent(log.message),
                        containerWidth - 30
                    )
                );
                Color bg = Color.clear;
                if (log.message.Contains("❌"))
                    bg = new Color(0.6f, 0.1f, 0.1f, 0.1f);
                else if (log.message.Contains("⚠️"))
                    bg = new Color(0.6f, 0.6f, 0.1f, 0.1f);
                else if (log.message.Contains("✅"))
                    bg = new Color(0.1f, 0.5f, 0.1f, 0.1f);
                else
                    bg = new Color(0.2f, 0.2f, 0.2f, 0.05f);
                EditorGUI.DrawRect(logRect, bg);
                GUI.contentColor = log.color;
                EditorGUI.LabelField(logRect, log.message, XYEditorStyles.LogEntry);
            }
            GUI.contentColor = Color.white;

            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
        }

        // ═══════════════════════════════════════════════════════════════════
        //  复制操作
        // ═══════════════════════════════════════════════════════════════════

        private void StartCopyOperation()
        {
            isProcessing = true;
            progress = 0f;
            currentStatus = "准备中...";
            cancellationTokenSource = new CancellationTokenSource();

            Task.Run(async () =>
            {
                try
                {
                    await CopyDirectoryDeepAsync(
                        sourcePath,
                        destinationPath,
                        cancellationTokenSource.Token
                    );
                }
                catch (OperationCanceledException)
                {
                    AddLog("操作已取消", LogType.Warning);
                }
                catch (Exception ex)
                {
                    AddLog($"发生错误: {ex.Message}", LogType.Error);
                    Debug.LogException(ex);
                }
                finally
                {
                    EditorApplication.delayCall += () =>
                    {
                        isProcessing = false;
                        progress = 1.0f;
                        currentStatus = "完成";
                        _repaint();
                    };
                }
            });
        }

        private void StartSingleFileCopyOperation(string assetPath)
        {
            isProcessing = true;
            progress = 0f;
            currentStatus = "准备中...";
            cancellationTokenSource = new CancellationTokenSource();

            UpdateProgressAndStatus(0.1f, "正在分析依赖...");
            string[] dependencies = AssetDatabase.GetDependencies(assetPath, true);
            var allFiles = new List<string>(dependencies);
            if (!allFiles.Contains(assetPath))
                allFiles.Add(assetPath);

            int originalCount = allFiles.Count;
            if (!includePackages)
            {
                allFiles = allFiles.Where(p => !p.StartsWith("Packages/")).ToList();
                int filtered = originalCount - allFiles.Count;
                if (filtered > 0)
                    AddLog($"已排除 {filtered} 个Packages目录中的依赖文件", LogType.Warning, true);
            }

            AddLog($"总共需要复制 {allFiles.Count} 个文件", LogType.Log, true);
            UpdateProgressAndStatus(0.2f, "分析完成，准备复制文件...");

            Task.Run(async () =>
            {
                try
                {
                    await CopySingleFileDeepAsync(
                        assetPath,
                        singleFileDestinationPath,
                        allFiles.ToArray(),
                        cancellationTokenSource.Token
                    );
                }
                catch (OperationCanceledException)
                {
                    AddLog("操作已取消", LogType.Warning);
                }
                catch (Exception ex)
                {
                    AddLog($"发生错误: {ex.Message}", LogType.Error);
                    Debug.LogException(ex);
                }
                finally
                {
                    EditorApplication.delayCall += () =>
                    {
                        isProcessing = false;
                        progress = 1.0f;
                        currentStatus = "完成";
                        _repaint();
                    };
                }
            });
        }

        private void AddLog(string message, LogType logType = LogType.Log, bool forceShow = false)
        {
            Color color;
            string formattedMessage = message;

            switch (logType)
            {
                case LogType.Warning:
                    color = new Color(1f, 0.9f, 0.2f);
                    formattedMessage = $"<color=#FFD700>⚠️ {message}</color>";
                    break;
                case LogType.Error:
                    color = new Color(1f, 0.3f, 0.3f);
                    formattedMessage = $"<color=#FF4500>❌ {message}</color>";
                    break;
                default:
                    if (message.Contains("完成") || message.Contains("成功"))
                    {
                        color = new Color(0.3f, 1f, 0.6f);
                        formattedMessage = $"<color=#00FF7F>✅ {message}</color>";
                    }
                    else if (message.Contains("复制文件:"))
                    {
                        color = new Color(0.7f, 0.7f, 1f);
                        formattedMessage = $"<color=#B0C4DE>📄 {message}</color>";
                    }
                    else if (message.Contains("---"))
                    {
                        color = new Color(0.7f, 0.7f, 0.7f);
                    }
                    else
                    {
                        color = new Color(0.9f, 0.9f, 0.9f);
                    }
                    break;
            }

            EditorApplication.delayCall += () =>
            {
                if (forceShow || showDetailedLogs || logType != LogType.Log)
                {
                    logEntries.Add(
                        new LogEntry
                        {
                            message = formattedMessage,
                            color = color,
                            timestamp = DateTime.Now,
                        }
                    );
                    if (logEntries.Count > maxLogEntries)
                        logEntries.RemoveAt(0);
                }
                switch (logType)
                {
                    case LogType.Warning:
                        Debug.LogWarning(message);
                        break;
                    case LogType.Error:
                        Debug.LogError(message);
                        break;
                    default:
                        Debug.Log(message);
                        break;
                }
                _repaint();
            };
        }

        private void UpdateProgressAndStatus(float p, string s)
        {
            EditorApplication.delayCall += () =>
            {
                progress = p;
                currentStatus = s;
                _repaint();
            };
        }

        // ═══════════════════════════════════════════════════════════════════
        //  文件复制实现（与原始代码相同）
        // ═══════════════════════════════════════════════════════════════════

        private string[] GetCodeExtensions() => new[] { ".cs" };

        private bool IsPackagePath(string path) => path.StartsWith("Packages/");

        private async Task CopySingleFileDeepAsync(
            string sourceAssetPath,
            string destinationBasePath,
            string[] allFilesToCopy,
            CancellationToken cancellationToken
        )
        {
            AddLog(
                $"单文件复制开始: {sourceAssetPath} -> {destinationBasePath}",
                LogType.Log,
                true
            );
            var allFiles = new List<string>(allFilesToCopy);

            if (!includeCodes)
            {
                var codeExt = GetCodeExtensions();
                var filtered = allFiles
                    .Where(f => !codeExt.Any(e => f.ToLower().EndsWith(e)))
                    .ToList();
                int removedCount = allFiles.Count - filtered.Count;
                if (removedCount > 0)
                    AddLog($"已过滤 {removedCount} 个代码文件", LogType.Warning, true);
                allFiles = filtered;
            }

            var guidMap = new Dictionary<string, string>();
            int filesCopied = 0;
            foreach (var file in allFiles)
            {
                cancellationToken.ThrowIfCancellationRequested();
                filesCopied++;
                float fp = 0.2f + 0.6f * (filesCopied / (float)allFiles.Count);
                UpdateProgressAndStatus(
                    fp,
                    $"复制文件 {filesCopied}/{allFiles.Count}: {Path.GetFileName(file)}"
                );

                try
                {
                    string relative = file.StartsWith("Assets/")
                        ? file.Substring("Assets/".Length)
                        : file;
                    string destFile = Path.Combine(destinationBasePath, relative);
                    string destDir = Path.GetDirectoryName(destFile);
                    if (!Directory.Exists(destDir))
                        Directory.CreateDirectory(destDir);
                    File.Copy(file, destFile, true);
                    AddLog($"复制文件: {file} -> {destFile}");

                    string metaFile = file + ".meta";
                    if (File.Exists(metaFile))
                    {
                        string destMeta = destFile + ".meta";
                        File.Copy(metaFile, destMeta, true);
                        using (var reader = new StreamReader(metaFile))
                        {
                            reader.ReadLine();
                            string guidLine = reader.ReadLine();
                            if (guidLine != null && guidLine.StartsWith("guid:"))
                            {
                                string orig = guidLine.Substring(6).Trim();
                                guidMap[orig] = Guid.NewGuid().ToString("N");
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    AddLog($"复制文件时出错: {file} - {ex.Message}", LogType.Error);
                }
            }

            UpdateProgressAndStatus(0.8f, "文件复制完成，更新GUID引用...");
            await ReplaceGuidsInFilesAsync(destinationBasePath, guidMap, cancellationToken);
            UpdateProgressAndStatus(1.0f, "完成");
            AddLog("-----------------------------------", LogType.Log, true);
            AddLog(
                $"单文件复制完成: {sourceAssetPath} -> {destinationBasePath}",
                LogType.Log,
                true
            );
            EditorApplication.delayCall += () =>
            {
                AssetDatabase.Refresh();
                Debug.Log("已刷新资源文件夹");
            };
        }

        private async Task CopyDirectoryDeepAsync(string src, string dst, CancellationToken ct)
        {
            AddLog($"资源目录复制开始: {src} -> {dst}", LogType.Log, true);
            await Task.Run(() => CopyDirectoryRecursively(src, dst, ct), ct);
            UpdateProgressAndStatus(0.2f, "目录复制完成，开始处理Meta文件...");

            var metaFiles = await Task.Run(
                () => GetFilesRecursively(dst, f => f.ToLower().EndsWith(".meta")),
                ct
            );
            UpdateProgressAndStatus(0.3f, "Meta文件列表获取完成，开始处理GUID...");

            var guidTable = new List<(string orig, string newG)>();
            foreach (var metaFile in metaFiles)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    using (var file = new StreamReader(metaFile))
                    {
                        file.ReadLine();
                        string guidLine = file.ReadLine();
                        if (guidLine != null && guidLine.StartsWith("guid:"))
                            guidTable.Add(
                                (guidLine.Substring(6).Trim(), Guid.NewGuid().ToString("N"))
                            );
                    }
                }
                catch (Exception ex)
                {
                    AddLog($"处理Meta文件时出错 {metaFile}: {ex.Message}", LogType.Error);
                }
            }

            UpdateProgressAndStatus(0.4f, "GUID处理完成，开始获取所有文件...");
            var allFiles = await Task.Run(() => GetFilesRecursively(dst), ct);
            AddLog($"所有GUID数量: {guidTable.Count}", LogType.Log, true);
            AddLog($"所有文件数量: {allFiles.Count}", LogType.Log, true);
            UpdateProgressAndStatus(0.5f, "开始替换文件中的GUID引用...");

            int processed = 0;
            foreach (var fileToModify in allFiles)
            {
                ct.ThrowIfCancellationRequested();
                processed++;
                float fp = 0.5f + 0.5f * (processed / (float)allFiles.Count);
                UpdateProgressAndStatus(
                    fp,
                    $"处理文件 ({processed}/{allFiles.Count}): {Path.GetFileName(fileToModify)}"
                );

                bool isBinary = await Task.Run(() => CheckForBinary(fileToModify), ct);
                if (isBinary)
                {
                    AddLog($"跳过二进制文件: {Path.GetFileName(fileToModify)}");
                    continue;
                }

                try
                {
                    string content = File.ReadAllText(fileToModify);
                    bool changed = false;
                    foreach (var (orig, newG) in guidTable)
                    {
                        string old = content;
                        content = content.Replace(orig, newG);
                        if (old != content)
                        {
                            changed = true;
                            AddLog(
                                $"替换GUID: {Path.GetFileName(fileToModify)} ({orig} -> {newG})"
                            );
                        }
                    }
                    if (changed)
                    {
                        File.WriteAllText(fileToModify, content);
                        AddLog($"更新文件: {Path.GetFileName(fileToModify)}");
                    }
                }
                catch (Exception ex)
                {
                    AddLog(
                        $"处理文件时出错 {Path.GetFileName(fileToModify)}: {ex.Message}",
                        LogType.Error
                    );
                }
            }

            UpdateProgressAndStatus(1.0f, "完成");
            AddLog("-----------------------------------", LogType.Log, true);
            AddLog($"目录文件复制完毕: {src} -> {dst}", LogType.Log, true);
            EditorApplication.delayCall += () =>
            {
                AssetDatabase.Refresh();
                Debug.Log("已刷新资源文件夹");
            };
        }

        private async Task ReplaceGuidsInFilesAsync(
            string directory,
            Dictionary<string, string> guidMap,
            CancellationToken ct
        )
        {
            if (guidMap.Count == 0)
            {
                AddLog("没有需要更新的GUID");
                return;
            }
            AddLog($"开始更新GUID引用，共有 {guidMap.Count} 个GUID需要替换");
            var allFiles = await Task.Run(() => GetFilesRecursively(directory), ct);

            foreach (var fileToModify in allFiles)
            {
                ct.ThrowIfCancellationRequested();
                bool isBinary = await Task.Run(() => CheckForBinary(fileToModify), ct);
                if (isBinary)
                {
                    AddLog($"跳过二进制文件: {Path.GetFileName(fileToModify)}");
                    continue;
                }
                try
                {
                    string content = File.ReadAllText(fileToModify);
                    bool changed = false;
                    foreach (var kv in guidMap)
                    {
                        string old = content;
                        content = content.Replace(kv.Key, kv.Value);
                        if (old != content)
                        {
                            changed = true;
                            AddLog(
                                $"替换GUID: {Path.GetFileName(fileToModify)} ({kv.Key} -> {kv.Value})"
                            );
                        }
                    }
                    if (changed)
                    {
                        File.WriteAllText(fileToModify, content);
                        AddLog($"更新文件: {Path.GetFileName(fileToModify)}");
                    }
                }
                catch (Exception ex)
                {
                    AddLog(
                        $"处理文件时出错 {Path.GetFileName(fileToModify)}: {ex.Message}",
                        LogType.Error
                    );
                }
            }
        }

        private void CopyDirectoryRecursively(string src, string dst, CancellationToken ct)
        {
            string[] excludeExt = includeCodes ? Array.Empty<string>() : GetCodeExtensions();
            var dir = new DirectoryInfo(src);
            if (!Directory.Exists(dst))
                Directory.CreateDirectory(dst);

            foreach (var file in dir.GetFiles())
            {
                ct.ThrowIfCancellationRequested();
                string ext = file.Extension.Trim().ToLower();
                if (excludeExt.Contains(ext))
                {
                    AddLog($"排除文件: {file.Name} (类型: {ext})");
                    continue;
                }
                if (ext == ".meta")
                {
                    string baseExt = Path.GetExtension(
                            file.FullName.Substring(0, file.FullName.Length - 5)
                        )
                        .ToLower();
                    if (excludeExt.Contains(baseExt))
                    {
                        AddLog($"排除文件: {file.Name} (关联文件类型: {baseExt})");
                        continue;
                    }
                }
                file.CopyTo(Path.Combine(dst, file.Name), true);
                AddLog($"复制文件: {file.Name}");
            }
            foreach (var subdir in dir.GetDirectories())
            {
                ct.ThrowIfCancellationRequested();
                CopyDirectoryRecursively(subdir.FullName, Path.Combine(dst, subdir.Name), ct);
            }
        }

        private static List<string> GetFilesRecursively(
            string path,
            Func<string, bool> criteria = null,
            List<string> files = null
        )
        {
            files ??= new List<string>();
            try
            {
                var current = Directory.GetFiles(path);
                files.AddRange(criteria == null ? current : current.Where(criteria));
                foreach (var dir in Directory.GetDirectories(path))
                    GetFilesRecursively(dir, criteria, files);
            }
            catch (Exception ex)
            {
                Debug.LogError($"获取文件列表时出错: {path}, {ex.Message}");
            }
            return files;
        }

        private static bool CheckForBinary(string filePath)
        {
            try
            {
                using (var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read))
                {
                    var buffer = new byte[8192];
                    int bytesRead = stream.Read(buffer, 0, buffer.Length);
                    for (int i = 0; i < bytesRead; i++)
                    {
                        byte b = buffer[i];
                        if (b == 0 || b > 127)
                            return true;
                    }
                }
                return false;
            }
            catch (Exception ex)
            {
                Debug.LogError($"检查文件类型时出错: {filePath}, {ex.Message}");
                return true;
            }
        }
    }
}
