# Pipeline 集成与升级

XY 的 MCP 层通过官方 HTTP 接口发现和执行命令，不复制命令实现或固定命令清单。
官方新增命令可直接通过实时发现使用。编辑器中的中文说明不影响模型收到的官方 Schema。

## 两条安装路线

- Unity 6 及以上：首次缺少 Pipeline 时，从 UPM 安装 `bundle.json` 中固定的
  `upstreamVersion`，保留官方包内容及版本声明。
- Unity 2022.3：安装随附的 `pipeline.tgz` 兼容发行版。当前为 `0.8.0-xy.2`，
  基于官方 `0.8.0-exp.1`，已验证 Unity `2022.3.53f1`。
- 已有安装：启动时不替换、不升级。状态显示安装来源和最低 Unity 版本提示。
- 嵌入式源码包 `Packages/com.unity.pipeline` 优先于 UPM 引用。升级入口拒绝覆盖它；
  必须先备份并迁出源码，再选择安装路线。当前开发项目中的嵌入式包不受影响。

不要仅通过降低官方包的 Unity 版本字段宣称兼容。2022 分支包含实际 API 适配：
PhysicsMaterial 别名、材质队列读取、控制台访问、分析 API 占位及插件导入格式。
其中旧版材质读取无法区分继承的渲染队列 `-1`，这是兼容分支的已知差异。

## 显式升级与回退

官方路线可通过 Unity 编辑器 C# 调用：

```csharp
XY.PipelineBootstrap.PipelineInstaller.InstallOfficialVersion("0.8.0-exp.1");
```

必须传完整版本，拒绝 `latest` 或范围；至少需要 Unity 6。具体新版本的更高 Unity
要求须根据官方发行信息检查，并在隔离项目验证。不会根据版本号推断协议兼容。

兼容路线使用菜单 `Tools > XY Hub > Pipeline > 升级到随附兼容版本`，或调用
`PipelineInstaller.UpgradeBundledVersion()`。安装前核验包 SHA-256 和最低 Unity 版本。

切换已有 UPM 引用前，保存旧引用、manifest 和 packages-lock 到
`ProjectSettings/XYBridgePipelineRollback/`。菜单“回退上次包切换”或
`PipelineInstaller.RollbackVersion()` 仅恢复 Pipeline 旧引用，由 UPM 重新解析依赖，
不会用整个旧 manifest 覆盖其他包的后续修改。若当前 Pipeline 引用已被其他操作改变，
回退会停止，备份供人工处理。回退需要旧包源仍可访问；本地 tgz 必须保留。

## 重建兼容发行版

`compatibility.json` 固定官方版本、官方原包 SHA-256、发行版本、补丁修订号和已验证
Unity 版本。`patches/unity-2022.3.patch` 保存最小兼容差异，不包含 Unity 自动写入
普通脚本 `.meta` 的默认导入字段。

从 profile 的 `upstreamUrl` 下载原包后，在开发工程根目录运行：

```powershell
python Assets/XYHubTools/MCP~/prepare_pipeline.py --archive <官方原包.tgz> --output <全新临时目录>
python Assets/XYHubTools/MCP~/bundle_pipeline.py --source <全新临时目录>
```

第一步核验原包、检查路径、验证并应用补丁，输出独立源码及来源记录，不修改已安装
源码。需要 Git 和 Python 3.9+。第二步生成确定性的 tgz 和 bundle.json，记录源码树、
原包及补丁校验值。修改版不携带失效的官方签名；原始许可条款仍适用。

升级基线时，先更新 profile 中的官方版本和校验值，审查补丁能否删除或调整，再提高
XY 发行版本。只列入真正通过验证的 Unity 版本。官方原包路线不经过修改和重新打包。

## 升级验收

```powershell
python Assets/XYHubTools/MCP~/test_pipeline_http.py
python Assets/XYHubTools/MCP~/test_pipeline_distribution.py
python Assets/XYHubTools/MCP~/verify_pipeline.py --project <已打开的隔离Unity工程绝对路径> --report <报告.json>
```

验证器检查分类索引、筛选分页、单命令 Schema、结构化执行、错误传播和后台任务结果。
只调用只读命令，不修改场景、资源或包。非零退出码表示验证未通过。Unity 6 官方路线
还需在相应 Unity 版本单独运行，不能仅因能够安装就认为兼容。

## 模型调用约定

- `unity_pipeline_commands {}`：分类索引。
- `unity_pipeline_commands {"tag":"assets","limit":20}`：精简列表，用 `nextOffset` 翻页。
- `unity_pipeline_commands {"command":"create_asset"}`：完整官方参数规范。
- `unity_pipeline {"command":"editor_status","job":true}`：提交后台任务。
- `unity_pipeline_job {"job": <完整xyJob对象>}`：查询任务；加 `"action":"cancel"` 请求协作式取消，再查询终态。

工程路径验证后保持绑定，HTTP 不再每次依赖命名管道。`unity_use_project` 支持绝对路径
直接绑定官方 Pipeline；切换时清除旧路径。令牌和端口每次重新读取，不缓存旧凭据。
任务句柄包含工程与服务启动时间，跨工程或服务重启时拒绝误用，不自动重放修改命令。

后台任务完成仅表示命令返回。build 等自身异步命令仍需轮询对应状态命令。
官方任务存在内存中，不能跨域重载；超时、任务过期或重载后必须核实实际效果。

更新 MCP 脚本后需重启客户端的 xybridge MCP 连接以加载新工具定义。
`BridgeSetup.CopyPythonServerOnly()` 只更新脚本及工程根目录兼容副本，不改客户端配置。
