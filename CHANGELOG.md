# 更新日志

所有重要的项目更改都将记录在此文件中。

格式基于 [Keep a Changelog](https://keepachangelog.com/zh-CN/1.0.0/)，
本项目遵循 [语义化版本](https://semver.org/lang/zh-CN/)。

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
