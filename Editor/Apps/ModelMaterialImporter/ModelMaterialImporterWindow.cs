using Framework.XYEditor;
using UnityEditor;
using UnityEngine;

namespace YZJ
{
    /// <summary>
    /// 模型材质导入器窗口 —— 仅作为独立窗口容器，
    /// 渲染逻辑由 <see cref="ModelMaterialImporterPanel"/> 承载，
    /// 面板可嵌入任意 GUILayout.BeginArea 区域复用。
    /// </summary>
    public class ModelMaterialImporterWindow : XYEditorWindowBase<ModelMaterialImporterPanel>
    {
        [MenuItem("依旧/XZ工具/模型材质导入设置", false, 200)]
        public static void ShowWindow()
        {
            var window = GetWindow<ModelMaterialImporterWindow>("模型材质导入设置");
            window.minSize = new Vector2(450, 400);
        }

        protected override ModelMaterialImporterPanel CreatePanel() =>
            new ModelMaterialImporterPanel(Repaint);
    }
}
