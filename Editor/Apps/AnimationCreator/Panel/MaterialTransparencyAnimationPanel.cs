using System;
using System.Collections.Generic;
using Framework.XYEditor;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using YZJ.AnimationCreator;

namespace YZJ
{
    /// <summary>
    /// 动画模板工具面板 —— 精简版，仅控制模板加载与动画创建。
    /// 所有动画参数（Shader 属性名、出现/消失配置等）均在模板 ScriptableObject 上编辑。
    /// </summary>
    public class MaterialTransparencyAnimationPanel : IXYPanel
    {
        // ── 模板 ──────────────────────────────────────────────────────────────
        private MaterialTransparencyAnimationTemplate _currentTemplate;

        // ── 保存路径 ──────────────────────────────────────────────────────────
        private string _animationFolder = "Assets/Animations";

        // ── 材质设置 ──────────────────────────────────────────────────────────
        private bool _animateAllMaterials = true;
        private int _specificMaterialIndex = 0;

        // ── 滚动位置 ──────────────────────────────────────────────────────────
        private Vector2 _scrollPosition;

        // ── 重绘回调 ──────────────────────────────────────────────────────────
        private readonly Action _repaint;

        public MaterialTransparencyAnimationPanel(Action repaint = null)
        {
            _repaint = repaint ?? (() => { });
        }

        public void Init()
        {
            _animationFolder = EditorPrefs.GetString("MatTransAnim_Folder", "Assets/Animations");
            _animateAllMaterials = EditorPrefs.GetBool("MatTransAnim_AllMaterials", true);
            _specificMaterialIndex = EditorPrefs.GetInt("MatTransAnim_MatIndex", 0);
        }

        public void Cleanup()
        {
            EditorPrefs.SetString("MatTransAnim_Folder", _animationFolder);
            EditorPrefs.SetBool("MatTransAnim_AllMaterials", _animateAllMaterials);
            EditorPrefs.SetInt("MatTransAnim_MatIndex", _specificMaterialIndex);
        }

        public void Draw(float containerWidth)
        {
            _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition);

            DrawHeader();
            DrawTemplateSection();
            DrawSavePathSection();
            DrawMaterialSettings();
            DrawCreateButtons();

            EditorGUILayout.EndScrollView();
        }

        #region UI Drawing

        private void DrawHeader()
        {
            EditorGUILayout.Space(5);
            GUILayout.Label("🎬 动画模板工具创建器", EditorStyles.boldLabel);

            var go = Selection.activeGameObject;
            if (go != null)
            {
                var renderer = go.GetComponent<Renderer>();
                var (meshCount, skinnedCount, spriteCount, totalMats) = CountRenderers(go);
                int totalRenderers = meshCount + skinnedCount + spriteCount;

                if (renderer != null || totalRenderers > 0)
                {
                    string info = $"当前选中: {go.name}";
                    if (renderer != null)
                        info += $"\n根物体材质数量: {renderer.sharedMaterials.Length}";

                    // 显示各类型渲染器数量
                    var rendererInfo = new System.Collections.Generic.List<string>();
                    if (meshCount > 0)
                        rendererInfo.Add($"Mesh:{meshCount}");
                    if (skinnedCount > 0)
                        rendererInfo.Add($"Skinned:{skinnedCount}");
                    if (spriteCount > 0)
                        rendererInfo.Add($"Sprite:{spriteCount}");
                    info +=
                        $"\nRenderer 总数: {totalRenderers} ({string.Join(", ", rendererInfo)})";
                    info += $"\n材质总数: {totalMats}";

                    EditorGUILayout.HelpBox(info, MessageType.Info);
                }
                else
                {
                    EditorGUILayout.HelpBox("选中的对象没有 Renderer 组件", MessageType.Warning);
                }
            }
            else
            {
                EditorGUILayout.HelpBox("请选择一个带有 Renderer 的对象", MessageType.Warning);
            }
            EditorGUILayout.Space(5);
        }

        /// <summary>统计所有支持的渲染器数量</summary>
        private (int meshCount, int skinnedCount, int spriteCount, int totalMats) CountRenderers(
            GameObject go
        )
        {
            var meshRenderers = go.GetComponentsInChildren<MeshRenderer>(true);
            var skinnedRenderers = go.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            var spriteRenderers = go.GetComponentsInChildren<SpriteRenderer>(true);

            int totalMats = 0;
            foreach (var r in meshRenderers)
                if (r != null)
                    totalMats += r.sharedMaterials.Length;
            foreach (var r in skinnedRenderers)
                if (r != null)
                    totalMats += r.sharedMaterials.Length;
            foreach (var r in spriteRenderers)
                if (r != null)
                    totalMats += r.sharedMaterials.Length;

            return (
                meshRenderers.Length,
                skinnedRenderers.Length,
                spriteRenderers.Length,
                totalMats
            );
        }

