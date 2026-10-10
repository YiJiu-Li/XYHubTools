"""
YZJBridge MCP Server
AI 通过 MCP 协议操控 Unity Editor
只需: pip install pywin32

Hub 模式 —— 一个 server 服务所有 Unity 工程
────────────────────────────────────────────────────────
管道名 = \\\\.\\pipe\\xybridge_unity_<工程路径 sanitized>，每个工程一条。
本脚本不再把「自己所在目录」当作唯一目标，而是自动发现所有存活的管道：

  1. --project=<关键字> 命令行参数 / XYBRIDGE_PROJECT 环境变量（显式指定）
  2. 脚本同目录推导出的管道（向后兼容旧的「一工程一脚本」用法）
  3. 唯一存活的管道
  4. 以上都不成立 → 报错并列出候选，用 unity_use_project 切换

因此脚本可以放在任意固定位置（推荐 ~/.xybridge/），
MCP 配置只需写一次，之后导入任何 Unity 工程都不用再改。
"""

import json, os, sys, time, threading, math
import urllib.request, urllib.error
import win32file, win32pipe, pywintypes

ROOT = os.path.dirname(os.path.abspath(__file__))
PIPE_PREFIX = "xybridge_unity_"
# 注意：不能用 raw string —— r"\\.\pipe\\" 里的 \\ 是两个字面反斜杠，
# 且 raw string 不能以单个反斜杠结尾。这里用普通字符串显式转义。
PIPE_DIR = "\\\\.\\pipe\\"

# 显式指定的目标（--project= / XYBRIDGE_PROJECT）
_PROJECT_ARG = None


def _sanitize(path: str) -> str:
    return (
        path.replace("\\", "_").replace("/", "_").replace(":", "_").replace(" ", "_")
    )


# 脚本同目录推导出的管道：旧的「一工程一脚本」模式下与 Unity 端算出来的一致
PIPE_SELF = PIPE_DIR + PIPE_PREFIX + _sanitize(ROOT)

# 运行时实际使用的管道保存在 UP.pipe（见 UnityPipe）——它取决于运行时发现结果，
# 因此不再设模块级 PIPE 常量。


def list_live_pipes(retries: int = 4):
    """枚举当前存活的 xybridge 管道（返回完整路径列表，已排序）。

    枚举 \\\\.\\pipe\\ 时可能撞上瞬时抖动：listdir 抛 OSError，或管道名
    在「上一实例 dispose → 新实例创建」的瞬间不可见。这类抖动是毫秒级的，
    因此拿到空结果时短暂重试，避免把瞬时状态误判成「没有实例」。
    """
    for attempt in range(retries):
        try:
            names = os.listdir(PIPE_DIR)
        except OSError:
            names = []
        hits = sorted(PIPE_DIR + n for n in names if n.startswith(PIPE_PREFIX))
        if hits:
            return hits
        if attempt < retries - 1:
            time.sleep(0.05)
    return []


def _requested_project():
    return _PROJECT_ARG or os.environ.get("XYBRIDGE_PROJECT") or None


def _match_pipe(keyword: str, candidates):
    """按关键字匹配管道名（大小写不敏感）。返回 (pipe, err)。"""
    k = _sanitize((keyword or "").strip()).lower()
    hits = [p for p in candidates if k and k in p.lower()]
    if len(hits) == 1:
        return hits[0], None
    if not hits:
        return None, f"没有匹配「{keyword}」的 Unity 实例"
    return None, f"「{keyword}」匹配到 {len(hits)} 个实例，请写得更具体"


def _format_candidates(candidates):
    if not candidates:
        return "（当前没有任何存活的 Unity 实例）"
    body = "\n".join("  - " + p for p in candidates)
    return "候选实例：\n" + body + "\n（用 unity_list_projects 可看到每个实例的工程路径与 PID）"


def resolve_pipe():
    """解析目标管道。返回 (pipe, err)；pipe 为 None 时 err 说明原因。"""
    live = list_live_pipes()

    want = _requested_project()
    if want:
        p, err = _match_pipe(want, live)
        if err:
            return None, err + "\n" + _format_candidates(live)
        return p, None

    # 向后兼容：脚本同目录推导出的管道优先（旧的「一工程一脚本」用法照旧）
    if PIPE_SELF in live:
        return PIPE_SELF, None

    if len(live) == 1:
        return live[0], None

    if not live:
        return None, (
            "未发现任何存活的 Unity Bridge。\n"
            "请确认：① Unity 已打开目标工程；"
            "② XYHub > MCP Bridge 面板中「管道服务」正在运行。"
        )

    return None, (
        f"发现 {len(live)} 个 Unity 实例，无法自动确定目标。\n"
        "请用 unity_use_project 指定，或给 server 加 --project=<关键字>。\n"
        + _format_candidates(live)
    )



