using System;
using System.Collections.Generic;
using UnityEngine;

namespace YZJ
{
    /// <summary>
    /// Shader材质模板 - 定义Shader的贴图映射和默认参数
    /// </summary>
    [CreateAssetMenu(
        fileName = "NewShaderTemplate",
        menuName = "XZ工具/Shader材质模板",
        order = 100
    )]
    public class ShaderMaterialTemplate : ScriptableObject
    {
        [Header("基本信息")]
        [Tooltip("模板名称")]
        public string templateName = "新模板";

        [Tooltip("目标Shader")]
        public Shader targetShader;

        [Header("贴图属性映射")]
        [Tooltip("贴图属性映射列表")]
        public List<TexturePropertyMapping> texturePropertyMappings =
            new List<TexturePropertyMapping>();

        [Header("默认材质参数")]
        [Tooltip("Float类型参数")]
        public List<FloatParameter> floatParameters = new List<FloatParameter>();

        [Tooltip("Int类型参数（用于枚举，如 Surface_Type, Blend_Mode 等）")]
        public List<IntParameter> intParameters = new List<IntParameter>();

        [Tooltip("颜色类型参数")]
        public List<ColorParameter> colorParameters = new List<ColorParameter>();

        [Tooltip("需要启用的Keyword")]
        public List<string> enableKeywords = new List<string>();

        [Tooltip("需要禁用的Keyword")]
        public List<string> disableKeywords = new List<string>();

        [Header("渲染类型")]
        [Tooltip("材质的RenderType标签（None=使用Shader默认值，不覆盖）")]
        public MaterialRenderType renderType = MaterialRenderType.None;

        [Header("Surface Options（Ignore=不设置此项）")]
        [Tooltip("工作流模式：Specular / Metallic（对应 _WorkflowMode）")]
        public TemplateWorkflowMode workflowMode = TemplateWorkflowMode.Ignore;

        [Tooltip("表面类型：Opaque / Transparent（对应 _Surface）")]
        public TemplateSurfaceType surfaceType = TemplateSurfaceType.Ignore;

        [Tooltip("渲染面：Front 正面 / Back 背面 / Both 双面（对应 _Cull）")]
        public TemplateRenderFace renderFace = TemplateRenderFace.Ignore;

        [Tooltip("深度写入模式：Auto / ForceEnabled / ForceDisabled（对应 _ZWriteControl）")]
        public TemplateDepthWrite depthWrite = TemplateDepthWrite.Ignore;

        [Tooltip("深度测试函数（对应 _ZTest）")]
        public TemplateDepthTest depthTest = TemplateDepthTest.Ignore;

        [Tooltip("Alpha 裁切开关（Enabled 时同步写入 _AlphaClip=1 并启用 _ALPHATEST_ON）")]
        public TemplateToggle alphaClipping = TemplateToggle.Ignore;

        [Tooltip("Alpha 裁切阈值（仅 Alpha Clipping = Enabled 时生效，对应 _Cutoff）")]
        [Range(0f, 1f)]
        public float alphaCutoff = 0.5f;

        [Tooltip("投射阴影开关（通过 SetShaderPassEnabled 控制 ShadowCaster Pass）")]
        public TemplateToggle castShadows = TemplateToggle.Ignore;

        [Tooltip("接收阴影开关（对应 _ReceiveShadows）")]
        public TemplateToggle receiveShadows = TemplateToggle.Ignore;

        /// <summary>
        /// 创建默认的URP/Lit模板
        /// </summary>
        public static ShaderMaterialTemplate CreateURPLitTemplate()
        {
            var template = CreateInstance<ShaderMaterialTemplate>();
            template.templateName = "URP/Lit";
            template.targetShader = Shader.Find("Universal Render Pipeline/Lit");

            // 贴图映射
            template.texturePropertyMappings = new List<TexturePropertyMapping>
            {
                new TexturePropertyMapping
                {
                    shaderPropertyName = "_BaseMap",
                    textureSuffixes = new List<string>
                    {
                        "_m_BaseColor",
                        "m_BaseColor",
                        "_BaseColor",
                        "_Albedo",
                        "_Diffuse",
                    },
                    isNormalMap = false,
                },
                new TexturePropertyMapping
                {
                    shaderPropertyName = "_BumpMap",
                    textureSuffixes = new List<string>
                    {
                        "_m_Normal",
                        "m_Normal",
                        "_Normal",
                        "_NormalMap",
                    },
                    isNormalMap = true,
                },
                new TexturePropertyMapping
                {
                    shaderPropertyName = "_MetallicGlossMap",
                    textureSuffixes = new List<string> { "_m_ARM", "m_ARM", "_ARM", "_MaskMap" },
                    isNormalMap = false,
                },
                new TexturePropertyMapping
                {
                    shaderPropertyName = "_OcclusionMap",
                    textureSuffixes = new List<string> { "_m_ARM", "m_ARM", "_ARM", "_AO" },
                    isNormalMap = false,
                },
            };

            // 默认参数
            template.floatParameters = new List<FloatParameter>
            {
                new FloatParameter { propertyName = "_SpecularHighlights", value = 0f },
                new FloatParameter { propertyName = "_EnvironmentReflections", value = 0f },
                new FloatParameter { propertyName = "_ReceiveShadows", value = 1f },
            };

            return template;
        }

