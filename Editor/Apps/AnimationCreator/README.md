# 动画模板工具

为选中 GameObject 的 Renderer 材质属性生成 **出现(chuxian) / 消失(xiaoshi)** 动画组，
支持 **轴向溶解(Axis)、整体溶解(OverRall)、透明度(Transprance)** 三种模式。

所有动画参数均通过 **ScriptableObject 模板** 配置，面板本身只负责选择模板、设置保存路径和创建动画。

---

## 使用步骤

1. **准备模板** — 在面板中快捷创建，或右键 → Create → **依旧/动画模板工具模板**
2. **编辑模板** — 选中模板资产，在 Inspector 中调整所有参数（帧率、Shader 属性名、出现/消失配置等）
3. **选中对象** — 在 Hierarchy 中选中带 `Renderer` 组件的 GameObject
4. **打开面板** — 在 Hub 左侧选择 **动画模板工具**
5. **指定模板** — 将模板拖入面板顶部的 **当前模板** 字段
6. **创建动画** — 点击底部按钮一键生成

---

## 动画模板

模板是 `MaterialTransparencyAnimationTemplate` (ScriptableObject)，存储全部动画参数。

### 模板包含的配置

| 分类 | 内容 |
| --- | --- |
| **基础** | 模板名称、帧率 |
| **Shader 属性名** | `_Transparency`、`_OverRall`、`IsDissolve`、`_DissolveThreshold`、`_AXIS`、`_NoiseScale`、`_NoiseIntensity` |
| **出现动画** | 时长、透明度/整体溶解/轴向溶解的起止值与曲线、噪声参数 |
| **消失动画** | 同上，独立配置 |

### 创建模板

- **面板快捷创建** — 左出现/右消失、上出现/下消失、简单淡入淡出
- **右键菜单** — Create → **依旧/动画模板工具模板**

### 编辑模板

选中模板后在 **Inspector** 中直接编辑所有参数，也可在面板中点击 **在 Inspector 中编辑模板** 按钮快捷跳转。

---

## 面板功能

面板保持精简，只包含以下区域：

| 区域 | 说明 |
| --- | --- |
| **动画模板** | 选择模板、查看摘要信息、快捷创建预设模板 |
| **保存路径** | 设置动画文件输出目录 |
| **材质设置** | 动画所有材质 / 指定材质索引、包含子物体 |
| **创建按钮** | 一键创建完整动画组，或单独创建某个动画 |

---

## 完整动画组

点击 **创建完整动画组** 后，会生成：

```
Assets/Animations/
├── {对象名}_chuxian_Axis.anim          ← 出现 · 轴向溶解
├── {对象名}_chuxian_OverRall.anim      ← 出现 · 整体溶解
├── {对象名}_chuxian_Transprance.anim   ← 出现 · 透明度
├── {对象名}_xiaoshi_Axis.anim          ← 消失 · 轴向溶解
├── {对象名}_xiaoshi_OverRall.anim      ← 消失 · 整体溶解
├── {对象名}_xiaoshi_Transprance.anim   ← 消失 · 透明度
└── {对象名}_TransparencyController.controller
```

Controller 中包含 6 个 Trigger 参数：

- `chuxian_Axis` / `chuxian_OverRall` / `chuxian_Transprance`
- `xiaoshi_Axis` / `xiaoshi_OverRall` / `xiaoshi_Transprance`

---

## 各动画类型写入的 Shader 属性

| 类型 | IsDissolve | _AXIS | _DissolveThreshold | _NoiseScale | _NoiseIntensity | _OverRall | _Transparency |
| --- | --- | --- | --- | --- | --- | --- | --- |
| **Axis** | 1 (常量) | 方向值 (常量) | 动画 from→to | 常量 | 常量 | — | — |
| **OverRall** | 0 (常量) | — | — | — | — | 动画 from→to | — |
| **Transprance** | 0 (常量) | — | — | — | — | — | 动画 from→to |

---

## 注意事项

- 轴向溶解需要 Shader 支持 `IsDissolve`、`_AXIS`、`_DissolveThreshold` 属性
- 生成的 Clip 路径若已存在同名文件，会自动追加数字后缀避免覆盖
- 模板文件 (.asset) 可在团队之间共享，确保动画参数一致
