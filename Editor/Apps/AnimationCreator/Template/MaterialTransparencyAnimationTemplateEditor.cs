using UnityEditor;
using UnityEngine;
using YZJ.AnimationCreator;
using YZJ.AnimationCreator.Editor;

namespace YZJ
{
    /// <summary>
    /// MaterialTransparencyAnimationTemplate 自定义 Inspector
    /// </summary>
    [CustomEditor(typeof(MaterialTransparencyAnimationTemplate))]
    public class MaterialTransparencyAnimationTemplateEditor : UnityEditor.Editor
    {
        // ── 折叠状态 ──────────────────────────────────────────────────────────
        private bool _foldAppear = true;
        private bool _foldDisappear = true;
        private bool _foldShaderNames = false;
        private bool _foldAppearCustom = false;
        private bool _foldDisappearCustom = false;

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            var tpl = (MaterialTransparencyAnimationTemplate)target;

            Undo.RecordObject(target, "Modify Animation Template");
            EditorGUI.BeginChangeCheck();

            // ── 基础信息 ──
            EditorGUIHelpers.DrawBoxSection(
                "[i] 基础信息",
                () =>
                {
                    EditorGUILayout.PropertyField(
                        serializedObject.FindProperty("templateName"),
                        new GUIContent("模板名称")
                    );
                    EditorGUILayout.PropertyField(
                        serializedObject.FindProperty("frameRate"),
                        new GUIContent("帧率 (FPS)")
                    );
                }
            );

            EditorGUILayout.Space(2);

            // ── 模块开关 ──
            EditorGUIHelpers.DrawBoxSection("⚙ 模块开关", () => DrawModuleToggles(tpl));

            EditorGUILayout.Space(2);

            // ── Shader 属性名 ──
            DrawShaderPropertyNames(tpl);

            EditorGUILayout.Space(4);

            // ── 阶段配置 ──
            DrawPhaseSection(
                "[+] 出现动画 (chuxian)",
                tpl.appearConfig,
                tpl,
                ref _foldAppear,
                ref _foldAppearCustom
            );

            EditorGUILayout.Space(4);

            DrawPhaseSection(
                "[-] 消失动画 (xiaoshi)",
                tpl.disappearConfig,
                tpl,
                ref _foldDisappear,
                ref _foldDisappearCustom
            );

            bool changed = EditorGUI.EndChangeCheck();

            EditorGUILayout.Space(8);
            TimelinePreviewDrawer.DrawTimelinePreview(tpl);

            serializedObject.ApplyModifiedProperties();
            if (changed)
                EditorUtility.SetDirty(target);
        }

        #region Module Toggles

        private void DrawModuleToggles(MaterialTransparencyAnimationTemplate tpl)
        {
            tpl.useTransparency = EditorGUILayout.Toggle(
                "渐显/渐隐 (_Transparency)",
                tpl.useTransparency
            );
            tpl.useOverall = EditorGUILayout.Toggle("整体溶解 (_OverRall)", tpl.useOverall);
            tpl.useAxisDissolve = EditorGUILayout.Toggle(
                "轴向溶解 (Axis + Dissolve + Noise)",
                tpl.useAxisDissolve
            );

            EditorGUILayout.Space(3);
            tpl.controlChildParticles = EditorGUILayout.Toggle(
                "控制子物体粒子系统",
                tpl.controlChildParticles
            );

            if (tpl.controlChildParticles)
            {
                EditorGUI.indentLevel++;
                tpl.particlePrefab = (GameObject)
                    EditorGUILayout.ObjectField(
                        "粒子预制体",
                        tpl.particlePrefab,
                        typeof(GameObject),
                        false
                    );

                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField("出现动画", GUILayout.Width(60));
                tpl.appearParticleTiming = (ParticleActivateTiming)
                    EditorGUILayout.EnumPopup(tpl.appearParticleTiming);
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField("消失动画", GUILayout.Width(60));
                tpl.disappearParticleTiming = (ParticleDeactivateTiming)
                    EditorGUILayout.EnumPopup(tpl.disappearParticleTiming);
                EditorGUILayout.EndHorizontal();

                if (tpl.particlePrefab == null)
                {
                    EditorGUILayout.HelpBox(
                        "请指定粒子预制体，将在每个 Renderer 下实例化。\n(支持: MeshRenderer, SkinnedMeshRenderer, SpriteRenderer)",
                        MessageType.Info
                    );
                }
                EditorGUI.indentLevel--;
            }

            if (!tpl.useTransparency && !tpl.useOverall && !tpl.useAxisDissolve)
                EditorGUILayout.HelpBox("至少需要启用一个动画模块。", MessageType.Warning);
        }

