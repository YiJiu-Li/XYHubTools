using System;
using UnityEngine;

namespace YZJ.AnimationCreator
{
    /// <summary>
    /// 可添加到动画中的自定义 Shader 属性，支持 Float/Int/Bool 类型，
    /// 可选常量或 From→To 动画模式。
    /// </summary>
    [Serializable]
    public class AnimatedShaderProperty
    {
        [Tooltip("Shader 属性名 (如 _MyProperty)")]
        public string propertyName = "_NewProperty";

        [Tooltip("属性值类型")]
        public ShaderPropertyType type = ShaderPropertyType.Float;

        [Tooltip("常量 = 整个动画保持不变；动画 = 从起始值变化到结束值")]
        public AnimPropertyMode mode = AnimPropertyMode.Constant;

        // ── 常量值 ──
        public float constantFloat;
        public int constantInt;
        public bool constantBool;

        // ── 动画值 ──
        public float fromFloat;
        public float toFloat = 1f;
        public int fromInt;
        public int toInt = 1;
        public AnimationCurve curve = AnimationCurve.Linear(0, 0, 1, 1);

        /// <summary>获取常量模式下的 float 值</summary>
        public float GetConstantValue()
        {
            switch (type)
            {
                case ShaderPropertyType.Float:
                    return constantFloat;
                case ShaderPropertyType.Int:
                    return constantInt;
                case ShaderPropertyType.Bool:
                    return constantBool ? 1f : 0f;
                default:
                    return 0f;
            }
        }

        /// <summary>获取动画起始值</summary>
        public float GetFromValue()
        {
            return type == ShaderPropertyType.Int ? fromInt : fromFloat;
        }

        /// <summary>获取动画结束值</summary>
        public float GetToValue()
        {
            return type == ShaderPropertyType.Int ? toInt : toFloat;
        }

        public AnimatedShaderProperty Clone()
        {
            return new AnimatedShaderProperty
            {
                propertyName = propertyName,
                type = type,
                mode = mode,
                constantFloat = constantFloat,
                constantInt = constantInt,
                constantBool = constantBool,
                fromFloat = fromFloat,
                toFloat = toFloat,
                fromInt = fromInt,
                toInt = toInt,
                curve = new AnimationCurve(curve.keys),
            };
        }
    }
}
