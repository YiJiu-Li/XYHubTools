using System;
using UnityEditor;
using UnityEngine;

namespace Framework.XYEditor.Tools
{
    /// <summary>
    /// 将 ModelMaterialImporterPanel 接入 XY_EditorHub 的适配器
    /// </summary>
    public class ModelMaterialImporterTool : XYEditorToolBase<ModelMaterialImporterPanel>
    {
        public override string ToolName => "模型材质导入";
        public override string Category => "模型工具";
        public override string Description => "提取模型材质并自动匹配贴图，支持 Shader 模板配置";
        public override string DocumentPath => "Assets/Editor/Apps/ModelMaterialImporter/README.md";
        public override Texture2D Icon => EditorGUIUtility.FindTexture("PrefabModel Icon");

        protected override ModelMaterialImporterPanel CreatePanel(Action repaint) =>
            new ModelMaterialImporterPanel(repaint);
    }
}
