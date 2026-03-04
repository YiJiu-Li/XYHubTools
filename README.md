# XY Hub Tools

Unity 编辑器工具集合。

## 功能

- **材质透明度动画创建器** - 快速创建材质透明度/溶解动画
- **模型材质导入器** - 批量导入和管理模型材质
- **GPU 噪声生成器** - GPU 加速的噪声纹理生成
- **Asset GUID 重生成器** - 重新生成资源 GUID
- **Copy App** - 资源复制工具

## 安装

### 通过 Git URL 安装 (推荐)

1. 打开 Unity Package Manager (`Window > Package Manager`)
2. 点击左上角 `+` 按钮
3. 选择 `Add package from git URL...`
4. 输入: `https://github.com/你的用户名/XYHubTools.git`

### 通过 manifest.json 安装

在 `Packages/manifest.json` 的 `dependencies` 中添加:

```json
"com.yzj.xyhubtools": "https://github.com/你的用户名/XYHubTools.git"
```

## 使用

在 Unity 菜单栏选择 `XY Tools > XY Editor Hub` 打开工具窗口。

## 要求

- Unity 2021.3 或更高版本

## 许可证

MIT License
