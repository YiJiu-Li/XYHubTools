// XY_EditorHub.DrawContent.cs  —— 右侧内容区绘制 & 错误面板

using System;
using UnityEditor;
using UnityEngine;

namespace Framework.XYEditor
{
    public partial class XY_EditorHub
    {
        // -- 右侧内容区主绘制 -------------------------------------------------
        private void DrawContent(Rect rect)
        {
            GUI.Box(rect, GUIContent.none, XY_HubStyles.ContentBg);

            if (_activeTool == null)
            {
                GUI.Label(rect, "← 请在左侧选择一个工具", XY_HubStyles.NoTool);
                return;
            }

            bool hasDesc = !string.IsNullOrEmpty(_activeTool.Description);
            float headerH = hasDesc ? 52f : 34f;
            bool hasDoc = !string.IsNullOrEmpty(_activeTool.DocumentPath);
            bool isPopped = IsPopped;

            EditorGUI.DrawRect(
                new Rect(rect.x, rect.y, rect.width, headerH),
                XY_HubStyles.ContentHeaderBg
            );

            // 右侧按钮区（从右向左排列）
            const float BTN_H = 22f;
            float btnRight = rect.xMax - 8f;
            float popW = 26f;
            var popRect = new Rect(btnRight - popW, rect.y + (headerH - BTN_H) * 0.5f, popW, BTN_H);

            if (
                GUI.Button(
                    popRect,
                    new GUIContent(
                        isPopped ? "↙" : "↗",
                        isPopped ? "收回到容器" : "脱离到独立窗口"
                    ),
                    EditorStyles.miniButton
                )
            )
            {
                if (isPopped)
                    DockBack();
                else
                    PopOut();
            }
            btnRight -= popW + 4f;

            if (hasDoc)
            {
                float docW = 60f;
                var docBtnR = new Rect(
                    btnRight - docW,
                    rect.y + (headerH - BTN_H) * 0.5f,
                    docW,
                    BTN_H
                );
                if (
                    GUI.Button(
                        docBtnR,
                        new GUIContent("📖 文档", "查看工具说明文档"),
                        EditorStyles.miniButton
                    )
                )
                    XY_MarkdownViewerWindow.Open(_activeTool.ToolName, _activeTool.DocumentPath);
                btnRight -= docW + 4f;
            }

            float headerTextW = rect.width - 24 - (rect.xMax - btnRight);
            GUI.Label(
                new Rect(rect.x + 16, rect.y + 8, headerTextW, 20),
                _activeTool.ToolName,
                XY_HubStyles.ToolTitle
            );
            if (hasDesc)
                GUI.Label(
                    new Rect(rect.x + 16, rect.y + 30, headerTextW, 16),
                    _activeTool.Description,
                    XY_HubStyles.ToolDesc
                );

            EditorGUI.DrawRect(
                new Rect(rect.x, rect.y + headerH, rect.width, 1),
                XY_HubStyles.SeparatorLine
            );

            var toolRect = new Rect(
                rect.x,
                rect.y + headerH + 1,
                rect.width,
                rect.height - headerH - 1
            );

            if (isPopped)
            {
                GUI.Label(
                    toolRect,
                    $"「{_activeTool.ToolName}」已在独立窗口打开\n点击 ↙ 可收回",
                    XY_HubStyles.NoTool
                );
                return;
            }

            if (_lastError != null)
            {
                DrawErrorPanel(toolRect);
                return;
            }

            try
            {
                _activeTool.OnGUI(toolRect);
            }
            catch (Exception e) when (e is not ExitGUIException)
            {
                if (Event.current.type == EventType.Repaint)
                {
                    _lastError = e;
                    _lastErrorStack = e.StackTrace;
                }
            }
        }

        // -- 错误面板（复制堆栈 / 重试）--------------------------------------
        private void DrawErrorPanel(Rect rect)
        {
            float pad = 16f;
            EditorGUI.HelpBox(
                new Rect(rect.x + pad, rect.y + pad, rect.width - pad * 2, 60),
                $"工具渲染异常：{_lastError?.Message}",
                MessageType.Error
            );
            if (
                GUI.Button(
                    new Rect(rect.x + pad, rect.y + pad + 68, 90, 22),
                    "复制堆栈",
                    EditorStyles.miniButton
                )
            )
                GUIUtility.systemCopyBuffer = _lastErrorStack;
            if (
                GUI.Button(
                    new Rect(rect.x + pad + 96, rect.y + pad + 68, 60, 22),
                    "重 试",
                    EditorStyles.miniButton
                )
            )
            {
                _lastError = null;
                _lastErrorStack = null;
            }
        }
    }
}
