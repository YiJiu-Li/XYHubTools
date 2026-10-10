using System;
using System.IO;
using System.Security.Cryptography;
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
        private static ListRequest listRequest;
        private static AddRequest addRequest;
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
            var existing = UnityEditor.PackageManager.PackageInfo.FindForPackageName(PackageName);
            if (existing != null)
            {
                Status = "Pipeline 已安装：" + existing.version + "（保留现有安装）";
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
                        Status = "Pipeline 已安装：" + package.version + "（保留现有安装）";
                        EditorApplication.update -= Poll;
                        return;
                    }
                    InstallBundle();
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

        private static void InstallBundle()
        {
            var xy = UnityEditor.PackageManager.PackageInfo.FindForPackageName("com.yzj.xyhubtools");
            var xyRoot = xy != null ? xy.resolvedPath : Path.Combine(Application.dataPath, "XYHubTools");
            var archive = Path.Combine(xyRoot, "Pipeline~", "pipeline.tgz");
            if (!File.Exists(archive)) throw new FileNotFoundException("XY 工具包缺少 Pipeline~/pipeline.tgz");
            string digest;
            using (var sha = SHA256.Create())
            using (var stream = File.OpenRead(archive))
                digest = BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
            // Project-relative manifest entry survives moving/cloning the project. Commit this
            // archive together with manifest.json and packages-lock.json for team use.
            var relative = "XYBridge~/pipeline-" + digest + ".tgz";
            var target = Path.Combine(Path.GetDirectoryName(Application.dataPath), "Packages", relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target));
            File.Copy(archive, target, true);
            Status = "正在安装 XY 随附的 Pipeline";
            addRequest = Client.Add("file:" + relative);
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
