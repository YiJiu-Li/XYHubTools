using System;
using System.Collections.Generic;
using UnityEngine;

namespace YZJ.AnimationCreator
{
    /// <summary>
    /// 单阶段动画配置（出现 or 消失）
    /// </summary>
    [Serializable]
    public class PhaseAnimationConfig
    {
        [Header("时长")]
        [Tooltip("动画时长（秒）")]
        public float duration = 1f;

        // ── 透明度 (_Transparency) ──────────────────────────────────────
        [Header("透明度 (_Transparency)")]
        public float transparencyFrom = 0f;
        public float transparencyTo = 1f;
        public AnimationCurve transparencyCurve = AnimationCurve.Linear(0, 0, 1, 1);

        // ── 整体溶解 (_OverRall) ────────────────────────────────────────
        [Header("整体溶解 (_OverRall)")]
        public float overallFrom = 0f;
        public float overallTo = 1f;
        public AnimationCurve overallCurve = AnimationCurve.Linear(0, 0, 1, 1);

        // ── 整体溶解的溶解参数 ─────────────────────────────────────────
        [Tooltip("整体溶解模式下的 _DissolveThreshold")]
        public float overallDissolveFrom = 1f;
        public float overallDissolveTo = -0.1f;
        public AnimationCurve overallDissolveCurve = AnimationCurve.Linear(0, 0, 1, 1);

        [Tooltip("整体溶解模式下的 _NoiseScale")]
        public float overallNoiseScale = 1f;

        [Tooltip("整体溶解模式下的 _NoiseIntensity")]
        public float overallNoiseIntensity = 0.5f;

        // ── 轴向溶解 ────────────────────────────────────────────────────
        [Header("轴向溶解 (IsDissolve + _AXIS + _DissolveThreshold)")]
        [Tooltip("Shader 属性: _AXIS (KeywordEnum)\n0=X 左→右, 1=_X 右→左, 2=Y 下→上, 3=_Y 上→下")]
        public AxisDirection axisDirection = AxisDirection.XPositive;

        [Tooltip("Shader 属性: _DissolveThreshold\n-0.1=显示, 1=消失")]
        public float dissolveThresholdFrom = 1f;

        [Tooltip("Shader 属性: _DissolveThreshold\n-0.1=显示, 1=消失")]
        public float dissolveThresholdTo = -0.1f;
        public AnimationCurve dissolveCurve = AnimationCurve.Linear(0, 0, 1, 1);

        // ── 噪声 ────────────────────────────────────────────────────────
        [Header("噪声 (_NoiseScale + _NoiseIntensity)")]
        [Tooltip("Shader 属性: _NoiseScale")]
        public float noiseScale = 1f;

        [Tooltip("Shader 属性: _NoiseIntensity")]
        public float noiseIntensity = 0.5f;

        // ── 自定义属性 ──────────────────────────────────────────────────
        public List<AnimatedShaderProperty> customProperties = new List<AnimatedShaderProperty>();

        // ── 工具方法 ────────────────────────────────────────────────────

        /// <summary>深拷贝（含 AnimationCurve）</summary>
        public PhaseAnimationConfig Clone()
        {
            var clone = new PhaseAnimationConfig
            {
                duration = duration,
                transparencyFrom = transparencyFrom,
                transparencyTo = transparencyTo,
                transparencyCurve = new AnimationCurve(transparencyCurve.keys),
                overallFrom = overallFrom,
                overallTo = overallTo,
                overallCurve = new AnimationCurve(overallCurve.keys),
                overallDissolveFrom = overallDissolveFrom,
                overallDissolveTo = overallDissolveTo,
                overallDissolveCurve = new AnimationCurve(overallDissolveCurve.keys),
                overallNoiseScale = overallNoiseScale,
                overallNoiseIntensity = overallNoiseIntensity,
                axisDirection = axisDirection,
                dissolveThresholdFrom = dissolveThresholdFrom,
                dissolveThresholdTo = dissolveThresholdTo,
                dissolveCurve = new AnimationCurve(dissolveCurve.keys),
                noiseScale = noiseScale,
                noiseIntensity = noiseIntensity,
                customProperties = new List<AnimatedShaderProperty>(),
            };
            foreach (var prop in customProperties)
                clone.customProperties.Add(prop.Clone());
            return clone;
        }

        /// <summary>创建默认出现配置</summary>
        public static PhaseAnimationConfig CreateDefaultAppear()
        {
            return new PhaseAnimationConfig
            {
                duration = 1f,
                transparencyFrom = 0f,
                transparencyTo = 1f,
                overallFrom = 0f,
                overallTo = 1f,
                overallDissolveFrom = 1f,
                overallDissolveTo = -0.1f,
                overallNoiseScale = 1f,
                overallNoiseIntensity = 0.5f,
                axisDirection = AxisDirection.XPositive,
                dissolveThresholdFrom = 1f,
                dissolveThresholdTo = -0.1f,
                noiseScale = 1f,
                noiseIntensity = 0.5f,
            };
        }

        /// <summary>创建默认消失配置</summary>
        public static PhaseAnimationConfig CreateDefaultDisappear()
        {
            return new PhaseAnimationConfig
            {
                duration = 1f,
                transparencyFrom = 1f,
                transparencyTo = 0f,
                overallFrom = 1f,
                overallTo = 0f,
                overallDissolveFrom = -0.1f,
                overallDissolveTo = 1f,
                overallNoiseScale = 1f,
                overallNoiseIntensity = 0.5f,
                axisDirection = AxisDirection.XNegative,
                dissolveThresholdFrom = -0.1f,
                dissolveThresholdTo = 1f,
                noiseScale = 1f,
                noiseIntensity = 0.5f,
            };
        }
    }
}
