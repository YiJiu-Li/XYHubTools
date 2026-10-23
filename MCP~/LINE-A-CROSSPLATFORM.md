# 线 A：Bridge 跨平台化（TCP 通道 + 去掉 pywin32）

> 目标：让 Unity 侧和 MCP 侧都不再依赖 Windows 专有机制，一次改动覆盖
> Windows / macOS / Linux、远程调试与容器化环境。做完即冻结，不再往这一层投人力。
>
> 状态：**代码完成，Windows 已实测通过。** 2026-10-23 决策：无 mac 用户、不做对外分发，
> macOS / Linux 验收与分发工作**关闭**（理由与将来要做的清单见第 4 节）。

---

## 1. 结论先说

| 项 | 状态 | 说明 |
|---|---|---|
| Named Pipe → 本机 TCP | ✅ 完成 | `PipeServer.cs` 删除，新增 `BridgeServer.cs`（606 行） |
| 去掉 pywin32 | ✅ 完成 | Python 侧只用 `socket` / `threading` / `json` 等标准库 |
| 多实例发现 | ✅ 完成 | `~/.xybridge/instances/*.json`，连通性即存活判据 |
| 令牌认证 | ✅ 完成 | 每次 Editor 启动/域重载重新生成，逐条校验 |
| Python 解释器跨平台 | ✅ 完成 | 探测 `python` / `python3` 并真实执行验证是 Python 3 |
| 可靠性测试 | ✅ 完成 | 重写为真实 TCP 回环 + 假 Unity 服务端，23 项通过 |
| Windows 端到端 | ✅ 已实测 | Unity 2022.3.53f1 编译通过，`ping` / `status` / 场景读取 / 令牌拒绝全部实测 |
| macOS / Linux 实机 | ⏸ 已决策不验收 | 2026-10-23：无 mac 用户，验收没有服务对象。代码无平台分支（无 `DllImport`、无 Windows 专有 API、`File.Replace` 已加 Unix 兜底），保持现状；真出现 mac 用户再验 |
| 一键安装（免改配置/免重启） | ⬜ 不做 | 内部使用，git URL + 一次配置即可，收益不足 |
| 干掉 Python 运行时 | ⬜ 不做 | 当前只做到「零第三方依赖」，自用场景够 |

---

## 2. 具体改动点

### 2.1 Unity 侧（C#）

| 文件 | 改动 |
|---|---|
| `Editor/Apps/Bridge/PipeServer.cs` | **删除**（445 行） |
| `Editor/Apps/Bridge/BridgeServer.cs` | **新增**。`TcpListener` 绑 `IPAddress.Loopback:0` 由系统分配端口；accept 循环与连接处理分离（探测连接不得阻塞主连接）；每条连接一个 `StreamWriter`，写操作走 `WriteLock` 串行化；`Stop()` 先删实例文件再断连接 |
| `Editor/Apps/Bridge/Types.cs` | `PipeEnvelope` → `BridgeEnvelope`，新增 `token` 字段；信封其余字段不变 |
| `Editor/Apps/Bridge/YZJBridge.cs` | 门面改名：`IsPipeServerRunning` → `IsServerRunning`，`PipeDisplayName` → `Endpoint` / `InstanceFile`；新增 `update` 兜底启动（`delayCall` 在失焦节流时不触发） |
| `Editor/Apps/Bridge/MessageDispatcher.cs` | 类型重命名；`ping` / `status` 回传 `processId` + `projectPath` |
| `Editor/Apps/Bridge/BridgeSetup.cs` | 新增 `ResolvePythonCommand()`：按平台候选顺序探测并 `-c "import sys;print(...)"` 验证；配置里的 `command` 由硬编码 `python` 改为解析结果。`InstallScript()` 的原子替换加兜底：`File.Replace` 在 Mono/Unix 上不支持时退回「备份 + 覆盖」 |
| `Editor/Apps/Bridge/BridgeServer.cs` | 实例文件写入的异常捕获从 `IOException` 扩到权限/不支持类：否则写失败会被静默当成「没有实例」，表现是 Unity 开着但 MCP 报未发现 |
| `Editor/Apps/Bridge/BridgeStatusTool.cs` | 面板显示「监听地址」「实例文件」；安装提示去掉 `pip install pywin32` |
| `Editor/Apps/Bridge/BridgeSelfTest.cs` | 自检项改为报告实际 endpoint |

