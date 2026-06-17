// ═══════════════════════════════════════════════════════════════
//  ScreenshotService — Game/Scene 视图截图
// ═══════════════════════════════════════════════════════════════

using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace Framework.XYEditor.Bridge
{
    internal static class ScreenshotService
    {
        /// <summary>截取 Game 或 Scene 视图，返回 base64 PNG</summary>
        internal static string Capture(string target, int width, int height)
        {
            target = (target ?? "game").ToLowerInvariant();
            if (width <= 0)
                width = 1920;
            if (height <= 0)
                height = 1080;

            Texture2D tex = null;
            try
            {
                if (target == "scene")
                    tex = CaptureSceneView(width, height);
                else
                    tex = CaptureGameView(width, height);

                if (tex == null)
                    return "{\"error\":\"failed to capture " + target + " view\"}";

                byte[] png = tex.EncodeToPNG();
                string base64 = Convert.ToBase64String(png);
                return JsonUtility.ToJson(
                    new ScreenshotResult
                    {
                        target = target,
                        width = tex.width,
                        height = tex.height,
                        base64 = base64,
                    }
                );
            }
            catch (Exception ex)
            {
                return "{\"error\":\"" + ex.Message.Replace("\"", "\\\"") + "\"}";
            }
            finally
            {
                if (tex != null)
                    UnityEngine.Object.DestroyImmediate(tex);
            }
        }

        private static Texture2D CaptureGameView(int width, int height)
        {
            // 通过反射获取 GameView 的 RenderTexture 或直接读取像素
            var gameViewType = typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView");
            if (gameViewType == null)
                return null;

            var gameView = EditorWindow.GetWindow(gameViewType);
            if (gameView == null)
                return null;

            var oldSize = gameView.position;
            try
            {
                gameView.position = new Rect(oldSize.x, oldSize.y, width, height);
                gameView.Focus();

                var tex = new Texture2D(width, height, TextureFormat.RGB24, false);
                var pixelRect = gameViewType.GetField(
                    "m_Pos",
                    BindingFlags.NonPublic | BindingFlags.Instance
                );
                // Fallback: read from screen
                var rect = gameView.position;
                tex.ReadPixels(
                    new Rect(rect.x, rect.y + (rect.height - height), width, height),
                    0,
                    0
                );
                tex.Apply();
                return tex;
            }
            finally
            {
                gameView.position = oldSize;
            }
        }

        private static Texture2D CaptureSceneView(int width, int height)
        {
            var sceneView = SceneView.lastActiveSceneView;
            if (sceneView == null)
                return null;

            var camera = sceneView.camera;
            if (camera == null)
                return null;

            var oldTargetTexture = camera.targetTexture;
            try
            {
                var rt = RenderTexture.GetTemporary(width, height, 24);
                camera.targetTexture = rt;
                camera.Render();

                var tex = new Texture2D(width, height, TextureFormat.RGB24, false);
                RenderTexture.active = rt;
                tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                tex.Apply();
                RenderTexture.active = null;
                RenderTexture.ReleaseTemporary(rt);
                return tex;
            }
            finally
            {
                camera.targetTexture = oldTargetTexture;
            }
        }

        [Serializable]
        private class ScreenshotResult
        {
            public string target;
            public int width;
            public int height;
            public string base64;
        }

        /// <summary>
        /// 直接写到磁盘（避免 base64 6MB+ 卡 MCP 传输）。
        /// path 留空则写到项目根 TempScreenshots/{timestamp}.png。
        /// </summary>
        internal static string CaptureToFile(string target, string path)
        {
            target = (target ?? "game").ToLowerInvariant();
            int w = 1920,
                h = 1080;
            Texture2D tex = null;
            try
            {
                tex = target == "scene" ? CaptureSceneView(w, h) : CaptureGameView(w, h);
                if (tex == null)
                    return "{\"error\":\"failed to capture " + target + " view\"}";

                byte[] png = tex.EncodeToPNG();
                if (string.IsNullOrWhiteSpace(path))
                {
                    string root = System.IO.Path.GetDirectoryName(UnityEngine.Application.dataPath);
                    string dir = System.IO.Path.Combine(root, "TempScreenshots");
                    System.IO.Directory.CreateDirectory(dir);
                    path = System.IO.Path.Combine(
                        dir,
                        "shot_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".png"
                    );
                }
                System.IO.File.WriteAllBytes(path, png);
                return "{\"ok\":true,\"path\":\""
                    + path.Replace("\\", "\\\\").Replace("\"", "\\\"")
                    + "\",\"width\":"
                    + tex.width
                    + ",\"height\":"
                    + tex.height
                    + "}";
            }
            catch (Exception ex)
            {
                return "{\"error\":\"" + ex.Message.Replace("\"", "\\\"") + "\"}";
            }
            finally
            {
                if (tex != null)
                    UnityEngine.Object.DestroyImmediate(tex);
            }
        }
    }
}