# ═══════════════ Named Pipe 客户端（自动重连） ═══════════════
class UnityPipe:
    def __init__(self, pipe=None, explicit=False):
        self.pipe = pipe          # 目标管道；None 表示交给 resolve_pipe() 决定
        self.explicit = explicit  # True = 显式指定，管道消失时不自动改绑
        self._h = None
        self._n = 0
        self._lock = threading.Lock()
        self._buf = b""
        self._last_error = None

    def set_pipe(self, pipe, explicit=True):
        """切换目标管道（会断开当前连接）。"""
        self.close()
        self.pipe = pipe
        self.explicit = explicit
        self._last_error = None

    def _read_raw(self, timeout_s: float = 5.0):
        if not self._h:
            return None
        result = [None]
        exc = [None]
        h = self._h
        def _do():
            try:
                _, data = win32file.ReadFile(h, 65536)
                result[0] = data
            except Exception as e:
                exc[0] = e
        t = threading.Thread(target=_do, daemon=True)
        t.start()
        t.join(timeout_s)
        if t.is_alive():
            return None, TimeoutError("timeout")
        return result[0], exc[0]

    def connect(self, timeout_ms: int = 5000):
        # 已绑定的管道若已消失（工程被关掉 / 域重载后重开），重新解析目标
        if self.pipe is not None and not self.explicit:
            if self.pipe not in list_live_pipes():
                self.pipe = None
        if self.pipe is None:
            # 解析失败可能是瞬时抖动，重试几次再判死
            p, err = None, None
            for _ in range(4):
                p, err = resolve_pipe()
                if p is not None:
                    break
                time.sleep(0.06)
            if p is None:
                self._last_error = err
                return False
            self.pipe = p
            self._last_error = None

        deadline = time.time() + max(timeout_ms, 1) / 1000.0
        while time.time() < deadline:
            try:
                wait_ms = max(1, int((deadline - time.time()) * 1000))
                win32pipe.WaitNamedPipe(self.pipe, min(wait_ms, 500))
                self._h = win32file.CreateFile(
                    self.pipe,
                    win32file.GENERIC_READ | win32file.GENERIC_WRITE,
                    0,
                    None,
                    win32file.OPEN_EXISTING,
                    0,
                    None,
                )
                self._buf = b""
                # 暖场 ping：确保 ReadFile 线程干净，避免冷启动首次 fail
                self._n += 1
                warm_id = str(self._n)
                warm_req = json.dumps({"id": warm_id, "type": "ping", "message": ""}) + "\n"
                win32file.WriteFile(self._h, warm_req.encode("utf-8"))
                warm_deadline = time.time() + 5.0
                while time.time() < warm_deadline:
                    data, err = self._read_raw(timeout_s=min(2.0, warm_deadline - time.time()))
                    if err is not None:
                        # 任何读错误（含超时）都关闭句柄，让残留读线程退出后重连
                        self.close()
                        break
                    if data:
                        self._buf += data
                        idx = self._buf.find(b"\n")
                        if idx >= 0:
                            line = self._buf[:idx].decode("utf-8", errors="ignore")
                            self._buf = self._buf[idx + 1:]
                            try:
                                r = json.loads(line)
                                # Unity 端响应带 reply_to 而不是 id
                                if r.get("id") == warm_id or r.get("reply_to") == warm_id:
                                    self._buf = b""
                                    return True
                            except json.JSONDecodeError:
                                pass
                if self._h:
                    self._buf = b""
                    return True
            except Exception:
                self.close()
                time.sleep(0.05)
        self._h = None
        return False

    def call(self, t: str, m: str = "", timeout_s: float = 30.0) -> dict:
        with self._lock:
            if not self._h and not self.connect():
                return {"ok": False, "error": self._last_error or "Unity 未连接"}

            self._n += 1
            req_id = str(self._n)
            req = json.dumps({"id": req_id, "type": t, "message": m}) + "\n"
            try:
                win32file.WriteFile(self._h, req.encode("utf-8"))
            except:
                self._h = None
                if not self.connect():
                    return {"ok": False, "error": "Unity 连接断开，重连失败"}
                try:
                    win32file.WriteFile(self._h, req.encode("utf-8"))
                except:
                    return {"ok": False, "error": "写入管道失败"}

            deadline = time.time() + timeout_s
            while time.time() < deadline:
                remaining = deadline - time.time()
                if remaining <= 0:
                    break
                # 先看缓冲区有没有完整行
                idx = self._buf.find(b"\n")
                if idx < 0:
                    data, err = self._read_raw(timeout_s=min(remaining, timeout_s))
                    if err is not None:
                        if isinstance(err, pywintypes.error) and err.winerror in (109, 233):
                            self.close()
                            return {"ok": False, "error": "管道断开"}
                        if isinstance(err, TimeoutError):
                            # 读超时会遗留一个仍阻塞在 ReadFile 的线程；必须关闭句柄
                            # 让它退出，否则下次读会与它争抢同一条管道
                            self.close()
                            return {"ok": False, "error": "响应超时"}
                    if data:
                        self._buf += data
                    idx = self._buf.find(b"\n")
                    if idx < 0:
                        continue
                line = self._buf[:idx].decode("utf-8", errors="ignore")
                self._buf = self._buf[idx + 1:]
                try:
                    r = json.loads(line)
                except json.JSONDecodeError:
                    continue
                if r.get("id") == req_id or r.get("reply_to") == req_id:
                    return r
            return {"ok": False, "error": "响应超时"}

    def close(self):
        if self._h:
            try:
                win32file.CloseHandle(self._h)
            except:
                pass
            self._h = None


UP = UnityPipe()