**没有改的**：`CodeExecutionService`、`SnippetCompiler`、各 `*Service`（场景/组件/ShaderGraph/物理/Profiler…）
一律不动 —— 传输层与业务层解耦，这是这次改造能只碰 7 个文件的原因。

### 2.2 MCP 侧（Python）

| 位置 | 改动 |
|---|---|
| `UnityPipe` → `UnitySocket` | `socket.create_connection` + `TCP_NODELAY`；自建行缓冲读取（`_read_line`）；`threading.Lock` 串行化请求 |
| 实例发现 | `list_instances()` 扫 `INSTANCE_DIR`，`schema != 2` 或 `transport != "tcp"` 直接跳过；`_is_alive()` 用 0.25s 连接探测；`_cleanup_stale()` 只清 7 天以上的残留 |
| `resolve_target()` | 优先级：`--project=` / `XYBRIDGE_PROJECT` → 脚本位于工程根（旧用法兼容）→ 唯一存活实例 → 报错并列出候选 |
| 重连 | `connect()` 每次重读实例文件；`call()` 前比对实例文件 mtime，变了就先断开 —— 域重载换端口/令牌后，**第一次调用就能连上新实例**，不再必然废掉一次 |
| `main()` | **修复**：残留调用已删除的 `resolve_pipe()` / `list_live_pipes()`，启动即 `NameError`；改为 `resolve_target()` / `list_instances()`，且启动时只报告不连接 |

### 2.3 协议（NDJSON，一行一条信封）

```json
// 请求
{"id":"7","type":"set_property","message":"Cube|m_Name|NewName","token":"<24字节hex>"}
// 响应
{"type":"response","reply_to":"7","ok":true,"message":"...","processId":36844,
 "projectPath":"E:\\...","token":""}
```

不变量（测试已固化）：

1. 连接建立后服务端主动推一条 `unity_connected`，**不含 token 与工程路径**（此时未认证）。
2. 令牌不匹配 → 立即回 `ok:false` + 中文原因，客户端应重读实例文件后重连，而不是重试。
3. 写入失败 / 超时 / 对端断开 → **一律不自动重放**，错误文案显式写明「执行结果未知」。
4. 一条 MCP 进程复用一条连接，用 `id` / `reply_to` 多路复用；服务端必须同时接受多条连接。

---

## 3. 为什么是 TCP 而不是 localhost HTTP

`com.unity.pipeline` 走 HTTP 是因为它要暴露一个**命令目录**给任意客户端；Bridge 的形态是
**长连接 + 一问一答 + 服务端主动推送**（`execute_code` 的进度、事件订阅），HTTP 要么退回轮询，
要么再叠一层 SSE/WebSocket。选 NDJSON over TCP 的代价是协议自持，收益是：

- 零依赖：Python 侧不需要 requests，Unity 侧不需要 HttpListener（后者在 Linux 上还要额外前缀配置）
- 天然支持服务端推送，`EventBus` / 执行进度不用改
- 与 Pipeline 的 HTTP 通道并存互不影响（两者端口、令牌、生命周期完全独立）

---

## 4. 剩余待办：已关闭

2026-10-23 决策：**不做对外分发，无 macOS 用户**。以下三项全部关闭，理由记录在此，
避免以后有人以为是漏做：

