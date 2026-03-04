using UnityEngine;

namespace Framework.GPUNoise
{
    /// <summary>
    /// 噪声预设 - 保存和加载噪声生成器设置
    /// </summary>
    [CreateAssetMenu(fileName = "NoisePreset", menuName = "噪声图工具/Noise Preset")]
    public class NoisePreset : ScriptableObject
    {
        [Header("基础设置")]
        public NoiseType noiseType = NoiseType.Perlin;
        public int resolution = 512;
        public float scale = 10f;
        public Vector2 offset = Vector2.zero;
        public float seed = 0f;

        [Header("FBM/Ridged/Turbulence 设置")]
        public int octaves = 4;
        public float persistence = 0.5f;
        public float lacunarity = 2f;

        [Header("输出设置")]
        public bool invert = false;
        public Color tintColor = Color.white;
        public bool seamless = false;
        public float contrast = 1f;
        public float brightness = 0f;

        [Header("法线贴图")]
        public float normalStrength = 1f;

        [Header("域变形 (Domain Warping)")]
        public bool enableDomainWarp = false;
        public float warpStrength = 0.5f;
        public float warpScale = 5f;
        public int warpIterations = 2;

        [Header("渐变映射 (Gradient Mapping)")]
        public bool enableGradientMap = false;
        public Texture2D gradientTexture;

        [Header("噪声混合 (Noise Blending)")]
        public bool enableBlend = false;
        public Texture2D blendTexture;
        public NoiseBlendMode blendMode = NoiseBlendMode.Mix;
        public float blendFactor = 0.5f;

        [Header("预设信息")]
        [TextArea(2, 4)]
        public string description = "";
        public string author = "";

        /// <summary>
        /// 从生成器复制设置到预设
        /// </summary>
        public void CopyFromGenerator(GPUNoiseGenerator generator)
        {
            noiseType = generator.NoiseType;
            resolution = generator.Resolution;
            scale = generator.Scale;
            offset = generator.Offset;
            seed = generator.Seed;

            octaves = generator.Octaves;
            persistence = generator.Persistence;
            lacunarity = generator.Lacunarity;

            invert = generator.Invert;
            tintColor = generator.TintColor;
            seamless = generator.Seamless;
            contrast = generator.Contrast;
            brightness = generator.Brightness;

            normalStrength = generator.NormalStrength;

            enableDomainWarp = generator.EnableDomainWarp;
            warpStrength = generator.WarpStrength;
            warpScale = generator.WarpScale;
            warpIterations = generator.WarpIterations;

            enableGradientMap = generator.EnableGradientMap;
            gradientTexture = generator.GradientTexture;

            enableBlend = generator.EnableBlend;
            blendTexture = generator.BlendTexture;
            blendMode = generator.BlendMode;
            blendFactor = generator.BlendFactor;
        }

        /// <summary>
        /// 应用预设设置到生成器
        /// </summary>
        public void ApplyToGenerator(GPUNoiseGenerator generator)
        {
            generator.NoiseType = noiseType;
            generator.Resolution = resolution;
            generator.Scale = scale;
            generator.Offset = offset;
            generator.Seed = seed;

            generator.Octaves = octaves;
            generator.Persistence = persistence;
            generator.Lacunarity = lacunarity;

            generator.Invert = invert;
            generator.TintColor = tintColor;
            generator.Seamless = seamless;
            generator.Contrast = contrast;
            generator.Brightness = brightness;

            generator.NormalStrength = normalStrength;

            generator.EnableDomainWarp = enableDomainWarp;
            generator.WarpStrength = warpStrength;
            generator.WarpScale = warpScale;
            generator.WarpIterations = warpIterations;

            generator.EnableGradientMap = enableGradientMap;
            generator.GradientTexture = gradientTexture;

            generator.EnableBlend = enableBlend;
            generator.BlendTexture = blendTexture;
            generator.BlendMode = blendMode;
            generator.BlendFactor = blendFactor;
        }

        /// <summary>
        /// 创建默认的内置预设
        /// </summary>
        public static NoisePreset[] CreateBuiltInPresets()
        {
            // 云层预设
            NoisePreset clouds = CreateInstance<NoisePreset>();
            clouds.name = "Clouds (云层)";
            clouds.description = "适合生成云层纹理";
            clouds.noiseType = NoiseType.FBM;
            clouds.scale = 8f;
            clouds.octaves = 6;
            clouds.persistence = 0.5f;
            clouds.lacunarity = 2f;
            clouds.contrast = 1.2f;
            clouds.brightness = 0.1f;

            // 大理石预设
            NoisePreset marble = CreateInstance<NoisePreset>();
            marble.name = "Marble (大理石)";
            marble.description = "适合生成大理石纹理";
            marble.noiseType = NoiseType.Turbulence;
            marble.scale = 6f;
            marble.octaves = 5;
            marble.persistence = 0.6f;
            marble.enableDomainWarp = true;
            marble.warpStrength = 0.8f;
            marble.warpScale = 3f;

            // 地形预设
            NoisePreset terrain = CreateInstance<NoisePreset>();
            terrain.name = "Terrain (地形)";
            terrain.description = "适合生成地形高度图";
            terrain.noiseType = NoiseType.Ridged;
            terrain.scale = 5f;
            terrain.octaves = 6;
            terrain.persistence = 0.5f;
            terrain.lacunarity = 2.2f;
            terrain.contrast = 1.1f;

            // 细胞预设
            NoisePreset cells = CreateInstance<NoisePreset>();
            cells.name = "Cells (细胞)";
            cells.description = "适合生成有机细胞纹理";
            cells.noiseType = NoiseType.Worley;
            cells.scale = 12f;
            cells.invert = true;
            cells.contrast = 1.3f;

            // 木纹预设
            NoisePreset wood = CreateInstance<NoisePreset>();
            wood.name = "Wood (木纹)";
            wood.description = "适合生成木纹纹理";
            wood.noiseType = NoiseType.Perlin;
            wood.scale = 3f;
            wood.enableDomainWarp = true;
            wood.warpStrength = 1.2f;
            wood.warpScale = 8f;
            wood.warpIterations = 3;
            wood.contrast = 1.4f;

            // 火焰预设
            NoisePreset fire = CreateInstance<NoisePreset>();
            fire.name = "Fire (火焰)";
            fire.description = "适合生成火焰效果";
            fire.noiseType = NoiseType.Turbulence;
            fire.scale = 10f;
            fire.octaves = 4;
            fire.persistence = 0.7f;
            fire.enableDomainWarp = true;
            fire.warpStrength = 1.5f;
            fire.warpScale = 5f;
            fire.contrast = 1.5f;
            fire.brightness = -0.1f;

            return new NoisePreset[] { clouds, marble, terrain, cells, wood, fire };
        }
    }
}