# Pipeline HTTP client. No Unity CLI executable or extra Python dependency required.
# Keep unity_cli as a command-line-compatible MCP alias; parsing belongs to Pipeline.
class PipelineToolResult(str):
    def __new__(cls, text, is_error=False):
        result = super().__new__(cls, text)
        result.is_error = is_error
        return result


class _NoRedirect(urllib.request.HTTPRedirectHandler):
    def redirect_request(self, req, fp, code, msg, headers, newurl):
        return None


_PIPELINE_HTTP = urllib.request.build_opener(urllib.request.ProxyHandler({}), _NoRedirect())


def bound_project_path():
    # Do not cache across unity_use_project or reconnects.
    r = UP.call("status", "", timeout_s=5.0)
    path = (r.get("projectPath") or "").strip() if isinstance(r, dict) else ""
    if path:
        return path
    # An explicit absolute project also permits HTTP use while its pipe is unavailable.
    want = _requested_project()
    if not UP.explicit and want and os.path.isdir(want):
        return os.path.abspath(want)
    raise ValueError("无法确定目标工程，请先用 unity_use_project 绑定 Unity 实例。")


def _pipeline_descriptor(project):
    path = os.path.join(project, "Library", "Pipeline", ".unity-pipeline-port")
    # Heartbeat writes are not atomic. Retry only discovery, never command execution.
    for attempt in range(3):
        try:
            with open(path, encoding="utf-8-sig") as f:
                descriptor = json.load(f)
            if not isinstance(descriptor, dict):
                raise ValueError("Pipeline 服务信息格式无效。")
            port = descriptor.get("port")
            token = descriptor.get("evalToken")
            if type(port) is not int or not 1 <= port <= 65535 or not isinstance(token, str) or not token:
                raise ValueError("Pipeline 服务信息不完整。")
            actual = os.path.normcase(os.path.realpath(descriptor.get("projectPath") or ""))
            if actual != os.path.normcase(os.path.realpath(project)):
                raise ValueError("Pipeline 服务信息与目标工程不一致。")
            return port, token
        except (OSError, ValueError):
            if attempt == 2:
                raise ValueError("Pipeline 尚未就绪：请检查 XY 的 Pipeline 安装状态，并等待 Unity 完成编译。") from None
            time.sleep(0.05)


def pipeline_request(endpoint, payload=None, timeout_s=60):
    timeout_s = float(timeout_s)
    if not math.isfinite(timeout_s) or not 1 <= timeout_s <= 600:
        raise ValueError("timeout 必须在 1 到 600 秒之间。")
    project = bound_project_path()
    port, token = _pipeline_descriptor(project)
    data = None if payload is None else json.dumps(payload, ensure_ascii=False).encode("utf-8")
    request = urllib.request.Request(
        "http://127.0.0.1:%d%s" % (port, endpoint), data=data,
        headers={"Authorization": "Bearer " + token, "Content-Type": "application/json"},
        method="GET" if data is None else "POST",
    )
    status = 200
    try:
        with _PIPELINE_HTTP.open(request, timeout=timeout_s) as response:
            raw = response.read().decode("utf-8")
    except urllib.error.HTTPError as e:
        status = e.code
        raw = e.read().decode("utf-8", errors="replace")
    except (urllib.error.URLError, OSError, TimeoutError):
        # An exec may already have run. Automatic replay could duplicate scene edits/builds.
        return False, {"error": "Pipeline 连接中断或超时；命令结果未知，未自动重放。请查询状态后再决定是否重试。"}
    try:
        result = json.loads(raw)
    except ValueError:
        return False, {"error": "Pipeline 返回了非 JSON 响应。", "httpStatus": status}
    if not isinstance(result, dict):
        return False, {"error": "Pipeline 响应格式无效。", "httpStatus": status}
    ok = 200 <= status < 300 and result.get("success") is not False and not result.get("error")
    return ok, result


def run_pipeline_tool(name, args):
    try:
        timeout = float(args.get("timeout", 60))
        if not math.isfinite(timeout) or not 1 <= timeout <= 600:
            raise ValueError("timeout 必须在 1 到 600 秒之间。")
        if name == "unity_pipeline_commands":
            if "command" in args and not isinstance(args["command"], str):
                raise ValueError("command 必须是字符串。")
            ok, result = pipeline_request("/api/commands", timeout_s=timeout)
            command = args.get("command")
            if ok and command:
                matches = [c for c in result.get("commands", []) if c.get("name") == command]
                if not matches:
                    ok, result = False, {"error": "未知 Pipeline 命令: " + command}
                else:
                    result = {"commands": matches}
        elif name == "unity_cli":
            spec = args.get("spec", "")
            if not isinstance(spec, str):
                raise ValueError("spec 必须是字符串。")
            payload = {"commandLine": spec, "timeout": int(timeout * 1000)} if spec.strip() else None
            ok, result = pipeline_request("/api/exec" if payload else "/api/commands", payload, timeout)
            if ok and payload is None:
                return PipelineToolResult(compact_command_list(json.dumps(result, ensure_ascii=False)))
        else:
            command = args.get("command")
            parameters = args.get("parameters", {})
            if not isinstance(command, str) or not command.strip() or not isinstance(parameters, dict):
                raise ValueError("command 必须是非空字符串，parameters 必须是对象。")
            ok, result = pipeline_request("/api/exec", {
                "command": command, "parameters": parameters, "timeout": int(timeout * 1000)
            }, timeout)
        return PipelineToolResult(json.dumps(result, ensure_ascii=False), not ok)
    except (ValueError, TypeError, OSError) as e:
        return PipelineToolResult(json.dumps({"error": str(e)}, ensure_ascii=False), True)