        /// <summary>
        /// 创建默认的PBR_SG模板
        /// </summary>
        public static ShaderMaterialTemplate CreatePBRSGTemplate()
        {
            var template = CreateInstance<ShaderMaterialTemplate>();
            template.templateName = "PBR_SG";
            template.targetShader = Shader.Find("Shader Graphs/PBR_SG");

            // 贴图映射
            template.texturePropertyMappings = new List<TexturePropertyMapping>
            {
                new TexturePropertyMapping
                {
                    shaderPropertyName = "_BaseMap",
                    textureSuffixes = new List<string>
                    {
                        "_m_BaseColor",
                        "m_BaseColor",
                        "_BaseColor",
                    },
                    isNormalMap = false,
                },
                new TexturePropertyMapping
                {
                    shaderPropertyName = "_NormalMap",
                    textureSuffixes = new List<string> { "_m_Normal", "m_Normal", "_Normal" },
                    isNormalMap = true,
                },
                new TexturePropertyMapping
                {
                    shaderPropertyName = "_ARMMap",
                    textureSuffixes = new List<string> { "_m_ARM", "m_ARM", "_ARM" },
                    isNormalMap = false,
                },
            };

            // 默认参数
            template.floatParameters = new List<FloatParameter>
            {
                new FloatParameter { propertyName = "_BlendModePreserveSpecular", value = 0f },
                new FloatParameter { propertyName = "_ReceiveShadows", value = 0f },
                new FloatParameter { propertyName = "_ARM_ON", value = 1f },
            };

            // 启用的Keyword
            template.enableKeywords = new List<string> { "_ARM_ON_ON" };

            return template;
        }
    }

    /// <summary>
    /// 材质RenderType枚举
    /// </summary>
    public enum MaterialRenderType
    {
        [Tooltip("不覆盖，使用Shader默认值")]
        None,
        Opaque,
        Transparent,
        TransparentCutout,
        Background,
        Overlay,
    }

    /// <summary>工作流模式（对应 _WorkflowMode）</summary>
    public enum TemplateWorkflowMode
    {
        Ignore = -1,
        Specular = 0,
        Metallic = 1,
    }

    /// <summary>表面类型（对应 _Surface）</summary>
    public enum TemplateSurfaceType
    {
        Ignore = -1,
        Opaque = 0,
        Transparent = 1,
    }

    /// <summary>渲染面（对应 _Cull；Front=显示正面/剔除背面，Back=显示背面/剔除正面，Both=双面）</summary>
    public enum TemplateRenderFace
    {
        Ignore = -1,
        Front = 0, // _Cull = 2 (CullMode.Back)
        Back = 1, // _Cull = 1 (CullMode.Front)
        Both = 2, // _Cull = 0 (CullMode.Off)
    }

    /// <summary>深度写入控制（对应 _ZWriteControl）</summary>
    public enum TemplateDepthWrite
    {
        Ignore = -1,
        Auto = 0,
        ForceEnabled = 1,
        ForceDisabled = 2,
    }

    /// <summary>深度测试函数（对应 _ZTest，使用 UnityEngine.Rendering.CompareFunction 值）</summary>
    public enum TemplateDepthTest
    {
        Ignore = -1,
        Disabled = 0,
        Never = 1,
        Less = 2,
        Equal = 3,
        LessEqual = 4,
        Greater = 5,
        NotEqual = 6,
        GreaterEqual = 7,
        Always = 8,
    }

    /// <summary>三态开关：Ignore=不设置，Disabled=关，Enabled=开</summary>
    public enum TemplateToggle
    {
        Ignore = -1,
        Disabled = 0,
        Enabled = 1,
    }

    /// <summary>
    /// 贴图属性映射
    /// </summary>
    [Serializable]
    public class TexturePropertyMapping
    {
        [Tooltip("Shader中的贴图属性名，如 _BaseMap, _NormalMap")]
        public string shaderPropertyName;

        [Tooltip("贴图文件名后缀，如 _m_BaseColor, m_Normal")]
        public List<string> textureSuffixes = new List<string>();

        [Tooltip("是否为法线贴图（会自动设置贴图类型）")]
        public bool isNormalMap;
    }

    /// <summary>
    /// Float类型参数
    /// </summary>
    [Serializable]
    public class FloatParameter
    {
        [Tooltip("Shader属性名")]
        public string propertyName;

        [Tooltip("默认值")]
        public float value;
    }

    /// <summary>
    /// Int类型参数（用于枚举属性，如 Surface_Type, Blend_Mode 等）
    /// </summary>
    [Serializable]
    public class IntParameter
    {
        [Tooltip("Shader属性名，如 _Surface, _Blend, _Cull")]
        public string propertyName;

        [Tooltip("整数值（0=Opaque, 1=Transparent 等）")]
        public int value;
    }

    /// <summary>
    /// 颜色类型参数
    /// </summary>
    [Serializable]
    public class ColorParameter
    {
        [Tooltip("Shader属性名")]
        public string propertyName;

        [Tooltip("默认颜色")]
        public Color value = Color.white;
    }
}