        #endregion

        #region Shader Property Names

        private void DrawShaderPropertyNames(MaterialTransparencyAnimationTemplate tpl)
        {
            _foldShaderNames = EditorGUILayout.Foldout(
                _foldShaderNames,
                "[S] Shader 属性名配置",
                true,
                EditorStyles.foldoutHeader
            );
            if (!_foldShaderNames)
                return;

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUI.indentLevel++;

            if (tpl.useTransparency)
                DrawPropertyField(
                    "透明度",
                    ref tpl.transparencyProperty,
                    ref tpl.transparencyPropertyType
                );

            if (tpl.useOverall)
                DrawPropertyField("整体溶解", ref tpl.overallProperty, ref tpl.overallPropertyType);

            if (tpl.useOverall || tpl.useAxisDissolve)
            {
                DrawPropertyField(
                    "溶解阈值",
                    ref tpl.dissolveThresholdProperty,
                    ref tpl.dissolveThresholdPropertyType
                );
                DrawPropertyField(
                    "噪声缩放",
                    ref tpl.noiseScaleProperty,
                    ref tpl.noiseScalePropertyType
                );
                DrawPropertyField(
                    "噪声强度",
                    ref tpl.noiseIntensityProperty,
                    ref tpl.noiseIntensityPropertyType
                );
            }

            if (tpl.useAxisDissolve)
            {
                DrawPropertyField(
                    "溶解开关",
                    ref tpl.dissolveToggleProperty,
                    ref tpl.dissolveTogglePropertyType
                );
                DrawPropertyField("轴向", ref tpl.axisProperty, ref tpl.axisPropertyType);
            }

            EditorGUI.indentLevel--;
            EditorGUILayout.EndVertical();
        }

        private static void DrawPropertyField(
            string label,
            ref string property,
            ref ShaderPropertyType type
        )
        {
            EditorGUILayout.BeginHorizontal();
            property = EditorGUILayout.TextField(label, property);
            type = (ShaderPropertyType)EditorGUILayout.EnumPopup(type, GUILayout.Width(70));
            EditorGUILayout.EndHorizontal();
        }

        #endregion

        #region Phase Section

        private void DrawPhaseSection(
            string title,
            PhaseAnimationConfig cfg,
            MaterialTransparencyAnimationTemplate tpl,
            ref bool fold,
            ref bool foldCustom
        )
        {
            fold = EditorGUILayout.Foldout(fold, title, true, EditorStyles.foldoutHeader);
            if (!fold)
                return;

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            // ── 时长 ──
            cfg.duration = Mathf.Max(
                0.01f,
                EditorGUILayout.FloatField("⏱ 时长 (秒)", cfg.duration)
            );

            // ── 渐显/渐隐 ──
            if (tpl.useTransparency)
            {
                DrawTransparencyModule(tpl, cfg);
            }

            // ── 整体溶解 ──
            if (tpl.useOverall)
            {
                DrawOverallModule(tpl, cfg);
            }

            // ── 轴向溶解 ──
            if (tpl.useAxisDissolve)
            {
                DrawAxisModule(tpl, cfg);
            }

            // ── 自定义属性 ──
            EditorGUIHelpers.DrawSeparator();
            DrawCustomProperties(cfg, ref foldCustom);

            EditorGUILayout.EndVertical();
        }

