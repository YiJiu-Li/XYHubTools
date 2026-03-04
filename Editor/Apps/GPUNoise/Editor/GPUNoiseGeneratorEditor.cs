using System.IO;
using Framework.XYEditor;
using UnityEditor;
using UnityEngine;

namespace Framework.GPUNoise.Editor
{
    /// <summary>
    /// GPU噪声生成器编辑器
    /// </summary>
    [CustomEditor(typeof(GPUNoiseGenerator))]
    public class GPUNoiseGeneratorEditor : UnityEditor.Editor
    {
        private GPUNoiseGenerator generator;
        private Texture2D previewTexture;
        private bool autoPreview = true;

        private SerializedProperty noiseComputeShader;
        private SerializedProperty noiseType;
        private SerializedProperty resolution;
        private SerializedProperty scale;
        private SerializedProperty offset;
        private SerializedProperty seed;
        private SerializedProperty octaves;
        private SerializedProperty persistence;
        private SerializedProperty lacunarity;
        private SerializedProperty invert;
        private SerializedProperty tintColor;

        private void OnEnable()
        {
            generator = (GPUNoiseGenerator)target;

            noiseComputeShader = serializedObject.FindProperty("noiseComputeShader");
            noiseType = serializedObject.FindProperty("noiseType");
            resolution = serializedObject.FindProperty("resolution");
            scale = serializedObject.FindProperty("scale");
            offset = serializedObject.FindProperty("offset");
            seed = serializedObject.FindProperty("seed");
            octaves = serializedObject.FindProperty("octaves");
            persistence = serializedObject.FindProperty("persistence");
            lacunarity = serializedObject.FindProperty("lacunarity");
            invert = serializedObject.FindProperty("invert");
            tintColor = serializedObject.FindProperty("tintColor");

            // 自动查找Compute Shader
            if (generator.NoiseComputeShader == null)
            {
                string[] guids = AssetDatabase.FindAssets("NoiseCompute t:ComputeShader");
                if (guids.Length > 0)
                {
                    string path = AssetDatabase.GUIDToAssetPath(guids[0]);
                    generator.NoiseComputeShader = AssetDatabase.LoadAssetAtPath<ComputeShader>(
                        path
                    );
                    EditorUtility.SetDirty(generator);
                }
            }
        }

        private void OnDisable()
        {
            if (previewTexture != null)
            {
                DestroyImmediate(previewTexture);
            }
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            EditorGUILayout.Space(5);
            XYEditorGUI.DrawHeader("GPU 器声生成器", "在GPU上高效生成各种类型的噪声纹理");
            EditorGUILayout.Space(10);

            // Compute Shader
            EditorGUILayout.PropertyField(noiseComputeShader, new GUIContent("Compute Shader"));

            if (generator.NoiseComputeShader == null)
            {
                EditorGUILayout.HelpBox("请设置 Compute Shader!", MessageType.Error);
            }

            EditorGUILayout.Space(10);

            // 噪声设置
            XYEditorGUI.DrawSection(
                "噪声设置",
                () =>
                {
                    EditorGUILayout.PropertyField(noiseType, new GUIContent("噪声类型"));
                    EditorGUILayout.PropertyField(resolution, new GUIContent("分辨率"));
                    EditorGUILayout.PropertyField(scale, new GUIContent("缩放"));
                    EditorGUILayout.PropertyField(offset, new GUIContent("偏移"));

                    EditorGUILayout.BeginHorizontal();
                    EditorGUILayout.PropertyField(seed, new GUIContent("种子"));
                    if (GUILayout.Button("随机", GUILayout.Width(50)))
                    {
                        generator.RandomizeSeed();
                        serializedObject.Update();
                        seed.floatValue = generator.Seed;
                    }
                    EditorGUILayout.EndHorizontal();
                },
                indent: true
            );

            // FBM 设置
            if (generator.NoiseType == NoiseType.FBM)
            {
                EditorGUILayout.Space(5);
                XYEditorGUI.DrawSection(
                    "FBM 设置",
                    () =>
                    {
                        EditorGUILayout.PropertyField(octaves, new GUIContent("八度数"));
                        EditorGUILayout.PropertyField(persistence, new GUIContent("持续度"));
                        EditorGUILayout.PropertyField(lacunarity, new GUIContent("雙度"));
                    },
                    indent: true
                );
            }

            // 输出设置
            EditorGUILayout.Space(5);
            XYEditorGUI.DrawSection(
                "输出设置",
                () =>
                {
                    EditorGUILayout.PropertyField(invert, new GUIContent("反转"));
                    EditorGUILayout.PropertyField(tintColor, new GUIContent("染色"));
                },
                indent: true
            );

            serializedObject.ApplyModifiedProperties();

            EditorGUILayout.Space(10);

            // 预览设置
            XYEditorGUI.DrawSection(
                "预览",
                () =>
                {
                    autoPreview = EditorGUILayout.Toggle("自动预览", autoPreview);

                    EditorGUILayout.BeginHorizontal();
                    if (GUILayout.Button("生成预览", GUILayout.Height(30)))
                    {
                        GeneratePreview();
                    }
                    if (GUILayout.Button("保存为PNG", GUILayout.Height(30)))
                    {
                        SaveAsPNG();
                    }
                    EditorGUILayout.EndHorizontal();
                },
                indent: true
            );

            // 显示预览
            if (previewTexture != null)
            {
                EditorGUILayout.Space(10);
                float previewSize = EditorGUIUtility.currentViewWidth - 40;
                previewSize = Mathf.Min(previewSize, 400);

                Rect rect = GUILayoutUtility.GetRect(previewSize, previewSize);
                rect.x = (EditorGUIUtility.currentViewWidth - previewSize) / 2;

                EditorGUI.DrawPreviewTexture(rect, previewTexture);
            }

            // 自动预览
            if (autoPreview && GUI.changed && generator.NoiseComputeShader != null)
            {
                GeneratePreview();
            }
        }

        private void GeneratePreview()
        {
            if (generator.NoiseComputeShader == null)
                return;

            if (previewTexture != null)
            {
                DestroyImmediate(previewTexture);
            }

            // 使用较小分辨率进行预览
            int originalRes = generator.Resolution;
            generator.Resolution = Mathf.Min(originalRes, 256);

            previewTexture = generator.GenerateNoiseTexture2D();

            generator.Resolution = originalRes;

            Repaint();
        }

        private void SaveAsPNG()
        {
            if (generator.NoiseComputeShader == null)
            {
                EditorUtility.DisplayDialog("错误", "请先设置 Compute Shader!", "确定");
                return;
            }

            string path = EditorUtility.SaveFilePanel(
                "保存噪声图",
                Application.dataPath,
                $"Noise_{generator.NoiseType}_{generator.Resolution}",
                "png"
            );

            if (string.IsNullOrEmpty(path))
                return;

            Texture2D texture = generator.GenerateNoiseTexture2D();
            if (texture == null)
                return;

            byte[] bytes = texture.EncodeToPNG();
            File.WriteAllBytes(path, bytes);

            DestroyImmediate(texture);

            // 如果保存在Assets目录下，刷新资源
            if (path.StartsWith(Application.dataPath))
            {
                AssetDatabase.Refresh();
            }

            EditorUtility.DisplayDialog("成功", $"噪声图已保存到:\n{path}", "确定");
        }
    }
}
