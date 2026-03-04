using System;
using UnityEditor;
using UnityEngine;

namespace Framework.XYEditor.Tools
{
    /// <summary>
    /// 将 AssetGUIDRegeneratorPanel 接入 XY_EditorHub 的适配器
    /// </summary>
    public class AssetGUIDRegeneratorTool : XYEditorToolBase<AssetGUIDRegeneratorPanel>
    {
        public override string ToolName => "GUID 重新生成";
        public override string Category => "项目工具";
        public override string Description => "为选中的资源重新生成 GUID，并自动更新所有引用";
        public override string DocumentPath => "Assets/Editor/Apps/AssetsGUID/README.md";
        public override Texture2D Icon => EditorGUIUtility.FindTexture("d_SceneAsset Icon");

        protected override AssetGUIDRegeneratorPanel CreatePanel(Action repaint) =>
            new AssetGUIDRegeneratorPanel(repaint);
    }
}
