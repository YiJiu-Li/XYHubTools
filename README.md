# XY Hub Tools

> Unity 编辑器工具集合 - 提升开发效率的实用工具套件

[![Unity Version](https://img.shields.io/badge/Unity-2022.3%2B-blue)](https://unity.com/)
[![License](https://img.shields.io/badge/License-MIT-green.svg)](LICENSE)

XY 自有代码使用 MIT；随附 Pipeline 及第三方组件遵循各自许可证，详见 [Pipeline 分发说明](Pipeline~/README.md)。

## ✨ 功能特性

### 🎬 材质透明度动画创建器
- 支持 **透明度**、**轴向溶解**、**整体溶解** 三种动画类型
- 可视化时间轴预览
- 支持 MeshRenderer、SkinnedMeshRenderer、SpriteRenderer
- 自动创建 AnimatorController 和动画剪辑
- 自定义动画曲线和参数
- 快速模板创建

### 🎨 模型材质导入器
- 批量导入和管理模型材质
- 支持自定义材质模板
- 自动应用 Shader 配置
- 批量材质替换

### 🌫️ GPU 噪声生成器
- GPU 加速的实时噪声纹理生成
- 支持多种噪声类型（Perlin、Simplex、Worley 等）
- 可自定义噪声参数
- 实时预览和导出

### 🔧 Asset GUID 重生成器
- 安全地重新生成资源 GUID
- 避免资源冲突
- 批量处理资源

### 📋 Copy App
- 快速复制和管理资源
- 支持自定义复制规则
- 批量操作支持

### 🤖 MCP Bridge
- 通过 MCP 让 AI Agent 查询和操作 Unity Editor
- 提供 28 个 `unity_*` 工具：C# 代码执行、场景/组件读写、Prefab 结构、资源管理、Console、截图、事件订阅、多工程切换
- `unity_cli` 直接通过 HTTP 调用 `com.unity.pipeline` 的 **160 个命令**（构建、测试、烘焙、动画、Project Settings、包管理等），
  两条通道互不影响
- 支持 Named Pipe 本地桥接
- 支持 Codex stdio 连接
- 支持 VS Code stdio 连接

### 🔔 Git 推送监听
- 检测远端分支是否有新推送
- 使用可拖动的非模态小窗提醒，不阻塞开发
- 只执行 `git fetch` 和 refs 比对，不自动 pull / merge / rebase
- 支持自定义仓库路径、远程名、追踪分支和轮询间隔

## 📦 安装

### 方法 1: 通过 Git URL 安装 (推荐)

1. 打开 Unity Package Manager (`Window > Package Manager`)
2. 点击左上角 `+` 按钮
3. 选择 `Add package from git URL...`
4. 输入:
   ```
   https://github.com/YiJiu-Li/XYHubTools.git
   ```

### 方法 2: 通过 manifest.json 安装

在项目的 `Packages/manifest.json` 文件中，添加以下内容到 `dependencies` 部分：

```json
{
  "dependencies": {
    "com.yzj.xyhubtools": "https://github.com/YiJiu-Li/XYHubTools.git"
  }
}
```

### 方法 3: 指定版本安装

```json
"com.yzj.xyhubtools": "https://github.com/YiJiu-Li/XYHubTools.git#v1.6.0"
```

## 🚀 快速开始

### 打开工具面板

在 Unity 菜单栏选择 `XY Tools > 编辑器工具集` 打开工具中心窗口。

### 使用材质透明度动画创建器

1. 在工具中心选择 **材质透明度动画创建器**
2. 在场景中选择带有 Renderer 组件的 GameObject
3. 选择或创建动画模板
4. 点击 **创建动画** 按钮
5. 自动生成动画剪辑和 AnimatorController

### 使用 GPU 噪声生成器

1. 在工具中心选择 **GPU 噪声生成器**
2. 选择噪声类型和参数
3. 实时预览效果
4. 导出为纹理资源

### 使用 MCP Bridge

1. 在工具中心选择 **MCP Bridge**
2. 点击 **启动管道**
3. 点击 **安装 / 更新 MCP（一次性）**
4. 安装 Python 依赖:
   ```bash
   pip install pywin32
   ```

#### Pipeline 命令浏览器

MCP Bridge 面板内可浏览当前服务的全部 Pipeline 命令，支持名称、功能说明和标签搜索、分类筛选。点击命令查看官方功能说明、参数类型和必填项，支持复制命令名称及完整参数 Schema。列表只用于查看，不会执行命令；服务更新后可点击“刷新命令”重新读取。

#### Pipeline：只引入 XY，无需 Unity CLI

首次加载时，缺少 `com.unity.pipeline` 的 Unity 6 项目通过 UPM 安装固定版本官方包，
Unity 2022.3 项目安装 XY 随附兼容包；已有 Pipeline 保留，不覆盖。依赖由 UPM
解析，首次安装可能需要网络。面板显示安装状态，可点 **检查 / 重试**；也可用
`Tools > XY Hub > 检查或安装 Pipeline`。

MCP 直接访问目标项目的 Pipeline HTTP 服务，不再寻找或启动 `unity.exe`：

| 工具 | 用法 |
|---|---|
| `unity_cli` | 保留旧入口，例如 `{"spec":"editor_status"}`；空 spec 返回精简命令列表 |
| `unity_pipeline` | 结构化调用，例如 `{"command":"editor_status","parameters":{}}` |
| `unity_pipeline_commands` | 默认分类索引；用 query/tag 筛选、nextOffset 翻页；`{"command":"eval"}` 返回完整参数 |
| `unity_pipeline_job` | `unity_pipeline` 加 `job:true` 提交后，传返回的完整 `xyJob` 查询或取消后台任务 |

命令参数由 Pipeline 自己解析，端口和认证令牌从当前目标项目读取。切换工程后不复用
旧项目的连接信息。构建、测试等异步命令仍需查询对应 `*_status`；连接超时不会自动
重复执行命令，以免重复修改资源。Pipeline 失败结果会返回 MCP `isError: true`。
旧 `unity_cli` 名称仅作为兼容入口，不支持 CLI 自身的全局选项或 Player 目标选择。

当前兼容包为 `0.8.0-xy.2`，由校验过的官方原包加独立补丁重建，已验证 Unity 2022.3.53f1。
Unity 6 官方路线需在对应编辑器中单独验收。Python + pywin32 仍是 Bridge 的运行前提。更新包后需更新 MCP 脚本并
重启 MCP 服务，才能使用新入口。

团队项目请一并提交自动生成的 `Packages/XYBridge~/`、`Packages/manifest.json`
和 `Packages/packages-lock.json`。已有 Pipeline 不自动升级；升级随附版本的方法见
[Pipeline 分发说明](Pipeline~/README.md)。

#### 配置只写一次（Hub 模式）

点击 **安装 / 更新 MCP（一次性）** 后，插件会把 `xy_mcp_server.py` 安装到与工程无关的固定位置：

```
%USERPROFILE%\.xybridge\xy_mcp_server.py
```

该脚本会**自动发现所有存活的 Unity 管道**，因此 MCP 配置只需写一次，
之后把插件导入任何新工程都不用再改。安装时会同时写入三处（均为单条 `xybridge`）：

| 客户端 | 位置 |
|---|---|
| Codex | `~/.codex/config.toml` → `[mcp_servers.xybridge]` |
| VS Code | `<工程>/.vscode/mcp.json` |
| WorkBuddy | `~/.workbuddy-ai/mcp.json` |

```toml
[mcp_servers.xybridge]
command = "python"
args = ["C:\\Users\\<你>\\.xybridge\\xy_mcp_server.py", "--transport=stdio"]
startup_timeout_sec = 30
```

> 旧版本按工程生成的 `xybridge_<工程名>` 条目会在安装时自动清理，配置不再随工程数量增长。

修改后需要重启客户端或新开会话，当前会话通常不会热加载新增 MCP。

#### 同时开着多个 Unity 工程

Hub 会自动发现所有实例：

- `unity_list_projects` — 列出所有存活的 Unity（工程路径、PID、当前绑定）
- `unity_use_project "<工程名或路径片段>"` — 切换目标

只有一个实例存活时自动绑定；**多个实例且未指定时会报错并列出候选，不会乱猜**。
也可以用 `--project=<关键字>` 参数或 `XYBRIDGE_PROJECT` 环境变量固定目标。

## 📋 系统要求

- Unity 2022.3（当前验证基线，其他版本需另行适配）
- .NET Framework 4.x 或更高
- 基础编辑器工具支持 Windows / macOS / Linux
- MCP Bridge 当前使用 Windows Named Pipe，需要 Windows + Python + pywin32

## 📖 文档

详细文档请访问：[XYHubTools Wiki](https://github.com/YiJiu-Li/XYHubTools)

## 🤝 贡献

欢迎提交 Issue 和 Pull Request！

## 📝 更新日志

### v1.5.0 (2026-09-29)
- 🔗 MCP Bridge 改为 **Hub 模式**：server 自动发现所有存活的 Unity 管道，脚本安装到固定位置 `~/.xybridge/`，**MCP 配置只写一次**，导入新工程无需再改
- 🧩 新增 `unity_list_projects`（列出所有 Unity 实例及工程路径/PID）与 `unity_use_project`（多开时切换目标），工具数 23 → 25
- 🐛 修复 `unity_ping` 恒返回 `PID: 0`；`status` / `ping` 现在回传工程路径（`projectPath`）

### v1.4.0 (2026-09-29)
- 🧩 MCP Bridge 新增 `unity_set_property`（改组件属性，走 Undo）和 `unity_get_prefab_structure`（读 Prefab 结构），工具数 21 → 23
- ⚡ Python 侧复用长连接，不再每次工具调用后断开重连
- 🐛 统一 JSON 转义，修复对象名含引号/反斜杠时产出非法 JSON 的问题

### v1.3.0 (2026-06-29)
- 🔔 Git 推送通知窗口显示每个 commit 的作者、时间、邮箱和完整标题（可滚动）
- 🔔 通知窗口顶部多出"作者"行；高度按 commit 数量自动伸缩
- 📁 设置面板「仓库路径」行新增「当前项目」按钮，一键填入当前 Unity 工程根
- 🔧 commit 解析用 `git log --pretty=format` 一次拿到所有元数据，避免多次调用 git

### v1.2.9 (2026-06-18)
- 🔧 MCP Bridge 恢复生成 VS Code 项目级 MCP 配置，使用 stdio，不再依赖 SSE
- 🔧 Codex 配置写入会先清理当前项目 server 的旧残留块，避免重复配置

### v1.2.8 (2026-06-18)
- 🐛 修复 MCP Bridge 重复安装配置可能导致 Codex `config.toml` 报 `duplicate key` 的问题

### v1.2.7 (2026-06-18)
- 🐛 修复 Git 推送通知可能被误标记为已读导致没有弹窗的问题
- 🔧 通知默认不自动消失，点击「知道了」后才确认当前推送

### v1.2.6 (2026-06-18)
- 🔧 MCP Bridge 移除 VS Code SSE 服务，收敛为 Codex stdio + Unity Named Pipe

### v1.2.5 (2026-06-18)
- 🐛 修复 Git 推送监听在未打开工具集窗口时不会弹出通知的问题

### v1.2.4 (2026-06-18)
- 🔔 新增 Git 推送监听工具：只检测远端是否有新推送，并在 Unity 内弹出可拖动小窗提醒

### v1.2.3 (2026-06-17)
- 🐛 Codex MCP 配置自动写入 `PYTHONIOENCODING=utf-8` 和 `PYTHONUTF8=1`

### v1.2.2 (2026-06-17)
- 🐛 修复 `printJson` 对匿名对象和字典输出 `{}` 的问题
- 🐛 `unity_get_component` 支持 `UnityEngine.Camera` 等完整组件类型名

### v1.2.1 (2026-06-17)
- 🐛 当前 Unity 项目可自动写入独立 Codex MCP server，避免多个项目共用固定 cwd

### v1.2.0 (2026-06-17)
- 🤖 新增 MCP Bridge
- 🔧 工具统一收拢到 XY Editor Hub
- 📝 更新 UPM 安装路径和 MCP 使用说明

### v1.0.0 (2026-03-04)
- ✨ 初始版本发布
- 🎬 材质透明度动画创建器
- 🎨 模型材质导入器
- 🌫️ GPU 噪声生成器
- 🔧 Asset GUID 重生成器
- 📋 Copy App 工具

## 📄 许可证

MIT License - 详见 [LICENSE](LICENSE) 文件

## 👨‍💻 作者

**YZJ** - [GitHub](https://github.com/YiJiu-Li)

---

⭐ 如果这个工具对你有帮助，请给个 Star！
