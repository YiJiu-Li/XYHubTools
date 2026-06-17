# XY Hub Tools

> Unity 编辑器工具集合 - 提升开发效率的实用工具套件

[![Unity Version](https://img.shields.io/badge/Unity-2021.3%2B-blue)](https://unity.com/)
[![License](https://img.shields.io/badge/License-MIT-green.svg)](LICENSE)

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
- 支持 Named Pipe 本地桥接
- 支持 Codex stdio 连接
- 支持 VS Code SSE 分组服务

## 📦 安装

### 方法 1: 通过 Git URL 安装 (推荐)

1. 打开 Unity Package Manager (`Window > Package Manager`)
2. 点击左上角 `+` 按钮
3. 选择 `Add package from git URL...`
4. 输入:
   ```
   https://github.com/YiJiu-Li/XYHubTools.git?path=/Assets/XYHubTools
   ```

### 方法 2: 通过 manifest.json 安装

在项目的 `Packages/manifest.json` 文件中，添加以下内容到 `dependencies` 部分：

```json
{
  "dependencies": {
    "com.yzj.xyhubtools": "https://github.com/YiJiu-Li/XYHubTools.git?path=/Assets/XYHubTools"
  }
}
```

### 方法 3: 指定版本安装

```json
"com.yzj.xyhubtools": "https://github.com/YiJiu-Li/XYHubTools.git?path=/Assets/XYHubTools#v1.2.3"
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
3. 点击 **安装/更新配置**
4. 安装 Python 依赖:
   ```bash
   pip install pywin32
   ```

#### Codex

Codex 推荐使用 stdio 连接，不需要启动下方 4 个 SSE 服务。

点击 Bridge 面板的 **安装/更新配置** 后，插件会把当前 Unity 项目写入用户级 Codex 配置：

```toml
[mcp_servers.xybridge_项目名]
command = "python"
args = ["xy_mcp_server.py", "--transport=stdio"]
cwd = "你的 Unity 项目根目录"
startup_timeout_sec = 30
```

每个 Unity 项目会生成自己的 MCP server 名称，避免多个项目都连接到同一个固定项目。

修改后需要重启 Codex 或新开项目线程，当前会话通常不会热加载新增 MCP。

#### VS Code

VS Code 使用 `.vscode/mcp.json` 的 SSE 配置。需要在 Bridge 面板启动对应 SSE 服务，首次配置后 Reload Window。

## 📋 系统要求

- Unity 2021.3 或更高版本
- .NET Framework 4.x 或更高
- 基础编辑器工具支持 Windows / macOS / Linux
- MCP Bridge 当前使用 Windows Named Pipe，需要 Windows + Python + pywin32

## 📖 文档

详细文档请访问：[XYHubTools Wiki](https://github.com/YiJiu-Li/XYHubTools)

## 🤝 贡献

欢迎提交 Issue 和 Pull Request！

## 📝 更新日志

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
