using System;
using System.IO;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using UnityEngine;

namespace XY.PipelineBootstrap
{
    // Separate assembly: bootstrapping must not depend on Pipeline or XY's compiler DLLs.
    [InitializeOnLoad]
    public static class PipelineInstaller
    {
        private const string PackageName = "com.unity.pipeline";
        private const string AttemptKey = "XY.PipelineBootstrap.Attempted";

        /// <summary>
        /// 按包名查 PackageInfo。
        /// 不能用 PackageInfo.FindForPackageName —— 那是 Unity 2022.2 才加的 API，
        /// 2021.3 上没有（CS0117）。GetAllRegisteredPackages 从 2020.1 就有，
        /// 两个版本都能跑。
        /// </summary>
        private static UnityEditor.PackageManager.PackageInfo FindPackage(string name)
        {
            // 全限定：UnityEditor 里也有个 PackageInfo，直接写 PackageInfo 会 CS0104 撞名
            var packages = UnityEditor.PackageManager.PackageInfo.GetAllRegisteredPackages();
            for (int i = 0; i < packages.Length; i++)
            {
                if (packages[i] != null && packages[i].name == name)
                    return packages[i];
            }
            return null;
        }

        private static ListRequest listRequest;
        private static AddRequest addRequest;
        [Serializable] private class Bundle { public string name, version, unity, sha256, upstreamVersion, upstreamUnity; }
        [Serializable] private class PackageMetadata { public string unity; }
        [Serializable] private class Rollback { public string previousReference, targetReference; }
        private static string ProjectRoot => Path.GetDirectoryName(Application.dataPath);
        private static string RollbackDirectory => Path.Combine(ProjectRoot, "ProjectSettings", "XYBridgePipelineRollback");
        private static string ManifestPath => Path.Combine(ProjectRoot, "Packages", "manifest.json");
        public static string Status { get; private set; } = "等待检查 Pipeline";

        static PipelineInstaller()
        {
            EditorApplication.delayCall += Check;
        }

        [MenuItem("Tools/XY Hub/检查或安装 Pipeline")]
        public static void Retry()
        {
            if (listRequest != null || addRequest != null) return;
            SessionState.EraseBool(AttemptKey);
            Check();
        }

        private static void Check()
        {
            if (AssetDatabase.IsAssetImportWorkerProcess()) return;
            var existing = FindPackage(PackageName);
            if (existing != null)
            {
                DescribeInstalled(existing);
                return;
            }
            if (SessionState.GetBool(AttemptKey, false)) return;
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                EditorApplication.delayCall += Check;
                return;
            }
            SessionState.SetBool(AttemptKey, true);
            try
            {
                Status = "检查已安装的 Pipeline";
                listRequest = Client.List(true, true);
                EditorApplication.update += Poll;
            }
            catch (Exception e) { Fail(e.Message); }
        }

        private static void Poll()
        {
            try
            {
                if (listRequest != null)
                {
                    if (!listRequest.IsCompleted) return;
                    var completed = listRequest;
                    listRequest = null;
                    if (completed.Status != StatusCode.Success)
                    {
                        Fail(completed.Error.message);
                        return;
                    }
                    foreach (var package in completed.Result)
                    {
                        if (package.name != PackageName) continue;
                        DescribeInstalled(package);
                        EditorApplication.update -= Poll;
                        return;
                    }
                    InstallDefault();
                }
                if (addRequest == null || !addRequest.IsCompleted) return;
                var added = addRequest;
                addRequest = null;
                EditorApplication.update -= Poll;
                if (added.Status == StatusCode.Success)
                {
                    Status = "Pipeline 已安装：" + added.Result.version;
                    Debug.Log("[XY Pipeline] " + Status + "，MCP 可直接使用 HTTP，无需 Unity CLI。");
                }
                else Fail(added.Error.message);
            }
            catch (Exception e) { Fail(e.Message); }
        }

        private static string BundleRoot()
        {
            var xy = FindPackage("com.yzj.xyhubtools");
            return Path.Combine(xy != null ? xy.resolvedPath : Path.Combine(Application.dataPath, "XYHubTools"), "Pipeline~");
        }

        private static void InstallDefault()
        {
            var metadata = JsonUtility.FromJson<Bundle>(File.ReadAllText(Path.Combine(BundleRoot(), "bundle.json")));
            if (int.Parse(Application.unityVersion.Split('.')[0]) >= 6000)
            {
                RequireUnity(metadata.upstreamUnity ?? "6000.0");
                ValidateOfficialVersion(metadata.upstreamVersion);
                BeginInstall(PackageName + "@" + metadata.upstreamVersion);
            }
            else InstallBundle();
        }

