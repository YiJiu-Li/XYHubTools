// ShaderLibraryTool.cs — 将资源库面板接入 XY_EditorHub 的适配器（类名保留兼容旧代码）

using System;
using UnityEditor;
using UnityEngine;

namespace Framework.XYEditor.Tools
{
    /// <summary>
    /// 资源库工具 —— 对接远程 XY Asset Library API，支持任意文件类型的分组下载。
    /// </summary>
    [XYTool(order: 10, tags: new[] { "资源", "素材", "文件", "shader", "远程" })]
    public class ShaderLibraryTool : XYEditorToolBase<ShaderLibrary.ShaderLibraryPanel>
    {
        public override string ToolName => "资源库";
        public override string Category => "资源管理";
        public override string Description =>
            "浏览并下载远程资源库，支持任意文件类型、分组下载、目录结构与 Unity GUID 修正";
        public override Texture2D Icon => EditorGUIUtility.FindTexture("Folder Icon");

        protected override ShaderLibrary.ShaderLibraryPanel CreatePanel(Action repaint) =>
            new ShaderLibrary.ShaderLibraryPanel(repaint);
    }
}
