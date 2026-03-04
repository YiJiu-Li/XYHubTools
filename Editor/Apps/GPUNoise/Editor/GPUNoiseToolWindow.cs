using Framework.GPUNoise.Editor;
using Framework.XYEditor;
using UnityEditor;
using UnityEngine;

namespace Framework.GPUNoise
{
    /// <summary>
    /// GPU 噪声生成工具窗口（壳子，渲染逻辑由 GPUNoisePanel 提供）
    /// </summary>
    public class GPUNoiseToolWindow : XYEditorWindowBase<GPUNoisePanel>
    {
        [MenuItem("依旧/特效图工具/GPU 噪声生成器 &N")]
        public static void ShowWindow()
        {
            var window = GetWindow<GPUNoiseToolWindow>();
            window.titleContent = new GUIContent(
                "GPU 噪声生成器",
                EditorGUIUtility.IconContent("d_PreTextureRGB").image
            );
            window.minSize = new Vector2(300, 450);
            window.Show();
        }

        protected override GPUNoisePanel CreatePanel() => new GPUNoisePanel(Repaint);
    }
}
