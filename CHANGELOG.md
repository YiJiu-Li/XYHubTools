# 更新日志

所有重要的项目更改都将记录在此文件中。

格式基于 [Keep a Changelog](https://keepachangelog.com/zh-CN/1.0.0/)，
本项目遵循 [语义化版本](https://semver.org/lang/zh-CN/)。

## [2.0.0] - 2026-10-23

### 变更（不兼容）
- Bridge 传输层由 Windows Named Pipe 改为本机 TCP：监听 `127.0.0.1` 上由系统分配的端口，Windows / macOS / Linux 使用同一套代码。`PipeServer` 移除，新增 `BridgeServer`；消息信封格式不变，`PipeEnvelope` 更名为 `BridgeEnvelope` 并新增 `token` 字段。
- 实例发现改为 `~/.xybridge/instances/xy-<工程指纹>-<pid>.json`，内容含 host/port/token/工程路径/PID/版本。文件存在不再等于实例存活，**能否建立 TCP 连接**才是判据，崩溃残留文件自动失效并定期清理。
- 一次性令牌每次 Editor 启动或域重载重新生成，服务端逐条校验；令牌不匹配在握手阶段即返回明确错误，不再让每个工具调用各自失败。
- 首次推送的 `unity_connected` 不含令牌与工程路径（此时尚未认证），相关字段改由必带令牌的 `ping` / `status` 返回。

### 移除
- 不再依赖 pywin32：`MCP~` 脚本只用 Python 标准库（socket / threading / json），安装时无需 `pip install`。
- 服务端 accept 循环不再串行等待连接处理结束，`unity_list_projects` 的探测连接不会阻塞主连接；命令执行仍由 `ExecuteCodeLock` 串行化。

### 修复
- `xy_mcp_server.py` 启动时残留调用已删除的 `resolve_pipe()` / `list_live_pipes()`，导致进程启动即 `NameError`；改为 `resolve_target()` / `list_instances()`。
- 域重载后第一次调用必定失败（客户端仍握着旧端口）：客户端每次调用前比对实例文件 mtime，变了就先断开重连。实测重载后首次 `ping` 0.0s 成功。
- `unity_execute_code` 的超时从 30s 放宽到 120s：冷启动要初始化 Roslyn（245 个引用），超时会被误报成「执行结果未知」。
- 安装配置时按平台解析 Python 命令名（Windows `python`，macOS / Linux `python3`），并真实执行一次确认是 Python 3，避免 Microsoft Store 占位程序或 Python 2 被写进配置；可用 `XYBRIDGE_PYTHON` 覆盖。
- 脚本安装的原子替换（`File.Replace`）在 Mono / Unix 上支持不一致且原先没有兜底，现改为失败时退回「备份 + 覆盖」，避免 macOS / Linux 上点安装直接失败。
- 实例文件写入原先只捕获 `IOException`，现覆盖权限与「不支持」类异常：否则写失败会被静默当成「没有实例」，表现为 Unity 开着但 MCP 报未发现。

### 验证
- `MCP~` 可靠性测试重写为真实 TCP 回环 + 假 Unity 服务端，23 项通过：实例发现、握手绑定工程、令牌拒绝、断开与超时后不重放、连接复用、stdio 错误透传、批量预检与断言。
- Windows 上 Unity 2022.3.53f1 编译通过；macOS / Linux 需在对应机器上按 `MCP~/RELIABILITY.md` 的清单验收。

## [1.8.0] - 2026-10-22

### 新增
- 新增 Shader Graph、物理查询、Profiler、资源预览和 Frame Debugger 五个 MCP 工具，并接入 Unity 主线程服务。
- 新增 `unity_batch`：整批参数预检、顺序执行、读回断言和首错停止；默认 dry-run，不承诺自动回滚。
- 内置工程级智能体技能包：Unity/Rokid 开发、Shader/VFX、Rust/Tauri/Vue 桌面 Agent 开发。按任务匹配，复用现有 Codex 与 XY MCP。
- Unity 智能体面板提供技能安装、更新与状态；日常直接在 Codex 描述需求，手动任务交接收进可选高级设置。
- 新增请求统计、专项工具自检、只读工程盘点脚本和可靠性/技能分发测试。

### 修复与可靠性
- MCP 统一传递服务 JSON 失败和管道失败，避免失败请求显示成功；参数与工具注册增加校验。
- 管道写入失败不再自动重放，避免执行状态未知时重复修改；stdio 统一使用 UTF-8。
- `unity_status` 报告运行版本、脚本路径与哈希、工具数量、安装一致性和重启需求。
- Bridge 安装器阻止低版本或工具数量减少的脚本覆盖，原子替换并保留 `.previous` 备份。
- 技能安装按文件清单校验，保护本地修改、未知文件和版本降级；更新时保留旧副本并隔离专项所有权。

### 验证与范围
- MCP/Pipeline 自动测试 40 项、技能分发/盘点测试 6 项通过；三个 Skill 格式校验通过。
- Unity 2022.3.53f1 脚本编译通过；实测场景/组件读取与断言，以及 URP 渐变 Shader Graph 创建、连线、导入和材质预览。
- 桌面 Agent 的四项隔离 Rust 状态测试通过，尚未覆盖完整 Tauri 应用、Rokid 真机或 VFX 性能。
- 技能仅分发提炼后的通用方法，不随包包含原始聊天记录；Timeline 专项继续暂缓。

## [1.7.0] - 2026-10-10

### 新增
- MCP Bridge 面板新增 Pipeline 命令浏览器：实时读取命令、搜索和分类筛选、功能与参数详情、复制名称和 Schema。
- `unity_pipeline_job` 开放官方后台任务查询/取消，任务句柄绑定工程和服务会话。
- 提供官方固定版本安装、随附版本升级和旧引用回退入口；升级前保存包引用及清单备份。
- 官方原包校验与独立兼容补丁重建流程，随附包更新为 `0.8.0-xy.2`；新增协议验收脚本。

### 调整
- 命令发现默认读取分类索引，支持 query/tag、精简列表、分页及单命令完整 Schema。
- Unity 6 首次安装选择固定版本官方包，Unity 2022.3 使用兼容包；已有安装不自动替换。

### 修复
- HTTP 工程绑定不再逐次依赖命名管道；绝对路径可直接绑定官方 Pipeline。
- 明确只读请求可重试、修改结果未知以及任务会话失效；保留不自动重放的行为。
- 编辑器退出时停止 XY 命名管道并取消自动启动，避免异步连接回调在 Mono 关闭时仍然运行。

## [1.6.0] - 2026-10-10

### 新增
- XY 随附 `0.8.0-xy.1` Pipeline UPM 安装包，缺少 Pipeline 的项目首次加载自动安装；保留已有安装。
- `unity_pipeline` 支持结构化参数，`unity_pipeline_commands` 查询实时命令 schema。
- Bridge 面板显示 Pipeline 安装状态并提供检查/重试入口。

### 调整
- `unity_cli` 保留命令字符串入口，改为直接调用 Pipeline HTTP，移除对 Unity CLI 可执行文件的依赖。
- 每次请求重新读取目标工程服务信息；超时不重放命令；Pipeline 失败透传 MCP `isError`。
- 随附 Pipeline 兼容基线为 Unity 2022.3，保留原始许可证及第三方声明。

### 修复
- 自检面板增加 Pipeline 分组及 4 项真实 HTTP 检查，异步执行避免阻塞主线程。
- 已有历史结果时仍显示新增的未运行用例。
- 修正包根目录 Git 安装地址，最低 Unity 版本统一为已验证的 2022.3。
- Bridge 支持并发管道连接、编译引用缓存及主线程预处理符号快照；修复 JSON 字符串转义。

## [1.5.0] - 2026-09-29

### 新增
- 🔗 **MCP Bridge 改为 Hub 模式 —— MCP 配置只写一次**
  - 原先管道名由 `xy_mcp_server.py` 所在目录推导，于是「一个工程 = 一份脚本 = 一条 MCP 配置」，
    每把一个插件导入新工程都要再写一条 `mcpServers`
  - 现在 server 枚举 `\\.\pipe\xybridge_unity_*` **自动发现所有存活的 Unity 实例**。
    目标解析优先级：`--project=<关键字>` / `XYBRIDGE_PROJECT` 环境变量
    → 脚本同目录推导（向后兼容旧的「一工程一脚本」用法）→ 唯一存活实例
    → 报错并列出候选（**多个实例时不会乱猜**）
  - 脚本安装到与工程无关的固定位置 `%USERPROFILE%\.xybridge\xy_mcp_server.py`
  - 安装时同时写入 Codex / VS Code / WorkBuddy 三处，均为**单条 `xybridge`**；
    旧版本按工程生成的 `xybridge_<工程名>` 条目会自动清理，配置不再随工程数量增长
- 🧩 新增 `unity_list_projects`：列出所有存活的 Unity（工程路径、PID、当前绑定）
- 🧩 新增 `unity_use_project`：多开 Unity 时切换目标，参数写工程名或路径片段即可
  - MCP 工具数 23 → 25

### 修复
- 🐛 **修复 `unity_ping` 恒返回 `PID: 0`**
  - Python 侧读的是 ping 响应里的 `processId`，而 `YZJBridge.Ok()` 未填该字段
    （真实 PID 只挂在 `unity_connected` 推送里，Python 收到后会丢弃）
- 🐛 `PipeEnvelope.processPath` 是「声明了但从未赋值」的死字段，改为语义正确的 `projectPath`，
  并在 `ping` / `status` 响应里填入 Unity 工程根目录 —— 多工程识别依赖它

### 变更
- 🔧 `BridgeSetup` 重写：server 名不再按工程生成（`xybridge_<工程名>` → 固定 `xybridge`）；
  Codex 配置改用脚本绝对路径并去掉 `cwd`，与工程彻底解耦
- 🔧 Bridge 状态面板新增「Hub 脚本」行，显示固定安装路径及是否已安装
- 🔧 WorkBuddy 的 `mcp.json` 合并交由 `python <script> --install-config` 完成
  （python 自带 `json` 模块，比在 C# 里手搓 JSON 合并可靠），且幂等

### 文档
- 📝 README 重写 MCP Bridge 配置章节，说明 Hub 模式与多工程切换

---

## [1.4.0] - 2026-09-29

### 新增
- 🧩 **MCP 工具补齐「组件写入」与「Prefab 读取」**
  - 新增 `unity_set_property`：修改 GameObject 组件属性，走 Undo，可 Ctrl+Z 撤销；
    属性路径支持友好名与序列化名（`Transform.localPosition` / `m_LocalScale.x`），
    支持 bool / int / float / string / enum / Vector2 / Vector3 / Color / ObjectReference
  - 新增 `unity_get_prefab_structure`：读取 Prefab 的子物体层级与每个物体的组件列表
  - 这两项后端（`ComponentService`）早已实现，但一直未接入路由；本次接线到
    `MessageDispatcher` 的 `set_property` / `get_prefab_structure`，MCP 工具数 21 → 23

### 优化
- ⚡ **MCP Bridge Python 侧复用长连接**，不再每次工具调用后断开重连
  （原先每次调用都要重新 `WaitNamedPipe` + 暖场 ping，最多多等 5 秒）
- 🔧 读管道超时时主动关闭句柄，避免残留读线程与后续读争抢同一条管道
- 🎨 **MCP Bridge 状态面板按框架规范重写 UI**
  - 改用 `GUILayout.BeginArea(rect)` + `EditorGUILayout.*` 自动布局，不再手算 Rect 高度、
    不再维护 `_contentHeight` 滚动高度；窗口缩放 / 面板变窄时自适应
  - 新增 `BridgeStyles` 样式缓存（对齐 `XY_HubStyles` 模式）：集中管理、延迟初始化、
    皮肤切换自动重建，消除 OnGUI 热路径里每帧 `new GUIStyle` / `new Texture2D`
  - 颜色改为皮肤感知（light / dark 双套），修复浅色皮肤下深色硬编码文字不可读的问题
  - 状态卡片、胶囊筛选、结果行、日志区统一为框架风格；分组标签按 core/assets/scene/events 着色

### 修复
- 🐛 **统一 Bridge 各服务的 JSON 字符串转义**，新增共享 `JsonEscape`
  - 修复 GameObject / 场景根节点名字含引号、反斜杠或换行时产出非法 JSON 的问题
  - `ComponentService`、`SceneService`、`MessageDispatcher` 的手工拼装统一走同一实现
- 🐛 **修复 Bridge 自检跑完后看不到结果**
  - 自检结果改为写入 `SessionState`，域重载（脚本重编译）后面板实例重建也能恢复显示
  - 每个用例跑完立即落盘，中途被打断也能保住已完成的部分；有历史结果时自动展开面板
  - 会触发重编译 / 资源刷新的用例标记为「破坏性」，默认「运行全部」跳过，
    需勾选「含破坏性用例」才执行
  - `unity_recompile_scripts` 自检不再真的调用 `RequestScriptCompilation`
    （那会把自检自己打断），改为验证编译服务与引用收集可用
  - `unity_set_property` 自检改用临时 GameObject 并测完销毁，不再改动用户场景里的对象

### 文档
- 📝 `MessageDispatcher` 头部注释由「11 种消息类型」更正为实际数量（25）
- 📝 Bridge 自检由 21 个 case 扩充到 23 个，状态面板文案同步

---

## [1.3.0] - 2026-06-29

### 新增
- 🔔 **Git 推送通知显示 commit 详情**
  - 通知窗口展示每条新 commit 的短 hash、提交时间、作者、邮箱以及完整标题（可换行/滚动）
  - 窗口高度按 commit 数量自动伸缩，最多展示 20 条
  - 顶部多出"作者"行，一眼看到这次推送的发起人
- 📁 **仓库路径一键设置**
  - 设置面板「仓库路径」行新增「当前项目」按钮，一键填入 `Application.dataPath` 的上一级目录

### 优化
- 通知窗口宽度 360 → 380，最小高度 280，长 commit 列表可滚动浏览
- commit 解析用 `git log --pretty=format:"%H\x00%an\x00%ae\x00%aI\x00%s"` 一次拿到所有元数据

---

## [1.2.9] - 2026-06-18

### 变更
- 🔧 MCP Bridge 恢复生成 VS Code 项目级 MCP 配置，改用 stdio，不再依赖 SSE 服务
- 🔧 Codex 配置写入改为先清理当前项目 server 的所有旧残留块，再写入干净配置

---

## [1.2.8] - 2026-06-18

### 修复
- 🐛 修复 MCP Bridge 重复点击「安装/更新配置」时，可能在 Codex `config.toml` 中留下重复 `.env` 子表，导致 Codex 报 `duplicate key` 的问题

---

## [1.2.7] - 2026-06-18

### 修复
- 🐛 修复 Git 推送通知可能在未被用户看到时就被标记为已读的问题
- 🔧 Git 推送通知默认不再自动消失，只有点击「知道了」才确认当前远端提交
- 🔧 同一个未确认推送会复用通知窗口，避免重复弹出多个小窗

---

## [1.2.6] - 2026-06-18

### 变更
- 🔧 MCP Bridge 移除 VS Code SSE 服务管理入口，收敛为 Unity Named Pipe + Codex stdio 配置
- 🔧 安装/更新配置不再生成 `.vscode/mcp.json`
- 🗑️ 删除不再使用的 `MCP~/mcp.template.json`

---

## [1.2.5] - 2026-06-18

### 修复
- 🐛 修复 Git 推送监听在未打开 XY 编辑器工具集窗口时不会弹出通知小窗的问题
- 🔧 Git 推送通知现在由后台监听服务直接触发，工具面板只负责显示状态和历史，避免重复弹窗

---

## [1.2.4] - 2026-06-18

### 新增
- 🔔 **Git 推送监听**（新工具）
  - 后台轮询 + FileSystemWatcher 双通道监听 `.git/refs/` 变化
  - 可拖动的非模态通知小窗，不阻塞开发
  - 自动执行 `git fetch` 并比对远端 refs，只提醒远端是否有新推送
  - 可配置轮询间隔（10-3600 秒）、远程名、追踪分支、通知方式、自动消失时间
  - 推送历史记录（最近 50 条），支持逐条重新显示通知
  - 全部设置通过 EditorPrefs 持久化

---

## [1.2.3] - 2026-06-17

### 修复
- 🐛 Codex MCP 配置自动写入 `PYTHONIOENCODING=utf-8` 和 `PYTHONUTF8=1`，避免 Windows 环境下中文输出编码异常

---

## [1.2.2] - 2026-06-17

### 修复
- 🐛 修复 `printJson` 对匿名对象、字典、数组等普通 C# 对象输出 `{}` 的问题
- 🐛 `unity_get_component` 支持 `Camera` 和 `UnityEngine.Camera` 等组件类型写法

---

## [1.2.1] - 2026-06-17

### 修复
- 🐛 当前 Unity 项目可自动写入独立 Codex MCP server，避免多个项目共用固定 cwd

---

## [1.2.0] - 2026-06-17

### 新增
- 🤖 **MCP Bridge**
  - 支持通过 Named Pipe 连接 Unity Editor
  - 支持 Codex stdio MCP 接入
  - 提供 Bridge 状态面板和工具自检面板

### 变更
- 🔧 工具入口统一收拢到 `XY Tools > 编辑器工具集`
- 🔧 右键资源菜单统一到 `Assets/XY Hub Tools`
- 📝 修正 Git URL 安装说明，支持 `?path=/Assets/XYHubTools`
- 📝 补充 MCP Bridge 的 Codex 配置说明
- 🔧 MCP Bridge 安装配置时自动为当前 Unity 项目写入 Codex MCP server

### 修复
- 🐛 修复 MCP ping 假成功的问题
- 🐛 优化 Codex stdio 下的 pipe 连接保持逻辑
- 🐛 修复部分中文显示和说明文案

---

## [1.0.0] - 2026-03-04

### 新增
- 🎬 **材质透明度动画创建器**
  - 支持透明度、轴向溶解、整体溶解三种动画类型
  - 可视化时间轴预览
  - 支持 MeshRenderer、SkinnedMeshRenderer、SpriteRenderer
  - 自动创建 AnimatorController
  - 自定义动画曲线和参数
  - 快速模板创建功能

- 🎨 **模型材质导入器**
  - 批量导入和管理模型材质
  - 自定义材质模板支持
  - 自动应用 Shader 配置
  - 批量材质替换功能

- 🌫️ **GPU 噪声生成器**
  - GPU 加速的实时噪声纹理生成
  - 支持多种噪声类型（Perlin、Simplex、Worley 等）
  - 可自定义噪声参数
  - 实时预览和导出功能

- 🔧 **Asset GUID 重生成器**
  - 安全地重新生成资源 GUID
  - 避免资源冲突
  - 批量处理资源

- 📋 **Copy App**
  - 快速复制和管理资源
  - 支持自定义复制规则
  - 批量操作支持

### 核心框架
- ✨ XY Editor Hub 工具中心系统
- 🔌 插件式工具架构
- 🎨 统一的编辑器 UI 风格
- 📝 Markdown 文档查看器
- 💾 工具状态持久化

### 文档
- 📖 完整的 README 文档
- 📋 详细的使用说明
- 🔖 MIT 开源协议

---

## 版本说明

### 版本号规则
- **主版本号**：重大架构变更或不兼容的 API 修改
- **次版本号**：向下兼容的功能性新增
- **修订号**：向下兼容的问题修正

### 图标说明
- ✨ 新增功能
- 🐛 Bug 修复
- 📝 文档更新
- 🎨 UI/UX 改进
- ⚡ 性能优化
- 🔧 配置/工具改进
- 🗑️ 移除功能
- 🔒 安全修复
