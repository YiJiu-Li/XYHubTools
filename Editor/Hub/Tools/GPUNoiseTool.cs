using System;
using Framework.GPUNoise.Editor;
using UnityEditor;
using UnityEngine;

namespace Framework.XYEditor.Tools
{
    /// <summary>
    /// GPU 噪声生成器 —— 工具集适配器（直接嵌入右侧容器）
    /// </summary>
    public class GPUNoiseTool : XYEditorToolBase<GPUNoisePanel>
    {
        public override string ToolName => "GPU 噪声生成器";
        public override string Category => "贴图工具";
        public override string Description => "使用 GPU 计算着色器生成各类噪声贴图";
        public override string DocumentPath => "Assets/Editor/Apps/GPUNoise/README.md";
        public override Texture2D Icon => EditorGUIUtility.FindTexture("Texture Icon");

        protected override GPUNoisePanel CreatePanel(Action repaint) => new GPUNoisePanel(repaint);
    }
}