def compact_command_list(txt: str) -> str:
    """把 `unity command` 的完整 JSON 清单压成「name — description [tags]」逐行。

    160 条命令带完整参数 schema 的 JSON 有 2 万+ 字符，直接丢给模型既浪费又会被截断。
    """
    try:
        d = json.loads(txt)
        cmds = d.get("commands") or (d.get("data") or {}).get("commands") or []
    except Exception:
        return txt
    if not cmds:
        return txt

    lines = ["共 %d 个命令（官方 com.unity.pipeline）:" % len(cmds)]
    for c in cmds:
        desc = (c.get("description") or "").replace("\n", " ").strip()
        if len(desc) > 80:
            desc = desc[:80] + "…"
        tags = ",".join(c.get("tags") or [])
        lines.append(
            "  %-36s %s%s"
            % (c.get("name", "?"), desc, ("  [" + tags + "]") if tags else "")
        )
    lines.append("")
    lines.append(
        "用 unity_pipeline_commands 的 command 参数查看指定命令的完整参数表。"
    )
    return "\n".join(lines)


# ═══════════════ 多工程：列举 / 切换 ═══════════════
def probe_pipe(pipe, timeout_s: float = 3.0):
    """临时连一次目标管道取 status，用于展示工程路径与 PID。失败返回 None。"""
    tmp = UnityPipe(pipe, explicit=True)
    try:
        if not tmp.connect(timeout_ms=int(timeout_s * 1000)):
            return None
        r = tmp.call("status", "", timeout_s=timeout_s)
        if not r.get("ok"):
            return None
        path = (r.get("projectPath") or "").rstrip("\\/")
        return {
            "projectPath": path,
            "projectName": os.path.basename(path) if path else "",
            "processId": r.get("processId", ""),
        }
    except Exception:
        return None
    finally:
        tmp.close()


def cmd_list_projects() -> str:
    live = list_live_pipes()
    if not live:
        return (
            "未发现任何存活的 Unity Bridge。\n"
            "请确认 Unity 已打开目标工程，且 XYHub > MCP Bridge 面板中「管道服务」正在运行。"
        )
    bound = UP.pipe
    lines = [f"发现 {len(live)} 个 Unity 实例（当前绑定：{bound or '未绑定'}）", ""]
    for p in live:
        mark = "●" if p == bound else "○"
        info = probe_pipe(p)
        if info:
            lines.append(f"{mark} {info['projectName'] or '(未知工程)'}")
            lines.append(f"     path : {info['projectPath'] or '?'}")
            lines.append(f"     pid  : {info['processId']}")
        else:
            lines.append(f"{mark} {p[len(PIPE_DIR) + len(PIPE_PREFIX):]}")
            lines.append("     （探测失败：可能正在域重载或正忙）")
        lines.append(f"     pipe : {p}")
    lines.append("")
    lines.append('用 unity_use_project "<工程名或路径片段>" 切换目标。')
    return "\n".join(lines)


def cmd_use_project(keyword: str) -> str:
    keyword = (keyword or "").strip()
    if not keyword:
        return '请提供工程名或路径片段，例如 unity_use_project "XY_Editor"'
    live = list_live_pipes()
    if not live:
        return "未发现任何存活的 Unity Bridge。"
    p, err = _match_pipe(keyword, live)
    if err:
        return f"❌ {err}\n{_format_candidates(live)}"
    UP.set_pipe(p)
    info = probe_pipe(p)
    if info:
        return (
            f"✅ 已绑定到 {info['projectName'] or p}\n"
            f"   path : {info['projectPath'] or '?'}\n"
            f"   pid  : {info['processId']}"
        )
    return f"✅ 已绑定到 {p}"