        private void DrawTransparencyModule(
            MaterialTransparencyAnimationTemplate tpl,
            PhaseAnimationConfig cfg
        )
        {
            EditorGUIHelpers.DrawModuleHeader(
                $"渐显/渐隐   {tpl.transparencyProperty}",
                TrackDataCollector.COL_TRANS
            );
            EditorGUI.indentLevel++;

            if (tpl.transparencyPropertyType == ShaderPropertyType.Bool)
            {
                EditorGUILayout.HelpBox(
                    $"{tpl.transparencyProperty} = 1 (Bool 常量)",
                    MessageType.None
                );
            }
            else
            {
                EditorGUIHelpers.DrawFromTo(ref cfg.transparencyFrom, ref cfg.transparencyTo);
                cfg.transparencyCurve = EditorGUILayout.CurveField("曲线", cfg.transparencyCurve);
            }
            EditorGUI.indentLevel--;
        }

        private void DrawOverallModule(
            MaterialTransparencyAnimationTemplate tpl,
            PhaseAnimationConfig cfg
        )
        {
            EditorGUIHelpers.DrawModuleHeader(
                $"整体溶解   {tpl.overallProperty}",
                TrackDataCollector.COL_OVER
            );
            EditorGUI.indentLevel++;

            if (tpl.overallPropertyType == ShaderPropertyType.Bool)
            {
                EditorGUILayout.HelpBox($"{tpl.overallProperty} = 1 (Bool 常量)", MessageType.None);
            }
            else
            {
                EditorGUIHelpers.DrawFromTo(ref cfg.overallFrom, ref cfg.overallTo);
                cfg.overallCurve = EditorGUILayout.CurveField("曲线", cfg.overallCurve);
            }

            EditorGUIHelpers.DrawSeparator();
            EditorGUIHelpers.DrawSubLabel(tpl.dissolveThresholdProperty);
            if (tpl.dissolveThresholdPropertyType != ShaderPropertyType.Bool)
            {
                EditorGUIHelpers.DrawFromTo(ref cfg.overallDissolveFrom, ref cfg.overallDissolveTo);
                cfg.overallDissolveCurve = EditorGUILayout.CurveField(
                    "曲线",
                    cfg.overallDissolveCurve
                );
            }

            EditorGUIHelpers.DrawSeparator();
            EditorGUIHelpers.DrawSubLabel("Noise");
            cfg.overallNoiseScale = EditorGUILayout.FloatField(
                tpl.noiseScaleProperty,
                cfg.overallNoiseScale
            );
            cfg.overallNoiseIntensity = EditorGUILayout.FloatField(
                tpl.noiseIntensityProperty,
                cfg.overallNoiseIntensity
            );

            EditorGUI.indentLevel--;
        }

        private void DrawAxisModule(
            MaterialTransparencyAnimationTemplate tpl,
            PhaseAnimationConfig cfg
        )
        {
            EditorGUIHelpers.DrawModuleHeader(
                "轴向溶解   Axis + Dissolve + Noise",
                TrackDataCollector.COL_AXIS
            );
            EditorGUI.indentLevel++;

            cfg.axisDirection = (AxisDirection)
                EditorGUILayout.EnumPopup("轴向方向", cfg.axisDirection);

            EditorGUIHelpers.DrawSeparator();
            EditorGUIHelpers.DrawSubLabel(tpl.dissolveThresholdProperty);
            if (tpl.dissolveThresholdPropertyType != ShaderPropertyType.Bool)
            {
                EditorGUIHelpers.DrawFromTo(
                    ref cfg.dissolveThresholdFrom,
                    ref cfg.dissolveThresholdTo
                );
                cfg.dissolveCurve = EditorGUILayout.CurveField("曲线", cfg.dissolveCurve);
            }

            EditorGUIHelpers.DrawSeparator();
            EditorGUIHelpers.DrawSubLabel("Noise");
            cfg.noiseScale = EditorGUILayout.FloatField(tpl.noiseScaleProperty, cfg.noiseScale);
            cfg.noiseIntensity = EditorGUILayout.FloatField(
                tpl.noiseIntensityProperty,
                cfg.noiseIntensity
            );

            EditorGUI.indentLevel--;
        }

