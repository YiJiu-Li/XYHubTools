using System.Collections.Generic;
using UnityEngine;
using YZJ.AnimationCreator;

namespace YZJ.AnimationCreator.Editor
{
    /// <summary>
    /// 轨道信息结构
    /// </summary>
    public struct TrackInfo
    {
        public string property; // Shader 属性名
        public string typeTag; // 类型标记 (F/I/B)
        public Color color;
        public float startTime;
        public float duration;
        public float fromVal;
        public float toVal;
        public bool isAnimated;
        public bool isConstant; // 常量曲线
    }

    /// <summary>
    /// 轨道数据收集器 - 从模板和配置中收集时间轴轨道信息
    /// </summary>
    public static class TrackDataCollector
    {
        // ── 模块颜色 ─────────────────────────────────────────────────────
        public static readonly Color COL_TRANS = new Color(0.35f, 0.70f, 1.0f);
        public static readonly Color COL_OVER = new Color(0.30f, 0.115f, 0.45f);
        public static readonly Color COL_AXIS = new Color(1.0f, 0.55f, 0.20f);
        public static readonly Color COL_CUSTOM = new Color(0.115f, 0.115f, 1.0f);

        public static string TypeTag(ShaderPropertyType t) =>
            t switch
            {
                ShaderPropertyType.Float => "F",
                ShaderPropertyType.Int => "I",
                ShaderPropertyType.Bool => "B",
                _ => "?",
            };

        /// <summary>收集阶段轨道数据</summary>
        public static List<TrackInfo> CollectPhaseTracks(
            MaterialTransparencyAnimationTemplate tpl,
            PhaseAnimationConfig cfg
        )
        {
            var tracks = new List<TrackInfo>();

            // ── 透明度 ──
            if (tpl.useTransparency)
            {
                bool isBool = tpl.transparencyPropertyType == ShaderPropertyType.Bool;
                tracks.Add(
                    new TrackInfo
                    {
                        property = tpl.transparencyProperty,
                        typeTag = TypeTag(tpl.transparencyPropertyType),
                        color = COL_TRANS,
                        duration = cfg.duration,
                        fromVal = isBool ? 1f : cfg.transparencyFrom,
                        toVal = isBool ? 1f : cfg.transparencyTo,
                        isAnimated = !isBool,
                        isConstant = isBool,
                    }
                );
            }

            // ── 整体溶解 ──
            if (tpl.useOverall)
            {
                CollectOverallTracks(tpl, cfg, tracks);
            }

            // ── 轴向溶解 ──
            if (tpl.useAxisDissolve)
            {
                CollectAxisTracks(tpl, cfg, tracks);
            }

            // ── 自定义属性 ──
            foreach (var prop in cfg.customProperties)
            {
                bool anim = prop.mode == AnimPropertyMode.Animated;
                tracks.Add(
                    new TrackInfo
                    {
                        property = prop.propertyName,
                        typeTag = TypeTag(prop.type),
                        color = COL_CUSTOM,
                        duration = cfg.duration,
                        fromVal = anim ? prop.GetFromValue() : prop.GetConstantValue(),
                        toVal = anim ? prop.GetToValue() : prop.GetConstantValue(),
                        isAnimated = anim,
                        isConstant = !anim,
                    }
                );
            }

            return tracks;
        }

