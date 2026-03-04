using UnityEngine;

namespace Framework.GPUNoise
{
    /// <summary>
    /// GPU噪声类型枚举
    /// </summary>
    public enum NoiseType
    {
        [InspectorName("Perlin (柏林噪声)")]
        Perlin,

        [InspectorName("Simplex (单形噪声)")]
        Simplex,

        [InspectorName("Worley (细胞噪声)")]
        Worley,

        [InspectorName("Value (值噪声)")]
        Value,

        [InspectorName("FBM (分形噪声)")]
        FBM,

        [InspectorName("Ridged (山脊噪声)")]
        Ridged,

        [InspectorName("Turbulence (湍流噪声)")]
        Turbulence,

        [InspectorName("VoronoiEdge (沃罗诺伊边缘)")]
        VoronoiEdge,
    }

    /// <summary>
    /// 噪声混合模式枚举
    /// </summary>
    public enum NoiseBlendMode
    {
        [InspectorName("Mix (混合)")]
        Mix = 0,

        [InspectorName("Add (叠加)")]
        Add = 1,

        [InspectorName("Multiply (正片叠底)")]
        Multiply = 2,

        [InspectorName("Screen (滤色)")]
        Screen = 3,

        [InspectorName("Overlay (叠加)")]
        Overlay = 4,

        [InspectorName("Difference (差值)")]
        Difference = 5,
    }

    /// <summary>
    /// GPU噪声生成器 - 在GPU上高效生成各种类型的噪声纹理
    /// </summary>
    [CreateAssetMenu(fileName = "GPUNoiseGenerator", menuName = "噪声图工具/GPU Noise Generator")]
    public class GPUNoiseGenerator : ScriptableObject
    {
        [Header("Compute Shader")]
        [SerializeField]
        private ComputeShader noiseComputeShader;

        [Header("噪声设置")]
        [SerializeField]
        private NoiseType noiseType = NoiseType.Perlin;

        [SerializeField, Range(64, 4096)]
        private int resolution = 512;

        [SerializeField, Range(0.1f, 100f)]
        private float scale = 10f;

        [SerializeField]
        private Vector2 offset = Vector2.zero;

        [SerializeField]
        private float seed = 0f;

        [Header("FBM/Ridged/Turbulence 设置")]
        [SerializeField, Range(1, 8)]
        private int octaves = 4;

        [SerializeField, Range(0f, 1f)]
        private float persistence = 0.5f;

        [SerializeField, Range(1f, 4f)]
        private float lacunarity = 2f;

        [Header("输出设置")]
        [SerializeField]
        private bool invert = false;

        [SerializeField]
        private Color tintColor = Color.white;

        [SerializeField]
        private bool seamless = false;

        [SerializeField, Range(0.5f, 3f)]
        private float contrast = 1f;

        [SerializeField, Range(-0.5f, 0.5f)]
        private float brightness = 0f;

        [Header("法线贴图")]
        [SerializeField, Range(0.1f, 10f)]
        private float normalStrength = 1f;

        [Header("域变形 (Domain Warping)")]
        [SerializeField]
        private bool enableDomainWarp = false;

        [SerializeField, Range(0f, 2f)]
        private float warpStrength = 0.5f;

        [SerializeField, Range(0.1f, 50f)]
        private float warpScale = 5f;

        [SerializeField, Range(1, 4)]
        private int warpIterations = 2;

        [Header("渐变映射 (Gradient Mapping)")]
        [SerializeField]
        private bool enableGradientMap = false;

        [SerializeField]
        private Texture2D gradientTexture;

        [Header("噪声混合 (Noise Blending)")]
        [SerializeField]
        private bool enableBlend = false;

        [SerializeField]
        private Texture2D blendTexture;

        [SerializeField]
        private NoiseBlendMode blendMode = NoiseBlendMode.Mix;

        [SerializeField, Range(0f, 1f)]
        private float blendFactor = 0.5f;