# ═══════════════ 工具定义 ═══════════════
ALL_TOOLS = [
    {
        "name": "unity_ping",
        "description": "检查 Unity Editor 连接状态",
        "inputSchema": {"type": "object", "properties": {}, "required": []},
    },
    {
        "name": "unity_status",
        "description": "获取 Unity Editor 状态（编辑/播放）和当前场景",
        "inputSchema": {"type": "object", "properties": {}, "required": []},
    },
    {
        "name": "unity_list_projects",
        "description": "列出所有存活的 Unity 实例（工程路径、PID、当前绑定）。同时开着多个 Unity 工程时先用它确认目标。",
        "inputSchema": {"type": "object", "properties": {}, "required": []},
    },
    {
        "name": "unity_use_project",
        "description": "把当前会话绑定到指定的 Unity 实例（多开 Unity 时切换目标）。参数写工程名或路径的一部分即可，如 'XY_Editor'。",
        "inputSchema": {
            "type": "object",
            "properties": {
                "project": {"type": "string", "description": "工程名或路径片段"}
            },
            "required": ["project"],
        },
    },
    {
        "name": "unity_execute_code",
        "description": "在 Unity 中执行 C# 代码。可访问 UnityEngine/UnityEditor 所有 API、GameObject.Find()、AssetDatabase 等。用 print() 输出结果、printJson() 输出 JSON。",
        "inputSchema": {
            "type": "object",
            "properties": {"code": {"type": "string", "description": "C# 代码"}},
            "required": ["code"],
        },
    },
    {
        "name": "unity_select_asset",
        "description": "在 Project 窗口中选中并高亮资源",
        "inputSchema": {
            "type": "object",
            "properties": {"asset_path": {"type": "string"}},
            "required": ["asset_path"],
        },
    },
    {
        "name": "unity_import_assets",
        "description": "强制导入资源（多个路径用 \\n 分隔）",
        "inputSchema": {
            "type": "object",
            "properties": {"paths": {"type": "string"}},
            "required": ["paths"],
        },
    },
    {
        "name": "unity_refresh_asset_database",
        "description": "刷新 Asset Database（重新扫描资源）",
        "inputSchema": {"type": "object", "properties": {}, "required": []},
    },
    {
        "name": "unity_recompile_scripts",
        "description": "触发脚本重编译",
        "inputSchema": {"type": "object", "properties": {}, "required": []},
    },
    {
        "name": "unity_get_console_logs",
        "description": "读取最近 N 条 Unity Console 日志（默认 50）",
        "inputSchema": {
            "type": "object",
            "properties": {"count": {"type": "number", "description": "日志条数"}},
            "required": [],
        },
    },
    {
        "name": "unity_clear_console",
        "description": "清空日志缓冲区",
        "inputSchema": {"type": "object", "properties": {}, "required": []},
    },
    {
        "name": "unity_get_selection",
        "description": "获取当前选中对象（名称/类型/路径/InstanceID）",
        "inputSchema": {"type": "object", "properties": {}, "required": []},
    },
    {
        "name": "unity_find_assets",
        "description": "按名称/类型搜索资源（AssetDatabase.FindAssets 语法，如 t:Prefab Player）",
        "inputSchema": {
            "type": "object",
            "properties": {"filter": {"type": "string"}},
            "required": ["filter"],
        },
    },
    {
        "name": "unity_get_scene_graph",
        "description": "获取场景层级结构（指定深度，0=无限）",
        "inputSchema": {
            "type": "object",
            "properties": {"depth": {"type": "number", "description": "层级深度"}},
            "required": [],
        },
    },
    {
        "name": "unity_get_scene_roots",
        "description": "获取场景根 GameObject 名称列表（轻量版）",
        "inputSchema": {"type": "object", "properties": {}, "required": []},
    },
    {
        "name": "unity_play_mode",
        "description": "只读：查询 Editor 是否在 Play Mode（不切，避免域重载卡 Bridge）",
        "inputSchema": {"type": "object", "properties": {}, "required": []},
    },
    {
        "name": "unity_screenshot",
        "description": "[已弃用] 截图返回 base64（巨大 JSON 卡传输）。改用 unity_screenshot_to_file 直接写文件",
        "inputSchema": {
            "type": "object",
            "properties": {
                "target": {"type": "string", "description": "game 或 scene"}
            },
            "required": [],
        },
    },
    {
        "name": "unity_get_component",
        "description": "读取 GameObject 组件属性。格式: 目标名称|组件类型(可选)。如 'Main Camera' 或 'Main Camera|UnityEngine.Camera'",
        "inputSchema": {
            "type": "object",
            "properties": {"target": {"type": "string"}},
            "required": ["target"],
        },
    },
    {
        "name": "unity_set_property",
        "description": "修改 GameObject 组件属性（走 Undo，可 Ctrl+Z 撤销）。格式: 目标名称|属性路径|值。属性路径支持友好名和序列化名，如 'Main Camera|Transform.localPosition|1,2,3' 或 'Cube|m_LocalScale.x|2'。支持 bool/int/float/string/enum/Vector2/Vector3/Color/ObjectReference。",
        "inputSchema": {
            "type": "object",
            "properties": {
                "spec": {"type": "string", "description": "目标名称|属性路径|值"}
            },
            "required": ["spec"],
        },
    },
    {
        "name": "unity_get_prefab_structure",
        "description": "读取 Prefab 内部结构（子物体层级 + 每个物体的组件名列表）。参数为 Prefab 资源路径或名称，如 'Assets/Prefabs/Enemy.prefab'。",
        "inputSchema": {
            "type": "object",
            "properties": {
                "asset_path": {"type": "string", "description": "Prefab 路径或名称"}
            },
            "required": ["asset_path"],
        },
    },
    {
        "name": "unity_screenshot_to_file",
        "description": "截取 Game/Scene 视图并直接写入磁盘 PNG（推荐；避免 base64 巨大 JSON）。参数: target|path，path 留空写到 项目根/TempScreenshots/shot_*.png",
        "inputSchema": {
            "type": "object",
            "properties": {
                "spec": {"type": "string", "description": "target|path，例如 'scene|' 或 'game|C:/shots/a.png'"}
            },
            "required": ["spec"],
        },
    },
    {
        "name": "unity_subscribe",
        "description": "订阅 Unity 主动事件（console/compile/scene 逗号分隔，all 订阅全部）。subscribe=订阅；poll_events 拉取累积事件。",
        "inputSchema": {
            "type": "object",
            "properties": {
                "types": {"type": "string", "description": "all 或 console,compile,scene"}
            },
            "required": [],
        },
    },
    {
        "name": "unity_subscribe_status",
        "description": "查询当前事件订阅状态（含队列深度）",
        "inputSchema": {"type": "object", "properties": {}, "required": []},
    },
    {
        "name": "unity_poll_events",
        "description": "拉取累积事件。参数: timeout_ms|max_items，例: '500|10'（等 500ms 最多拿 10 条）",
        "inputSchema": {
            "type": "object",
            "properties": {
                "spec": {"type": "string", "description": "timeout_ms|max_items，例 '500|10'"}
            },
            "required": [],
        },
    },
    {
        "name": "unity_unsubscribe",
        "description": "取消事件订阅并清空队列",
        "inputSchema": {"type": "object", "properties": {}, "required": []},
    },
    {
        "name": "unity_cli",
        "description": (
            "直接调用 Pipeline HTTP，无需安装 Unity CLI；兼容原有命令字符串入口"
            "（构建、测试、烘焙、动画、Project Settings、包管理、run_script 热更等），"
            "走 HTTP 回环，与本 Bridge 的命名管道互不影响。"
            "spec 写「命令名 参数...」，例：'editor_status'、'recompile'、"
            "'eval \"return 1+1;\"'、'get_scene_hierarchy --path Assets/Scenes/Main.unity'、"
            "'set_autotick --enable true'。留空则列出全部可用命令。"
            "异步命令（recompile / build / run_tests / *_bake）需要轮询对应的 *_status。"
        ),
        "inputSchema": {
            "type": "object",
            "properties": {
                "spec": {
                    "type": "string",
                    "description": "命令名与参数；留空则列出全部可用命令",
                },
                "timeout": {
                    "type": "number",
                    "description": "超时秒数，默认 60，上限 600",
                },
            },
            "required": [],
        },
    },
]

