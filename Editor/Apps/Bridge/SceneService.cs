// ═══════════════════════════════════════════════════════════════
//  SceneService — 场景层级 + Play Mode 控制
// ═══════════════════════════════════════════════════════════════

using System;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Framework.XYEditor.Bridge
{
    internal static class SceneService
    {
        [Serializable]
        internal class GameObjectInfo
        {
            public string name;
            public int instanceId;
            public bool activeInHierarchy;
            public string tag;
            public int layer;
            public string[] components;
            public GameObjectInfo[] children;
        }

        [Serializable]
        internal class SceneGraphResult
        {
            public string sceneName;
            public string scenePath;
            public int rootCount;
            public int totalCount;
            public GameObjectInfo[] roots;
        }

        /// <summary>获取场景层级结构（指定深度，0=无限）</summary>
        internal static string GetGraph(int maxDepth)
        {
            var scene = EditorSceneManager.GetActiveScene();
            var roots = scene.GetRootGameObjects();
            var result = new SceneGraphResult
            {
                sceneName = scene.name,
                scenePath = scene.path ?? "",
                rootCount = roots.Length,
            };

            var list = new List<GameObjectInfo>(roots.Length);
            int counter = 0;
            foreach (var go in roots)
            {
                var info = BuildGameObjectInfo(
                    go,
                    maxDepth > 0 ? maxDepth : int.MaxValue,
                    ref counter
                );
                if (info != null)
                    list.Add(info);
            }
            result.roots = list.ToArray();
            result.totalCount = counter;
            return JsonUtility.ToJson(result);
        }

        private static GameObjectInfo BuildGameObjectInfo(
            GameObject go,
            int remainingDepth,
            ref int counter
        )
        {
            if (go == null)
                return null;
            counter++;

            var components = go.GetComponents<Component>();
            var compNames = new List<string>(components.Length);
            foreach (var c in components)
            {
                if (c != null)
                    compNames.Add(c.GetType().Name);
            }

            var info = new GameObjectInfo
            {
                name = go.name,
                instanceId = go.GetInstanceID(),
                activeInHierarchy = go.activeInHierarchy,
                tag = go.tag,
                layer = go.layer,
                components = compNames.ToArray(),
            };

            if (remainingDepth > 1 && go.transform.childCount > 0)
            {
                var children = new List<GameObjectInfo>(go.transform.childCount);
                for (int i = 0; i < go.transform.childCount; i++)
                {
                    var child = BuildGameObjectInfo(
                        go.transform.GetChild(i).gameObject,
                        remainingDepth - 1,
                        ref counter
                    );
                    if (child != null)
                        children.Add(child);
                }
                info.children = children.ToArray();
            }

            return info;
        }

        /// <summary>获取根 GameObject 名称列表（轻量版）</summary>
        internal static string GetRootNames()
        {
            var scene = EditorSceneManager.GetActiveScene();
            var roots = scene.GetRootGameObjects();
            var names = new string[roots.Length];
            for (int i = 0; i < roots.Length; i++)
                names[i] = roots[i] != null ? roots[i].name : null;
            var sb = new StringBuilder();
            sb.Append("{\"scene\":\"");
            sb.Append(scene.name);
            sb.Append("\",\"roots\":[");
            for (int i = 0; i < names.Length; i++)
            {
                if (i > 0)
                    sb.Append(',');
                sb.Append('"');
                sb.Append(names[i] ?? "null");
                sb.Append('"');
            }
            sb.Append("]}");
            return sb.ToString();
        }

        /// <summary>Play Mode 控制：enter / exit / toggle</summary>
        internal static string ControlPlayMode(string action)
        {
            switch (action ?? "toggle")
            {
                case "enter":
                    if (!EditorApplication.isPlaying)
                    {
                        EditorApplication.isPlaying = true;
                        return "{\"action\":\"enter\",\"state\":\"starting\"}";
                    }
                    return "{\"action\":\"enter\",\"state\":\"already_playing\"}";
                case "exit":
                    if (EditorApplication.isPlaying)
                    {
                        EditorApplication.isPlaying = false;
                        return "{\"action\":\"exit\",\"state\":\"stopping\"}";
                    }
                    return "{\"action\":\"exit\",\"state\":\"already_stopped\"}";
                case "toggle":
                default:
                    EditorApplication.isPlaying = !EditorApplication.isPlaying;
                    return EditorApplication.isPlaying
                        ? "{\"action\":\"toggle\",\"state\":\"entering_play\"}"
                        : "{\"action\":\"toggle\",\"state\":\"exiting_play\"}";
            }
        }
    }
}
