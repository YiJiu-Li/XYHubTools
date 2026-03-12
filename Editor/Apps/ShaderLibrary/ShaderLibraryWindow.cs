using Framework.XYEditor;
using Framework.XYEditor.ShaderLibrary;
using UnityEditor;
using UnityEngine;

namespace Framework.XYEditor.ShaderLibrary
{
    public class ShaderLibraryWindow : XYEditorWindowBase<ShaderLibraryPanel>
    {
        [MenuItem("依旧/Shader 库")]
        public static void ShowWindow()
        {
            var win = GetWindow<ShaderLibraryWindow>("Shader 库");
            win.minSize = new Vector2(480, 600);
            win.Show();
        }

        protected override ShaderLibraryPanel CreatePanel() => new ShaderLibraryPanel(Repaint);
    }
}
