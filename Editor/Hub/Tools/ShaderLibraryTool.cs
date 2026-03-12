// ShaderLibraryTool.cs — 将 ShaderLibraryPanel 接入 XY_EditorHub 的适配器

using System;
using UnityEditor;
using UnityEngine;

namespace Framework.XYEditor.Tools
{
    /// <summary>
    /// Shader 库工具 —— 对接远程 XY ShaderLibrary API，
    /// 支持文件树浏览、在线编辑、关键词搜索与 Git 版本管理。
    /// </summary>
    [XYTool(order: 10, tags: new[] { "shader", "glsl", "hlsl", "库", "远程" })]
    public class ShaderLibraryTool : XYEditorToolBase<ShaderLibrary.ShaderLibraryPanel>
    {
        public override string ToolName => "Shader 库";
        public override string Category => "资源管理";
        public override string Description =>
            "浏览并编辑远程 Shader 库，支持文件树、在线编辑、关键词搜索与 Git 版本管理";
        public override Texture2D Icon => EditorGUIUtility.FindTexture("Shader Icon");

        protected override ShaderLibrary.ShaderLibraryPanel CreatePanel(Action repaint) =>
            new ShaderLibrary.ShaderLibraryPanel(repaint);
    }
}
