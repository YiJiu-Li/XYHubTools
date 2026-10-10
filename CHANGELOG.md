# 更新日志

所有重要的项目更改都将记录在此文件中。

格式基于 [Keep a Changelog](https://keepachangelog.com/zh-CN/1.0.0/)，
本项目遵循 [语义化版本](https://semver.org/lang/zh-CN/)。

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
