using Framework.XYEditor;
using UnityEditor;
using UnityEngine;

namespace YiJiu.UnityCopyApp
{
    /// <summary>
    /// 资源复制工具窗口（壳子，渲染逻辑由 CopyAppPanel 提供）
    /// </summary>
    public class CopyAppWindow : XYEditorWindowBase<CopyAppPanel>
    {
        [MenuItem("依旧/Asset Copy Tool")]
        public static void ShowWindow()
        {
            var win = GetWindow<CopyAppWindow>("资源复制工具");
            win.minSize = new Vector2(750, 680);
            win.Show();
        }

        protected override CopyAppPanel CreatePanel() => new CopyAppPanel(Repaint);
    }
}