        // Kernel indices
        private int perlinKernel;
        private int simplexKernel;
        private int worleyKernel;
        private int valueKernel;
        private int fbmKernel;
        private int ridgedKernel;
        private int turbulenceKernel;
        private int voronoiEdgeKernel;
        private int normalMapKernel;
        private int domainWarpKernel;
        private int gradientMapKernel;
        private int noiseBlendKernel;

        // Properties
        public ComputeShader NoiseComputeShader
        {
            get => noiseComputeShader;
            set => noiseComputeShader = value;
        }

        public NoiseType NoiseType
        {
            get => noiseType;
            set => noiseType = value;
        }

        public int Resolution
        {
            get => resolution;
            set => resolution = Mathf.Clamp(value, 64, 4096);
        }

        public float Scale
        {
            get => scale;
            set => scale = Mathf.Max(0.1f, value);
        }

        public Vector2 Offset
        {
            get => offset;
            set => offset = value;
        }

        public float Seed
        {
            get => seed;
            set => seed = value;
        }

        public int Octaves
        {
            get => octaves;
            set => octaves = Mathf.Clamp(value, 1, 8);
        }

        public float Persistence
        {
            get => persistence;
            set => persistence = Mathf.Clamp01(value);
        }

        public float Lacunarity
        {
            get => lacunarity;
            set => lacunarity = Mathf.Clamp(value, 1f, 4f);
        }

        public bool Invert
        {
            get => invert;
            set => invert = value;
        }

        public Color TintColor
        {
            get => tintColor;
            set => tintColor = value;
        }

        public bool Seamless
        {
            get => seamless;
            set => seamless = value;
        }

        public float Contrast
        {
            get => contrast;
            set => contrast = Mathf.Clamp(value, 0.5f, 3f);
        }

        public float Brightness
        {
            get => brightness;
            set => brightness = Mathf.Clamp(value, -0.5f, 0.5f);
        }

        public float NormalStrength
        {
            get => normalStrength;
            set => normalStrength = Mathf.Clamp(value, 0.1f, 10f);
        }

        // 域变形属性
        public bool EnableDomainWarp
        {
            get => enableDomainWarp;
            set => enableDomainWarp = value;
        }

        public float WarpStrength
        {
            get => warpStrength;
            set => warpStrength = Mathf.Clamp(value, 0f, 2f);
        }

        public float WarpScale
        {
            get => warpScale;
            set => warpScale = Mathf.Clamp(value, 0.1f, 50f);
        }

        public int WarpIterations
        {
            get => warpIterations;
            set => warpIterations = Mathf.Clamp(value, 1, 4);
        }

        // 渐变映射属性
        public bool EnableGradientMap
        {
            get => enableGradientMap;
            set => enableGradientMap = value;
        }

        public Texture2D GradientTexture
        {
            get => gradientTexture;
            set => gradientTexture = value;
        }

        // 噪声混合属性
        public bool EnableBlend
        {
            get => enableBlend;
            set => enableBlend = value;
        }

        public Texture2D BlendTexture
        {
            get => blendTexture;
            set => blendTexture = value;
        }

        public NoiseBlendMode BlendMode
        {
            get => blendMode;
            set => blendMode = value;
        }

        public float BlendFactor
        {
            get => blendFactor;
            set => blendFactor = Mathf.Clamp01(value);
        }

        private void OnEnable()
        {
            InitializeKernels();
        }

        private void InitializeKernels()
        {
            if (noiseComputeShader == null)
                return;

            perlinKernel = noiseComputeShader.FindKernel("CSPerlinNoise");
            simplexKernel = noiseComputeShader.FindKernel("CSSimplexNoise");
            worleyKernel = noiseComputeShader.FindKernel("CSWorleyNoise");
            valueKernel = noiseComputeShader.FindKernel("CSValueNoise");
            fbmKernel = noiseComputeShader.FindKernel("CSFBMNoise");
            ridgedKernel = noiseComputeShader.FindKernel("CSRidgedNoise");
            turbulenceKernel = noiseComputeShader.FindKernel("CSTurbulenceNoise");
            voronoiEdgeKernel = noiseComputeShader.FindKernel("CSVoronoiEdgeNoise");
            normalMapKernel = noiseComputeShader.FindKernel("CSNormalMap");
            domainWarpKernel = noiseComputeShader.FindKernel("CSDomainWarp");
            gradientMapKernel = noiseComputeShader.FindKernel("CSGradientMap");
            noiseBlendKernel = noiseComputeShader.FindKernel("CSNoiseBlend");
        }