        private void DrawTemplateSection()
        {
            XYEditorGUI.DrawSection(
                "动画模板",
                () =>
                {
                    _currentTemplate = (MaterialTransparencyAnimationTemplate)
                        EditorGUILayout.ObjectField(
                            "当前模板",
                            _currentTemplate,
                            typeof(MaterialTransparencyAnimationTemplate),
                            false
                        );

                    if (_currentTemplate != null)
                    {
                        DrawTemplateInfo();
                    }
                    else
                    {
                        EditorGUILayout.HelpBox(
                            "请指定一个动画模板，或使用下方按钮快捷创建。",
                            MessageType.Info
                        );
                    }

                    DrawQuickCreateButtons();
                }
            );
        }

        private void DrawTemplateInfo()
        {
            EditorGUILayout.Space(3);
            EditorGUI.indentLevel++;
            EditorGUILayout.LabelField("模板名称", _currentTemplate.templateName);
            EditorGUILayout.LabelField("帧率", _currentTemplate.frameRate.ToString());

            string modules = "";
            if (_currentTemplate.useTransparency)
                modules += "渐显 ";
            if (_currentTemplate.useOverall)
                modules += "整体 ";
            if (_currentTemplate.useAxisDissolve)
                modules += "轴向溶解 ";
            if (_currentTemplate.controlChildParticles)
                modules += "[粒子] ";
            EditorGUILayout.LabelField(
                "启用模块",
                string.IsNullOrEmpty(modules) ? "(无)" : modules
            );
            EditorGUILayout.LabelField("出现时长", $"{_currentTemplate.appearConfig.duration:F2}s");
            EditorGUILayout.LabelField(
                "消失时长",
                $"{_currentTemplate.disappearConfig.duration:F2}s"
            );
            EditorGUI.indentLevel--;

            EditorGUILayout.Space(3);
            if (GUILayout.Button("在 Inspector 中编辑模板", GUILayout.Height(24)))
            {
                Selection.activeObject = _currentTemplate;
                EditorGUIUtility.PingObject(_currentTemplate);
            }
        }

