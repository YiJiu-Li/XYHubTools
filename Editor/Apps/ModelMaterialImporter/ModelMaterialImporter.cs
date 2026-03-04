using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace YZJ
{
    /// <summary>
    /// 模型材质处理工具
    /// 使用ShaderMaterialTemplate模板系统来配置不同Shader的属性映射
    /// </summary>
    public class ModelMaterialImporter
    {
        // 获取当前选中的模板
        private static ShaderMaterialTemplate CurrentTemplate =>
            ModelMaterialImporterSettings.GetCurrentTemplate();

        /// <summary>
        /// 处理模型材质
        /// </summary>
        private static void ProcessModelMaterials(string modelPath, string modelName)
        {
            try
            {
                string directory = Path.GetDirectoryName(modelPath).Replace("\\", "/");

                // 使用模型同级的Materials文件夹
                string materialsFolder = Path.Combine(directory, "Materials").Replace("\\", "/");

                // 确保Materials文件夹存在
                if (!AssetDatabase.IsValidFolder(materialsFolder))
                {
                    string parentFolder = Path.GetDirectoryName(materialsFolder).Replace("\\", "/");
                    string folderName = Path.GetFileName(materialsFolder);
                    AssetDatabase.CreateFolder(parentFolder, folderName);
                }

                // 获取模型资源
                ModelImporter modelImporter = AssetImporter.GetAtPath(modelPath) as ModelImporter;
                if (modelImporter == null)
                    return;

                // 提取材质
                ExtractMaterials(modelPath, materialsFolder);

                // 刷新资源
                AssetDatabase.Refresh();

                // 查找并处理所有提取的材质
                string[] materialGuids = AssetDatabase.FindAssets(
                    "t:Material",
                    new[] { materialsFolder }
                );
                foreach (string guid in materialGuids)
                {
                    string materialPath = AssetDatabase.GUIDToAssetPath(guid);
                    ProcessMaterial(materialPath, directory);
                }

                Debug.Log(
                    $"<color=green>[ModelMaterialImporter]</color> 成功处理模型材质: {modelName}"
                );
            }
            catch (Exception e)
            {
                Debug.LogError(
                    $"[ModelMaterialImporter] 处理模型材质失败: {e.Message}\n{e.StackTrace}"
                );
            }
        }

        /// <summary>
        /// 提取材质到指定文件夹
        /// </summary>
        private static void ExtractMaterials(string modelPath, string destinationPath)
        {
            var assetObj = AssetDatabase.LoadMainAssetAtPath(modelPath);
            if (assetObj == null)
                return;

            var objects = AssetDatabase.LoadAllAssetsAtPath(modelPath);
            foreach (var obj in objects)
            {
                if (obj is Material material)
                {
                    string materialPath = Path.Combine(destinationPath, material.name + ".mat")
                        .Replace("\\", "/");

                    // 如果材质已存在，跳过
                    if (AssetDatabase.LoadAssetAtPath<Material>(materialPath) != null)
                        continue;

                    // 创建材质副本
                    Material newMaterial = new Material(material);
                    AssetDatabase.CreateAsset(newMaterial, materialPath);
                }
            }
        }

        /// <summary>
        /// 处理单个材质 - 替换Shader并匹配贴图
        /// </summary>
        private static void ProcessMaterial(string materialPath, string modelDirectory)
        {
            Material material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            if (material == null)
                return;

            var template = CurrentTemplate;
            if (template == null)
            {
                Debug.LogError(
                    "[ModelMaterialImporter] 未配置Shader模板，请在设置中选择或创建模板"
                );
                return;
            }

            // 获取目标Shader
            Shader targetShader = template.targetShader;
            if (targetShader == null)
            {
                Debug.LogError(
                    $"[ModelMaterialImporter] 模板 {template.templateName} 的Shader未设置"
                );
                return;
            }

            // 替换Shader
            material.shader = targetShader;

            // 设置默认材质参数（从模板读取）
            ApplyTemplateProperties(material, template);

            // 查找并匹配贴图
            string materialName = Path.GetFileNameWithoutExtension(materialPath);
            AssignTextures(material, materialName, modelDirectory, template);

            // 保存更改
            EditorUtility.SetDirty(material);
            AssetDatabase.SaveAssets();
        }

        /// <summary>
        /// 应用模板中的默认参数
        /// </summary>
        private static void ApplyTemplateProperties(
            Material material,
            ShaderMaterialTemplate template
        )
        {
            // 设置Float参数
            foreach (var param in template.floatParameters)
            {
                if (material.HasProperty(param.propertyName))
                {
                    material.SetFloat(param.propertyName, param.value);
                }
            }

            // 设置Int参数（枚举类型，如 Surface_Type, Blend_Mode 等）
            foreach (var param in template.intParameters)
            {
                if (material.HasProperty(param.propertyName))
                {
                    material.SetInt(param.propertyName, param.value);
                }
            }

            // 设置颜色参数
            foreach (var param in template.colorParameters)
            {
                if (material.HasProperty(param.propertyName))
                {
                    material.SetColor(param.propertyName, param.value);
                }
            }

            // 启用Keyword
            foreach (var keyword in template.enableKeywords)
            {
                if (!string.IsNullOrEmpty(keyword))
                {
                    material.EnableKeyword(keyword);
                }
            }

            // 禁用Keyword
            foreach (var keyword in template.disableKeywords)
            {
                if (!string.IsNullOrEmpty(keyword))
                {
                    material.DisableKeyword(keyword);
                }
            }
        }

        /// <summary>
        /// 自动匹配并分配贴图
        /// </summary>
        private static void AssignTextures(
            Material material,
            string materialName,
            string searchDirectory,
            ShaderMaterialTemplate template
        )
        {
            // 使用模型同级的Texture文件夹
            string textureFolder = Path.Combine(searchDirectory, "Texture").Replace("\\", "/");

            List<string> allTexturePaths = new List<string>();

            if (AssetDatabase.IsValidFolder(textureFolder))
            {
                // 只搜索Texture目录下的贴图，不递归子文件夹
                string[] allFiles = AssetDatabase.FindAssets(
                    "t:Texture2D",
                    new[] { textureFolder }
                );
                foreach (string guid in allFiles)
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    string fileDir = Path.GetDirectoryName(path).Replace("\\", "/");
                    if (fileDir.Equals(textureFolder, StringComparison.OrdinalIgnoreCase))
                    {
                        allTexturePaths.Add(path);
                    }
                }
            }
            else
            {
                Debug.LogWarning($"[ModelMaterialImporter] 未找到贴图目录: {textureFolder}");
            }

            // 备选：也搜索Textures目录
            string texturesFolder = Path.Combine(searchDirectory, "Textures").Replace("\\", "/");
            if (AssetDatabase.IsValidFolder(texturesFolder))
            {
                string[] guids = AssetDatabase.FindAssets("t:Texture2D", new[] { texturesFolder });
                foreach (string guid in guids)
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    string fileDir = Path.GetDirectoryName(path).Replace("\\", "/");
                    if (fileDir.Equals(texturesFolder, StringComparison.OrdinalIgnoreCase))
                    {
                        allTexturePaths.Add(path);
                    }
                }
            }

            allTexturePaths = allTexturePaths.Distinct().ToList();

            Debug.Log(
                $"<color=yellow>[ModelMaterialImporter]</color> 材质 {materialName} 搜索到 {allTexturePaths.Count} 张贴图"
            );

            // 从模板获取贴图映射，为每个贴图属性查找匹配的贴图
            foreach (var mapping in template.texturePropertyMappings)
            {
                string propertyName = mapping.shaderPropertyName;
                string[] suffixes = mapping.textureSuffixes.ToArray();

                if (!material.HasProperty(propertyName))
                    continue;

                Texture2D texture = FindMatchingTexture(allTexturePaths, materialName, suffixes);
                if (texture != null)
                {
                    material.SetTexture(propertyName, texture);

                    // 如果是法线贴图，确保设置正确的贴图类型
                    if (mapping.isNormalMap)
                    {
                        SetTextureAsNormalMap(texture);
                    }

                    Debug.Log(
                        $"<color=cyan>[ModelMaterialImporter]</color> 材质 {material.name} 分配贴图 {propertyName}: {texture.name}"
                    );
                }
            }
        }

        /// <summary>
        /// 查找匹配的贴图
        /// 材质名如：MoNiDian_DiZuo_m_BaseColor
        /// 贴图名如：MoNiDian_DiZuo_m_BaseColor, MoNiDian_DiZuo_m_Normal, MoNiDian_DiZuo_m_ARM
        /// 提取基础名 MoNiDian_DiZuo，然后匹配不同后缀
        /// </summary>
        private static Texture2D FindMatchingTexture(
            List<string> texturePaths,
            string materialName,
            string[] suffixes
        )
        {
            // 从材质名中提取基础名（去掉 _m_BaseColor, _m_Normal, _m_ARM 等后缀）
            string baseName = materialName;
            string[] knownSuffixes =
            {
                "_m_BaseColor",
                "_m_Normal",
                "_m_ARM",
                "_m_AO",
                "_BaseColor",
                "_Normal",
                "_ARM",
            };
            foreach (string knownSuffix in knownSuffixes)
            {
                if (materialName.EndsWith(knownSuffix, StringComparison.OrdinalIgnoreCase))
                {
                    baseName = materialName.Substring(0, materialName.Length - knownSuffix.Length);
                    break;
                }
            }

            Debug.Log(
                $"<color=orange>[ModelMaterialImporter]</color> 材质: {materialName}, 基础名: {baseName}, 后缀: {string.Join(", ", suffixes)}"
            );

            foreach (string texturePath in texturePaths)
            {
                string textureName = Path.GetFileNameWithoutExtension(texturePath);

                foreach (string suffix in suffixes)
                {
                    string cleanSuffix = suffix.TrimStart('_'); // m_ARM, m_Normal, m_BaseColor

                    // 匹配模式: 基础名_后缀 (如: MoNiDian_DiZuo_m_ARM)
                    string pattern = $"{baseName}_{cleanSuffix}";

                    if (textureName.Equals(pattern, StringComparison.OrdinalIgnoreCase))
                    {
                        Debug.Log(
                            $"<color=green>[ModelMaterialImporter]</color> 匹配成功: {textureName}"
                        );
                        Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
                        if (texture != null)
                            return texture;
                    }
                }
            }

            // 备用匹配：包含基础名且以后缀结尾
            foreach (string texturePath in texturePaths)
            {
                string textureName = Path.GetFileNameWithoutExtension(texturePath);

                foreach (string suffix in suffixes)
                {
                    string cleanSuffix = suffix.TrimStart('_');

                    if (
                        textureName.Contains(baseName, StringComparison.OrdinalIgnoreCase)
                        && textureName.EndsWith(cleanSuffix, StringComparison.OrdinalIgnoreCase)
                    )
                    {
                        Debug.Log(
                            $"<color=green>[ModelMaterialImporter]</color> 备用匹配成功: {textureName}"
                        );
                        Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
                        if (texture != null)
                            return texture;
                    }
                }
            }

            Debug.LogWarning(
                $"<color=red>[ModelMaterialImporter]</color> 材质 {materialName} (基础名: {baseName}) 未找到匹配的贴图"
            );
            return null;
        }

        /// <summary>
        /// 设置贴图为法线贴图类型
        /// </summary>
        private static void SetTextureAsNormalMap(Texture2D texture)
        {
            string texturePath = AssetDatabase.GetAssetPath(texture);
            TextureImporter importer = AssetImporter.GetAtPath(texturePath) as TextureImporter;
            if (importer != null && importer.textureType != TextureImporterType.NormalMap)
            {
                importer.textureType = TextureImporterType.NormalMap;
                importer.SaveAndReimport();
            }
        }

        #region 手动处理菜单

        [MenuItem("Assets/XZ工具/处理模型材质", false, 100)]
        private static void ProcessSelectedModels()
        {
            foreach (var obj in Selection.objects)
            {
                string path = AssetDatabase.GetAssetPath(obj);
                if (IsModelFile(path))
                {
                    ProcessModelMaterials(path, obj.name);
                }
            }
        }

        [MenuItem("Assets/XZ工具/处理模型材质", true)]
        private static bool ProcessSelectedModelsValidate()
        {
            foreach (var obj in Selection.objects)
            {
                string path = AssetDatabase.GetAssetPath(obj);
                if (IsModelFile(path))
                    return true;
            }
            return false;
        }

        private static bool IsModelFile(string path)
        {
            string ext = Path.GetExtension(path).ToLower();
            return ext == ".fbx"
                || ext == ".obj"
                || ext == ".blend"
                || ext == ".dae"
                || ext == ".gltf"
                || ext == ".glb";
        }

        #endregion
    }

    /// <summary>
    /// 模型材质导入器设置
    /// </summary>
    public static class ModelMaterialImporterSettings
    {
        private const string PREFS_TEMPLATE_GUID = "ModelMaterialImporter_TemplateGuid";

        /// <summary>
        /// 当前选中的模板GUID
        /// </summary>
        public static string TemplateGuid
        {
            get => EditorPrefs.GetString(PREFS_TEMPLATE_GUID, "");
            set => EditorPrefs.SetString(PREFS_TEMPLATE_GUID, value);
        }

        /// <summary>
        /// 获取当前选中的模板
        /// </summary>
        public static ShaderMaterialTemplate GetCurrentTemplate()
        {
            string guid = TemplateGuid;
            if (string.IsNullOrEmpty(guid))
                return null;

            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (string.IsNullOrEmpty(path))
                return null;

            return AssetDatabase.LoadAssetAtPath<ShaderMaterialTemplate>(path);
        }

        /// <summary>
        /// 设置当前模板
        /// </summary>
        public static void SetCurrentTemplate(ShaderMaterialTemplate template)
        {
            if (template == null)
            {
                TemplateGuid = "";
                return;
            }

            string path = AssetDatabase.GetAssetPath(template);
            TemplateGuid = AssetDatabase.AssetPathToGUID(path);
        }
    }
}
