using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Framework.XYEditor.Agent
{
    [XYTool(order: 0, tags: new[] { "智能体", "Agent", "Codex", "AI", "点位", "Rokid", "诊断", "批量验证" })]
    public sealed class XYUnityAgentTool : IXYEditorTool
    {
        public string ToolName => "XY Unity 智能体";
        public string Category => "AI";
        public string Description => "查看智能体安装状态；日常直接在 Codex 描述 Unity 需求即可。";
        public string DocumentPath => Path.Combine(XYUnityAgentService.SourceRoot, "README.md");
        public Texture2D Icon => EditorGUIUtility.FindTexture("d_UnityEditor.ConsoleWindow");

        private AgentManifest _manifest;
        private string[] _titles;
        private int _workflow;
        private bool _analysisOnly = true;
        private string _task = "";
        private string _module = "";
        private string _prompt = "";
        private string _status = "";
        private string _notice = "";
        private bool _noticeIsError;
        private Vector2 _scroll;
        private bool _showPrompt;
        private bool _showAdvanced;
        private string StateKey => "XYUnityAgent:" + XYUnityAgentService.ProjectRoot + ":";

        public void OnEnable()
        {
            _task = SessionState.GetString(StateKey + "task", "");
            _module = SessionState.GetString(StateKey + "module", "");
            _analysisOnly = SessionState.GetBool(StateKey + "analysisOnly", true);
            _workflow = SessionState.GetInt(StateKey + "workflow", 0);
            RunAction(() => {
                _manifest = XYUnityAgentService.LoadManifest();
                _titles = _manifest.workflows.Select(w => w.title).ToArray();
                _workflow = Mathf.Clamp(_workflow, 0, _titles.Length - 1);
                _status = XYUnityAgentService.InstallationStatus();
                return "";
            });
        }

        public void OnDisable() => SaveTask();

        private void SaveTask()
        {
            SessionState.SetString(StateKey + "task", _task);
            SessionState.SetString(StateKey + "module", _module);
            SessionState.SetBool(StateKey + "analysisOnly", _analysisOnly);
            SessionState.SetInt(StateKey + "workflow", _workflow);
        }

        private void RunAction(Func<string> action)
        {
            try { _notice = action(); _noticeIsError = false; }
            catch (Exception ex) { _notice = ex.Message; _noticeIsError = true; }
        }

        public void OnGUI(Rect rect)
        {
            GUILayout.BeginArea(rect);
            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            GUILayout.Space(10);
            EditorGUILayout.LabelField("直接在 Codex 里说你要做什么", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Skill 安装好、XY MCP 连通后，直接在当前工程的 Codex 聊天里描述需求。Codex 会按任务匹配智能体，并通过 MCP 读取需要的 Unity 上下文，无需在这里选择任务或复制指令。", MessageType.Info);
            EditorGUILayout.LabelField("例如：检查 1001 点位第二次进入后按钮不响应的原因，先别改代码。", EditorStyles.wordWrappedLabel);

            if (_manifest == null)
            {
                EditorGUILayout.HelpBox(_notice, MessageType.Error);
                if (GUILayout.Button("重新读取智能体资源")) OnEnable();
                EditorGUILayout.EndScrollView();
                GUILayout.EndArea();
                return;
            }

            EditorGUILayout.LabelField("内置版本", _manifest.version);
            EditorGUILayout.LabelField("当前工程", XYUnityAgentService.ProjectRoot, EditorStyles.wordWrappedLabel);
            EditorGUILayout.LabelField("Skill 状态", _status, EditorStyles.wordWrappedLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("安装 / 更新当前工程智能体技能包", GUILayout.Height(28)))
                    RunAction(() => {
                        string message = XYUnityAgentService.InstallToProject();
                        _status = XYUnityAgentService.InstallationStatus();
                        return message + "\n接下来直接在 Codex 描述需求即可。若未发现 Skill，重新打开聊天或显式使用 $xy-unity-agent。";
                    });
                if (GUILayout.Button("检查状态", GUILayout.Width(90), GUILayout.Height(28)))
                    RunAction(() => _status = XYUnityAgentService.InstallationStatus());
            }

            GUILayout.Space(12);
            _showAdvanced = EditorGUILayout.Foldout(_showAdvanced, "高级选项：手动生成交接任务（可选）", true);
            if (_showAdvanced)
            {
            EditorGUILayout.HelpBox("仅在需要把当前选择固定成上下文快照，或交给另一个聊天时使用。日常操作不需要这一步。", MessageType.None);
            EditorGUI.BeginChangeCheck();
            _workflow = EditorGUILayout.Popup("任务类型", _workflow, _titles);
            EditorGUILayout.HelpBox(_manifest.workflows[_workflow].guidance, MessageType.None);
            _analysisOnly = EditorGUILayout.Popup("执行范围", _analysisOnly ? 0 : 1, new[] { "仅分析", "修改并验证" }) == 0;
            _module = EditorGUILayout.TextField("模块 / 点位提示", _module);
            EditorGUILayout.LabelField("本次任务", EditorStyles.boldLabel);
            _task = EditorGUILayout.TextArea(_task, EditorStyles.textArea, GUILayout.MinHeight(110));
            if (EditorGUI.EndChangeCheck()) { SaveTask(); _prompt = ""; }
            EditorGUILayout.LabelField("示例：" + _manifest.workflows[_workflow].example, EditorStyles.wordWrappedMiniLabel);

            using (new EditorGUILayout.HorizontalScope())
            using (new EditorGUI.DisabledScope(string.IsNullOrWhiteSpace(_task)))
            {
                if (GUILayout.Button("生成 / 刷新上下文", GUILayout.Height(30))) RunAction(() => {
                    _prompt = XYUnityAgentService.BuildPrompt(_manifest.workflows[_workflow].id, _analysisOnly, _task, _module);
                    _showPrompt = true;
                    return "已生成；场景和选择发生变化后，请刷新上下文。";
                });
                if (GUILayout.Button("复制任务到 Codex", GUILayout.Height(30))) RunAction(() => {
                    _prompt = XYUnityAgentService.BuildPrompt(_manifest.workflows[_workflow].id, _analysisOnly, _task, _module);
                    EditorGUIUtility.systemCopyBuffer = _prompt;
                    _showPrompt = true;
                    return "已复制。粘贴到当前工程的 Codex 聊天即可。";
                });
            }

            _showPrompt = EditorGUILayout.Foldout(_showPrompt, "生成的任务指令", true);
            if (_showPrompt && !string.IsNullOrEmpty(_prompt))
                EditorGUILayout.TextArea(_prompt, EditorStyles.textArea, GUILayout.MinHeight(180));
            }
            if (!string.IsNullOrEmpty(_notice))
                EditorGUILayout.HelpBox(_notice, _noticeIsError ? MessageType.Error : MessageType.Info);
            GUILayout.Space(8);
            EditorGUILayout.LabelField("用自然语言限定范围：说“先看看、不要改”就只分析；说“修复并验证”就执行对应修改。", EditorStyles.wordWrappedMiniLabel);
            EditorGUILayout.EndScrollView();
            GUILayout.EndArea();
        }
    }

    public sealed class XYUnityAgentWindow : EditorWindow
    {
        private XYUnityAgentTool _tool;

        [MenuItem("XY Tools/AI/XY Unity 智能体")]
        public static void Open()
        {
            var window = GetWindow<XYUnityAgentWindow>("XY Unity 智能体");
            window.minSize = new Vector2(480, 500);
            window.Show();
        }

        private void OnEnable() { _tool = new XYUnityAgentTool(); _tool.OnEnable(); }
        private void OnDisable() => _tool?.OnDisable();
        private void OnGUI() => _tool?.OnGUI(new Rect(0, 0, position.width, position.height));
    }
}