ALL_TOOLS.extend([
    {
        "name": "unity_pipeline",
        "description": "直接调用 Pipeline HTTP 命令，无需 Unity CLI。参数名及类型通过 unity_pipeline_commands 查询。异步命令需查询对应 *_status；超时不会自动重放。",
        "inputSchema": {"type": "object", "properties": {
            "command": {"type": "string", "description": "Pipeline 命令名"},
            "parameters": {"type": "object", "description": "命令参数，保留数字、布尔、数组等 JSON 类型"},
            "timeout": {"type": "number", "minimum": 1, "maximum": 600, "description": "超时秒数，默认 60"}
        }, "required": ["command"]}
    },
    {
        "name": "unity_pipeline_commands",
        "description": "读取 Pipeline 实时命令与参数 schema；传 command 查看单条命令，不传则返回全部。",
        "inputSchema": {"type": "object", "properties": {
            "command": {"type": "string", "description": "可选：需要查询的命令名"}
        }, "required": []}
    }
])

# ═══════════════ 工具分组（保留给 stdio 按需裁剪工具列表）═══════════════
TOOL_GROUPS = {
    "core": [  # 基础通讯 & 多工程
        "unity_ping",
        "unity_status",
        "unity_list_projects",
        "unity_use_project",
        "unity_get_console_logs",
        "unity_clear_console",
        "unity_cli",
        "unity_pipeline",
        "unity_pipeline_commands",
    ],
    "assets": [  # 资源管理 — 6 tools
        "unity_select_asset",
        "unity_find_assets",
        "unity_import_assets",
        "unity_refresh_asset_database",
        "unity_recompile_scripts",
        "unity_get_prefab_structure",
    ],
    "scene": [  # 场景 & 运行时 — 8 tools
        "unity_execute_code",
        "unity_get_selection",
        "unity_get_scene_graph",
        "unity_get_scene_roots",
        "unity_play_mode",
        "unity_screenshot_to_file",
        "unity_get_component",
        "unity_set_property",
    ],
    "events": [  # 事件订阅 & 截屏 — 5 tools
        "unity_subscribe",
        "unity_subscribe_status",
        "unity_poll_events",
        "unity_unsubscribe",
        "unity_screenshot",
    ],
}

# 默认：全部工具（兼容旧配置）
TOOLS = ALL_TOOLS


