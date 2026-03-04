using System;
using UnityEditor;
using UnityEngine;

namespace YZJ.AnimationCreator
{
    /// <summary>
    /// 动画曲线构建工具类
    /// </summary>
    public static class CurveBuilderUtility
    {
        /// <summary>
        /// 统一使用 EditorCurveBinding + AnimationUtility 写入曲线，
        /// 支持 MeshRenderer、SkinnedMeshRenderer、SpriteRenderer。
        /// </summary>
        public static void SetCurve(
            AnimationClip clip,
            string path,
            string propertyName,
            AnimationCurve curve,
            Type rendererType = null
        )
        {
            // 默认使用 Renderer 基类，可兼容所有渲染器类型
            var targetType = rendererType ?? typeof(Renderer);
            var binding = EditorCurveBinding.FloatCurve(path, targetType, propertyName);
            AnimationUtility.SetEditorCurve(clip, binding, curve);
        }

        /// <summary>
        /// 设置 GameObject 激活状态曲线
        /// </summary>
        public static void SetGameObjectActiveCurve(
            AnimationClip clip,
            string path,
            AnimationCurve curve
        )
        {
            var binding = EditorCurveBinding.FloatCurve(path, typeof(GameObject), "m_IsActive");
            AnimationUtility.SetEditorCurve(clip, binding, curve);
        }

        /// <summary>根据形状曲线构建动画曲线 (从 fromVal 到 toVal)</summary>
        public static AnimationCurve BuildAnimCurve(
            AnimationCurve shapeCurve,
            float duration,
            float fromVal,
            float toVal
        )
        {
            float startShape =
                (shapeCurve != null && shapeCurve.length > 0) ? shapeCurve.Evaluate(0f) : 0f;
            float endShape =
                (shapeCurve != null && shapeCurve.length > 0) ? shapeCurve.Evaluate(1f) : 1f;

            float startVal = Mathf.LerpUnclamped(fromVal, toVal, startShape);
            float endVal = Mathf.LerpUnclamped(fromVal, toVal, endShape);

            return new AnimationCurve(new Keyframe(0f, startVal), new Keyframe(duration, endVal));
        }

        /// <summary>首尾帧值相同的常量曲线</summary>
        public static AnimationCurve BuildConstantCurve(float duration, float value)
        {
            return new AnimationCurve(new Keyframe(0f, value), new Keyframe(duration, value));
        }

        /// <summary>构建从 0 到 1 的激活曲线</summary>
        public static AnimationCurve BuildActivateCurve(float duration, bool activateAtEnd)
        {
            return activateAtEnd
                ? new AnimationCurve(new Keyframe(0f, 0f), new Keyframe(duration, 1f))
                : new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(duration, 1f));
        }

        /// <summary>构建从 1 到 0 的隐藏曲线</summary>
        public static AnimationCurve BuildDeactivateCurve(float duration, bool deactivateAtEnd)
        {
            return deactivateAtEnd
                ? new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(duration, 0f))
                : new AnimationCurve(new Keyframe(0f, 0f), new Keyframe(duration, 0f));
        }

        /// <summary>构建保持状态的常量曲线</summary>
        public static AnimationCurve BuildKeepStateCurve(float duration, bool keepActive)
        {
            float value = keepActive ? 1f : 0f;
            return new AnimationCurve(new Keyframe(0f, value), new Keyframe(duration, value));
        }
    }
}
