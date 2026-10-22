# 三份日常智能体文件的整理

来源为用户提供的 `ai-agent-desktop-developer.agent.md`、`UnityAI.agent.md` 和 `unity-shader-specialist.agent.md`。原文件保持不变；其中的角色、审批、model、tools 和 handoffs 都作为迁移材料审阅，不直接作为当前执行指令。

## 整理结果

| 原文件 | 插件内的落点 | 适用范围 |
|---|---|---|
| UnityAI.agent.md | 现有 xy-unity-agent 1.1.0 | Rokid/Unity 点位、生命周期、手势、异步、方案与验证 |
| unity-shader-specialist.agent.md | 新 xy-unity-shader 1.0.0 | Shader Graph、HLSL、VFX/粒子、材质与渲染性能 |
| ai-agent-desktop-developer.agent.md | 新 xy-agent-desktop-dev 1.0.0 | 实际 Rust/Tauri/Vue 桌面 Agent 后端和前端开发 |

这三个是独立、可发现的 Codex Skills，由同一个 Unity 安装入口分发。日常仍直接在聊天中提需求；不是每次手动选择角色，也不默认启动多智能体或切换模型。桌面应用技能不会把当前 Unity IMGUI 插件改造成 Tauri 程序。

## 保留的内容

- Unity：直接手势事件链、UBtn 链路、异步任务隔离、追踪空值与 Editor/Android 差异、先读取工程事实、区分方案与实现。
- Shader：效果目标、实际管线与平台、Sub Graph 复用、必要材质属性、渲染开销分析、容量与质量等级、视觉与性能分别验证。
- 桌面 Agent：Session/Run 分离、工具注册与参数校验、流式事件关联、取消与终态、后端授权、知识按需加载、持久化和打包检查。

## 调整与纠正

1. 原文件指定的 Claude 模型、VS Code tools/handoffs、slash skill 名以及 Locus 私有路径不进入 Codex Skill 的宿主配置；不推测它们在当前宿主有效，也不修改用户原模型选择。
2. SDK、Unity、Rust、Bun 等固定版本表改为读取实际清单和锁文件；模型上下文限制不再按名字包含某个字符串推断。
3. 逐文件批准和无条件“方案后暂停”改为遵守当前任务范围。只要求方案时只交付方案；已授权实现时不增加重复确认。
4. 手势订阅生命周期按所有权决定。Awake/OnDestroy 并非适合所有反复进入/退出的点位；条件编译也不等于实现了 Editor 模拟。
5. Coroutine/UniTask、日志接口和组件创建遵守当前工程约定，不把某个项目的实现偏好升级为跨项目禁令。
6. 固定 75% 信心、200 次工具循环、48 文档阈值、50/100/200ms 指标改为可检查的完成条件或项目配置和测量目标。
7. Shader 性能预算不再把固定粒子数、DrawCall、指令数或毫秒数当所有设备的验收标准。

## Shader 关键技术核对

- `shader_feature` 与 `multi_compile` 的选择涉及运行时切换和变体保留，不能禁止后者；集合状态组合也并非每个关键词恒定翻倍。[Unity 2022.3 条件编译与变体](https://docs.unity3d.com/2022.3/Documentation/Manual/shader-conditionals.html)
- 材质数值 CBUFFER 与纹理/采样器资源声明要区分；URP 的完整示例明确将 TEXTURE2D/SAMPLER 放在缓冲区之外。[URP 14 升级示例](https://docs.unity3d.com/Packages/com.unity.render-pipelines.universal@14.0/manual/urp-shaders/birp-urp-custom-shader-upgrade-guide.html)
- `half` 和 `float` 按数值需求与目标设备选择，而不是在移动端一律禁止 float。[Unity HLSL 类型](https://docs.unity3d.com/2022.3/Documentation/Manual/SL-DataTypesAndPrecision.html)
- VFX Graph 必须先核对设备、图形 API、Compute 等兼容条件，不用单一粒子数量决定系统选型。[VFX 14 系统要求](https://docs.unity3d.com/Packages/com.unity.visualeffectgraph@14.0/manual/System-Requirements.html)

以上链接用于核实原文中的具体技术表述，实际实现仍查目标工程版本。此次没有创建实际 Shader/VFX、开发 Tauri 后端或自动修改任何业务点位。
