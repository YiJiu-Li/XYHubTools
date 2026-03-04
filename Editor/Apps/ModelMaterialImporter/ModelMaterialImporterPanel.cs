using System;
using Framework.XYEditor;
using UnityEditor;
using UnityEngine;
using YZJ;

/// <summary>
/// 模型材质导入面板 —— 独立渲染逻辑，可嵌入任意 GUILayout.BeginArea 区域，
/// 也可由 ModelMaterialImporterWindow 作为独立窗口使用。
/// </summary>
public class ModelMaterialImporterPanel : IXYPanel
{
    // ── 状态 ──────────────────────────────────────────────────────────────
    private ShaderMaterialTemplate _currentTemplate;
    private Vector2 _scroll;

    // ── 重绘回调 ──────────────────────────────────────────────────────────
    private readonly Action _repaint;

    // ═══════════════════════════════════════════════════════════════════════
    //  公共 API
    // ═══════════════════════════════════════════════════════════════════════

    public ModelMaterialImporterPanel(Action repaint = null)
    {
        _repaint = repaint ?? (() => { });
    }

    /// <summary>激活时调用</summary>
    public void Init()
    {
        _currentTemplate = ModelMaterialImporterSettings.GetCurrentTemplate();
    }

    /// <summary>停用时调用</summary>
    public void Cleanup() { }

    /// <summary>
    /// 主绘制方法：在调用前需已进入 GUILayout/EditorGUILayout 上下文。
    /// </summary>
    /// <param name="containerWidth">当前容器可用宽度（px）</param>
    public void Draw(float containerWidth)
    {
        _scroll = EditorGUILayout.BeginScrollView(_scroll);

        EditorGUILayout.Space(10);
        EditorGUILayout.LabelField("模型材质处理设置", EditorStyles.boldLabel);
        EditorGUILayout.Space(10);

        DrawTemplateSettings();

        EditorGUILayout.Space(20);

        DrawUsageGuide();

        EditorGUILayout.EndScrollView();
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  绘制子区域
    // ═══════════════════════════════════════════════════════════════════════

    private void DrawTemplateSettings()
    {
        XYEditorGUI.DrawSection(
            "Shader模板",
            () =>
            {
                EditorGUI.BeginChangeCheck();
                _currentTemplate = (ShaderMaterialTemplate)
                    EditorGUILayout.ObjectField(
                        "当前模板",
                        _currentTemplate,
                        typeof(ShaderMaterialTemplate),
                        false
                    );
                if (EditorGUI.EndChangeCheck())
                {
                    ModelMaterialImporterSettings.SetCurrentTemplate(_currentTemplate);
                }

                if (_currentTemplate != null)
                {
                    EditorGUILayout.LabelField("模板名称:", _currentTemplate.templateName);
                    EditorGUILayout.LabelField(
                        "目标Shader:",
                        _currentTemplate.targetShader != null
                            ? _currentTemplate.targetShader.name
                            : "未设置"
                    );
                    EditorGUILayout.LabelField(
                        "贴图映射数:",
                        _currentTemplate.texturePropertyMappings.Count.ToString()
                    );
                    EditorGUILayout.LabelField(
                        "默认参数数:",
                        _currentTemplate.floatParameters.Count.ToString()
                    );

                    EditorGUILayout.Space(5);
                    if (GUILayout.Button("编辑模板", GUILayout.Height(25)))
                    {
                        Selection.activeObject = _currentTemplate;
                    }
                }
                else
                {
                    EditorGUILayout.HelpBox(
                        "请选择一个Shader模板，或创建新模板。",
                        MessageType.Warning
                    );
                }

                EditorGUILayout.Space(5);
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("创建URP/Lit模板", GUILayout.Height(25)))
                {
                    CreateAndSaveTemplate(
                        ShaderMaterialTemplate.CreateURPLitTemplate(),
                        "URPLit_Template"
                    );
                }
                if (GUILayout.Button("创建PBR_SG模板", GUILayout.Height(25)))
                {
                    CreateAndSaveTemplate(
                        ShaderMaterialTemplate.CreatePBRSGTemplate(),
                        "PBRSG_Template"
                    );
                }
                EditorGUILayout.EndHorizontal();
            }
        );
    }

    private void DrawUsageGuide()
    {
        XYEditorGUI.DrawSection(
            "使用说明",
            () =>
            {
                EditorGUILayout.HelpBox(
                    "目录结构：\n"
                        + "├── Model.fbx\n"
                        + "├── Materials/  (材质输出目录)\n"
                        + "└── Texture/    (贴图搜索目录)\n\n"
                        + "模板配置说明：\n"
                        + "1. 创建或选择一个Shader模板\n"
                        + "2. 在模板中配置贴图属性映射\n"
                        + "3. 配置默认Float/Color参数和Keyword\n"
                        + "4. 右键模型 → XZ工具 → 处理模型材质",
                    MessageType.None
                );
            }
        );
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  业务逻辑
    // ═══════════════════════════════════════════════════════════════════════

    private void CreateAndSaveTemplate(ShaderMaterialTemplate template, string defaultName)
    {
        string path = EditorUtility.SaveFilePanelInProject(
            "保存Shader模板",
            defaultName,
            "asset",
            "选择保存位置",
            "Assets/Framework/Editor/XZ"
        );

        if (!string.IsNullOrEmpty(path))
        {
            AssetDatabase.CreateAsset(template, path);
            AssetDatabase.SaveAssets();
            _currentTemplate = template;
            ModelMaterialImporterSettings.SetCurrentTemplate(template);
            Selection.activeObject = template;
            Debug.Log($"<color=green>[ModelMaterialImporter]</color> 已创建模板: {path}");
            _repaint();
        }
    }
}