        #endregion

        #region Custom Properties

        private void DrawCustomProperties(PhaseAnimationConfig cfg, ref bool fold)
        {
            fold = EditorGUILayout.Foldout(
                fold,
                $"✦ 自定义属性 ({cfg.customProperties.Count})",
                true
            );
            if (!fold)
                return;

            int removeIndex = -1;
            for (int i = 0; i < cfg.customProperties.Count; i++)
            {
                var prop = cfg.customProperties[i];
                EditorGUILayout.BeginVertical("box");

                // 标题行
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField($"#{i}", GUILayout.Width(22));
                prop.propertyName = EditorGUILayout.TextField(prop.propertyName);
                prop.type = (ShaderPropertyType)
                    EditorGUILayout.EnumPopup(prop.type, GUILayout.Width(70));
                prop.mode = (AnimPropertyMode)
                    EditorGUILayout.EnumPopup(prop.mode, GUILayout.Width(115));
                if (GUILayout.Button("×", GUILayout.Width(20)))
                    removeIndex = i;
                EditorGUILayout.EndHorizontal();

                // 值
                EditorGUI.indentLevel++;
                DrawCustomPropertyValue(prop);
                EditorGUI.indentLevel--;

                EditorGUILayout.EndVertical();
            }

            if (removeIndex >= 0)
                cfg.customProperties.RemoveAt(removeIndex);

            EditorGUILayout.Space(2);
            if (GUILayout.Button("+ 添加自定义属性", GUILayout.Height(22)))
                cfg.customProperties.Add(new AnimatedShaderProperty());
        }

        private void DrawCustomPropertyValue(AnimatedShaderProperty prop)
        {
            if (prop.mode == AnimPropertyMode.Constant)
            {
                switch (prop.type)
                {
                    case ShaderPropertyType.Float:
                        prop.constantFloat = EditorGUILayout.FloatField("值", prop.constantFloat);
                        break;
                    case ShaderPropertyType.Int:
                        prop.constantInt = EditorGUILayout.IntField("值", prop.constantInt);
                        break;
                    case ShaderPropertyType.Bool:
                        prop.constantBool = EditorGUILayout.Toggle("值", prop.constantBool);
                        break;
                }
            }
            else
            {
                switch (prop.type)
                {
                    case ShaderPropertyType.Float:
                        EditorGUILayout.BeginHorizontal();
                        EditorGUILayout.PrefixLabel("起始");
                        prop.fromFloat = EditorGUILayout.FloatField(prop.fromFloat);
                        GUILayout.Label("→", GUILayout.Width(18));
                        prop.toFloat = EditorGUILayout.FloatField(prop.toFloat);
                        EditorGUILayout.EndHorizontal();
                        prop.curve = EditorGUILayout.CurveField("曲线", prop.curve);
                        break;
                    case ShaderPropertyType.Int:
                        EditorGUILayout.BeginHorizontal();
                        EditorGUILayout.PrefixLabel("起始");
                        prop.fromInt = EditorGUILayout.IntField(prop.fromInt);
                        GUILayout.Label("→", GUILayout.Width(18));
                        prop.toInt = EditorGUILayout.IntField(prop.toInt);
                        EditorGUILayout.EndHorizontal();
                        prop.curve = EditorGUILayout.CurveField("曲线", prop.curve);
                        break;
                    case ShaderPropertyType.Bool:
                        EditorGUILayout.HelpBox("Bool 类型仅支持常量模式", MessageType.Info);
                        prop.mode = AnimPropertyMode.Constant;
                        break;
                }
            }
        }

        #endregion
    }
}
