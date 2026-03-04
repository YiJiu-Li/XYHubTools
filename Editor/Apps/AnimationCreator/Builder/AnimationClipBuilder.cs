using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace YZJ.AnimationCreator
{
    /// <summary>
    /// 动画剪辑构建器 - 负责根据模板和配置创建 AnimationClip
    /// 支持 MeshRenderer、SkinnedMeshRenderer、SpriteRenderer
    /// </summary>
    public class AnimationClipBuilder
    {
        private readonly MaterialTransparencyAnimationTemplate _template;
        private readonly bool _animateAllMaterials;
        private readonly int _specificMaterialIndex;

        public AnimationClipBuilder(
            MaterialTransparencyAnimationTemplate template,
            bool animateAllMaterials = true,
            int specificMaterialIndex = 0
        )
        {
            _template = template;
            _animateAllMaterials = animateAllMaterials;
            _specificMaterialIndex = specificMaterialIndex;
        }

        /// <summary>
        /// 根据类型构建动画剪辑（不保存到磁盘）
        /// </summary>
        public AnimationClip BuildClip(
            string clipName,
            ClipType type,
            PhaseAnimationConfig cfg,
            GameObject targetObject
        )
        {
            if (targetObject == null || _template == null)
                return null;

            var clip = new AnimationClip { frameRate = _template.frameRate, name = clipName };

            // 获取所有支持的渲染器类型
            var allRenderers = GetAllSupportedRenderers(targetObject);

            if (allRenderers.Count == 0)
            {
                Debug.LogWarning(
                    $"[AnimationClipBuilder] {targetObject.name} 及其子物体中未找到任何 Renderer (MeshRenderer/SkinnedMeshRenderer/SpriteRenderer)"
                );
                return null;
            }

            foreach (var (renderer, rendererType) in allRenderers)
            {
                if (renderer == null)
                    continue;

                string relPath = GetRelativePath(targetObject.transform, renderer.transform);
                int matCount = renderer.sharedMaterials.Length;

                for (int i = 0; i < matCount; i++)
                {
                    if (!_animateAllMaterials && i != _specificMaterialIndex)
                        continue;

                    string prefix = matCount > 1 ? $"m_Materials.Array.data[{i}]." : "material.";

                    switch (type)
                    {
                        case ClipType.Axis:
                            SetAxisCurves(clip, relPath, prefix, cfg, rendererType);
                            break;
                        case ClipType.OverRall:
                            SetOverRallCurves(clip, relPath, prefix, cfg, rendererType);
                            break;
                        case ClipType.Transprance:
                            SetTranspranceCurves(clip, relPath, prefix, cfg, rendererType);
                            break;
                    }
                }
            }

            Debug.Log(
                $"[AnimationClipBuilder] {clipName}: 共处理 {allRenderers.Count} 个 Renderer"
            );

            // 控制子物体粒子系统的显示/隐藏
            if (_template.controlChildParticles)
            {
                AddParticleSystemCurves(clip, targetObject, cfg, clipName.Contains("chuxian"));
            }

            return clip;
        }

        /// <summary>
        /// 获取所有支持的渲染器（MeshRenderer, SkinnedMeshRenderer, SpriteRenderer）
        /// </summary>
        private List<(Renderer renderer, Type type)> GetAllSupportedRenderers(GameObject go)
        {
            var result = new List<(Renderer, Type)>();

            // MeshRenderer
            foreach (var r in go.GetComponentsInChildren<MeshRenderer>(true))
                result.Add((r, typeof(MeshRenderer)));

            // SkinnedMeshRenderer
            foreach (var r in go.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                result.Add((r, typeof(SkinnedMeshRenderer)));

            // SpriteRenderer
            foreach (var r in go.GetComponentsInChildren<SpriteRenderer>(true))
                result.Add((r, typeof(SpriteRenderer)));

            return result;
        }

        #region Curve Setters

        /// <summary>Axis 类型: IsDissolve=1, _AXIS, _DissolveThreshold, _NoiseScale, _NoiseIntensity</summary>
        private void SetAxisCurves(
            AnimationClip clip,
            string path,
            string prefix,
            PhaseAnimationConfig cfg,
            Type rendererType
        )
        {
            var t = _template;

            // _IsDissolve
            CurveBuilderUtility.SetCurve(
                clip,
                path,
                prefix + t.dissolveToggleProperty,
                t.dissolveTogglePropertyType == ShaderPropertyType.Bool
                    ? CurveBuilderUtility.BuildConstantCurve(cfg.duration, 1f)
                    : CurveBuilderUtility.BuildConstantCurve(cfg.duration, 1f),
                rendererType
            );

            // _AXIS
            CurveBuilderUtility.SetCurve(
                clip,
                path,
                prefix + t.axisProperty,
                CurveBuilderUtility.BuildConstantCurve(cfg.duration, (float)cfg.axisDirection),
                rendererType
            );

            // _DissolveThreshold
            if (t.dissolveThresholdPropertyType == ShaderPropertyType.Bool)
            {
                CurveBuilderUtility.SetCurve(
                    clip,
                    path,
                    prefix + t.dissolveThresholdProperty,
                    CurveBuilderUtility.BuildConstantCurve(cfg.duration, 1f),
                    rendererType
                );
            }
            else
            {
                CurveBuilderUtility.SetCurve(
                    clip,
                    path,
                    prefix + t.dissolveThresholdProperty,
                    CurveBuilderUtility.BuildAnimCurve(
                        cfg.dissolveCurve,
                        cfg.duration,
                        cfg.dissolveThresholdFrom,
                        cfg.dissolveThresholdTo
                    ),
                    rendererType
                );
            }

            // _NoiseScale
            CurveBuilderUtility.SetCurve(
                clip,
                path,
                prefix + t.noiseScaleProperty,
                t.noiseScalePropertyType == ShaderPropertyType.Bool
                    ? CurveBuilderUtility.BuildConstantCurve(cfg.duration, 1f)
                    : CurveBuilderUtility.BuildConstantCurve(cfg.duration, cfg.noiseScale),
                rendererType
            );

            // _NoiseIntensity
            CurveBuilderUtility.SetCurve(
                clip,
                path,
                prefix + t.noiseIntensityProperty,
                t.noiseIntensityPropertyType == ShaderPropertyType.Bool
                    ? CurveBuilderUtility.BuildConstantCurve(cfg.duration, 1f)
                    : CurveBuilderUtility.BuildConstantCurve(cfg.duration, cfg.noiseIntensity),
                rendererType
            );
        }

        /// <summary>OverRall 类型: _OverRall + _DissolveThreshold + Noise</summary>
        private void SetOverRallCurves(
            AnimationClip clip,
            string path,
            string prefix,
            PhaseAnimationConfig cfg,
            Type rendererType
        )
        {
            var t = _template;

            // _OverRall
            if (t.overallPropertyType == ShaderPropertyType.Bool)
            {
                CurveBuilderUtility.SetCurve(
                    clip,
                    path,
                    prefix + t.overallProperty,
                    CurveBuilderUtility.BuildConstantCurve(cfg.duration, 1f),
                    rendererType
                );
            }
            else
            {
                CurveBuilderUtility.SetCurve(
                    clip,
                    path,
                    prefix + t.overallProperty,
                    CurveBuilderUtility.BuildAnimCurve(
                        cfg.overallCurve,
                        cfg.duration,
                        cfg.overallFrom,
                        cfg.overallTo
                    ),
                    rendererType
                );
            }

            // _DissolveThreshold
            if (t.dissolveThresholdPropertyType == ShaderPropertyType.Bool)
            {
                CurveBuilderUtility.SetCurve(
                    clip,
                    path,
                    prefix + t.dissolveThresholdProperty,
                    CurveBuilderUtility.BuildConstantCurve(cfg.duration, 1f),
                    rendererType
                );
            }
            else
            {
                CurveBuilderUtility.SetCurve(
                    clip,
                    path,
                    prefix + t.dissolveThresholdProperty,
                    CurveBuilderUtility.BuildAnimCurve(
                        cfg.overallDissolveCurve,
                        cfg.duration,
                        cfg.overallDissolveFrom,
                        cfg.overallDissolveTo
                    ),
                    rendererType
                );
            }

            // _NoiseScale
            CurveBuilderUtility.SetCurve(
                clip,
                path,
                prefix + t.noiseScaleProperty,
                t.noiseScalePropertyType == ShaderPropertyType.Bool
                    ? CurveBuilderUtility.BuildConstantCurve(cfg.duration, 1f)
                    : CurveBuilderUtility.BuildConstantCurve(cfg.duration, cfg.overallNoiseScale),
                rendererType
            );

            // _NoiseIntensity
            CurveBuilderUtility.SetCurve(
                clip,
                path,
                prefix + t.noiseIntensityProperty,
                t.noiseIntensityPropertyType == ShaderPropertyType.Bool
                    ? CurveBuilderUtility.BuildConstantCurve(cfg.duration, 1f)
                    : CurveBuilderUtility.BuildConstantCurve(
                        cfg.duration,
                        cfg.overallNoiseIntensity
                    ),
                rendererType
            );
        }

        /// <summary>Transprance 类型: _Transparency 从 from → to</summary>
        private void SetTranspranceCurves(
            AnimationClip clip,
            string path,
            string prefix,
            PhaseAnimationConfig cfg,
            Type rendererType
        )
        {
            var t = _template;

            if (t.transparencyPropertyType == ShaderPropertyType.Bool)
            {
                CurveBuilderUtility.SetCurve(
                    clip,
                    path,
                    prefix + t.transparencyProperty,
                    CurveBuilderUtility.BuildConstantCurve(cfg.duration, 1f),
                    rendererType
                );
            }
            else
            {
                CurveBuilderUtility.SetCurve(
                    clip,
                    path,
                    prefix + t.transparencyProperty,
                    CurveBuilderUtility.BuildAnimCurve(
                        cfg.transparencyCurve,
                        cfg.duration,
                        cfg.transparencyFrom,
                        cfg.transparencyTo
                    ),
                    rendererType
                );
            }
        }

        #endregion

        #region Particle System

        /// <summary>为子物体粒子系统添加激活/隐藏动画曲线</summary>
        private void AddParticleSystemCurves(
            AnimationClip clip,
            GameObject go,
            PhaseAnimationConfig cfg,
            bool isAppear
        )
        {
            var childParticles = go.GetComponentsInChildren<ParticleSystem>(true);
            int count = 0;

            foreach (var ps in childParticles)
            {
                if (ps == null || ps.transform == go.transform)
                    continue;

                string relPath = GetRelativePath(go.transform, ps.transform);
                AnimationCurve curve = BuildParticleCurve(cfg, isAppear);

                if (curve != null)
                {
                    CurveBuilderUtility.SetGameObjectActiveCurve(clip, relPath, curve);
                    count++;
                }
            }

            if (count > 0)
            {
                Debug.Log($"[AnimationClipBuilder] 已为 {count} 个粒子系统添加激活动画");
            }
        }

        private AnimationCurve BuildParticleCurve(PhaseAnimationConfig cfg, bool isAppear)
        {
            if (isAppear)
            {
                switch (_template.appearParticleTiming)
                {
                    case ParticleActivateTiming.AtStart:
                        return CurveBuilderUtility.BuildKeepStateCurve(cfg.duration, true);
                    case ParticleActivateTiming.AtEnd:
                        return CurveBuilderUtility.BuildActivateCurve(cfg.duration, true);
                    case ParticleActivateTiming.KeepActive:
                        return CurveBuilderUtility.BuildKeepStateCurve(cfg.duration, true);
                    case ParticleActivateTiming.KeepHidden:
                        return CurveBuilderUtility.BuildKeepStateCurve(cfg.duration, false);
                }
            }
            else
            {
                switch (_template.disappearParticleTiming)
                {
                    case ParticleDeactivateTiming.AtStart:
                        return CurveBuilderUtility.BuildKeepStateCurve(cfg.duration, false);
                    case ParticleDeactivateTiming.AtEnd:
                        return CurveBuilderUtility.BuildDeactivateCurve(cfg.duration, true);
                    case ParticleDeactivateTiming.KeepActive:
                        return CurveBuilderUtility.BuildKeepStateCurve(cfg.duration, true);
                    case ParticleDeactivateTiming.KeepHidden:
                        return CurveBuilderUtility.BuildKeepStateCurve(cfg.duration, false);
                }
            }
            return null;
        }

        #endregion

        #region Utilities

        private static string GetRelativePath(Transform root, Transform target)
        {
            if (root == target)
                return "";

            var pathParts = new List<string>();
            Transform current = target;
            while (current != null && current != root)
            {
                pathParts.Insert(0, current.name);
                current = current.parent;
            }
            return string.Join("/", pathParts);
        }

        #endregion
    }
}
