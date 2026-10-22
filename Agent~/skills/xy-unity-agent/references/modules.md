# 点位与模板

历史工程常见 `Assets/Modules/<Ident>/Main/Scripts`、`Main/Res/Pfbs`、模块根部 `ModuleConfigAsset.asset` 和项目级 `Assets/Modules/data.json`。这些是定位线索，不是要求每个 Unity 项目照此建目录。

## 创建或迁移

1. 找一个当前项目中能正常加载、相似交互的模块，对照入口、PoiMgr、本地 Template、主 Prefab 和注册配置。先检查模块创建工具是否已有能力，不另造不兼容生成器。
2. 核对目录、Ident、namespace、asmdef、入口类、主 Prefab、资源包名、PoiCode 与注册表的映射。模块目录名不一定等于入口类；不要把 `FY5076`、`XYFY5076` 和旧点位码简单全局替换。
3. 检查实际加载器调用 `OnEnter/OnExit` 还是异步入口。历史中出现过“正式入口调用异步方法，Editor 测试入口只调用同步方法”的差异；从当前调用链确认，不能只看重写方法存在。
4. 确认资源实际来自 AssetDatabase、本地 DLL、AssetBundle 还是设备缓存。源码编译成功不意味着运行端 DLL 已替换。没有授权时不上传、发布或提交。
5. Framework 和模块本地 Template 同名文件可能有业务分叉。按相对路径和内容比较后逐处迁移；`Util.Node.FindNode` 与 `Util.FindNode`、Task/UniTask 封装等必须匹配本模块版本。

## 序列化引用

- 移动现有资源要保留 `.meta`；同工程创建新副本要生成新 GUID 并正确重映射内部引用。不要原样复制全部 `.meta` 或批量删掉元文件后希望引用自动修复。
- 源码字段或层级调整后检查 Prefab 中绑定对象；原始 YAML 同名字符串不是引用有效性的充分证据。
- `Root/PB-zh/...`、`NLQ`、`Button_Ray` 等历史名称不能当作全项目固定结构。节点找不到时读取实际 Prefab 层级，确定初始化时机和父子级。
- 不自动把用户当前按钮体系替换成相似 SDK 组件。组件创建方式、日志方式遵守当前工程约定，不强制套用历史技能中互相矛盾的 AddComponent 禁令。

完成时给出受影响的模块标识、脚本、Prefab/资源、配置清单，以及是否需要重新生成模块 DLL 或资源包。只声明已实际执行的构建。
