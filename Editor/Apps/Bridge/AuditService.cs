// ═══════════════════════════════════════════════════════════════
//  AuditService — 体检器的「事实采集」层
// ═══════════════════════════════════════════════════════════════
//  设计原则（重要，别改）：
//    这一层**只记录看到什么，不判断对错**。
//      · 采集：场景里有哪些对象、每个对象挂了哪些组件、哪些组件是 missing
//      · 不采集判断结果，不写 severity，不拼错误文案
//    判断由规则包（JSON）在 MCP 侧完成 —— 这样加规则不用重编译 Unity 程序集，
//    也不会把规则散落到 C# 里。
//
//  规则包随包分发在 Agent~/rules/<pack>/rules.json，本层只负责把原文读出来塞进
//  返回结果（Unity 不解释规则内容）。
// ═══════════════════════════════════════════════════════════════

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;

namespace Framework.XYEditor.Bridge
{
    /// <summary>
    /// 只采集事实、不做判断。public 是为了让用户自己的编辑器脚本也能直接调用
    /// （例如在 CI 里跑一遍 AuditService.CollectFacts 存档）。
    /// </summary>
    public static class AuditService
    {
        /// <summary>单次体检最多采集的对象数，超出置 truncated，避免大场景把通道打满。</summary>
        private const int DefaultMaxObjects = 2000;

        // ───────────────── 事实结构（字段名即 JSON 字段名）─────────────────

        [Serializable]
        internal class ComponentFact
        {
            public string type;
            public bool missing;
            public bool enabled;
            /// <summary>在 GameObject 组件列表中的位置，用于定位缺失项。</summary>
            public int index;
        }

        [Serializable]
        internal class ObjectFact
        {
            public string path;
            public bool active;
            public string tag;
            public int layer;
            public int componentCount;
            /// <summary>missing 组件数（Unity 官方口径，等价于丢失脚本的 MonoBehaviour 个数）。</summary>
            public int missingScripts;
            public ComponentFact[] components;
        }

        [Serializable]
        internal class RulePackFact
        {
            public string name;
            public string path;
            /// <summary>rules.json 原文。Unity 不解析它。</summary>
            public string json;
        }

        [Serializable]
        internal class AuditFacts
        {
            public string scope;
            public string scene;
            public string scenePath;
            public bool isPlaying;
            public bool dirty;
            public string unityVersion;
            public int objectCount;
            public int maxObjects;
            public bool truncated;
            public ObjectFact[] objects;
            public RulePackFact[] rulePacks;
            public string[] warnings;
        }

        // ═══════════════════════════════════════════════════════════════
        //  采集入口
        // ═══════════════════════════════════════════════════════════════

        /// <summary>
        /// 采集**场景**事实。参数为 JSON：{"scope":"scene","maxObjects":2000}
        /// 工程资产事实（GUID 重复 / asmdef 断裂 / 悬空引用）不在这里做 ——
        /// 那些是纯文件内容，MCP 侧直接读文件，不需要打开编辑器。
        /// </summary>
        public static string CollectFacts(string argsJson)
        {
            string scope = "scene";
            int maxObjects = DefaultMaxObjects;
            if (!string.IsNullOrEmpty(argsJson))
            {
                var args = JsonUtility.FromJson<AuditArgs>(argsJson);
                if (args != null)
                {
                    if (!string.IsNullOrEmpty(args.scope))
                        scope = args.scope;
                    if (args.maxObjects > 0)
                        maxObjects = args.maxObjects;
                }
            }

            var warnings = new List<string>();
            var facts = new AuditFacts
            {
                scope = scope,
                isPlaying = EditorApplication.isPlaying,
                unityVersion = Application.unityVersion,
                maxObjects = maxObjects,
            };

            if (scope != "scene")
            {
                // 明确失败，不返回半份数据 —— 让调用方知道是没实现，而不是「工程很干净」
                throw new NotSupportedException(
                    "AuditService 只采集 scene 事实，收到: " + scope
                );
            }
            CollectSceneFacts(facts, maxObjects, warnings);

            facts.rulePacks = LoadRulePacks(warnings).ToArray();
            facts.warnings = warnings.ToArray();
            return JsonUtility.ToJson(facts);
        }

        private static void CollectSceneFacts(AuditFacts facts, int maxObjects, List<string> warnings)
        {
            var scene = EditorSceneManager.GetActiveScene();
            var roots = scene.GetRootGameObjects();

            var objects = new List<ObjectFact>(Math.Min(roots.Length * 4, maxObjects));
            bool truncated = false;
            foreach (var root in roots)
            {
                if (root == null)
                    continue;
                CollectRecursive(root.transform, "", objects, maxObjects, ref truncated);
                if (truncated)
                    break;
            }

            facts.scene = scene.name;
            facts.scenePath = scene.path ?? "";
            facts.dirty = scene.isDirty;
            facts.objectCount = objects.Count;
            facts.truncated = truncated;
            facts.objects = objects.ToArray();

            if (truncated)
                warnings.Add(
                    "对象数超过 maxObjects=" + maxObjects + "，结果不完整；缩小场景或调大上限"
                );
        }