        private void DrawQuickCreateButtons()
        {
            EditorGUILayout.Space(5);
            EditorGUILayout.LabelField("快捷创建模板", EditorStyles.miniLabel);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("透明度模板", GUILayout.Height(24)))
                CreateAndSaveTemplate(
                    MaterialTransparencyAnimationTemplate.CreateTransparencyTemplate(),
                    "Transparency_Template"
                );
            if (GUILayout.Button("轴向溶解模板", GUILayout.Height(24)))
                CreateAndSaveTemplate(
                    MaterialTransparencyAnimationTemplate.CreateAxisDissolveTemplate(),
                    "AxisDissolve_Template"
                );
            if (GUILayout.Button("整体溶解模板", GUILayout.Height(24)))
                CreateAndSaveTemplate(
                    MaterialTransparencyAnimationTemplate.CreateOverallDissolveTemplate(),
                    "OverallDissolve_Template"
                );
            EditorGUILayout.EndHorizontal();
        }

        private void DrawSavePathSection()
        {
            XYEditorGUI.DrawSection(
                "保存路径",
                () =>
                {
                    EditorGUILayout.BeginHorizontal();
                    _animationFolder = EditorGUILayout.TextField("输出目录", _animationFolder);
                    if (GUILayout.Button("...", GUILayout.Width(30)))
                    {
                        string path = EditorUtility.OpenFolderPanel("选择文件夹", "Assets", "");
                        if (!string.IsNullOrEmpty(path) && path.Contains(Application.dataPath))
                            _animationFolder =
                                "Assets" + path.Substring(Application.dataPath.Length);
                    }
                    EditorGUILayout.EndHorizontal();
                }
            );
        }

        private void DrawMaterialSettings()
        {
            XYEditorGUI.DrawSection(
                "材质设置",
                () =>
                {
                    var go = Selection.activeGameObject;
                    if (go == null)
                        return;

                    var renderer = go.GetComponent<Renderer>();
                    if (renderer != null && renderer.sharedMaterials.Length > 1)
                    {
                        _animateAllMaterials = EditorGUILayout.Toggle(
                            "动画所有材质",
                            _animateAllMaterials
                        );
                        if (!_animateAllMaterials)
                        {
                            _specificMaterialIndex = EditorGUILayout.IntSlider(
                                "材质索引",
                                _specificMaterialIndex,
                                0,
                                renderer.sharedMaterials.Length - 1
                            );

                            if (_specificMaterialIndex < renderer.sharedMaterials.Length)
                            {
                                var mat = renderer.sharedMaterials[_specificMaterialIndex];
                                if (mat != null)
                                    EditorGUILayout.LabelField(
                                        "材质名称",
                                        mat.name,
                                        EditorStyles.miniLabel
                                    );
                            }
                        }
                    }

                    var childRenderers = go.GetComponentsInChildren<Renderer>();
                    if (childRenderers.Length > 1)
                    {
                        EditorGUILayout.Space(5);
                        EditorGUILayout.HelpBox(
                            $"将自动遍历所有子物体，共 {childRenderers.Length} 个 Renderer",
                            MessageType.Info
                        );
                    }
                }
            );
        }

        private void DrawCreateButtons()
        {
            EditorGUILayout.Space(10);

            if (_currentTemplate == null)
            {
                EditorGUILayout.HelpBox("请先选择动画模板后再创建动画", MessageType.Warning);
                return;
            }

            if (_currentTemplate.ActiveModuleCount == 0)
            {
                EditorGUILayout.HelpBox(
                    "模板中没有启用任何动画模块，请编辑模板",
                    MessageType.Warning
                );
                return;
            }

            // 一键创建
            int clipCount = _currentTemplate.ActiveModuleCount * 2;
            GUI.backgroundColor = new Color(0.4f, 0.8f, 0.4f);
            if (
                GUILayout.Button(
                    $"✓ 创建完整动画组 ({clipCount} 个动画 + Controller)",
                    GUILayout.Height(35)
                )
            )
                CreateFullAnimationGroup();
            GUI.backgroundColor = Color.white;

            EditorGUILayout.Space(5);

            // 单独创建
            DrawSingleClipButtons("chuxian", "出现", _currentTemplate.appearConfig);
            DrawSingleClipButtons("xiaoshi", "消失", _currentTemplate.disappearConfig);

            EditorGUILayout.Space(10);
        }

        private void DrawSingleClipButtons(string phase, string label, PhaseAnimationConfig cfg)
        {
            EditorGUILayout.LabelField($"── 单独创建{label}动画 ──", EditorStyles.miniLabel);
            EditorGUILayout.BeginHorizontal();
            if (
                _currentTemplate.useAxisDissolve
                && GUILayout.Button($"{phase}_Axis", GUILayout.Height(24))
            )
                CreateSingleClip(phase, ClipType.Axis, cfg);
            if (
                _currentTemplate.useOverall
                && GUILayout.Button($"{phase}_OverRall", GUILayout.Height(24))
            )
                CreateSingleClip(phase, ClipType.OverRall, cfg);
            if (
                _currentTemplate.useTransparency
                && GUILayout.Button($"{phase}_Transprance", GUILayout.Height(24))
            )
                CreateSingleClip(phase, ClipType.Transprance, cfg);
            EditorGUILayout.EndHorizontal();
        }

        #endregion

        #region Animation Creation

        private void CreateSingleClip(string phase, ClipType type, PhaseAnimationConfig cfg)
        {
            var go = Selection.activeGameObject;
            if (!ValidateSelection(go))
                return;

            InstantiateParticlePrefabs(go);

            if (type == ClipType.Axis)
                MaterialKeywordHelper.ApplyAxisKeywordToMaterials(
                    go,
                    cfg.axisDirection,
                    _currentTemplate.axisProperty
                );

            var builder = new AnimationClipBuilder(
                _currentTemplate,
                _animateAllMaterials,
                _specificMaterialIndex
            );
            string clipName = $"{go.name}_{phase}_{type}";
            var clip = builder.BuildClip(clipName, type, cfg, go);

            if (clip != null)
                SaveClip(clip, clipName);
        }

        private void CreateFullAnimationGroup()
        {
            var go = Selection.activeGameObject;
            if (!ValidateSelection(go))
                return;

            InstantiateParticlePrefabs(go);
            EnsureFolder(_animationFolder);

            var builder = new AnimationClipBuilder(
                _currentTemplate,
                _animateAllMaterials,
                _specificMaterialIndex
            );
            var appearCfg = _currentTemplate.appearConfig;
            var disappearCfg = _currentTemplate.disappearConfig;

            // 收集启用的模块
            var enabledTypes = new List<ClipType>();
            if (_currentTemplate.useAxisDissolve)
                enabledTypes.Add(ClipType.Axis);
            if (_currentTemplate.useOverall)
                enabledTypes.Add(ClipType.OverRall);
            if (_currentTemplate.useTransparency)
                enabledTypes.Add(ClipType.Transprance);

            // 设置 Axis keyword
            if (_currentTemplate.useAxisDissolve)
            {
                MaterialKeywordHelper.ApplyAxisKeywordToMaterials(
                    go,
                    appearCfg.axisDirection,
                    _currentTemplate.axisProperty
                );
                if (disappearCfg.axisDirection != appearCfg.axisDirection)
                    MaterialKeywordHelper.ApplyAxisKeywordToMaterials(
                        go,
                        disappearCfg.axisDirection,
                        _currentTemplate.axisProperty
                    );
            }

            // 构建剪辑
            var savedClips = new List<(string name, AnimationClip clip, string path)>();
            foreach (ClipType type in enabledTypes)
            {
                string appearName = $"{go.name}_chuxian_{type}";
                var appearClip = builder.BuildClip(appearName, type, appearCfg, go);
                if (appearClip != null)
                {
                    string path = AssetDatabase.GenerateUniqueAssetPath(
                        $"{_animationFolder}/{appearName}.anim"
                    );
                    AssetDatabase.CreateAsset(appearClip, path);
                    savedClips.Add((appearName, appearClip, path));
                }

                string disappearName = $"{go.name}_xiaoshi_{type}";
                var disappearClip = builder.BuildClip(disappearName, type, disappearCfg, go);
                if (disappearClip != null)
                {
                    string path = AssetDatabase.GenerateUniqueAssetPath(
                        $"{_animationFolder}/{disappearName}.anim"
                    );
                    AssetDatabase.CreateAsset(disappearClip, path);
                    savedClips.Add((disappearName, disappearClip, path));
                }
            }

            // 创建 Controller
            CreateAnimatorController(go, savedClips);
        }

        private void CreateAnimatorController(
            GameObject go,
            List<(string name, AnimationClip clip, string path)> clips
        )
        {
            string ctrlPath = AssetDatabase.GenerateUniqueAssetPath(
                $"{_animationFolder}/{go.name}_TransparencyController.controller"
            );
            var controller = AnimatorController.CreateAnimatorControllerAtPath(ctrlPath);

            var rootSM = controller.layers[0].stateMachine;
            var idleState = rootSM.AddState("Idle");
            rootSM.defaultState = idleState;

            foreach (var (name, clip, _) in clips)
            {
                string triggerName = name.Replace(go.name + "_", "");
                controller.AddParameter(triggerName, AnimatorControllerParameterType.Trigger);
                var state = rootSM.AddState(triggerName);
                state.motion = clip;
            }

            // 挂载 Animator (支持 Prefab)
            var animator = go.GetComponent<Animator>();
            if (animator == null)
            {
                animator = Undo.AddComponent<Animator>(go);
            }
            animator.runtimeAnimatorController = controller;
            EditorUtility.SetDirty(go);

            AssetDatabase.SaveAssets();

            Debug.Log($"✓ 已创建完整动画组 ({clips.Count} 个剪辑): {ctrlPath}");
            EditorUtility.DisplayDialog(
                "完成",
                $"已创建 {clips.Count} 个动画剪辑 + AnimatorController",
                "确定"
            );

            Selection.activeObject = controller;
            EditorGUIUtility.PingObject(controller);
        }

        #endregion

        #region Utilities

        private void CreateAndSaveTemplate(
            MaterialTransparencyAnimationTemplate tpl,
            string defaultName
        )
        {
            string path = EditorUtility.SaveFilePanelInProject(
                "保存动画模板",
                defaultName,
                "asset",
                "选择模板保存位置",
                "Assets/Editor/Apps/AnimationCreator"
            );

            if (string.IsNullOrEmpty(path))
                return;

            AssetDatabase.CreateAsset(tpl, path);
            AssetDatabase.SaveAssets();
            _currentTemplate = tpl;
            Selection.activeObject = tpl;
            Debug.Log($"<color=green>[MaterialTransparencyAnim]</color> 已创建模板: {path}");
            _repaint();
        }

        private void InstantiateParticlePrefabs(GameObject go)
        {
            if (_currentTemplate == null || !_currentTemplate.controlChildParticles)
                return;

            var prefab = _currentTemplate.particlePrefab;
            if (prefab == null)
                return;

            // 获取所有支持的渲染器类型
            var allRenderers = new System.Collections.Generic.List<Renderer>();
            allRenderers.AddRange(go.GetComponentsInChildren<MeshRenderer>(true));
            allRenderers.AddRange(go.GetComponentsInChildren<SkinnedMeshRenderer>(true));
            allRenderers.AddRange(go.GetComponentsInChildren<SpriteRenderer>(true));

            int count = 0;

            foreach (var renderer in allRenderers)
            {
                if (renderer == null)
                    continue;

                bool alreadyExists = false;
                foreach (Transform child in renderer.transform)
                {
                    if (child.name == prefab.name || child.name.StartsWith(prefab.name))
                    {
                        alreadyExists = true;
                        break;
                    }
                }

                if (alreadyExists)
                    continue;

                var instance = (GameObject)
                    PrefabUtility.InstantiatePrefab(prefab, renderer.transform);
                if (instance != null)
                {
                    instance.name = prefab.name;
                    instance.transform.localPosition = Vector3.zero;
                    instance.transform.localRotation = Quaternion.identity;
                    instance.transform.localScale = Vector3.one;

                    // 根据渲染器类型设置粒子系统形状
                    var ps = instance.GetComponent<ParticleSystem>();
                    if (ps != null)
                    {
                        var shape = ps.shape;
                        if (renderer is MeshRenderer meshRenderer)
                        {
                            shape.shapeType = ParticleSystemShapeType.MeshRenderer;
                            shape.meshRenderer = meshRenderer;
                        }
                        else if (renderer is SkinnedMeshRenderer skinnedRenderer)
                        {
                            shape.shapeType = ParticleSystemShapeType.SkinnedMeshRenderer;
                            shape.skinnedMeshRenderer = skinnedRenderer;
                        }
                        else if (renderer is SpriteRenderer spriteRenderer)
                        {
                            shape.shapeType = ParticleSystemShapeType.SpriteRenderer;
                            shape.spriteRenderer = spriteRenderer;
                        }
                    }

                    Undo.RegisterCreatedObjectUndo(instance, "Instantiate Particle Prefab");
                    count++;
                }
            }

            if (count > 0)
                Debug.Log(
                    $"[MaterialTransparencyAnim] 已为 {count} 个 Renderer 创建粒子预制体实例"
                );
        }

        private bool ValidateSelection(GameObject go)
        {
            if (go == null)
            {
                EditorUtility.DisplayDialog("提示", "请选择带有 Renderer 的对象", "确定");
                return false;
            }

            // 检查是否有任何支持的渲染器类型
            bool hasMesh =
                go.GetComponent<MeshRenderer>() != null
                || go.GetComponentInChildren<MeshRenderer>() != null;
            bool hasSkinned =
                go.GetComponent<SkinnedMeshRenderer>() != null
                || go.GetComponentInChildren<SkinnedMeshRenderer>() != null;
            bool hasSprite =
                go.GetComponent<SpriteRenderer>() != null
                || go.GetComponentInChildren<SpriteRenderer>() != null;

            if (!hasMesh && !hasSkinned && !hasSprite)
            {
                EditorUtility.DisplayDialog(
                    "提示",
                    "请选择带有 Renderer 的对象\n(支持: MeshRenderer, SkinnedMeshRenderer, SpriteRenderer)",
                    "确定"
                );
                return false;
            }
            return true;
        }

        private void SaveClip(AnimationClip clip, string clipName)
        {
            EnsureFolder(_animationFolder);
            string path = AssetDatabase.GenerateUniqueAssetPath(
                $"{_animationFolder}/{clipName}.anim"
            );
            AssetDatabase.CreateAsset(clip, path);
            AssetDatabase.SaveAssets();
            Debug.Log($"✓ 动画已创建: {path}");
            Selection.activeObject = clip;
            EditorGUIUtility.PingObject(clip);
        }

        private void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
                return;

            string[] folders = path.Split('/');
            string currentPath = folders[0];
            for (int i = 1; i < folders.Length; i++)
            {
                string newPath = currentPath + "/" + folders[i];
                if (!AssetDatabase.IsValidFolder(newPath))
                    AssetDatabase.CreateFolder(currentPath, folders[i]);
                currentPath = newPath;
            }
        }

        #endregion
    }
}
