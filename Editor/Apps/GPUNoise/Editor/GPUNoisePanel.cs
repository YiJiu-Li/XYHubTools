using System;
using System.Collections.Generic;
using System.IO;
using Framework.XYEditor;
using UnityEditor;
using UnityEngine;

namespace Framework.GPUNoise.Editor
{
    /// <summary>
    /// GPU 噪声面板 —— 独立渲染逻辑，可嵌入任意 GUILayout.BeginArea 区域，
    /// 也可由 GPUNoiseToolWindow 作为独立窗口使用。
    /// </summary>
    public class GPUNoisePanel : IXYPanel
    {
        // ── 噪声设置 ──────────────────────────────────────────────────────
        private ComputeShader computeShader;
        private NoiseType noiseType = NoiseType.Perlin;
        private int resolution = 512;
        private float scale = 10f;
        private Vector2 offset = Vector2.zero;
        private float seed = 0f;

        // ── FBM / Ridged / Turbulence ────────────────────────────────────
        private int octaves = 4;
        private float persistence = 0.5f;
        private float lacunarity = 2f;

        // ── 输出设置 ─────────────────────────────────────────────────────
        private bool invert = false;
        private Color tintColor = Color.white;
        private bool seamless = false;
        private float contrast = 1f;
        private float brightness = 0f;

        // ── 法线贴图 ─────────────────────────────────────────────────────
        private float normalStrength = 1f;

        // ── 域变形 ───────────────────────────────────────────────────────
        private bool enableDomainWarp = false;
        private float warpStrength = 0.5f;
        private float warpScale = 5f;
        private int warpIterations = 2;

        // ── 渐变映射 ─────────────────────────────────────────────────────
        private bool enableGradientMap = false;
        private Texture2D gradientTexture;

        // ── 噪声混合 ─────────────────────────────────────────────────────
        private bool enableBlend = false;
        private Texture2D blendTexture;
        private NoiseBlendMode blendMode = NoiseBlendMode.Mix;
        private float blendFactor = 0.5f;

        // ── 预览 ─────────────────────────────────────────────────────────
        private Texture2D previewTexture;
        private bool autoPreview = true;
        private Vector2 scrollPosition;

        // ── 批量生成 ─────────────────────────────────────────────────────
        private bool batchMode = false;
        private int batchCount = 4;
        private string batchPrefix = "Noise";

        // ── 预设系统 ─────────────────────────────────────────────────────
        private NoisePreset currentPreset;
        private int selectedBuiltInPreset = -1;

        // ── 历史记录 ─────────────────────────────────────────────────────
        private List<NoiseHistoryEntry> history = new List<NoiseHistoryEntry>();
        private int historyIndex = -1;
        private const int MaxHistoryCount = 20;

        // ── 折叠状态 ─────────────────────────────────────────────────────
        private bool showDomainWarp = false;
        private bool showGradientMap = false;
        private bool showNoiseBlend = false;
        private bool showPresets = false;
        private bool showHistory = false;

        // ── 重绘回调（供主窗口/工具集调用 Repaint）─────────────────────
        private Action _repaintAction;

        // ── 静态数据 ─────────────────────────────────────────────────────
        private static readonly string[] noiseTypeNames =
        {
            "Perlin (柏林噪声)",
            "Simplex (单形噪声)",
            "Worley (细胞噪声)",
            "Value (值噪声)",
            "FBM (分形噪声)",
            "Ridged (山脊噪声)",
            "Turbulence (湍流噪声)",
            "VoronoiEdge (沃罗诺伊边缘)",
        };

        private static readonly string[] blendModeNames =
        {
            "Mix (混合)",
            "Add (叠加)",
            "Multiply (正片叠底)",
            "Screen (滤色)",
            "Overlay (叠加)",
            "Difference (差值)",
        };

        private static readonly string[] builtInPresetNames =
        {
            "无",
            "Clouds (云层)",
            "Marble (大理石)",
            "Terrain (地形)",
            "Cells (细胞)",
            "Wood (木纹)",
            "Fire (火焰)",
        };

        [Serializable]
        private class NoiseHistoryEntry
        {
            public NoiseType noiseType;
            public float scale;
            public float seed;
            public int octaves;
            public float persistence;
            public bool invert;
            public bool seamless;
            public float contrast;
            public float brightness;
            public bool enableDomainWarp;
            public float warpStrength;
            public string timestamp;
        }