        private static void CollectOverallTracks(
            MaterialTransparencyAnimationTemplate tpl,
            PhaseAnimationConfig cfg,
            List<TrackInfo> tracks
        )
        {
            // _IsOverRall
            bool isBool = tpl.overallPropertyType == ShaderPropertyType.Bool;
            tracks.Add(
                new TrackInfo
                {
                    property = tpl.overallProperty,
                    typeTag = TypeTag(tpl.overallPropertyType),
                    color = COL_OVER,
                    duration = cfg.duration,
                    fromVal = isBool ? 1f : cfg.overallFrom,
                    toVal = isBool ? 1f : cfg.overallTo,
                    isAnimated = !isBool,
                    isConstant = isBool,
                }
            );

            // _DissolveThreshold
            bool dtBool = tpl.dissolveThresholdPropertyType == ShaderPropertyType.Bool;
            tracks.Add(
                new TrackInfo
                {
                    property = tpl.dissolveThresholdProperty,
                    typeTag = TypeTag(tpl.dissolveThresholdPropertyType),
                    color = COL_OVER,
                    duration = cfg.duration,
                    fromVal = dtBool ? 1f : cfg.overallDissolveFrom,
                    toVal = dtBool ? 1f : cfg.overallDissolveTo,
                    isAnimated = !dtBool,
                    isConstant = dtBool,
                }
            );

            // _NoiseScale
            bool nsBool = tpl.noiseScalePropertyType == ShaderPropertyType.Bool;
            tracks.Add(
                new TrackInfo
                {
                    property = tpl.noiseScaleProperty,
                    typeTag = TypeTag(tpl.noiseScalePropertyType),
                    color = COL_OVER,
                    duration = cfg.duration,
                    fromVal = nsBool ? 1f : cfg.overallNoiseScale,
                    toVal = nsBool ? 1f : cfg.overallNoiseScale,
                    isAnimated = false,
                    isConstant = true,
                }
            );

            // _NoiseIntensity
            bool niBool = tpl.noiseIntensityPropertyType == ShaderPropertyType.Bool;
            tracks.Add(
                new TrackInfo
                {
                    property = tpl.noiseIntensityProperty,
                    typeTag = TypeTag(tpl.noiseIntensityPropertyType),
                    color = COL_OVER,
                    duration = cfg.duration,
                    fromVal = niBool ? 1f : cfg.overallNoiseIntensity,
                    toVal = niBool ? 1f : cfg.overallNoiseIntensity,
                    isAnimated = false,
                    isConstant = true,
                }
            );
        }

        private static void CollectAxisTracks(
            MaterialTransparencyAnimationTemplate tpl,
            PhaseAnimationConfig cfg,
            List<TrackInfo> tracks
        )
        {
            // _IsDissolve
            tracks.Add(
                new TrackInfo
                {
                    property = tpl.dissolveToggleProperty,
                    typeTag = TypeTag(tpl.dissolveTogglePropertyType),
                    color = COL_AXIS,
                    duration = cfg.duration,
                    fromVal = 1f,
                    toVal = 1f,
                    isAnimated = false,
                    isConstant = true,
                }
            );

            // _AXIS
            tracks.Add(
                new TrackInfo
                {
                    property = tpl.axisProperty,
                    typeTag = TypeTag(tpl.axisPropertyType),
                    color = COL_AXIS,
                    duration = cfg.duration,
                    fromVal = (float)cfg.axisDirection,
                    toVal = (float)cfg.axisDirection,
                    isAnimated = false,
                    isConstant = true,
                }
            );

            // _DissolveThreshold
            bool dtBool = tpl.dissolveThresholdPropertyType == ShaderPropertyType.Bool;
            tracks.Add(
                new TrackInfo
                {
                    property = tpl.dissolveThresholdProperty,
                    typeTag = TypeTag(tpl.dissolveThresholdPropertyType),
                    color = COL_AXIS,
                    duration = cfg.duration,
                    fromVal = dtBool ? 1f : cfg.dissolveThresholdFrom,
                    toVal = dtBool ? 1f : cfg.dissolveThresholdTo,
                    isAnimated = !dtBool,
                    isConstant = dtBool,
                }
            );

            // _NoiseScale
            bool nsBool = tpl.noiseScalePropertyType == ShaderPropertyType.Bool;
            tracks.Add(
                new TrackInfo
                {
                    property = tpl.noiseScaleProperty,
                    typeTag = TypeTag(tpl.noiseScalePropertyType),
                    color = COL_AXIS,
                    duration = cfg.duration,
                    fromVal = nsBool ? 1f : cfg.noiseScale,
                    toVal = nsBool ? 1f : cfg.noiseScale,
                    isAnimated = false,
                    isConstant = true,
                }
            );

            // _NoiseIntensity
            bool niBool = tpl.noiseIntensityPropertyType == ShaderPropertyType.Bool;
            tracks.Add(
                new TrackInfo
                {
                    property = tpl.noiseIntensityProperty,
                    typeTag = TypeTag(tpl.noiseIntensityPropertyType),
                    color = COL_AXIS,
                    duration = cfg.duration,
                    fromVal = niBool ? 1f : cfg.noiseIntensity,
                    toVal = niBool ? 1f : cfg.noiseIntensity,
                    isAnimated = false,
                    isConstant = true,
                }
            );
        }
    }
}
