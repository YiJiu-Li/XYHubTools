#if UNITY_EDITOR

using System;
using System.IO;
using D.Tools;
using Framework.XYEditor;
using UnityEditor;
using UnityEngine;

/// <summary>
/// GUID 重新生成面板 —— 独立渲染逻辑，可嵌入任意 GUILayout.BeginArea 区域，
/// 也可由独立 EditorWindow 使用。
/// </summary>
public class AssetGUIDRegeneratorPanel : IXYPanel
{
    // ── 状态 ──────────────────────────────────────────────────────────────
    private Vector2 _scroll;

    // ── 重绘回调 ──────────────────────────────────────────────────────────
    private readonly Action _repaint;

    // ═══════════════════════════════════════════════════════════════════════
    //  公共 API
    // ═══════════════════════════════════════════════════════════════════════

    public AssetGUIDRegeneratorPanel(Action repaint = null)
    {
        _repaint = repaint ?? (() => { });
    }

    /// <summary>激活时调用</summary>
    public void Init()
    {
        _scroll = Vector2.zero;
    }

    /// <summary>停用时调用</summary>
    public void Cleanup() { }

    /// <summary>
    /// 主绘制方法：在调用前需已进入 GUILayout/EditorGUILayout 上下文。
    /// </summary>
    /// <param name="containerWidth">当前容器可用宽度（px）</param>
    public void Draw(float containerWidth)
    {
        _scroll = EditorGUILayout.BeginScrollView(_scroll);

        EditorGUILayout.Space(12);

        DrawNotice();
        EditorGUILayout.Space(10);
        DrawSelectionInfo();
        EditorGUILayout.Space(10);
        DrawActions();
        EditorGUILayout.Space(10);
        DrawModeDesc();

        EditorGUILayout.EndScrollView();

        XYEditorGUI.DrawFooter(
            "作者: 依旧 | GitHub: https://github.com/YiJiu-Li",
            "https://github.com/YiJiu-Li"
        );
        EditorGUILayout.Space(10);
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  绘制子区域
    // ═══════════════════════════════════════════════════════════════════════

    private void DrawNotice()
    {
        XYEditorGUI.DrawSection(
            "使用说明",
            () =>
            {
                EditorGUILayout.HelpBox(
                    "1. 在 Project 窗口中选中需要重新生成 GUID 的资源（文件或文件夹）\n"
                        + "2. 选择下方操作模式并点击对应按钮\n"
                        + "3. 在弹出的确认对话框中点击「Yes, please」开始执行\n\n"
                        + "⚠ 操作前建议先提交版本控制或做好备份，GUID 变更不可自动撤销。",
                    MessageType.Warning
                );
            }
        );
    }

    private void DrawSelectionInfo()
    {
        XYEditorGUI.DrawSection(
            "当前选中资源",
            () =>
            {
                var selectedGUIDs = Selection.assetGUIDs;
                if (selectedGUIDs == null || selectedGUIDs.Length == 0)
                {
                    EditorGUILayout.HelpBox(
                        "尚未在 Project 窗口中选中任何资源。",
                        MessageType.Info
                    );
                }
                else
                {
                    EditorGUILayout.LabelField(
                        $"共选中 {selectedGUIDs.Length} 个资源：",
                        EditorStyles.miniLabel
                    );
                    EditorGUILayout.Space(2);
                    foreach (var guid in selectedGUIDs)
                    {
                        var path = AssetDatabase.GUIDToAssetPath(guid);
                        string icon = IsDirectory(path) ? "📁" : "📄";
                        EditorGUILayout.LabelField($"  {icon} {path}", EditorStyles.miniLabel);
                    }
                }
            }
        );
    }

    private void DrawActions()
    {
        XYEditorGUI.DrawSection(
            "操作",
            () =>
            {
                var selectedGUIDs = Selection.assetGUIDs;
                bool hasSelection = selectedGUIDs != null && selectedGUIDs.Length > 0;
                bool isValid = hasSelection && IsSelectionValid(selectedGUIDs);

                using (new EditorGUI.DisabledScope(!isValid))
                {
                    EditorGUILayout.BeginHorizontal();

                    GUI.backgroundColor = new Color(0.4f, 0.75f, 1f);
                    if (
                        GUILayout.Button(
                            new GUIContent(
                                "仅文件",
                                "只为选中的文件重新生成 GUID（跳过文件夹本身的 GUID）"
                            ),
                            GUILayout.Height(36)
                        )
                    )
                    {
                        AssetGUIDRegeneratorMenu.RegenerateGUID_Implementation();
                    }

                    GUI.backgroundColor = new Color(0.5f, 1f, 0.6f);
                    if (
                        GUILayout.Button(
                            new GUIContent(
                                "文件 + 文件夹",
                                "同时为文件夹本身及其内部文件重新生成 GUID"
                            ),
                            GUILayout.Height(36)
                        )
                    )
                    {
                        AssetGUIDRegeneratorMenu.RegenerateGUIDWithFolders_Implementation();
                    }

                    GUI.backgroundColor = Color.white;
                    EditorGUILayout.EndHorizontal();
                }

                if (!isValid && hasSelection)
                    EditorGUILayout.HelpBox(
                        "当前选择包含无效资源，请重新选择。",
                        MessageType.Error
                    );
            }
        );
    }

    private void DrawModeDesc()
    {
        XYEditorGUI.DrawSection(
            "模式说明",
            () =>
            {
                EditorGUILayout.LabelField("仅文件", EditorStyles.boldLabel);
                EditorGUILayout.LabelField(
                    "选中文件夹时，递归处理其内部所有文件，但不修改文件夹自身的 GUID。",
                    XYEditorStyles.WrapLabel
                );
                EditorGUILayout.Space(6);
                EditorGUILayout.LabelField("文件 + 文件夹", EditorStyles.boldLabel);
                EditorGUILayout.LabelField(
                    "在「仅文件」基础上，同时为文件夹自身也生成新的 GUID。",
                    XYEditorStyles.WrapLabel
                );
            }
        );
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  辅助方法
    // ═══════════════════════════════════════════════════════════════════════

    private static bool IsSelectionValid(string[] guids)
    {
        foreach (var guid in guids)
        {
            if (string.IsNullOrEmpty(guid) || guid == "0")
                return false;
        }
        return true;
    }

    private static bool IsDirectory(string path)
    {
        try
        {
            return File.GetAttributes(path).HasFlag(System.IO.FileAttributes.Directory);
        }
        catch
        {
            return false;
        }
    }
}

#endif
