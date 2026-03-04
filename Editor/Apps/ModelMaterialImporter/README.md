# 模型材质导入

将 FBX 等模型导入 Unity 后，**一次右键操作**完成材质提取、Shader 替换、贴图自动匹配全流程。

---

## 目录结构约定

```
ModelFolder/
├── Model.fbx
├── Materials/      ← 材质提取输出目录（自动创建）
└── Texture/        ← 贴图搜索目录
    ├── Mesh_m_BaseColor.png
    ├── Mesh_m_Normal.png
    └── Mesh_m_ARM.png
```

---

## 贴图命名规则

材质名格式：`基础名_m_BaseColor`（如 `MoNiDian_DiZuo_m_BaseColor`）

工具会提取出基础名 `MoNiDian_DiZuo`，然后在 `Texture/` 目录中按模板配置的后缀列表查找：

| 后缀 | 对应贴图槽 |
| --- | --- |
| `m_BaseColor` / `BaseColor` | Albedo / Base Color |
| `m_Normal` / `Normal` | 法线贴图 |
| `m_ARM` / `ARM` | AO + Roughness + Metallic |

---

## Shader 模板（ShaderMaterialTemplate）

`ShaderMaterialTemplate` 是一个 ScriptableObject 资产，用于配置：

- **目标 Shader** — 材质替换后使用的 Shader
- **贴图属性映射** — Shader 属性名 → 贴图后缀列表
- **Float / Int / Color 默认参数** — 替换后自动写入的材质属性值
- **启用 / 禁用 Keyword** — 自动设置的 Shader Keyword

### 内置模板

| 按钮 | 说明 |
| --- | --- |
| **创建 URP/Lit 模板** | 适用于 URP Lit Shader |
| **创建 PBR_SG 模板** | 适用于自定义 PBR_SG Shader |

---

## 使用步骤

### 第一步：准备目录结构

按如下结构组织模型文件夹，工具会自动识别：

```
ModelFolder/
├── Model.fbx
├── Materials/      ← 材质提取输出目录（自动创建）
└── Texture/        ← 贴图搜索目录
    ├── Mesh_m_BaseColor.png
    ├── Mesh_m_Normal.png
    └── Mesh_m_ARM.png
```

### 第二步：配置 Shader 模板

1. 在 Hub 中选择 **模型材质导入**
2. 点击 **创建 URP/Lit 模板** 或 **创建 PBR_SG 模板** 生成模板资产
3. 在弹出的保存对话框中选择保存位置
4. 点击 **编辑模板** 在 Inspector 中配置贴图映射和默认参数

### 第三步：处理模型

1. 在 Hub 顶部的模板选择框中选中刚才创建的模板
2. 在 **Project 窗口**中右键点击 `.fbx` 模型文件
3. 选择 `XZ工具 → 处理模型材质`
4. 工具自动完成：提取材质 → 替换 Shader → 匹配贴图 → 保存

---

## 注意事项

- 如果 `Texture/` 目录不存在，工具自动回退到 `Textures/` 目录搜索
- 法线贴图会自动将 TextureImporter 设置为 `NormalMap` 类型并重新导入
- 目标 `Materials/` 下已有同名 `.mat` 时跳过提取，不覆盖已有材质
- 可在 Console 观察 `[ModelMaterialImporter]` 前缀的日志确认每步执行结果
