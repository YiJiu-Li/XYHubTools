// 放在 Editor 文件夹下
using Framework.XYEditor;
using UnityEditor;
using UnityEngine;
using YZJ;

/// <summary>
/// 动画模板工具窗口 —— 仅作为独立窗口容器，
/// 渲染逻辑由 <see cref="MaterialTransparencyAnimationPanel"/> 承载，
/// 面板可嵌入任意 GUILayout.BeginArea 区域复用。
/// </summary>
public class MaterialTransparencyAnimationWindow
    : XYEditorWindowBase<MaterialTransparencyAnimationPanel>
{
    [MenuItem("依旧/动画模板工具创建器")]
    public static void ShowWindow()
    {
        var window = GetWindow<MaterialTransparencyAnimationWindow>("动画模板工具");
        window.minSize = new Vector2(320, 480);
    }

    protected override MaterialTransparencyAnimationPanel CreatePanel() =>
        new MaterialTransparencyAnimationPanel(Repaint);
}
