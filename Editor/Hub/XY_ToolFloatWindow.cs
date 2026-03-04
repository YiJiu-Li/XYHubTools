using System;
using UnityEditor;
using UnityEngine;

namespace Framework.XYEditor
{
    // =======================================================================
    //  浮动工具窗口（从 XY_EditorHub 中拆分，职责单一）
    // =======================================================================
    internal class XY_ToolFloatWindow : EditorWindow
    {
        private IXYEditorTool _tool;
        private XY_EditorHub _hub;

        internal static XY_ToolFloatWindow Create(IXYEditorTool tool, XY_EditorHub hub)
        {
            var win = CreateInstance<XY_ToolFloatWindow>();
            win._tool = tool;
            win._hub = hub;
            win.titleContent = new GUIContent(
                tool.ToolName,
                tool.Icon ?? EditorGUIUtility.FindTexture("d_winbtn_win_restore")
            );
            win.minSize = new Vector2(400, 300);
            win.Show();
            return win;
        }

        private void OnGUI()
        {
            if (_tool == null)
            {
                Close();
                return;
            }

            XY_HubStyles.EnsureInited();
            try
            {
                _tool.OnGUI(new Rect(0, 0, position.width, position.height));
            }
            catch (Exception e) when (e is not ExitGUIException)
            {
                if (Event.current.type == EventType.Repaint)
                    EditorGUI.HelpBox(
                        new Rect(10, 10, position.width - 20, 80),
                        $"工具渲染异常：{e.Message}",
                        MessageType.Error
                    );
            }
        }

        private void OnDestroy()
        {
            _hub?.OnFloatWindowClosed(_tool);
        }
    }
}