def handle(name: str, args: dict, close_after: bool = False) -> str:
    # close_after 默认 False：stdio 进程常驻，复用同一条管道连接，
    # 避免每次工具调用都断开重连（重连含 WaitNamedPipe + 暖场 ping，最多 5s）。
    # 需要强制本次调用后关闭连接时显式传 True。
    try:
        if name == "unity_list_projects":
            return cmd_list_projects()
        elif name == "unity_use_project":
            return cmd_use_project(args.get("project", ""))
        elif name == "unity_ping":
            r = UP.call("ping")
            if not r.get("ok"):
                return f"❌ Unity 未连接: {r.get('error', r.get('message', r))}"
            proj = os.path.basename((r.get("projectPath") or "").rstrip("\\/"))
            where = f" | 工程: {proj}" if proj else ""
            return f"✅ pong | PID: {r.get('processId', '?')}{where}"
        elif name == "unity_status":
            r = UP.call("status")
            return f"状态: {r.get('message', r)}"
        elif name == "unity_execute_code":
            r = UP.call("execute_code", args["code"])
            if r.get("ok"):
                return f"✅ 执行成功:\n{r.get('message', '(无输出)')}"
            return f"❌ 失败:\n{r.get('error', r.get('message', str(r)))}"
        elif name == "unity_select_asset":
            UP.call("select_asset", args["asset_path"])
            return f"✅ 已选中: {args['asset_path']}"
        elif name == "unity_import_assets":
            r = UP.call("import_assets", args["paths"])
            return f"✅ {r.get('message', 'done')}"
        elif name == "unity_refresh_asset_database":
            UP.call("refresh_asset_database")
            return "✅ 已刷新"
        elif name == "unity_recompile_scripts":
            UP.call("request_recompile")
            return "✅ 已触发"
        elif name == "unity_get_console_logs":
            c = str(args.get("count", 50))
            r = UP.call("get_console_logs", c)
            return f"{r.get('message', r)}"
        elif name == "unity_clear_console":
            r = UP.call("clear_console")
            return f"✅ {r.get('message', '已清空')}"
        elif name == "unity_get_selection":
            r = UP.call("get_selection")
            return f"当前选中:\n{r.get('message', r)}"
        elif name == "unity_find_assets":
            r = UP.call("find_assets", args["filter"])
            return f"{r.get('message', r)}"
        elif name == "unity_get_scene_graph":
            d = str(args.get("depth", 0))
            r = UP.call("get_scene_graph", d)
            return f"{r.get('message', r)}"
        elif name == "unity_get_scene_roots":
            r = UP.call("get_scene_roots")
            return f"{r.get('message', r)}"
        elif name == "unity_play_mode":
            r = UP.call("play_mode", "")
            return f"{r.get('message', r)}"
        elif name == "unity_screenshot":
            t = args.get("target", "game")
            r = UP.call("screenshot", t)
            m = str(r.get("message", r))
            if len(m) > 200:
                return f"✅ 截图成功（{len(m)} 字符 base64，已弃用，建议 screenshot_to_file）"
            return f"❌ {r.get('error', m)}"
        elif name == "unity_screenshot_to_file":
            spec = args.get("spec", "game|")
            r = UP.call("screenshot_to_file", spec)
            return f"{r.get('message', r)}"
        elif name == "unity_subscribe":
            types = args.get("types", "all")
            r = UP.call("subscribe", types)
            return f"{r.get('message', r)}"
        elif name == "unity_subscribe_status":
            r = UP.call("subscribe_status", "")
            return f"{r.get('message', r)}"
        elif name == "unity_poll_events":
            spec = args.get("spec", "500|10")
            r = UP.call("poll_events", spec)
            return f"{r.get('message', r)}"
        elif name == "unity_unsubscribe":
            r = UP.call("unsubscribe", "")
            return f"{r.get('message', r)}"
        elif name in ("unity_cli", "unity_pipeline", "unity_pipeline_commands"):
            return run_pipeline_tool(name, args)
        elif name == "unity_get_component":
            r = UP.call("get_component", args["target"])
            return f"{r.get('message', r)}"
        elif name == "unity_set_property":
            r = UP.call("set_property", args["spec"])
            if not r.get("ok"):
                return f"❌ {r.get('error', r.get('message', str(r)))}"
            return f"{r.get('message', 'ok')}"
        elif name == "unity_get_prefab_structure":
            r = UP.call("get_prefab_structure", args["asset_path"])
            return f"{r.get('message', r)}"
        else:
            return f"未知: {name}"
    except Exception as e:
        return f"❌ {e}"
    finally:
        if close_after:
            UP.close()


# ═══════════════ 配置安装（由 Unity 的 BridgeSetup 调用）═══════════════
def install_workbuddy_config(stable_script: str, python_exe: str) -> str:
    """把一条稳定的 xybridge 条目合并进 ~/.workbuddy-ai/mcp.json（幂等）。

    会清掉此前按工程生成的 xybridge_<工程名> 条目，只保留这一条。
    已存在条目的 command 会被沿用，避免把用户配好的解释器路径改掉。
    """
    cfg_dir = os.path.join(os.path.expanduser("~"), ".workbuddy-ai")
    cfg_path = os.path.join(cfg_dir, "mcp.json")

    data = {}
    if os.path.exists(cfg_path):
        try:
            with open(cfg_path, "r", encoding="utf-8") as f:
                data = json.load(f) or {}
        except Exception as e:
            return f"❌ 现有配置解析失败，未改动：{cfg_path}\n{e}"

    if not isinstance(data, dict):
        return f"❌ 现有配置根节点不是对象，未改动：{cfg_path}"

    servers = data.get("mcpServers")
    if servers is None:
        servers = data["mcpServers"] = {}
    if not isinstance(servers, dict):
        return f"❌ 现有配置的 mcpServers 不是对象，未改动：{cfg_path}"

    # 沿用已有 xybridge* 条目里的 command（用户可能指定了带 pywin32 的解释器）
    for k, v in servers.items():
        if (k == "xybridge" or k.startswith("xybridge_")) and isinstance(v, dict):
            if v.get("command"):
                python_exe = v["command"]
                break

    removed = [k for k in list(servers) if k == "xybridge" or k.startswith("xybridge_")]
    for k in removed:
        del servers[k]

    servers["xybridge"] = {
        "command": python_exe,
        "args": [stable_script, "--transport=stdio"],
        "env": {"PYTHONIOENCODING": "utf-8", "PYTHONUTF8": "1"},
        "disabled": False,
    }

    os.makedirs(cfg_dir, exist_ok=True)
    with open(cfg_path, "w", encoding="utf-8") as f:
        json.dump(data, f, ensure_ascii=False, indent=2)
        f.write("\n")

    return (
        f"✅ 已写入 {cfg_path}\n"
        f"   条目: xybridge -> {stable_script}\n"
        f"   解释器: {python_exe}\n"
        f"   清理旧条目: {', '.join(removed) if removed else '无'}"
    )