        [Serializable]
        internal class AuditArgs
        {
            public string scope;
            public int maxObjects;
        }

        private static void CollectRecursive(
            Transform transform,
            string parentPath,
            List<ObjectFact> output,
            int maxObjects,
            ref bool truncated
        )
        {
            if (transform == null || truncated)
                return;
            if (output.Count >= maxObjects)
            {
                truncated = true;
                return;
            }

            var go = transform.gameObject;
            string path = string.IsNullOrEmpty(parentPath)
                ? go.name
                : parentPath + "/" + go.name;

            output.Add(BuildObjectFact(go, path));

            int childCount = transform.childCount;
            for (int i = 0; i < childCount; i++)
            {
                if (truncated)
                    return;
                CollectRecursive(transform.GetChild(i), path, output, maxObjects, ref truncated);
            }
        }

        private static ObjectFact BuildObjectFact(GameObject go, string path)
        {
            // GetComponents<Component>() 对丢失脚本会返回 null 占位项 —— 这正是要记录的事实。
            var all = go.GetComponents<Component>();
            var list = new List<ComponentFact>(all.Length);
            int missing = 0;
            for (int i = 0; i < all.Length; i++)
            {
                var component = all[i];
                if (component == null)
                {
                    missing++;
                    list.Add(
                        new ComponentFact
                        {
                            type = "<missing>",
                            missing = true,
                            enabled = false,
                            index = i,
                        }
                    );
                    continue;
                }

                var behaviour = component as Behaviour;
                list.Add(
                    new ComponentFact
                    {
                        type = component.GetType().FullName,
                        missing = false,
                        enabled = behaviour == null || behaviour.enabled,
                        index = i,
                    }
                );
            }

            return new ObjectFact
            {
                path = path,
                active = go.activeInHierarchy,
                tag = go.tag,
                layer = go.layer,
                componentCount = all.Length,
                // 用官方计数做交叉校验：两个口径不一致时，规则里以 missingScripts 为准
                missingScripts = missing,
                components = list.ToArray(),
            };
        }

        // ═══════════════════════════════════════════════════════════════
        //  规则包读取（只读原文，不解析）
        // ═══════════════════════════════════════════════════════════════

        private static List<RulePackFact> LoadRulePacks(List<string> warnings)
        {
            var result = new List<RulePackFact>();
            string rulesRoot;
            try
            {
                rulesRoot = ResolveRulesRoot();
            }
            catch (Exception ex)
            {
                warnings.Add("无法定位规则包目录: " + ex.Message);
                return result;
            }

            if (rulesRoot == null || !Directory.Exists(rulesRoot))
            {
                warnings.Add("规则包目录不存在: " + (rulesRoot ?? "(未解析)"));
                return result;
            }

            foreach (string packDir in Directory.GetDirectories(rulesRoot))
            {
                string file = Path.Combine(packDir, "rules.json");
                if (!File.Exists(file))
                    continue;
                try
                {
                    result.Add(
                        new RulePackFact
                        {
                            name = Path.GetFileName(packDir),
                            path = file.Replace('\\', '/'),
                            json = File.ReadAllText(file, Encoding.UTF8),
                        }
                    );
                }
                catch (Exception ex)
                {
                    warnings.Add("读取规则包失败 " + file + ": " + ex.Message);
                }
            }

            if (result.Count == 0)
                warnings.Add("未找到任何 rules.json（应在 Agent~/rules/<pack>/rules.json）");
            return result;
        }

        /// <summary>
        /// 优先按包的实际安装位置解析（UPM 安装时在缓存目录），
        /// 找不到再退回内嵌路径，最后退回工程内 Assets 路径。
        /// </summary>
        private static string ResolveRulesRoot()
        {
            try
            {
                var info = PackageInfo.FindForAssembly(typeof(AuditService).Assembly);
                if (info != null && !string.IsNullOrEmpty(info.resolvedPath))
                {
                    string path = Path.Combine(info.resolvedPath, "Agent~", "rules");
                    if (Directory.Exists(path))
                        return path;
                }
            }
            catch
            {
                // 包信息不可用时继续走后面的兜底
            }

            string embedded = Path.Combine(Application.dataPath, "XYHubTools", "Agent~", "rules");
            return Directory.Exists(embedded) ? embedded : embedded;
        }
    }
}