        /// <summary>
        /// 生成噪声纹理
        /// </summary>
        /// <returns>生成的RenderTexture</returns>
        public RenderTexture GenerateNoise()
        {
            if (noiseComputeShader == null)
            {
                Debug.LogError("GPUNoiseGenerator: Compute Shader未设置!");
                return null;
            }

            InitializeKernels();

            // 创建RenderTexture
            RenderTexture renderTexture = new RenderTexture(
                resolution,
                resolution,
                0,
                RenderTextureFormat.ARGB32
            );
            renderTexture.enableRandomWrite = true;
            renderTexture.Create();

            // 设置Shader参数
            SetShaderParameters(renderTexture);

            // 获取对应的kernel
            int kernel = GetKernelForNoiseType();

            // 计算线程组数量
            int threadGroups = Mathf.CeilToInt(resolution / 8.0f);

            // 分派计算
            noiseComputeShader.Dispatch(kernel, threadGroups, threadGroups, 1);

            // 应用后处理效果
            ApplyPostProcessing(renderTexture);

            return renderTexture;
        }

        /// <summary>
        /// 应用后处理效果（域变形、渐变映射、噪声混合）
        /// </summary>
        private void ApplyPostProcessing(RenderTexture renderTexture)
        {
            int threadGroups = Mathf.CeilToInt(resolution / 8.0f);

            // 1. 域变形 (Domain Warping)
            if (enableDomainWarp)
            {
                noiseComputeShader.SetTexture(domainWarpKernel, "_SourceTex", renderTexture);
                noiseComputeShader.SetTexture(domainWarpKernel, "Result", renderTexture);
                noiseComputeShader.SetFloat("_WarpStrength", warpStrength);
                noiseComputeShader.SetFloat("_WarpScale", warpScale);
                noiseComputeShader.SetInt("_WarpIterations", warpIterations);
                noiseComputeShader.SetInt("_Resolution", resolution);
                noiseComputeShader.SetFloat("_Seed", seed);
                noiseComputeShader.Dispatch(domainWarpKernel, threadGroups, threadGroups, 1);
            }

            // 2. 噪声混合 (Noise Blending)
            if (enableBlend && blendTexture != null)
            {
                noiseComputeShader.SetTexture(noiseBlendKernel, "_SourceTex", renderTexture);
                noiseComputeShader.SetTexture(noiseBlendKernel, "_BlendTex", blendTexture);
                noiseComputeShader.SetTexture(noiseBlendKernel, "Result", renderTexture);
                noiseComputeShader.SetFloat("_BlendFactor", blendFactor);
                noiseComputeShader.SetInt("_BlendMode", (int)blendMode);
                noiseComputeShader.SetInt("_Resolution", resolution);
                noiseComputeShader.Dispatch(noiseBlendKernel, threadGroups, threadGroups, 1);
            }

            // 3. 渐变映射 (Gradient Mapping)
            if (enableGradientMap && gradientTexture != null)
            {
                noiseComputeShader.SetTexture(gradientMapKernel, "_SourceTex", renderTexture);
                noiseComputeShader.SetTexture(gradientMapKernel, "_GradientTex", gradientTexture);
                noiseComputeShader.SetTexture(gradientMapKernel, "Result", renderTexture);
                noiseComputeShader.SetInt("_Resolution", resolution);
                noiseComputeShader.Dispatch(gradientMapKernel, threadGroups, threadGroups, 1);
            }
        }

        /// <summary>
        /// 生成噪声并返回Texture2D
        /// </summary>
        /// <returns>生成的Texture2D</returns>
        public Texture2D GenerateNoiseTexture2D()
        {
            RenderTexture rt = GenerateNoise();
            if (rt == null)
                return null;

            Texture2D texture = new Texture2D(resolution, resolution, TextureFormat.RGBA32, false);

            RenderTexture.active = rt;
            texture.ReadPixels(new Rect(0, 0, resolution, resolution), 0, 0);
            texture.Apply();
            RenderTexture.active = null;

            rt.Release();
            DestroyImmediate(rt);

            return texture;
        }

