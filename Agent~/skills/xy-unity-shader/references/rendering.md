# 渲染实现与验证

## Shader Graph 与 HLSL

- 重复的图逻辑可抽 Sub Graph，但先确认多个材质确实共享语义，避免把业务专用参数做成难以维护的全能图。
- 普通 URP 材质数值属性使用适当的 UnityPerMaterial CBUFFER 布局，并保持相关 Pass 一致；纹理和采样器资源按管线宏单独声明，不把所有 uniform/资源无差别塞进常量缓冲区。检查实际 Inspector/帧调试兼容状态，不能只看代码中是否有一个 CBUFFER 字符串。[URP 14 示例](https://docs.unity3d.com/Packages/com.unity.render-pipelines.universal@14.0/manual/urp-shaders/birp-urp-custom-shader-upgrade-guide.html)
- 按数值范围选精度。颜色等可评估 half；世界位置、深度、大数值 UV 或精度敏感计算可能仍需 float。性能收益依赖硬件、编译器和图形 API，不能规定移动端禁止 float。[HLSL 数据类型](https://docs.unity3d.com/2022.3/Documentation/Manual/SL-DataTypesAndPrecision.html)
- 变体数通常按关键字集合状态组合增长，并非每个关键字恒为两倍。区分 local/global、阶段、静态变体和动态分支。材质构建时固定选项可考虑 shader_feature；运行时切换需确保所需变体保留，可根据情况用 multi_compile、显式保留或动态分支，不一律替换 multi_compile。[条件与变体](https://docs.unity3d.com/2022.3/Documentation/Manual/shader-conditionals.html)
- SRP Batcher、GPU Instancing、MaterialPropertyBlock 等存在使用条件和取舍，按实际渲染路径选择。CPU 合批改善不能直接证明 GPU 填充开销降低。

## 粒子、后处理与 Render Pass

粒子数量不是 VFX Graph 与 Shuriken 的唯一分界。核对包版本、Compute Shader、图形 API、目标设备、XR 支持与模拟/渲染成本；具备 Compute 支持也不代表全部 VFX 特性可用。[VFX 14 兼容要求](https://docs.unity3d.com/Packages/com.unity.visualeffectgraph@14.0/manual/System-Requirements.html)

为粒子设置容量、生成速率、生命周期和停止/退出回收策略。不要把海量或频繁同步 GPU→CPU 回读放进逐帧热路径；必要异步回读需测量。

URP 的 RendererFeature/ScriptableRenderPass 与 HDRP CustomPass 不能直接互换。读取本项目安装版本的 API，Unity 2022/URP 14 与新版本 RenderGraph 示例不要混用。检查注入点、深度/颜色输入、中间 RT、释放、相机筛选与 XR；Volume 管理是否需要取决于当前后处理实现，而非所有 Pass 的硬性基类。

## 验证顺序

1. 导入/编译和错误日志；关键字、Pass、属性、贴图绑定与管线兼容。
2. 实际材质在目标场景、透明叠加、深度、阴影及不同视角的表现。
3. 同一场景与分辨率下记录修改前后 CPU/GPU、Overdraw/带宽或批次变化；标明采样条件。
4. 根据任务检查目标设备、图形 API、XR 与质量档位。包兼容、Shader 编译、视觉正确和性能合格分别报告。

这些链接核对了关键概念；API 实现仍以目标工程实际版本和对应官方文档为准，不自动升级包来迎合示例。
