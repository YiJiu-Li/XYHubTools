---
name: xy-unity-shader
description: 在 Unity 中实现或诊断 Shader Graph、HLSL 着色器、VFX/粒子、渲染 Pass、材质显示和 GPU 渲染性能。用于溶解、透明、特效、变体与合批问题；普通点位业务或纯 CPU/异步问题由 Unity 开发工作流处理。
metadata:
  version: "1.0.0"
---

# XY Unity Shader / VFX

## 先确认当前渲染环境

读取工程规则和实际工作区改动；存在 `.codegraph/` 时先用 CodeGraph 定位代码。确认 Unity、URP/HDRP/内置管线和 Shader Graph/VFX 包版本，当前 Graphics/Quality 使用的 Render Pipeline Asset、Renderer、目标平台、图形 API 与 XR 模式。不要把原始智能体文件中的版本表作为当前环境。

效果任务先检查现有材质、Shader、依赖纹理/网格和用户视觉目标。性能任务先确认是 CPU 提交、GPU 填充、带宽、过度绘制、变体还是资源问题，记录基线。能从工程读取的信息不再反复向用户询问。

实现或评审前读取 [渲染实现与验证](references/rendering.md)。遵守当前项目命名和组织方式；SG_、VFX_ 仅适用于需要新建命名约定的项目，不强制重命名现有资产。

## 使用现有工具

- XY MCP 可用时，先核对绑定工程。Shader Graph 先 `status/info/nodes/properties` 确认 API、图结构和槽位，再编辑；按工具要求先关闭图窗口，保留回滚快照，完成后重新读取结构验证。不假定 VFX Graph 也支持相同编辑工具。
- 优先使用现有 Shader Graph 模型 API，不手工拼接未经验证的序列化 JSON。HLSL 或 Render Pass 选择取决于效果与版本能力，不因偏好迁移整个项目管线。
- 用 Asset Preview、Scene/Game 截图查看效果；性能判断结合 Frame Debugger、Profiler 和目标设备。捕获或采样结束后恢复本次改变的开关，不关闭用户原已开启的工具。
- Pipeline 命令先发现实时 schema；缺少编辑工具时可以用文件/API 路径实现用户已授权的任务，并说明未验证的部分。

## 范围和交付

用户只要求方案或分析时保持只读；明确要求实现时完成必要修改并验证，不逐文件重复询问许可。保留 `.meta`、材质引用、属性名和动画绑定；不同渲染管线的专用 Shader 不直接互换。

检查 Shader 导入错误、必要 Pass、材质属性和 RenderQueue/Blend/ZWrite/Cull，按任务查看实际视觉。报告效果、受影响资产、设备/质量等级、验证范围与性能变化。Editor 截图和编译通过不能证明移动端/XR 性能达标。

没有统一的“移动端 500 DrawCall、128 指令、VFX 2ms”合格线；从目标刷新率、设备、双眼渲染、分辨率和已有预算确定验收条件，并用实测数据支撑。