        /// <summary>
        /// 使用自定义参数生成噪声
        /// </summary>
        public RenderTexture GenerateNoise(
            NoiseType type,
            int res,
            float scl,
            Vector2 off,
            float sd
        )
        {
            noiseType = type;
            resolution = res;
            scale = scl;
            offset = off;
            seed = sd;

            return GenerateNoise();
        }

        private void SetShaderParameters(RenderTexture result)
        {
            int kernel = GetKernelForNoiseType();

            noiseComputeShader.SetTexture(kernel, "Result", result);
            noiseComputeShader.SetVector("_Offset", offset);
            noiseComputeShader.SetFloat("_Scale", scale);
            noiseComputeShader.SetFloat("_Seed", seed);
            noiseComputeShader.SetInt("_Octaves", octaves);
            noiseComputeShader.SetFloat("_Persistence", persistence);
            noiseComputeShader.SetFloat("_Lacunarity", lacunarity);
            noiseComputeShader.SetInt("_Resolution", resolution);
            noiseComputeShader.SetInt("_Invert", invert ? 1 : 0);
            noiseComputeShader.SetVector("_TintColor", tintColor);
            noiseComputeShader.SetInt("_Seamless", seamless ? 1 : 0);
            noiseComputeShader.SetFloat("_Contrast", contrast);
            noiseComputeShader.SetFloat("_Brightness", brightness);
            noiseComputeShader.SetFloat("_NormalStrength", normalStrength);
        }

        private int GetKernelForNoiseType()
        {
            switch (noiseType)
            {
                case NoiseType.Perlin:
                    return perlinKernel;
                case NoiseType.Simplex:
                    return simplexKernel;
                case NoiseType.Worley:
                    return worleyKernel;
                case NoiseType.Value:
                    return valueKernel;
                case NoiseType.FBM:
                    return fbmKernel;
                case NoiseType.Ridged:
                    return ridgedKernel;
                case NoiseType.Turbulence:
                    return turbulenceKernel;
                case NoiseType.VoronoiEdge:
                    return voronoiEdgeKernel;
                default:
                    return perlinKernel;
            }
        }

        /// <summary>
        /// 生成法线贴图
        /// </summary>
        public Texture2D GenerateNormalMap()
        {
            // 先生成噪声
            Texture2D noiseTex = GenerateNoiseTexture2D();
            if (noiseTex == null)
                return null;

            // 创建结果纹理
            RenderTexture normalRT = new RenderTexture(
                resolution,
                resolution,
                0,
                RenderTextureFormat.ARGB32
            );
            normalRT.enableRandomWrite = true;
            normalRT.Create();

            // 设置参数
            noiseComputeShader.SetTexture(normalMapKernel, "_SourceTex", noiseTex);
            noiseComputeShader.SetTexture(normalMapKernel, "Result", normalRT);
            noiseComputeShader.SetInt("_Resolution", resolution);
            noiseComputeShader.SetFloat("_NormalStrength", normalStrength);

            int threadGroups = Mathf.CeilToInt(resolution / 8.0f);
            noiseComputeShader.Dispatch(normalMapKernel, threadGroups, threadGroups, 1);

            // 读取结果
            Texture2D normalTex = new Texture2D(
                resolution,
                resolution,
                TextureFormat.RGBA32,
                false
            );
            RenderTexture.active = normalRT;
            normalTex.ReadPixels(new Rect(0, 0, resolution, resolution), 0, 0);
            normalTex.Apply();
            RenderTexture.active = null;

            // 清理
            normalRT.Release();
            DestroyImmediate(normalRT);
            DestroyImmediate(noiseTex);

            return normalTex;
        }

        /// <summary>
        /// 随机化种子
        /// </summary>
        public void RandomizeSeed()
        {
            seed = Random.Range(-10000f, 10000f);
        }
    }
}
