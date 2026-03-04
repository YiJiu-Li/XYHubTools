using System;
using UnityEditor;
using UnityEngine;
using YZJ;

namespace Framework.XYEditor.Tools
{
    /// <summary>
    /// 将 MaterialTransparencyAnimationPanel 接入 XY_EditorHub 的适配器
    /// </summary>
    public class MaterialTransparencyAnimationTool
        : XYEditorToolBase<MaterialTransparencyAnimationPanel>
    {
        public override string ToolName => "动画模板工具";
        public override string Category => "动画工具";
        public override string Description =>
            "为选中对象的 Renderer 材质属性创建淡入淡出动画 Clip 及 Animator";
        public override string DocumentPath => "Assets/Editor/Apps/AnimationCreator/README.md";
        public override Texture2D Icon => EditorGUIUtility.FindTexture("Animation Icon");

        protected override MaterialTransparencyAnimationPanel CreatePanel(Action repaint) =>
            new MaterialTransparencyAnimationPanel(repaint);
    }
}