        // ═══════════════════════════════════════════════════════════════════
        //  公共 API
        // ═══════════════════════════════════════════════════════════════════

        /// <param name="repaint">当需要刷新时调用的委托，通常传入 EditorWindow.Repaint</param>
        public GPUNoisePanel(Action repaint = null)
        {
            _repaintAction = repaint ?? (() => { });
        }

        /// <summary>激活时调用（自动查找 Compute Shader）</summary>
        public void Init()
        {
            if (computeShader != null)
                return;

            string[] guids = AssetDatabase.FindAssets("NoiseCompute t:ComputeShader");
            if (guids.Length > 0)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[0]);
                computeShader = AssetDatabase.LoadAssetAtPath<ComputeShader>(path);
            }
        }

        /// <summary>停用时调用（清理预览纹理）</summary>
        public void Cleanup()
        {
            CleanupPreview();
        }

        /// <summary>
        /// 主绘制方法：在调用前需已进入 GUILayout.BeginArea(rect)，
        /// 或传入 containerWidth 作为宽度参考（用于预览缩放）。
        /// </summary>
        /// <param name="containerWidth">当前容器可用宽度（px）</param>
        public void Draw(float containerWidth)
        {
            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);

            DrawHeader();
            EditorGUILayout.Space(10);

            EditorGUI.BeginChangeCheck();

            computeShader = (ComputeShader)
                EditorGUILayout.ObjectField(
                    "Compute Shader",
                    computeShader,
                    typeof(ComputeShader),
                    false
                );

            if (computeShader == null)
            {
                EditorGUILayout.HelpBox(
                    "请指定 Compute Shader (NoiseCompute.compute)",
                    MessageType.Warning
                );
                EditorGUILayout.EndScrollView();
                return;
            }

            EditorGUILayout.Space(5);
            DrawNoiseTypeSelection();
            EditorGUILayout.Space(3);
            DrawBasicSettings();
            EditorGUILayout.Space(3);
            DrawAdvancedSettings();
            EditorGUILayout.Space(3);
            DrawPresetSection();
            EditorGUILayout.Space(3);
            DrawHistorySection();
            EditorGUILayout.Space(3);
            DrawBatchSettings();

            bool changed = EditorGUI.EndChangeCheck();

            EditorGUILayout.Space(10);
            DrawActionButtons();
            EditorGUILayout.Space(10);
            DrawPreviewArea(containerWidth);

            EditorGUILayout.EndScrollView();

            if (autoPreview && changed)
                GeneratePreview();

            XYEditorGUI.DrawFooter(
                "作者: 依旧 | GitHub: https://github.com/YiJiu-Li",
                "https://github.com/YiJiu-Li"
            );
            EditorGUILayout.Space(10);
        }

        // ═══════════════════════════════════════════════════════════════════
        //  私有绘制
        // ═══════════════════════════════════════════════════════════════════

        private void DrawHeader()
        {
            XYEditorGUI.DrawHeader("🎨 GPU 噪声生成工具");
        }

        private void DrawNoiseTypeSelection()
        {
            int idx = (int)noiseType;
            idx = EditorGUILayout.Popup("噪声类型", idx, noiseTypeNames);
            noiseType = (NoiseType)idx;
        }

        private void DrawBasicSettings()
        {
            XYEditorGUI.DrawBox(() =>
            {
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.PrefixLabel("分辨率");
                int[] resOptions = { 128, 256, 512, 1024, 2048, 4096 };
                string[] resLabels = { "128", "256", "512", "1024", "2048", "4096" };
                int idx = System.Array.IndexOf(resOptions, resolution);
                if (idx < 0)
                    idx = 2;
                idx = EditorGUILayout.Popup(idx, resLabels);
                resolution = resOptions[idx];
                EditorGUILayout.EndHorizontal();

                scale = EditorGUILayout.Slider("缩放", scale, 0.1f, 100f);
                offset = EditorGUILayout.Vector2Field("偏移", offset);

                EditorGUILayout.BeginHorizontal();
                seed = EditorGUILayout.FloatField("种子", seed);
                if (GUILayout.Button("随机", GUILayout.Width(50)))
                    seed = UnityEngine.Random.Range(-10000f, 10000f);
                EditorGUILayout.EndHorizontal();

                if (
                    noiseType == NoiseType.FBM
                    || noiseType == NoiseType.Ridged
                    || noiseType == NoiseType.Turbulence
                )
                {
                    EditorGUILayout.Space(3);
                    octaves = EditorGUILayout.IntSlider("八度数", octaves, 1, 8);
                    persistence = EditorGUILayout.Slider("持续度", persistence, 0f, 1f);
                    lacunarity = EditorGUILayout.Slider("隙度", lacunarity, 1f, 4f);
                }

                EditorGUILayout.Space(3);
                EditorGUILayout.BeginHorizontal();
                invert = EditorGUILayout.Toggle("反转", invert);
                seamless = EditorGUILayout.Toggle("无缝", seamless);
                tintColor = EditorGUILayout.ColorField(tintColor, GUILayout.Width(50));
                EditorGUILayout.EndHorizontal();

                contrast = EditorGUILayout.Slider("对比度", contrast, 0.5f, 3f);
                brightness = EditorGUILayout.Slider("亮度", brightness, -0.5f, 0.5f);
            });
        }

        private void DrawAdvancedSettings()
        {
            XYEditorGUI.DrawFoldoutSection(
                "🌀 域变形 (Domain Warping)",
                ref showDomainWarp,
                () =>
                {
                    enableDomainWarp = EditorGUILayout.Toggle("启用域变形", enableDomainWarp);
                    if (enableDomainWarp)
                    {
                        EditorGUI.indentLevel++;
                        warpStrength = EditorGUILayout.Slider("变形强度", warpStrength, 0f, 2f);
                        warpScale = EditorGUILayout.Slider("变形缩放", warpScale, 0.1f, 50f);
                        warpIterations = EditorGUILayout.IntSlider(
                            "迭代次数",
                            warpIterations,
                            1,
                            4
                        );
                        EditorGUI.indentLevel--;
                    }
                }
            );

            XYEditorGUI.DrawFoldoutSection(
                "🎨 渐变映射 (Gradient Mapping)",
                ref showGradientMap,
                () =>
                {
                    enableGradientMap = EditorGUILayout.Toggle("启用渐变映射", enableGradientMap);
                    if (enableGradientMap)
                    {
                        EditorGUI.indentLevel++;
                        gradientTexture = (Texture2D)
                            EditorGUILayout.ObjectField(
                                "渐变纹理",
                                gradientTexture,
                                typeof(Texture2D),
                                false
                            );
                        EditorGUILayout.HelpBox(
                            "使用1xN或Nx1的渐变纹理进行颜色映射",
                            MessageType.Info
                        );
                        EditorGUI.indentLevel--;
                    }
                }
            );

            XYEditorGUI.DrawFoldoutSection(
                "🔀 噪声混合 (Noise Blending)",
                ref showNoiseBlend,
                () =>
                {
                    enableBlend = EditorGUILayout.Toggle("启用噪声混合", enableBlend);
                    if (enableBlend)
                    {
                        EditorGUI.indentLevel++;
                        blendTexture = (Texture2D)
                            EditorGUILayout.ObjectField(
                                "混合纹理",
                                blendTexture,
                                typeof(Texture2D),
                                false
                            );
                        blendMode = (NoiseBlendMode)
                            EditorGUILayout.Popup("混合模式", (int)blendMode, blendModeNames);
                        blendFactor = EditorGUILayout.Slider("混合因子", blendFactor, 0f, 1f);
                        EditorGUI.indentLevel--;
                    }
                }
            );
        }

        private void DrawPresetSection()
        {
            XYEditorGUI.DrawFoldoutSection(
                "💾 预设系统",
                ref showPresets,
                () =>
                {
                    EditorGUILayout.BeginHorizontal();
                    EditorGUILayout.PrefixLabel("内置预设");
                    int newPreset = EditorGUILayout.Popup(
                        selectedBuiltInPreset + 1,
                        builtInPresetNames
                    );
                    if (newPreset != selectedBuiltInPreset + 1)
                    {
                        selectedBuiltInPreset = newPreset - 1;
                        if (selectedBuiltInPreset >= 0)
                            ApplyBuiltInPreset(selectedBuiltInPreset);
                    }
                    EditorGUILayout.EndHorizontal();

                    currentPreset = (NoisePreset)
                        EditorGUILayout.ObjectField(
                            "自定义预设",
                            currentPreset,
                            typeof(NoisePreset),
                            false
                        );

                    EditorGUILayout.BeginHorizontal();
                    if (GUILayout.Button("加载预设", GUILayout.Height(22)))
                    {
                        if (currentPreset != null)
                            LoadFromPreset(currentPreset);
                        else
                            EditorUtility.DisplayDialog("提示", "请先选择一个预设", "确定");
                    }
                    if (GUILayout.Button("保存预设", GUILayout.Height(22)))
                        SaveToPreset();
                    EditorGUILayout.EndHorizontal();
                }
            );
        }

        private void DrawHistorySection()
        {
            string historyTitle = $"📜 历史记录 ({history.Count})";
            XYEditorGUI.DrawFoldoutSection(
                historyTitle,
                ref showHistory,
                () =>
                {
                    EditorGUILayout.BeginHorizontal();
                    GUI.enabled = historyIndex > 0;
                    if (GUILayout.Button("◀ 撤销", GUILayout.Height(22)))
                    {
                        historyIndex--;
                        RestoreFromHistory(historyIndex);
                    }
                    GUI.enabled = historyIndex < history.Count - 1;
                    if (GUILayout.Button("重做 ▶", GUILayout.Height(22)))
                    {
                        historyIndex++;
                        RestoreFromHistory(historyIndex);
                    }
                    GUI.enabled = true;
                    if (GUILayout.Button("清除", GUILayout.Width(50), GUILayout.Height(22)))
                    {
                        history.Clear();
                        historyIndex = -1;
                    }
                    EditorGUILayout.EndHorizontal();

                    int displayCount = Mathf.Min(history.Count, 5);
                    for (
                        int i = history.Count - 1;
                        i >= history.Count - displayCount && i >= 0;
                        i--
                    )
                    {
                        var entry = history[i];
                        string label =
                            $"{entry.timestamp} - {noiseTypeNames[(int)entry.noiseType]}";
                        if (i == historyIndex)
                            GUI.color = Color.cyan;
                        if (GUILayout.Button(label, EditorStyles.miniButton))
                        {
                            historyIndex = i;
                            RestoreFromHistory(i);
                        }
                        GUI.color = Color.white;
                    }
                }
            );
        }

        private void DrawBatchSettings()
        {
            batchMode = EditorGUILayout.Foldout(batchMode, "📦 批量生成", true);
            if (!batchMode)
                return;
            EditorGUI.indentLevel++;
            batchCount = EditorGUILayout.IntSlider("数量", batchCount, 2, 16);
            batchPrefix = EditorGUILayout.TextField("前缀", batchPrefix);
            normalStrength = EditorGUILayout.Slider("法线强度", normalStrength, 0.1f, 10f);
            EditorGUI.indentLevel--;
        }

        private void DrawActionButtons()
        {
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("生成预览", GUILayout.Height(28)))
                GeneratePreview();
            if (GUILayout.Button("保存PNG", GUILayout.Height(28)))
                SaveAsPNG();
            if (GUILayout.Button("保存法线", GUILayout.Height(28)))
                SaveNormalMap();
            EditorGUILayout.EndHorizontal();

            if (batchMode)
                if (GUILayout.Button($"批量生成 ({batchCount}张)", GUILayout.Height(24)))
                    BatchSave();
        }

        private void DrawPreviewArea(float containerWidth)
        {
            autoPreview = EditorGUILayout.ToggleLeft("自动预览", autoPreview);

            if (previewTexture == null)
                return;

            float previewSize = Mathf.Min(containerWidth - 20, 280);

            Rect rect = GUILayoutUtility.GetRect(
                previewSize,
                previewSize,
                GUILayout.ExpandWidth(false)
            );
            rect.width = previewSize;
            rect.height = previewSize;
            rect.x = (containerWidth - previewSize) / 2f;

            GUI.DrawTexture(rect, previewTexture, ScaleMode.ScaleToFit);
        }

        // ═══════════════════════════════════════════════════════════════════
        //  生成 / 保存
        // ═══════════════════════════════════════════════════════════════════

        private void GeneratePreview()
        {
            if (computeShader == null)
                return;
            CleanupPreview();

            var generator = ScriptableObject.CreateInstance<GPUNoiseGenerator>();
            ConfigureGenerator(generator);
            generator.Resolution = Mathf.Min(resolution, 256);

            previewTexture = generator.GenerateNoiseTexture2D();
            UnityEngine.Object.DestroyImmediate(generator);

            _repaintAction?.Invoke();
        }

        private void CleanupPreview()
        {
            if (previewTexture != null)
            {
                UnityEngine.Object.DestroyImmediate(previewTexture);
                previewTexture = null;
            }
        }

        private Texture2D GenerateFullTexture()
        {
            var generator = ScriptableObject.CreateInstance<GPUNoiseGenerator>();
            ConfigureGenerator(generator);
            var tex = generator.GenerateNoiseTexture2D();
            UnityEngine.Object.DestroyImmediate(generator);
            return tex;
        }

        private void ConfigureGenerator(GPUNoiseGenerator generator)
        {
            generator.NoiseComputeShader = computeShader;
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

        private void SaveAsPNG()
        {
            if (computeShader == null)
            {
                EditorUtility.DisplayDialog("错误", "请先设置 Compute Shader!", "确定");
                return;
            }
            string path = EditorUtility.SaveFilePanel(
                "保存噪声图",
                Application.dataPath,
                $"Noise_{noiseType}_{resolution}",
                "png"
            );
            if (string.IsNullOrEmpty(path))
                return;
            var tex = GenerateFullTexture();
            if (tex == null)
                return;
            File.WriteAllBytes(path, tex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(tex);
            if (path.StartsWith(Application.dataPath))
                AssetDatabase.Refresh();
            EditorUtility.DisplayDialog("成功", $"噪声图已保存到:\n{path}", "确定");
        }

        private void SaveNormalMap()
        {
            if (computeShader == null)
            {
                EditorUtility.DisplayDialog("错误", "请先设置 Compute Shader!", "确定");
                return;
            }
            string path = EditorUtility.SaveFilePanel(
                "保存法线贴图",
                Application.dataPath,
                $"Normal_{noiseType}_{resolution}",
                "png"
            );
            if (string.IsNullOrEmpty(path))
                return;
            var generator = ScriptableObject.CreateInstance<GPUNoiseGenerator>();
            ConfigureGenerator(generator);
            var normalTex = generator.GenerateNormalMap();
            UnityEngine.Object.DestroyImmediate(generator);
            if (normalTex == null)
                return;
            File.WriteAllBytes(path, normalTex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(normalTex);
            if (path.StartsWith(Application.dataPath))
                AssetDatabase.Refresh();
            EditorUtility.DisplayDialog("成功", $"法线贴图已保存到:\n{path}", "确定");
        }

        private void BatchSave()
        {
            string folder = EditorUtility.SaveFolderPanel(
                "选择保存文件夹",
                Application.dataPath,
                ""
            );
            if (string.IsNullOrEmpty(folder))
                return;
            float originalSeed = seed;
            for (int i = 0; i < batchCount; i++)
            {
                seed = originalSeed + i * 100f;
                var tex = GenerateFullTexture();
                if (tex == null)
                    continue;
                File.WriteAllBytes(
                    Path.Combine(folder, $"{batchPrefix}_{noiseType}_{i:D2}.png"),
                    tex.EncodeToPNG()
                );
                UnityEngine.Object.DestroyImmediate(tex);
                EditorUtility.DisplayProgressBar(
                    "批量生成",
                    $"正在生成 {i + 1}/{batchCount}",
                    (float)(i + 1) / batchCount
                );
            }
            seed = originalSeed;
            EditorUtility.ClearProgressBar();
            if (folder.StartsWith(Application.dataPath))
                AssetDatabase.Refresh();
            EditorUtility.DisplayDialog("完成", $"已生成 {batchCount} 张噪声图!", "确定");
        }

        // ═══════════════════════════════════════════════════════════════════
        //  预设 / 历史
        // ═══════════════════════════════════════════════════════════════════

        private void ApplyBuiltInPreset(int index)
        {
            var presets = NoisePreset.CreateBuiltInPresets();
            if (index >= 0 && index < presets.Length)
            {
                LoadFromPreset(presets[index]);
                foreach (var p in presets)
                    UnityEngine.Object.DestroyImmediate(p);
            }
        }

        private void LoadFromPreset(NoisePreset preset)
        {
            SaveToHistory();
            noiseType = preset.noiseType;
            resolution = preset.resolution;
            scale = preset.scale;
            offset = preset.offset;
            seed = preset.seed;
            octaves = preset.octaves;
            persistence = preset.persistence;
            lacunarity = preset.lacunarity;
            invert = preset.invert;
            tintColor = preset.tintColor;
            seamless = preset.seamless;
            contrast = preset.contrast;
            brightness = preset.brightness;
            normalStrength = preset.normalStrength;
            enableDomainWarp = preset.enableDomainWarp;
            warpStrength = preset.warpStrength;
            warpScale = preset.warpScale;
            warpIterations = preset.warpIterations;
            enableGradientMap = preset.enableGradientMap;
            gradientTexture = preset.gradientTexture;
            enableBlend = preset.enableBlend;
            blendTexture = preset.blendTexture;
            blendMode = preset.blendMode;
            blendFactor = preset.blendFactor;
            if (autoPreview)
                GeneratePreview();
        }

        private void SaveToPreset()
        {
            string path = EditorUtility.SaveFilePanelInProject(
                "保存预设",
                "NewNoisePreset",
                "asset",
                "选择保存位置"
            );
            if (string.IsNullOrEmpty(path))
                return;
            var preset = ScriptableObject.CreateInstance<NoisePreset>();
            preset.noiseType = noiseType;
            preset.resolution = resolution;
            preset.scale = scale;
            preset.offset = offset;
            preset.seed = seed;
            preset.octaves = octaves;
            preset.persistence = persistence;
            preset.lacunarity = lacunarity;
            preset.invert = invert;
            preset.tintColor = tintColor;
            preset.seamless = seamless;
            preset.contrast = contrast;
            preset.brightness = brightness;
            preset.normalStrength = normalStrength;
            preset.enableDomainWarp = enableDomainWarp;
            preset.warpStrength = warpStrength;
            preset.warpScale = warpScale;
            preset.warpIterations = warpIterations;
            preset.enableGradientMap = enableGradientMap;
            preset.gradientTexture = gradientTexture;
            preset.enableBlend = enableBlend;
            preset.blendTexture = blendTexture;
            preset.blendMode = blendMode;
            preset.blendFactor = blendFactor;
            AssetDatabase.CreateAsset(preset, path);
            AssetDatabase.SaveAssets();
            currentPreset = preset;
            EditorGUIUtility.PingObject(preset);
        }

        private void SaveToHistory()
        {
            if (historyIndex < history.Count - 1)
                history.RemoveRange(historyIndex + 1, history.Count - historyIndex - 1);
            history.Add(
                new NoiseHistoryEntry
                {
                    noiseType = noiseType,
                    scale = scale,
                    seed = seed,
                    octaves = octaves,
                    persistence = persistence,
                    invert = invert,
                    seamless = seamless,
                    contrast = contrast,
                    brightness = brightness,
                    enableDomainWarp = enableDomainWarp,
                    warpStrength = warpStrength,
                    timestamp = DateTime.Now.ToString("HH:mm:ss"),
                }
            );
            historyIndex = history.Count - 1;
            if (history.Count > MaxHistoryCount)
            {
                history.RemoveAt(0);
                historyIndex--;
            }
        }

        private void RestoreFromHistory(int index)
        {
            if (index < 0 || index >= history.Count)
                return;
            var e = history[index];
            noiseType = e.noiseType;
            scale = e.scale;
            seed = e.seed;
            octaves = e.octaves;
            persistence = e.persistence;
            invert = e.invert;
            seamless = e.seamless;
            contrast = e.contrast;
            brightness = e.brightness;
            enableDomainWarp = e.enableDomainWarp;
            warpStrength = e.warpStrength;
            if (autoPreview)
                GeneratePreview();
        }
    }
}