def main(server_name: str = "yzjbridge"):
    global TOOLS
    _p, _err = resolve_pipe()
    if _p:
        print(
            f"[YZJ-MCP:{server_name}] 启动, 目标管道: {_p}, tools: {len(TOOLS)}",
            file=sys.stderr,
        )
    else:
        print(
            f"[YZJ-MCP:{server_name}] 启动, 未绑定目标"
            f"（存活实例 {len(list_live_pipes())} 个）, tools: {len(TOOLS)}",
            file=sys.stderr,
        )

    while True:
        mid = None
        try:
            line = sys.stdin.readline()
            if not line:
                print(f"[YZJ-MCP:{server_name}] stdin closed, exiting", file=sys.stderr)
                break
            print(f"[YZJ-MCP:{server_name}] <<< {line.strip()[:120]}", file=sys.stderr)
            msg = json.loads(line.strip())
            mid = msg.get("id")
            m = msg.get("method", "")

            if m == "initialize":
                resp = json.dumps(
                    {
                        "jsonrpc": "2.0",
                        "id": mid,
                        "result": {
                            "protocolVersion": "2024-11-05",
                            "serverInfo": {"name": server_name, "version": "1.0"},
                            "capabilities": {"tools": {"listChanged": False}},
                        },
                    }
                )
                print(
                    f"[YZJ-MCP:{server_name}] >>> initialize ({len(resp)} bytes)",
                    file=sys.stderr,
                )
                sys.stdout.write(resp + "\n")
            elif m == "notifications/initialized":
                print(
                    f"[YZJ-MCP:{server_name}] <<< initialized notification",
                    file=sys.stderr,
                )
            elif m == "tools/list":
                resp = json.dumps(
                    {"jsonrpc": "2.0", "id": mid, "result": {"tools": TOOLS}}
                )
                print(
                    f"[YZJ-MCP:{server_name}] >>> tools/list -> {len(TOOLS)} tools, {len(resp)} bytes",
                    file=sys.stderr,
                )
                sys.stdout.write(resp + "\n")
            elif m == "tools/call":
                tn = msg["params"]["name"]
                print(
                    f"[YZJ-MCP:{server_name}] >>> tools/call -> {tn}", file=sys.stderr
                )
                ta = msg["params"].get("arguments", {})
                txt = handle(tn, ta)
                sys.stdout.write(
                    json.dumps(
                        {
                            "jsonrpc": "2.0",
                            "id": mid,
                            "result": {"content": [{"type": "text", "text": txt}], "isError": getattr(txt, "is_error", False)},
                        }
                    )
                    + "\n"
                )
            else:
                print(
                    f"[YZJ-MCP:{server_name}] ??? unknown method: {m}", file=sys.stderr
                )
            sys.stdout.flush()
        except json.JSONDecodeError:
            continue
        except Exception as e:
            sys.stdout.write(
                json.dumps(
                    {
                        "jsonrpc": "2.0",
                        "id": mid,
                        "error": {"code": -32603, "message": str(e)},
                    }
                )
                + "\n"
            )
            sys.stdout.flush()

    UP.close()


if __name__ == "__main__":
    group = "all"
    transport = "stdio"
    port = 3100
    install_config = False
    for a in sys.argv[1:]:
        if a.startswith("--group="):
            group = a.split("=", 1)[1]
        elif a.startswith("--transport="):
            transport = a.split("=", 1)[1]
        elif a.startswith("--port="):
            port = int(a.split("=", 1)[1])
        elif a.startswith("--project="):
            _PROJECT_ARG = a.split("=", 1)[1].strip()
        elif a == "--install-config":
            install_config = True

    if install_config:
        # 由 Unity 的 BridgeSetup 调用：把稳定条目写进 WorkBuddy 的 mcp.json
        exe = os.environ.get("XYBRIDGE_PYTHON") or sys.executable
        print(install_workbuddy_config(os.path.abspath(__file__), exe))
        sys.exit(0)

    name_map = {
        "core": "xybridge-core",
        "assets": "xybridge-assets",
        "scene": "xybridge-scene",
        "events": "xybridge-events",
    }
    if transport != "stdio":
        print(
            f"[YZJ-MCP] 不再支持 transport={transport}；请使用 --transport=stdio",
            file=sys.stderr,
        )
        sys.exit(2)

    if group in TOOL_GROUPS:
        names = TOOL_GROUPS[group]
        TOOLS = [t for t in ALL_TOOLS if t["name"] in names]
        sname = name_map.get(group, "xybridge")
        print(
            f"[YZJ-MCP] 分组: {group} ({len(TOOLS)} tools), 传输: {transport}",
            file=sys.stderr,
        )
        main(sname)
    else:
        print(f"[YZJ-MCP] 全量模式, 传输: {transport}", file=sys.stderr)
        main("xybridge")
