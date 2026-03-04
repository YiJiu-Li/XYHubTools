using System;
using UnityEditor;
using UnityEngine;
using YiJiu.UnityCopyApp;

namespace Framework.XYEditor.Tools
{
    /// <summary>
    /// 将 CopyAppPanel 接入 XY_EditorHub 的适配器
    /// </summary>
    public class CopyAppTool : XYEditorToolBase<CopyAppPanel>
    {
        public override string ToolName => "资源复制工具";
        public override string Category => "项目工具";
        public override string Description =>
            "复制 Unity 项目资源，支持目录 / 单文件模式及 GUID 重映射";
        public override string DocumentPath => "Assets/Editor/Apps/CopyApp/README.md";
        public override Texture2D Icon => EditorGUIUtility.FindTexture("Folder Icon");

        protected override CopyAppPanel CreatePanel(Action repaint) => new CopyAppPanel(repaint);
    }
}