| 项 | 决定 | 理由 |
|---|---|---|
| macOS / Linux 实机验收 | ⏸ 押后 | 验收没有服务对象。代码本身无平台分支，`File.Replace` / `File.Move` 已加 Unix 兜底。**真出现 mac 用户再验**，成本不会更高 |
| 一键安装（免改配置 / 免重启） | ⬜ 不做 | 自用 + 团队用，配置写一次就够；收益抵不上改造成本 |
| 干掉 Python 运行时 | ⬜ 不做 | 已做到「零第三方依赖」，自用场景不需要单文件二进制 |

如果将来要做，具体清单仍保留在这里（按原优先级）：

1. `~/.xybridge/` 路径在 macOS 上由 `Environment.SpecialFolder.UserProfile` 解析，确认不是空值
2. 实例文件权限：默认 644，令牌在文件里 —— 确认同机其他用户读不到（必要时设 600）
3. `python3` 探测：macOS 系统自带 3.9（Command Line Tools），确认 `ResolvePythonCommand()` 命中
4. 中文路径 / 含空格路径：实例文件名用 SHA256 指纹，理论上安全，实测确认
5. Linux 容器内 `-batchmode`：确认 Bridge 在无 GUI 的 batchmode 下不崩（`Start` 挂在 `update` 上）
6. 分发（samples / OpenUPM / 双语 README）：`"samples": []` 保持空数组，不为「看起来完整」而填

---

## 5. 验收清单（可直接照做）

**Windows（已通过，2026-10-23）**

| 检查 | 命令 / 操作 | 结果 |
|---|---|---|
| 编译 | Unity 2022.3.53f1 触发重编译 | ✅ 无 `error CS`（删除 .cs 后必须先 `AssetDatabase.Refresh()`，否则报 `CS2001`） |
| 监听 | `ls ~/.xybridge/instances/` | ✅ `xy-650ce9bc797d-36844.json`，`transport: tcp` |
| 握手 | `unity_ping` | ✅ `pong \| PID: 36844 \| 工程: XY_Editor` |
| 令牌 | 用错误 token 直连端口 | ✅ `认证失败：令牌不匹配` |
| 读场景 / 组件 | `unity_get_scene_roots` / `unity_get_component` | ✅ 正常返回 |
| 写入 | `unity_execute_code` 建→改名→删临时对象 | ✅ 0.1s，场景无残留 |
| 批量 | `unity_batch` 预检 + 执行 + 读回断言 | ✅ dry-run 不执行、执行后 `verified:true` |
| 残留实例 | 手写一个指向死端口的实例文件 | ✅ 不计入存活（仍报「发现 1 个」） |
| 域重载重连 | 触发重编译，观察端口/令牌轮换 | ✅ 60635→60058，**重载后首次 `ping` 0.0s 成功** |
| 单元测试 | `python -m unittest discover -s Assets/XYHubTools/MCP~` | ✅ 50/50 |

**macOS / Linux（待跑）**：照上表逐项，另加「域重载后自动重连」「容器内 batchmode」。

---

## 6. 回滚

- 脚本：`~/.xybridge/xy_mcp_server.py.previous`、工程根 `xy_mcp_server.py.previous`（安装器自动保留）
- 代码：`git checkout -- Editor/Apps/Bridge` 回到 1.8.0 的 `PipeServer` 实现
- 协议：实例文件带 `schema` 字段，旧版本脚本读到 `schema != 2` 会跳过而不是误连

---

## 7. 与线 B / C 的衔接

线 A 冻结后，Bridge 只承担「把请求送到 Unity 主线程并拿回结构化结果」。线 B 的体检器
（`unity_audit_module`）可以直接作为新的 `type` 复用这条通道，无需再动传输层：

```
type: "audit_module"
→ { ruleId, severity, evidence, expected, actual }   // 每条规则一条结果
```

规则包（JSON/YAML）放 `Agent~/rules/<sdk>/`，C# 侧只做「取场景事实」，判断逻辑留在规则里 ——
这样新增规则不需要重编译 Unity 程序集，也不受版本兼容牵连。
