using UnityEngine;
using YZJ.AnimationCreator;

namespace YZJ
{
    /// <summary>
    /// 动画模板工具模板 —— ScriptableObject 配置文件，
    /// 存储出现/消失动画的全部参数，可在面板中随时加载切换。
    /// </summary>
    [CreateAssetMenu(
        fileName = "NewTransparencyAnimTemplate",
        menuName = "依旧/动画模板工具模板",
        order = 101
    )]
    public class MaterialTransparencyAnimationTemplate : ScriptableObject
    {
        [Header("模板信息")]
        [Tooltip("模板显示名称")]
        public string templateName = "新动画模板";

        [Header("帧率")]
        [Range(12, 60)]
        public int frameRate = 30;

        // ── 动画模块开关 ──────────────────────────────────────────────
        [Header("动画模块")]
        [Tooltip("启用渐显/渐隐模块 (_Transparency)")]
        public bool useTransparency = true;

        [Tooltip("启用整体溶解模块 (_OverRall)")]
        public bool useOverall = true;

        [Tooltip("启用轴向溶解模块 (IsDissolve + _AXIS + _DissolveThreshold + Noise)")]
        public bool useAxisDissolve = true;

        [Tooltip("控制子物体粒子系统的显示/隐藏 (通过 GameObject.SetActive)")]
        public bool controlChildParticles = false;

        [Tooltip("粒子特效预制体，会在每个 Renderer 下实例化")]
        public GameObject particlePrefab;

        [Tooltip("出现动画中粒子的行为")]
        public ParticleActivateTiming appearParticleTiming = ParticleActivateTiming.AtStart;

        [Tooltip("消失动画中粒子的行为")]
        public ParticleDeactivateTiming disappearParticleTiming = ParticleDeactivateTiming.AtEnd;

        // ── Shader 属性名 ──────────────────────────────────────────────
        [Header("Shader 属性名")]
        [Tooltip("透明度属性")]
        public string transparencyProperty = "_Transparency";
        public ShaderPropertyType transparencyPropertyType = ShaderPropertyType.Float;

        [Tooltip("整体溶解属性")]
        public string overallProperty = "_IsOverRall";
        public ShaderPropertyType overallPropertyType = ShaderPropertyType.Bool;

        [Tooltip("溶解开关属性 (float 0/1)")]
        public string dissolveToggleProperty = "_IsDissolve";
        public ShaderPropertyType dissolveTogglePropertyType = ShaderPropertyType.Bool;

        [Tooltip("溶解阈值属性")]
        public string dissolveThresholdProperty = "_DissolveThreshold";
        public ShaderPropertyType dissolveThresholdPropertyType = ShaderPropertyType.Float;

        [Tooltip("轴向属性 (float 0‑3)")]
        public string axisProperty = "_AXIS";
        public ShaderPropertyType axisPropertyType = ShaderPropertyType.Int;

        [Tooltip("噪声缩放属性")]
        public string noiseScaleProperty = "_NoiseScale";
        public ShaderPropertyType noiseScalePropertyType = ShaderPropertyType.Float;

        [Tooltip("噪声强度属性")]
        public string noiseIntensityProperty = "_NoiseIntensity";
        public ShaderPropertyType noiseIntensityPropertyType = ShaderPropertyType.Float;

        // ── 出现 / 消失配置 ────────────────────────────────────────────
        [Header("出现动画 (chuxian)")]
        public PhaseAnimationConfig appearConfig = PhaseAnimationConfig.CreateDefaultAppear();

        [Header("消失动画 (xiaoshi)")]
        public PhaseAnimationConfig disappearConfig = PhaseAnimationConfig.CreateDefaultDisappear();

        // ═══════════════════════════════════════════════════════════════
        //  公共属性
        // ═══════════════════════════════════════════════════════════════

        /// <summary>活跃模块数量</summary>
        public int ActiveModuleCount
        {
            get
            {
                int count = 0;
                if (useTransparency)
                    count++;
                if (useOverall)
                    count++;
                if (useAxisDissolve)
                    count++;
                return count;
            }
        }

        // ═══════════════════════════════════════════════════════════════
        //  快捷工厂方法
        // ═══════════════════════════════════════════════════════════════

        /// <summary>创建: 透明度模板（仅渐显/渐隐）</summary>
        public static MaterialTransparencyAnimationTemplate CreateTransparencyTemplate()
        {
            var t = CreateInstance<MaterialTransparencyAnimationTemplate>();
            t.templateName = "透明度模板";
            t.useTransparency = true;
            t.useOverall = false;
            t.useAxisDissolve = false;
            t.appearConfig = new PhaseAnimationConfig
            {
                duration = 1f,
                transparencyFrom = 0f,
                transparencyTo = 1f,
            };
            t.disappearConfig = new PhaseAnimationConfig
            {
                duration = 1f,
                transparencyFrom = 1f,
                transparencyTo = 0f,
            };
            return t;
        }

        /// <summary>创建: 轴向溶解模板</summary>
        public static MaterialTransparencyAnimationTemplate CreateAxisDissolveTemplate()
        {
            var t = CreateInstance<MaterialTransparencyAnimationTemplate>();
            t.templateName = "轴向溶解模板";
            t.useTransparency = false;
            t.useOverall = false;
            t.useAxisDissolve = true;
            t.appearConfig = new PhaseAnimationConfig
            {
                duration = 1f,
                axisDirection = AxisDirection.XPositive,
                dissolveThresholdFrom = 1f,
                dissolveThresholdTo = -0.1f,
                noiseScale = 1f,
                noiseIntensity = 0.5f,
            };
            t.disappearConfig = new PhaseAnimationConfig
            {
                duration = 1f,
                axisDirection = AxisDirection.XNegative,
                dissolveThresholdFrom = -0.1f,
                dissolveThresholdTo = 1f,
                noiseScale = 1f,
                noiseIntensity = 0.5f,
            };
            return t;
        }

        /// <summary>创建: 整体溶解模板</summary>
        public static MaterialTransparencyAnimationTemplate CreateOverallDissolveTemplate()
        {
            var t = CreateInstance<MaterialTransparencyAnimationTemplate>();
            t.templateName = "整体溶解模板";
            t.useTransparency = false;
            t.useOverall = true;
            t.useAxisDissolve = false;
            t.appearConfig = new PhaseAnimationConfig
            {
                duration = 1f,
                overallFrom = 0f,
                overallTo = 1f,
                overallDissolveFrom = 1f,
                overallDissolveTo = -0.1f,
                overallNoiseScale = 1f,
                overallNoiseIntensity = 0.5f,
            };
            t.disappearConfig = new PhaseAnimationConfig
            {
                duration = 1f,
                overallFrom = 1f,
                overallTo = 0f,
                overallDissolveFrom = -0.1f,
                overallDissolveTo = 1f,
                overallNoiseScale = 1f,
                overallNoiseIntensity = 0.5f,
            };
            return t;
        }
    }
}
