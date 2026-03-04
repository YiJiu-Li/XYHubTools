using UnityEngine;

namespace Framework.GPUNoise
{
    /// <summary>
    /// GPU噪声渲染器 - 运行时实时生成和显示噪声
    /// </summary>
    [RequireComponent(typeof(Renderer))]
    public class GPUNoiseRenderer : MonoBehaviour
    {
        [Header("噪声生成器")]
        [SerializeField]
        private GPUNoiseGenerator noiseGenerator;

        [SerializeField]
        private ComputeShader noiseComputeShader;

        [Header("噪声设置")]
        [SerializeField]
        private NoiseType noiseType = NoiseType.Perlin;

        [SerializeField, Range(64, 2048)]
        private int resolution = 512;

        [SerializeField, Range(0.1f, 100f)]
        private float scale = 10f;

        [SerializeField]
        private Vector2 offset = Vector2.zero;

        [SerializeField]
        private float seed = 0f;

        [Header("FBM 设置")]
        [SerializeField, Range(1, 8)]
        private int octaves = 4;

        [SerializeField, Range(0f, 1f)]
        private float persistence = 0.5f;

        [SerializeField, Range(1f, 4f)]
        private float lacunarity = 2f;

        [Header("动画设置")]
        [SerializeField]
        private bool animate = false;

        [SerializeField]
        private Vector2 animationSpeed = new Vector2(0.1f, 0.1f);

        [Header("输出设置")]
        [SerializeField]
        private bool invert = false;

        [SerializeField]
        private Color tintColor = Color.white;

        [SerializeField]
        private string texturePropertyName = "_MainTex";

        private Renderer targetRenderer;
        private MaterialPropertyBlock propertyBlock;
        private RenderTexture currentTexture;
        private GPUNoiseGenerator runtimeGenerator;

        private void Awake()
        {
            targetRenderer = GetComponent<Renderer>();
            propertyBlock = new MaterialPropertyBlock();

            CreateRuntimeGenerator();
        }

        private void Start()
        {
            GenerateAndApply();
        }

        private void Update()
        {
            if (animate)
            {
                offset += animationSpeed * Time.deltaTime;
                GenerateAndApply();
            }
        }

        private void OnDestroy()
        {
            if (currentTexture != null)
            {
                currentTexture.Release();
                DestroyImmediate(currentTexture);
            }

            if (runtimeGenerator != null)
            {
                DestroyImmediate(runtimeGenerator);
            }
        }

        private void CreateRuntimeGenerator()
        {
            if (noiseGenerator != null)
            {
                runtimeGenerator = Instantiate(noiseGenerator);
            }
            else if (noiseComputeShader != null)
            {
                runtimeGenerator = ScriptableObject.CreateInstance<GPUNoiseGenerator>();
                runtimeGenerator.NoiseComputeShader = noiseComputeShader;
            }
        }

        /// <summary>
        /// 生成噪声并应用到材质
        /// </summary>
        public void GenerateAndApply()
        {
            if (runtimeGenerator == null)
            {
                CreateRuntimeGenerator();
                if (runtimeGenerator == null)
                {
                    Debug.LogError("GPUNoiseRenderer: 没有可用的噪声生成器!");
                    return;
                }
            }

            // 更新生成器参数
            UpdateGeneratorSettings();

            // 释放旧纹理
            if (currentTexture != null)
            {
                currentTexture.Release();
            }

            // 生成新纹理
            currentTexture = runtimeGenerator.GenerateNoise();

            if (currentTexture != null)
            {
                // 应用到材质
                targetRenderer.GetPropertyBlock(propertyBlock);
                propertyBlock.SetTexture(texturePropertyName, currentTexture);
                targetRenderer.SetPropertyBlock(propertyBlock);
            }
        }

        private void UpdateGeneratorSettings()
        {
            runtimeGenerator.NoiseType = noiseType;
            runtimeGenerator.Resolution = resolution;
            runtimeGenerator.Scale = scale;
            runtimeGenerator.Offset = offset;
            runtimeGenerator.Seed = seed;
            runtimeGenerator.Octaves = octaves;
            runtimeGenerator.Persistence = persistence;
            runtimeGenerator.Lacunarity = lacunarity;
            runtimeGenerator.Invert = invert;
            runtimeGenerator.TintColor = tintColor;
        }

        /// <summary>
        /// 随机化种子
        /// </summary>
        public void RandomizeSeed()
        {
            seed = Random.Range(-10000f, 10000f);
            GenerateAndApply();
        }

        /// <summary>
        /// 设置动画开关
        /// </summary>
        public void SetAnimate(bool value)
        {
            animate = value;
        }

        /// <summary>
        /// 获取当前生成的纹理
        /// </summary>
        public RenderTexture GetCurrentTexture()
        {
            return currentTexture;
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (Application.isPlaying && runtimeGenerator != null)
            {
                GenerateAndApply();
            }
        }
#endif
    }
}
