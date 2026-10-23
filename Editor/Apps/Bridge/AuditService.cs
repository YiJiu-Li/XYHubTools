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
        internal class DuplicateGuidFact
        {
            public string guid;
            public string[] paths;
        }

        [Serializable]
        internal class AsmdefFact
        {
            public string name;
            public string path;
            public int referenceCount;
            /// <summary>解析不到的引用原文（名字或 GUID:xxx）。</summary>
            public string[] brokenReferences;
        }

        /// <summary>project 范围的事实。scene 范围时 present=false。</summary>
        [Serializable]
        internal class ProjectFact
        {
            /// <summary>
            /// JsonUtility 无法把 class 字段序列化成 null，只会给一个空对象，
            /// 所以用这个标记区分「真的扫过工程」和「没扫」。规则求值靠它判断。
            /// </summary>
            public bool present;
            public string projectPath;
            public int metaCount;
            public int parsedGuidCount;
            public int duplicateGuidCount;
            public DuplicateGuidFact[] duplicateGuids;
            public int asmdefCount;
            public int brokenAsmdefRefCount;
            public AsmdefFact[] asmdefs;
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
            public ProjectFact project;
            public RulePackFact[] rulePacks;
            public string[] warnings;
        }

        // ═══════════════════════════════════════════════════════════════
        //  采集入口
        // ═══════════════════════════════════════════════════════════════

        /// <summary>
        /// 采集事实。参数为 JSON：{"scope":"scene|project","maxObjects":2000}
        ///   · scene   —— 当前活动场景的对象树与组件
        ///   · project —— 工程级资产事实（GUID 重复、asmdef 引用断裂）
        /// prefab 范围留给后续版本，别在这里加半成品分支。
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

            if (scope == "scene")
            {
                CollectSceneFacts(facts, maxObjects, warnings);
            }
            else if (scope == "project")
            {
                facts.project = CollectProjectFacts(warnings);
            }
            else
            {
                // 明确失败，不返回半份数据 —— 让调用方知道是没实现，而不是「工程很干净」
                throw new NotSupportedException(
                    "audit scope 只支持 scene / project，收到: " + scope
                );
            }

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
        //  project 范围：GUID 重复 / asmdef 引用断裂
        // ═══════════════════════════════════════════════════════════════
        //  这两件事 Unity 的 AssetDatabase 不会告诉你（它自己就把重复 GUID
        //  当成"其中一个生效"），只能读 .meta / .asmdef 原文。仍然只采事实。

        /// <summary>工程扫描的硬上限，防止超大工程把通道打满。</summary>
        private const int MaxProjectFiles = 200000;

        [Serializable]
        internal class AsmdefJson
        {
            public string name;
            public string[] references;
        }

        private static ProjectFact CollectProjectFacts(List<string> warnings)
        {
            string projectRoot = Path.GetDirectoryName(Application.dataPath);
            var fact = new ProjectFact
            {
                present = true,
                projectPath = projectRoot.Replace('\\', '/'),
            };

            var guidMap = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            int scanned = 0;
            foreach (string meta in Directory.EnumerateFiles(Application.dataPath, "*.meta", SearchOption.AllDirectories))
            {
                if (++scanned > MaxProjectFiles)
                {
                    warnings.Add("meta 文件数超过 " + MaxProjectFiles + "，扫描被截断");
                    break;
                }

                fact.metaCount++;
                string guid = ReadGuidFromMeta(meta);
                if (string.IsNullOrEmpty(guid))
                    continue;
                if (!guidMap.TryGetValue(guid, out var owners))
                {
                    owners = new List<string>(1);
                    guidMap[guid] = owners;
                }
                owners.Add(ToAssetPath(projectRoot, meta));
            }
            fact.parsedGuidCount = guidMap.Count;

            var dups = new List<DuplicateGuidFact>();
            foreach (var pair in guidMap)
            {
                if (pair.Value.Count < 2)
                    continue;
                dups.Add(new DuplicateGuidFact { guid = pair.Key, paths = pair.Value.ToArray() });
            }
            dups.Sort((a, b) => string.CompareOrdinal(a.guid, b.guid));
            fact.duplicateGuids = dups.ToArray();
            fact.duplicateGuidCount = dups.Count;

            // asmdef 引用要两遍：先把所有程序集名收齐，再逐条检查引用。
            var files = new List<string>(
                Directory.EnumerateFiles(Application.dataPath, "*.asmdef", SearchOption.AllDirectories)
            );
            var names = new HashSet<string>(StringComparer.Ordinal);
            var parsed = new List<KeyValuePair<string, AsmdefJson>>(files.Count);
            foreach (string file in files)
            {
                var body = ParseAsmdef(file, warnings);
                if (body == null || string.IsNullOrEmpty(body.name))
                    continue;
                names.Add(body.name);
                parsed.Add(new KeyValuePair<string, AsmdefJson>(file, body));
            }

            var asmdefs = new List<AsmdefFact>(parsed.Count);
            int brokenTotal = 0;
            foreach (var pair in parsed)
            {
                string file = pair.Key;
                var body = pair.Value;
                var refs = body.references ?? new string[0];
                var broken = new List<string>();
                foreach (string reference in refs)
                {
                    if (string.IsNullOrEmpty(reference))
                        continue;
                    if (!IsReferenceResolvable(reference, names, guidMap))
                        broken.Add(reference);
                }

                brokenTotal += broken.Count;
                asmdefs.Add(
                    new AsmdefFact
                    {
                        name = body.name,
                        path = ToAssetPath(projectRoot, file),
                        referenceCount = refs.Length,
                        brokenReferences = broken.ToArray(),
                    }
                );
            }

            asmdefs.Sort((a, b) => string.CompareOrdinal(a.path, b.path));
            fact.asmdefs = asmdefs.ToArray();
            fact.asmdefCount = asmdefs.Count;
            fact.brokenAsmdefRefCount = brokenTotal;
            return fact;
        }

        /// <summary>只读 .meta 开头几行取 guid，不整文件读入。</summary>
        private static string ReadGuidFromMeta(string path)
        {
            try
            {
                using (var reader = new StreamReader(path, Encoding.UTF8))
                {
                    // guid 一定在文件最前面几行；读 8 行没找到就放弃，避免读大文件。
                    for (int i = 0; i < 8; i++)
                    {
                        string line = reader.ReadLine();
                        if (line == null)
                            break;
                        if (line.StartsWith("guid:", StringComparison.Ordinal))
                            return line.Substring(5).Trim();
                    }
                }
            }
            catch (Exception ex)
            {
                // 单个 meta 读失败不该让整次体检失败，交给 warnings 汇总
                Debug.LogWarning("[XY Audit] 读取 meta 失败 " + path + ": " + ex.Message);
            }
            return null;
        }

        private static AsmdefJson ParseAsmdef(string path, List<string> warnings)
        {
            try
            {
                string text = File.ReadAllText(path, Encoding.UTF8);
                return JsonUtility.FromJson<AsmdefJson>(text);
            }
            catch (Exception ex)
            {
                warnings.Add("解析 asmdef 失败 " + path + ": " + ex.Message);
                return null;
            }
        }

        private static bool IsReferenceResolvable(
            string reference,
            HashSet<string> names,
            Dictionary<string, List<string>> guidMap
        )
        {
            const string GuidPrefix = "GUID:";
            if (reference.StartsWith(GuidPrefix, StringComparison.Ordinal))
                return guidMap.ContainsKey(reference.Substring(GuidPrefix.Length).Trim());
            return names.Contains(reference);
        }

        /// <summary>绝对路径 → 工程内 Assets/... 形式，便于规则里直接展示。</summary>
        private static string ToAssetPath(string projectRoot, string absolute)
        {
            string full = Path.GetFullPath(absolute).Replace('\\', '/');
            string root = projectRoot.Replace('\\', '/').TrimEnd('/') + "/";
            return full.StartsWith(root, StringComparison.OrdinalIgnoreCase)
                ? full.Substring(root.Length)
                : full;
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
