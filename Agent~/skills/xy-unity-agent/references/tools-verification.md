# XY MCP 和可验证编辑

根据当前工具清单发现能力，不硬编码工具总数。没有 XY MCP 时仍可分析工程文件；需要编辑器或设备状态而无法取得时，说明限制。

## 工具选择

- 轻量查询：工程/状态、选中对象、场景根、资源搜索、组件、Prefab 结构和 Console。
- Pipeline：先用 `unity_pipeline_commands` 按分类或命令查询实时 schema，再用结构化 `unity_pipeline` 调用，保留数字与布尔类型。异步命令按各自 status 查询，后台 job 返回不等于实际构建完成。
- 专项工具：Shader Graph、Physics、Profiler、Asset Preview、Frame Debugger，先查询状态；不能因工具已注册就宣称专项操作全部已验证。
- 截图优先 `unity_screenshot_to_file`，取回文件后实际查看；不要拉取巨量 base64。
- `unity_execute_code` 用于确实需要的 API 操作。编辑资产时使用适当 Undo/SerializedObject/Prefab 保存机制，不用任意代码绕过当前任务范围。

## 批量编辑

当前 Bridge 若提供 `unity_batch`，其默认 `dry_run=true` 只检查参数结构，不验证对象和属性存在。

1. 先读目标状态，消除同名对象歧义。确认场景实例与 Prefab 资产的目标区别。
2. 提交整批步骤预检，之后在已授权修改范围内执行 `dry_run=false`。
3. 使用 `unity_get_component` 等读回，并对 JSON 路径做 `expect` 断言；尽量验证业务相关属性，而不只验证请求返回。
4. 首个失败或断言不符后检查已执行结果。该工具顺序执行，不承诺事务、自动回滚或统一 Undo 组。

示例结构（具体对象与属性需从当前工程读取）：

```json
{
  "dry_run": false,
  "steps": [
    {"tool": "unity_set_property", "arguments": {"spec": "Target|Light.m_Enabled|false"}},
    {"tool": "unity_get_component", "arguments": {"target": "Target|UnityEngine.Light"},
     "expect": {"path": "components.0.properties.m_Enabled", "equals": false}}
  ]
}
```

Pipeline 另有 `batch` 命令，可能支持事务 Undo 与结果引用；先查实时 schema 和允许命令，不混淆两种 batch 的保证。

## 完成标准

同时检查 MCP `isError` 与返回体错误。传输失败、超时或域重载期间执行状态可能未知，先读回再决定重试，不盲目重放写操作。

脚本修改检查 Unity 编译；Prefab/组件修改检查引用和属性读回；资源改名检查 GUID 与依赖；流程修改检查重入和退出。标明哪些已实际测试，哪些只是静态分析，以及真机未测的部分。
