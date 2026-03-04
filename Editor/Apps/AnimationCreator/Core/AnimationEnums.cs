using UnityEngine;

namespace YZJ.AnimationCreator
{
    /// <summary>
    /// 轴向方向 — 对应 Shader 中 _AXIS (float 0‑3)
    /// </summary>
    public enum AxisDirection
    {
        [InspectorName("0: X (左→右)")]
        XPositive = 0,

        [InspectorName("1: _X (右→左)")]
        XNegative = 1,

        [InspectorName("2: Y (下→上)")]
        YPositive = 2,

        [InspectorName("3: _Y (上→下)")]
        YNegative = 3,
    }

    /// <summary>Shader 属性值类型</summary>
    public enum ShaderPropertyType
    {
        Float,
        Int,
        Bool,
    }

    /// <summary>属性动画模式</summary>
    public enum AnimPropertyMode
    {
        [InspectorName("常量 (Constant)")]
        Constant,

        [InspectorName("动画 (From → To)")]
        Animated,
    }

    /// <summary>粒子激活时机</summary>
    public enum ParticleActivateTiming
    {
        [InspectorName("动画开始时激活")]
        AtStart,

        [InspectorName("动画结束时激活")]
        AtEnd,

        [InspectorName("保持激活")]
        KeepActive,

        [InspectorName("保持隐藏")]
        KeepHidden,
    }

    /// <summary>粒子隐藏时机</summary>
    public enum ParticleDeactivateTiming
    {
        [InspectorName("动画开始时隐藏")]
        AtStart,

        [InspectorName("动画结束时隐藏")]
        AtEnd,

        [InspectorName("保持激活")]
        KeepActive,

        [InspectorName("保持隐藏")]
        KeepHidden,
    }

    /// <summary>动画剪辑类型</summary>
    public enum ClipType
    {
        Axis,
        OverRall,
        Transprance,
    }
}