        private static void InstallBundle()
        {
            var archive = Path.Combine(BundleRoot(), "pipeline.tgz");
            if (!File.Exists(archive)) throw new FileNotFoundException("XY 工具包缺少 Pipeline~/pipeline.tgz");
            var metadata = JsonUtility.FromJson<Bundle>(File.ReadAllText(Path.Combine(BundleRoot(), "bundle.json")));
            if (metadata == null || metadata.name != PackageName || string.IsNullOrEmpty(metadata.version))
                throw new InvalidDataException("Pipeline bundle.json 格式无效");
            RequireUnity(metadata.unity);
            string digest;
            using (var sha = SHA256.Create())
            using (var stream = File.OpenRead(archive))
                digest = BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
            if (!string.Equals(digest, metadata.sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Pipeline 随附包校验失败：SHA-256 与 bundle.json 不一致");
            // Project-relative manifest entry survives moving/cloning the project. Commit this
            // archive together with manifest.json and packages-lock.json for team use.
            var relative = "XYBridge~/pipeline-" + digest + ".tgz";
            var target = Path.Combine(Path.GetDirectoryName(Application.dataPath), "Packages", relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target));
            File.Copy(archive, target, true);
            Status = "正在安装 XY 随附的 Pipeline";
            BeginInstall("file:" + relative);
        }

        // Explicit entry points for automation. Never upgrade a project on editor startup.
        // Official packages are left intact; UPM resolves their declared dependencies.
        public static void InstallOfficialVersion(string version)
        {
            RunExplicit(() =>
            {
                ValidateOfficialVersion(version);
                RequireUnity("6000.0");
                BeginInstall(PackageName + "@" + version);
            });
        }

        private static void ValidateOfficialVersion(string version)
        {
            if (string.IsNullOrEmpty(version) || !Regex.IsMatch(version, @"^\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?$"))
                throw new ArgumentException("必须指定完整官方版本，不能使用 latest 或版本范围");
        }

        [MenuItem("Tools/XY Hub/Pipeline/升级到随附兼容版本")]
        public static void UpgradeBundledVersion() => RunExplicit(InstallBundle);

        [MenuItem("Tools/XY Hub/Pipeline/回退上次包切换")]
        public static void RollbackVersion()
        {
            RunExplicit(() =>
            {
                var saved = JsonUtility.FromJson<Rollback>(File.ReadAllText(Path.Combine(RollbackDirectory, "state.json")));
                if (saved == null || string.IsNullOrEmpty(saved.previousReference))
                    throw new InvalidOperationException("没有可回退的 Pipeline 版本");
                if (CurrentReference() != saved.targetReference)
                    throw new InvalidOperationException("Pipeline 引用已在其他位置改变，请根据备份手动处理，避免覆盖后续修改");
                BeginInstall(saved.previousReference, false);
            });
        }

        private static void RunExplicit(Action action)
        {
            if (listRequest != null || addRequest != null || EditorApplication.isCompiling || EditorApplication.isUpdating)
                throw new InvalidOperationException("Unity 或包管理器正忙，请稍后重试");
            RejectEmbeddedPackage();
            try { action(); }
            catch (Exception e) { Fail(e.Message); throw; }
        }

        private static void RejectEmbeddedPackage()
        {
            // Unity's virtual package filesystem can make cached packages appear here too.
            // Use UPM's source identity, not Directory.Exists, to distinguish embedded source.
            var package = FindPackage(PackageName);
            if (package != null && package.source == PackageSource.Embedded)
                throw new InvalidOperationException("当前为嵌入式 Pipeline 源码包。请先备份并迁出 Packages/com.unity.pipeline，再通过 UPM 安装；不会自动删除源码");
        }

        private static string CurrentReference()
        {
            var match = Regex.Match(File.ReadAllText(ManifestPath), "\"com\\.unity\\.pipeline\"\\s*:\\s*\"([^\"]+)\"");
            if (!match.Success) return null;
            var value = match.Groups[1].Value;
            return Regex.IsMatch(value, @"^\d+\.\d+\.\d+") ? PackageName + "@" + value : value;
        }

        private static void BeginInstall(string reference, bool saveRollback = true)
        {
            RejectEmbeddedPackage();
            var previous = CurrentReference();
            if (previous == reference)
            {
                Status = "Pipeline 已使用目标引用：" + reference;
                EditorApplication.update -= Poll;
                return;
            }
            if (saveRollback && !string.IsNullOrEmpty(previous))
            {
                Directory.CreateDirectory(RollbackDirectory);
                File.Copy(ManifestPath, Path.Combine(RollbackDirectory, "manifest.before.json"), true);
                var lockFile = Path.Combine(ProjectRoot, "Packages", "packages-lock.json");
                if (File.Exists(lockFile)) File.Copy(lockFile, Path.Combine(RollbackDirectory, "packages-lock.before.json"), true);
                File.WriteAllText(Path.Combine(RollbackDirectory, "state.json"), JsonUtility.ToJson(new Rollback
                { previousReference = previous, targetReference = reference }, true));
            }
            Status = "正在安装 Pipeline：" + reference;
            addRequest = Client.Add(reference);
            EditorApplication.update -= Poll;
            EditorApplication.update += Poll;
        }

        private static void RequireUnity(string minimum)
        {
            if (string.IsNullOrEmpty(minimum)) throw new InvalidDataException("包缺少最低 Unity 版本声明");
            var required = Regex.Match(minimum, @"^(\d+)\.(\d+)");
            var current = Regex.Match(Application.unityVersion, @"^(\d+)\.(\d+)");
            if (!required.Success || !current.Success) throw new InvalidDataException("无法解析 Unity 版本");
            var requiredVersion = new Version(int.Parse(required.Groups[1].Value), int.Parse(required.Groups[2].Value));
            var currentVersion = new Version(int.Parse(current.Groups[1].Value), int.Parse(current.Groups[2].Value));
            if (currentVersion < requiredVersion)
                throw new InvalidOperationException("此包至少需要 Unity " + minimum + "，当前为 " + Application.unityVersion);
        }

        private static void DescribeInstalled(UnityEditor.PackageManager.PackageInfo package)
        {
            Status = "Pipeline 已安装：" + package.version + "（" + package.source + "，保留现有安装）";
            try
            {
                var metadata = JsonUtility.FromJson<PackageMetadata>(File.ReadAllText(Path.Combine(package.resolvedPath, "package.json")));
                RequireUnity(metadata?.unity);
            }
            catch (Exception e) { Status += "；兼容性提示：" + e.Message; }
        }

        private static void Fail(string message)
        {
            listRequest = null;
            addRequest = null;
            EditorApplication.update -= Poll;
            Status = "Pipeline 安装失败：" + message;
            Debug.LogError("[XY Pipeline] " + Status + "。可通过 Tools/XY Hub/检查或安装 Pipeline 重试。");
        }
    }
}
