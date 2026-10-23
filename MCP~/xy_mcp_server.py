"""
YZJBridge MCP Server
AI 通过 MCP 协议操控 Unity Editor
无需任何第三方依赖：只用 Python 标准库（2.0 起不再需要 pywin32）

Hub 模式 —— 一个 server 服务所有 Unity 工程
────────────────────────────────────────────────────────
每个 Unity Editor 在 127.0.0.1 上监听一个由系统分配的端口，并把
{host, port, token, projectPath, pid} 写入
    ~/.xybridge/instances/xy-<工程指纹>-<pid>.json
本脚本扫描该目录发现所有存活实例：

  1. --project=<关键字> 命令行参数 / XYBRIDGE_PROJECT 环境变量（显式指定）
  2. 脚本位于工程根目录时推导出的该工程（向后兼容旧的「一工程一脚本」用法）
  3. 唯一存活的实例
  4. 以上都不成立 → 报错并列出候选，用 unity_use_project 切换

因此脚本可以放在任意固定位置（推荐 ~/.xybridge/），
MCP 配置只需写一次，之后导入任何 Unity 工程都不用再改。

文件存在不等于实例存活：Editor 崩溃会留下残留文件，
所以「能否建立 TCP 连接」才是存活判据。
"""

import json, os, sys, time, threading, math, hashlib, socket
import urllib.request, urllib.error, urllib.parse

ROOT = os.path.dirname(os.path.abspath(__file__))
SERVER_VERSION = "2.0.0"
with open(__file__, "rb") as _script_file:
    RUNNING_SCRIPT_SHA256 = hashlib.sha256(_script_file.read()).hexdigest()

# Unity 端（BridgeServer.InstanceDir）写入实例文件的目录，两侧必须一致
INSTANCE_DIR = os.path.join(os.path.expanduser("~"), ".xybridge", "instances")
INSTANCE_SCHEMA = 2
LOOPBACK = "127.0.0.1"

# 显式指定的目标（--project= / XYBRIDGE_PROJECT）
_PROJECT_ARG = None


def _norm(path: str) -> str:
    return os.path.normcase(os.path.normpath(path or ""))


def _sanitize(path: str) -> str:
    return (
        path.replace("\\", "_").replace("/", "_").replace(":", "_").replace(" ", "_")
    )


def _load_instance(path: str):
    """读一个实例文件；schema/字段不合法（含旧版管道残留）返回 None。"""
    try:
        with open(path, "r", encoding="utf-8") as stream:
            data = json.load(stream)
    except (OSError, ValueError):
        return None
    if not isinstance(data, dict) or data.get("schema") != INSTANCE_SCHEMA:
        return None
    if data.get("transport") != "tcp":
        return None
    port = data.get("port")
    if not isinstance(port, int) or not 1 <= port <= 65535:
        return None
    data["host"] = data.get("host") or LOOPBACK
    data["path"] = path
    return data


def _is_alive(host: str, port: int, timeout_s: float = 0.25) -> bool:
    """能否建立 TCP 连接 —— 判断实例是否存活的唯一依据。"""
    try:
        probe = socket.create_connection((host, port), timeout=timeout_s)
    except OSError:
        return False
    try:
        probe.close()
    except OSError:
        pass
    return True


def _cleanup_stale(path: str, days: int = 7) -> None:
    """清掉长期失效的残留实例文件。

    只在文件很旧时动手：刚创建的实例可能还在启动，误删会让它彻底发现不了。
    """
    try:
        if time.time() - os.path.getmtime(path) > days * 86400:
            os.remove(path)
    except OSError:
        pass


def list_instances(retries: int = 3):
    """扫描实例目录，返回当前存活的实例列表。

    枚举目录时可能撞上瞬时抖动（文件正在被替换），拿到空结果时短暂重试，
    避免把毫秒级状态误判成「没有实例」。
    """
    for attempt in range(retries):
        try:
            names = sorted(n for n in os.listdir(INSTANCE_DIR) if n.endswith(".json"))
        except OSError:
            names = []
        found = []
        for name in names:
            path = os.path.join(INSTANCE_DIR, name)
            data = _load_instance(path)
            if data is None:
                _cleanup_stale(path)
                continue
            if _is_alive(data["host"], data["port"]):
                found.append(data)
            else:
                _cleanup_stale(path)
        if found:
            return found
        if attempt < retries - 1:
            time.sleep(0.05)
    return []


def _requested_project():
    return _PROJECT_ARG or os.environ.get("XYBRIDGE_PROJECT") or None


def _match_instance(keyword: str, candidates):
    """按工程路径片段匹配实例（大小写不敏感）。返回 (instance, err)。"""
    k = (keyword or "").strip().lower()
    hits = [i for i in candidates if k and k in (i.get("projectPath") or "").lower()]
    if len(hits) == 1:
        return hits[0], None
    if not hits:
        return None, f"没有匹配「{keyword}」的 Unity 实例"
    return None, f"「{keyword}」匹配到 {len(hits)} 个实例，请写得更具体"


def _endpoint(inst):
    return "%s:%s" % (inst.get("host", LOOPBACK), inst.get("port", "?"))


def _format_candidates(candidates):
    if not candidates:
        return "（当前没有任何存活的 Unity 实例）"
    body = "\n".join(
        "  - " + (i.get("projectPath") or "?") + "  [" + _endpoint(i) + "]"
        for i in candidates
    )
    return "候选实例：\n" + body + "\n（用 unity_list_projects 可看到每个实例的工程路径与 PID）"


def resolve_target():
    """解析目标实例。返回 (instance, err)；instance 为 None 时 err 说明原因。"""
    live = list_instances()

    want = _requested_project()
    if want:
        inst, err = _match_instance(want, live)
        if err:
            return None, err + "\n" + _format_candidates(live)
        return inst, None

    # 向后兼容：脚本直接放在工程根目录（旧的「一工程一脚本」用法）
    if os.path.isdir(os.path.join(ROOT, "Assets")):
        for inst in live:
            if _norm(inst.get("projectPath")) == _norm(ROOT):
                return inst, None

    if len(live) == 1:
        return live[0], None

    if not live:
        return None, (
            "未发现任何存活的 Unity Bridge。\n"
            "请确认：① Unity 已打开目标工程；"
            "② XYHub > MCP Bridge 面板中「本机服务」正在运行。"
        )

    return None, (
        f"发现 {len(live)} 个 Unity 实例，无法自动确定目标。\n"
        "请用 unity_use_project 指定，或给 server 加 --project=<关键字>。\n"
        + _format_candidates(live)
    )



# ═══════════════ 本机 TCP 客户端（自动重连） ═══════════════
class UnitySocket:
    """到单个 Unity Editor 的常驻连接。

    重连时会重读实例文件：Editor 域重载后端口和 token 都会变，
    沿用旧值只会得到「认证失败」。
    """

    def __init__(self, instance=None, explicit=False):
        self.instance = instance  # 目标实例 dict；None 表示交给 resolve_target() 决定
        self.explicit = explicit  # True = 显式指定，实例消失时不自动改绑
        self._sock = None
        self._token = None
        self._n = 0
        self._lock = threading.Lock()
        self._buf = b""
        self._peer_closed = False
        self._last_error = None
        self._instance_mtime = 0.0
        self.project_path = (instance or {}).get("projectPath")

    # ── 目标切换 ──
    def set_instance(self, instance, explicit=True):
        """切换目标实例（会断开当前连接）。"""
        self.close()
        self.instance = instance
        self.explicit = explicit
        self._last_error = None
        self.project_path = (instance or {}).get("projectPath")

    @property
    def endpoint(self):
        return _endpoint(self.instance) if self.instance else None

    # ── 收发 ──
    def _recv_some(self, timeout_s: float):
        self._sock.settimeout(max(0.001, timeout_s))
        try:
            data = self._sock.recv(65536)
        except socket.timeout:
            raise TimeoutError("timeout")
        if not data:
            raise ConnectionError("closed")
        return data

    def _read_line(self, deadline: float):
        """读到一行（不含换行符）；超时返回 None，对端断开时置 _peer_closed。"""
        idx = self._buf.find(b"\n")
        while idx < 0:
            remaining = deadline - time.time()
            if remaining <= 0:
                return None
            try:
                data = self._recv_some(min(remaining, 5.0))
            except TimeoutError:
                return None
            except (ConnectionError, OSError):
                self._peer_closed = True
                self.close()
                return None
            self._buf += data
            idx = self._buf.find(b"\n")
        line = self._buf[:idx]
        self._buf = self._buf[idx + 1 :]
        return line.decode("utf-8", errors="ignore")

    def _send(self, payload: dict) -> bool:
        try:
            self._sock.sendall((json.dumps(payload) + "\n").encode("utf-8"))
            return True
        except OSError:
            self.close()
            return False

    # ── 连接 ──
    def connect(self, timeout_ms: int = 5000):
        if self.instance is not None:
            fresh = _load_instance(self.instance.get("path", ""))
            if fresh:
                self.instance = fresh
            elif not self.explicit:
                # 文件消失说明工程已关掉 / 实例已退出；
                # 显式绑定时保留旧目标，让连接失败把问题暴露出来，而不是悄悄改绑
                self.instance = None

        if self.instance is None:
            inst, err = None, None
            for _ in range(4):
                inst, err = resolve_target()
                if inst is not None:
                    break
                time.sleep(0.06)
            if inst is None:
                self._last_error = err
                return False
            self.instance = inst

        self._token = self.instance.get("token") or ""
        # 记下这一版实例文件的 mtime：之后每次 call 靠它判断是否发生了域重载
        try:
            self._instance_mtime = os.path.getmtime(self.instance.get("path") or "")
        except OSError:
            self._instance_mtime = 0.0
        deadline = time.time() + max(timeout_ms, 1) / 1000.0
        while time.time() < deadline:
            remaining = deadline - time.time()
            if remaining <= 0:
                break
            try:
                self._sock = socket.create_connection(
                    (self.instance["host"], self.instance["port"]),
                    timeout=max(0.05, min(remaining, 2.0)),
                )
            except OSError:
                self.close()
                time.sleep(0.05)
                continue

            try:
                self._sock.setsockopt(socket.IPPROTO_TCP, socket.TCP_NODELAY, 1)
            except OSError:
                pass
            self._buf = b""
            self._last_error = None

            if self._handshake(max(0.5, min(5.0, deadline - time.time()))):
                return True

            self.close()
            # 认证失败不是重试能解决的；但如果实例文件刚被替换（域重载），
            # 下一轮 connect 会重读文件拿到新的端口和 token，所以继续循环而不是直接放弃
            time.sleep(0.05)

        self._last_error = self._last_error or "无法连接 Unity 实例"
        return False

    def _handshake(self, timeout_s: float) -> bool:
        """连接后先 ping 一次。

        既是暖场，也顺便验证 token：令牌不对时能在这里给出明确错误，
        而不是让后面每个工具调用都失败。
        服务端连接后会主动推一条 unity_connected，本循环会跳过它。
        """
        self._n += 1
        hid = str(self._n)
        if not self._send({"id": hid, "type": "ping", "message": "", "token": self._token}):
            self._last_error = "写入连接失败"
            return False

        deadline = time.time() + timeout_s
        while time.time() < deadline:
            line = self._read_line(deadline)
            if line is None:
                break
            try:
                reply = json.loads(line)
            except ValueError:
                continue
            if reply.get("id") != hid and reply.get("reply_to") != hid:
                continue
            if not reply.get("ok"):
                self._last_error = reply.get("error") or "握手被拒绝"
                return False
            self.project_path = reply.get("projectPath") or self.project_path
            return True

        self._last_error = self._last_error or "握手超时"
        return False

    # ── 请求 ──
    def _refresh_if_changed(self):
        """实例文件变了就先断开，让 connect() 重读端口与令牌。

        Unity 每次域重载都会换端口和令牌并重写实例文件（文件名不变，内容变）。
        没有这一步时，重载后的**第一次**调用必然撞在已经失效的连接上，
        报「连接已断开」——虽然不会误判成功，但每次改 C# 都要白废一次调用。
        只看 mtime，一次 stat 的成本可以忽略。
        """
        path = (self.instance or {}).get("path")
        if not path:
            return
        try:
            mtime = os.path.getmtime(path)
        except OSError:
            return
        if mtime == self._instance_mtime:
            return
        self._instance_mtime = mtime
        fresh = _load_instance(path)
        if fresh:
            self.instance = fresh
            self._token = fresh.get("token") or ""
            self.close()

    def call(self, t: str, m: str = "", timeout_s: float = 30.0) -> dict:
        with self._lock:
            self._refresh_if_changed()
            if not self._sock and not self.connect():
                return {"ok": False, "error": self._last_error or "Unity 未连接"}

            self._n += 1
            req_id = str(self._n)
            if not self._send({"id": req_id, "type": t, "message": m, "token": self._token}):
                return {"ok": False, "error": "写入连接失败；执行结果未知，未自动重放请求"}

            deadline = time.time() + timeout_s
            while time.time() < deadline:
                line = self._read_line(deadline)
                if line is None:
                    break
                try:
                    reply = json.loads(line)
                except ValueError:
                    continue
                if reply.get("id") == req_id or reply.get("reply_to") == req_id:
                    return reply

            # 超时和断开都不能证明操作没执行 —— 一律不自动重放
            peer_closed = self._peer_closed
            self._peer_closed = False
            self.close()
            return {
                "ok": False,
                "error": ("连接已断开" if peer_closed else "响应超时")
                + "；执行结果未知，未自动重放请求",
            }

    def close(self):
        if self._sock:
            try:
                self._sock.close()
            except OSError:
                pass
        self._sock = None
        self._buf = b""


UP = UnitySocket()


# Pipeline HTTP client. No Unity CLI executable or extra Python dependency required.
# Keep unity_cli as a command-line-compatible MCP alias; parsing belongs to Pipeline.
class PipelineToolResult(str):
    def __new__(cls, text, is_error=False):
        result = super().__new__(cls, text)
        result.is_error = is_error
        return result


def tool_result(value):
    """Normalize both transport failures and service JSON failures for MCP."""
    error = getattr(value, "is_error", False)
    text = str(value)
    try:
        data = json.loads(text)
        if isinstance(data, dict):
            error = error or data.get("ok") is False or data.get("success") is False or bool(data.get("error"))
    except (ValueError, TypeError):
        error = error or text.startswith(("❌", "未知:", "❌ 失败"))
    return PipelineToolResult(text, error)


def script_info(path):
    try:
        with open(path, "rb") as stream:
            contents = stream.read()
        return {"path": os.path.abspath(path), "sha256": hashlib.sha256(contents).hexdigest()}
    except OSError as e:
        return {"path": os.path.abspath(path), "error": str(e)}


def bridge_health(project):
    installed = script_info(os.path.join(os.path.expanduser("~"), ".xybridge", "xy_mcp_server.py"))
    source = script_info(os.path.join(project, "Assets", "XYHubTools", "MCP~", "xy_mcp_server.py")) if project else None
    return {
        "version": SERVER_VERSION, "pid": os.getpid(), "scriptPath": os.path.abspath(__file__),
        "runningSha256": RUNNING_SCRIPT_SHA256, "disk": script_info(__file__),
        "installed": installed, "projectSource": source,
        "registeredTools": len(ALL_TOOLS), "exposedTools": len(TOOLS),
        "restartRequired": script_info(__file__).get("sha256") != RUNNING_SCRIPT_SHA256,
        "sourceMatchesInstalled": source.get("sha256") == installed.get("sha256") if source and "sha256" in source and "sha256" in installed else None,
    }


class _NoRedirect(urllib.request.HTTPRedirectHandler):
    def redirect_request(self, req, fp, code, msg, headers, newurl):
        return None


_PIPELINE_HTTP = urllib.request.build_opener(urllib.request.ProxyHandler({}), _NoRedirect())


def bound_project_path():
    # Pin the verified project across reloads; never silently switch to another editor.
    if UP.project_path:
        return UP.project_path
    want = _requested_project()
    if not UP.explicit and want and os.path.isabs(want) and os.path.isdir(want):
        project = os.path.realpath(want)
        _pipeline_descriptor(project)
        # 绑定同一个工程的 Unity 实例（找不到实例时只保留 Pipeline 工程路径）
        for inst in list_instances():
            if _norm(inst.get("projectPath")) == _norm(project):
                UP.set_instance(inst, explicit=True)
                break
        UP.project_path = project
        return project
    r = UP.call("status", "", timeout_s=5.0)
    path = (r.get("projectPath") or "").strip() if isinstance(r, dict) else ""
    if path and os.path.isabs(path):
        UP.project_path = os.path.realpath(path)
        UP.explicit = True
        return UP.project_path
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
            return descriptor
        except (OSError, ValueError):
            if attempt == 2:
                raise ValueError("Pipeline 尚未就绪：请检查 XY 的 Pipeline 安装状态，并等待 Unity 完成编译。") from None
            time.sleep(0.05)


def pipeline_request(endpoint, payload=None, timeout_s=60, job_handle=None):
    timeout_s = float(timeout_s)
    if not math.isfinite(timeout_s) or not 1 <= timeout_s <= 600:
        raise ValueError("timeout 必须在 1 到 600 秒之间。")
    project = bound_project_path()
    descriptor = _pipeline_descriptor(project)
    port, token = descriptor["port"], descriptor["evalToken"]
    started = descriptor.get("startedAt")
    if job_handle is not None:
        if os.path.normcase(os.path.realpath(job_handle["projectPath"])) != os.path.normcase(project):
            return False, {"error": "任务属于另一工程，请先切换到任务工程。", "code": "job_project_mismatch", "retryable": False}
        if not started or job_handle["serverStartedAt"] != started:
            return False, {"error": "Pipeline 已重启或发生域重载，原内存任务已失效。请核实操作结果，不要自动重放。", "code": "job_session_expired", "retryable": False, "outcomeUnknown": True}
    if payload and payload.get("job") and not started:
        return False, {"error": "此 Pipeline 未提供任务会话标识，无法安全跟踪后台任务。", "code": "job_session_unavailable", "retryable": False}
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
        return False, {"error": "Pipeline 连接中断或超时，未自动重放。读取可重试；修改结果未知，请先查询状态。", "code": "transport_interrupted", "retryable": payload is None, "outcomeUnknown": payload is not None}
    try:
        result = json.loads(raw)
    except ValueError:
        return False, {"error": "Pipeline 返回了非 JSON 响应。", "code": "invalid_response", "httpStatus": status, "outcomeUnknown": payload is not None}
    if not isinstance(result, dict):
        return False, {"error": "Pipeline 响应格式无效。", "httpStatus": status}
    if status >= 400:
        result.setdefault("httpStatus", status)
        if job_handle is not None and status == 404:
            result.setdefault("code", "job_unavailable")
            result.setdefault("recovery", "任务不存在、已过保留期或该服务不支持任务接口。请核实结果，不要自动重放。")
    ok = 200 <= status < 300 and result.get("success") is not False and not result.get("error") and result.get("state") not in ("failed", "canceled")
    submitted = result.get("result")
    if isinstance(submitted, dict) and submitted.get("success") is False:
        ok = False
    if ok and payload and payload.get("job") and isinstance(submitted, dict) and submitted.get("jobId"):
        result["xyJob"] = {"id": submitted["jobId"], "projectPath": project, "serverStartedAt": started}
    return ok, result


def run_pipeline_tool(name, args):
    try:
        if not isinstance(args, dict):
            raise ValueError("工具参数必须是 JSON 对象。")
        timeout = float(args.get("timeout", 60))
        if not math.isfinite(timeout) or not 1 <= timeout <= 600:
            raise ValueError("timeout 必须在 1 到 600 秒之间。")
        if name == "unity_pipeline_commands":
            for key in ("command", "query", "tag", "detail"):
                if key in args and not isinstance(args[key], str):
                    raise ValueError(key + " 必须是字符串。")
            command = args.get("command")
            if command and any(key in args for key in ("query", "tag", "detail", "offset", "limit")):
                raise ValueError("command 精确查询不能与筛选或分页参数混用。")
            detail = args.get("detail", "compact" if args.get("query") or args.get("tag") else "tags")
            if detail not in ("full", "compact", "tags"):
                raise ValueError("detail 必须是 full、compact 或 tags。")
            query = {"detail": "full" if command else detail}
            if command:
                query["query"] = command
            else:
                query.update({k: args[k] for k in ("query", "tag") if args.get(k)})
                for key, default, minimum in (("offset", 0, 0), ("limit", 50, 1)):
                    value = args.get(key, default)
                    if type(value) is not int or value < minimum or (key == "limit" and value > 200):
                        raise ValueError(key + " 必须是有效整数（offset >= 0，limit 为 1–200）。")
                    query[key] = value
            ok, result = pipeline_request("/api/commands?" + urllib.parse.urlencode(query), timeout_s=timeout)
            if ok and command:
                matches = [c for c in result.get("commands", []) if c.get("name") == command]
                if not matches:
                    ok, result = False, {"error": "未知 Pipeline 命令: " + command}
                else:
                    result = {"commands": matches, "count": len(matches), "total": len(matches), "server": result.get("server")}
            elif ok and isinstance(result.get("count"), int) and isinstance(result.get("total"), int):
                next_offset = result.get("offset", 0) + result["count"]
                result["nextOffset"] = next_offset if next_offset < result["total"] else None
        elif name == "unity_pipeline_job":
            job = args.get("job")
            action = args.get("action", "status")
            if not isinstance(job, dict) or any(not isinstance(job.get(k), str) or not job[k] for k in ("id", "projectPath", "serverStartedAt")):
                raise ValueError("job 必须是提交任务返回的完整 xyJob 对象。")
            if action not in ("status", "cancel"):
                raise ValueError("action 必须是 status 或 cancel。")
            endpoint = "/api/job?" + urllib.parse.urlencode({"id": job["id"]}) if action == "status" else "/api/job/cancel"
            ok, result = pipeline_request(endpoint, None if action == "status" else {"id": job["id"]}, timeout, job_handle=job)
        elif name == "unity_cli":
            spec = args.get("spec", "")
            if not isinstance(spec, str):
                raise ValueError("spec 必须是字符串。")
            payload = {"commandLine": spec, "timeout": int(timeout * 1000)} if spec.strip() else None
            ok, result = pipeline_request("/api/exec" if payload else "/api/commands?detail=compact", payload, timeout)
            if ok and payload is None:
                return PipelineToolResult(compact_command_list(json.dumps(result, ensure_ascii=False)))
        else:
            command = args.get("command")
            parameters = args.get("parameters", {})
            if not isinstance(command, str) or not command.strip() or not isinstance(parameters, dict):
                raise ValueError("command 必须是非空字符串，parameters 必须是对象。")
            job = args.get("job", False)
            if type(job) is not bool:
                raise ValueError("job 必须是布尔值。")
            payload = {
                "command": command, "parameters": parameters, "timeout": int(timeout * 1000)
            }
            if job:
                payload["job"] = True
            ok, result = pipeline_request("/api/exec", payload, timeout)
        return PipelineToolResult(json.dumps(result, ensure_ascii=False), not ok)
    except (ValueError, TypeError, OSError) as e:
        return PipelineToolResult(json.dumps({"error": str(e), "code": "client_validation_or_discovery_failed"}, ensure_ascii=False), True)


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
def probe_instance(inst, timeout_s: float = 3.0):
    """临时连一次目标实例取 status，用于展示工程路径与 PID。失败返回 None。"""
    tmp = UnitySocket(inst, explicit=True)
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
    live = list_instances()
    if not live:
        return (
            "未发现任何存活的 Unity Bridge。\n"
            "请确认 Unity 已打开目标工程，且 XYHub > MCP Bridge 面板中「本机服务」正在运行。"
        )
    bound = UP.endpoint
    lines = [f"发现 {len(live)} 个 Unity 实例（当前绑定：{bound or '未绑定'}）", ""]
    for inst in live:
        mark = "●" if _endpoint(inst) == bound else "○"
        info = probe_instance(inst)
        if info:
            lines.append(f"{mark} {info['projectName'] or '(未知工程)'}")
            lines.append(f"     path : {info['projectPath'] or '?'}")
            lines.append(f"     pid  : {info['processId']}")
            lines.append(f"     addr : {_endpoint(inst)}")
        else:
            name = os.path.basename((inst.get("projectPath") or "").rstrip("\\/")) or "?"
            lines.append(f"{mark} {name}")
            lines.append("     （探测失败：可能正在域重载或正忙）")
            lines.append(f"     addr : {_endpoint(inst)}")
    lines.append("")
    lines.append('用 unity_use_project "<工程名或路径片段>" 切换目标。')
    return "\n".join(lines)


def cmd_use_project(keyword: str) -> str:
    keyword = (keyword or "").strip()
    if not keyword:
        return '请提供工程名或路径片段，例如 unity_use_project "XY_Editor"'
    if os.path.isabs(keyword) and os.path.isdir(keyword):
        project = os.path.realpath(keyword)
        try:
            _pipeline_descriptor(project)
        except ValueError as e:
            return PipelineToolResult(json.dumps({"error": str(e)}, ensure_ascii=False), True)
        for inst in list_instances():
            if _norm(inst.get("projectPath")) == _norm(project):
                UP.set_instance(inst)
                return "已绑定 Unity 实例：" + project
        UP.set_instance(None)
        UP.project_path = project
        return "已绑定 Pipeline 工程（未发现该工程的 Unity 实例）：" + project

    live = list_instances()
    if not live:
        return "未发现任何存活的 Unity Bridge。"
    inst, err = _match_instance(keyword, live)
    if err:
        return f"❌ {err}\n{_format_candidates(live)}"
    UP.set_instance(inst)
    info = probe_instance(inst)
    if info:
        if info.get("projectPath"):
            UP.project_path = os.path.realpath(info["projectPath"])
        return (
            f"✅ 已绑定到 {info['projectName'] or _endpoint(inst)}\n"
            f"   path : {info['projectPath'] or '?'}\n"
            f"   pid  : {info['processId']}\n"
            f"   addr : {_endpoint(inst)}"
        )
    return f"✅ 已绑定到 {_endpoint(inst)}"


ADVANCED_MESSAGES = {
    "unity_shadergraph": "shadergraph",
    "unity_physics": "physics",
    "unity_profiler": "profiler",
    "unity_asset_preview": "asset_preview",
    "unity_frame_debugger": "frame_debugger",
}


def run_advanced_tool(name, args):
    # 保留数字和布尔类型；不重放可能修改资源的请求。
    r = UP.call(ADVANCED_MESSAGES[name], json.dumps(args, ensure_ascii=False), timeout_s=60)
    if not r.get("ok"):
        return json.dumps({"ok": False, "error": r.get("error", r.get("message", "Unity 请求失败"))}, ensure_ascii=False)
    message = r.get("message", "")
    return message if isinstance(message, str) else json.dumps(message, ensure_ascii=False)


# ═══════════════ 工具定义 ═══════════════
ALL_TOOLS = [
    {
        "name": "unity_ping",
        "description": "检查 Unity Editor 连接状态",
        "inputSchema": {"type": "object", "properties": {}, "required": []},
    },
    {
        "name": "unity_status",
        "description": "获取 Unity 状态、场景、MCP 运行版本与脚本哈希、工具数量、安装一致性和是否需要重启。",
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
            "走 HTTP 回环，与本 Bridge 的本机 TCP 通道互不影响。"
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
        "description": "直接调用官方 Pipeline HTTP 命令。先用 unity_pipeline_commands 查询实时参数规范。耗时调用可用 job=true，凭返回的 xyJob 查询后台任务；任务完成仅代表命令返回，build 等异步命令仍需查询各自 *_status。超时不自动重放。",
        "inputSchema": {"type": "object", "properties": {
            "command": {"type": "string", "description": "Pipeline 命令名"},
            "parameters": {"type": "object", "description": "命令参数，保留数字、布尔、数组等 JSON 类型"},
            "job": {"type": "boolean", "description": "以官方后台任务提交，返回 xyJob 供 unity_pipeline_job 查询。任务不跨域重载保留。默认 false"},
            "timeout": {"type": "number", "minimum": 1, "maximum": 600, "description": "超时秒数，默认 60"}
        }, "required": ["command"]}
    },
    {
        "name": "unity_pipeline_commands",
        "description": "按需发现官方实时命令。无参数返回分类索引；用 tag 或 query 获取精简列表（默认每页 50 条），按 nextOffset 翻页；最后用 command 获取单条命令完整 schema。保留英文原始规范，新增命令自动可用。",
        "inputSchema": {"type": "object", "properties": {
            "command": {"type": "string", "description": "精确命令名，返回完整 schema，不能与筛选或分页混用"},
            "query": {"type": "string", "description": "匹配命令名、说明或标签的子串"},
            "tag": {"type": "string", "description": "分类及其子分类，如 assets 或 baking/lighting"},
            "detail": {"type": "string", "enum": ["tags", "compact", "full"], "description": "无筛选默认 tags，有 query/tag 默认 compact；full 包含参数"},
            "offset": {"type": "integer", "minimum": 0},
            "limit": {"type": "integer", "minimum": 1, "maximum": 200},
            "timeout": {"type": "number", "minimum": 1, "maximum": 600}
        }, "required": []}
    },
    {
        "name": "unity_pipeline_job",
        "description": "查询或请求取消官方后台任务。原样传入提交时返回的 xyJob，先绑定其工程。服务重启或域重载后会报告任务失效，不会重放。running 任务取消是协作式的，需继续查询终态；已完成任务仅在官方保留期内可查。",
        "inputSchema": {"type": "object", "properties": {
            "job": {"type": "object", "properties": {
                "id": {"type": "string"}, "projectPath": {"type": "string"}, "serverStartedAt": {"type": "string"}
            }, "required": ["id", "projectPath", "serverStartedAt"]},
            "action": {"type": "string", "enum": ["status", "cancel"], "default": "status"},
            "timeout": {"type": "number", "minimum": 1, "maximum": 600}
        }, "required": ["job"]}
    }
])

ALL_TOOLS.extend([
    {
        "name": "unity_shadergraph",
        "description": "Shader Graph 读写，使用 Unity GraphData API。先用 status 检查支持，再用 nodes/properties 获取 ID 和槽位。写入前用 close 关闭图窗口；undo 回滚自动保存的文件快照。节点类型支持短名，连接支持槽位 ID 或名称。",
        "inputSchema": {"type": "object", "properties": {
            "op": {"type": "string", "enum": ["status", "list", "info", "nodes", "edges", "properties", "add_property", "set_property_default", "create", "open", "close", "undo", "add_node", "remove_node", "move_node", "connect", "disconnect", "set_value", "node_types"]},
            "path": {"type": "string", "description": "Assets/ 下的 .shadergraph 路径"},
            "nodeType": {"type": "string", "description": "节点类型，如 Add、Multiply、Property"},
            "nodeId": {"type": "string"},
            "propertyId": {"type": "string", "description": "黑板属性 objectId"},
            "name": {"type": "string", "description": "新增属性名"},
            "type": {"type": "string", "enum": ["float", "vector2", "vector3", "vector4", "color", "bool", "texture2d"]},
            "x": {"type": "number"}, "y": {"type": "number"},
            "outputNodeId": {"type": "string"}, "outputSlotId": {"type": "integer"},
            "outputSlot": {"type": "string", "description": "输出槽名，如 Out"},
            "inputNodeId": {"type": "string"}, "inputSlotId": {"type": "integer"},
            "inputSlot": {"type": "string", "description": "输入槽名，如 A、Base Color"},
            "field": {"type": "string", "description": "set_value 字段，默认 m_Value"},
            "value": {"type": "string", "description": "数值或逗号分隔向量，如 0.5、1,0,0,1"},
            "template": {"type": "string", "enum": ["lit", "unlit", "sprite_lit", "sprite_unlit", "blank"]},
            "overwrite": {"type": "boolean"}, "filter": {"type": "string"},
            "max": {"type": "integer"}, "withSlots": {"type": "boolean"}
        }, "required": ["op"]}
    },
    {
        "name": "unity_physics",
        "description": "只读物理查询：射线、球形、盒形和重叠检测。坐标为 x,y,z 字符串，返回命中对象、距离、点及法线。layerMask 支持层名或整型掩码字符串。",
        "inputSchema": {"type": "object", "properties": {
            "op": {"type": "string", "enum": ["raycast", "raycast_all", "spherecast", "spherecast_all", "boxcast", "linecast", "overlap_sphere", "overlap_box"]},
            "origin": {"type": "string"}, "direction": {"type": "string"},
            "position": {"type": "string"}, "from": {"type": "string"}, "to": {"type": "string"},
            "maxDistance": {"type": "number"}, "radius": {"type": "number"},
            "halfExtents": {"type": "string", "description": "盒的半尺寸 x,y,z"},
            "rotation": {"type": "string", "description": "四元数 x,y,z,w"},
            "layerMask": {"type": "string"}, "queryTriggers": {"type": "boolean"},
            "maxResults": {"type": "integer"}
        }, "required": ["op"]}
    },
    {
        "name": "unity_profiler",
        "description": "性能采样：status 查询状态；start 后等待编辑器渲染几帧，再用 stats/overview 获取 CPU、GC、内存和渲染统计；stop 停止；save 保存 .raw。无帧时返回 pending，请稍后查询。",
        "inputSchema": {"type": "object", "properties": {
            "op": {"type": "string", "enum": ["status", "start", "stop", "stats", "overview", "save"]},
            "deep": {"type": "boolean", "description": "深度采样，开销大"},
            "gpu": {"type": "boolean"}, "reset": {"type": "boolean"},
            "frames": {"type": "integer", "description": "stats 最近帧数"},
            "area": {"type": "string", "description": "overview 区域，如 CPU、Rendering、Memory、GPU"},
            "path": {"type": "string", "description": "save 输出 .raw 路径"}
        }, "required": ["op"]}
    },
    {
        "name": "unity_asset_preview",
        "description": "将材质、Prefab、贴图或图标渲染为 PNG，返回磁盘路径。op=get 用 path；batch 用 paths。预览异步生成时返回 pending=true，稍后重试。",
        "inputSchema": {"type": "object", "properties": {
            "op": {"type": "string", "enum": ["get", "batch"]},
            "path": {"type": "string", "description": "Assets/ 下的资源路径"},
            "paths": {"type": "string", "description": "逗号或换行分隔的资源路径"},
            "width": {"type": "integer", "minimum": 32, "maximum": 1024},
            "height": {"type": "integer", "minimum": 32, "maximum": 1024},
            "transparent": {"type": "boolean"}, "maxResults": {"type": "integer"}
        }, "required": ["op"]}
    },
    {
        "name": "unity_frame_debugger",
        "description": "帧调试：status 查询；enable/disable 开关捕获；events/details 获取事件；summary 汇总 DrawCall、三角形、着色器及合批中断原因。捕获后可能返回 pending，请等待编辑器渲染一帧再查询。",
        "inputSchema": {"type": "object", "properties": {
            "op": {"type": "string", "enum": ["status", "enable", "disable", "events", "details", "summary"]},
            "offset": {"type": "integer"}, "limit": {"type": "integer"},
            "index": {"type": "integer"}, "keepEnabled": {"type": "boolean", "description": "summary 后保留捕获"}
        }, "required": ["op"]}
    }
])

BATCH_TOOLS = {
    "unity_set_property", "unity_get_component", "unity_get_scene_graph", "unity_get_scene_roots",
    "unity_get_selection", "unity_find_assets", "unity_get_prefab_structure", "unity_physics",
}
ALL_TOOLS.append({
    "name": "unity_batch",
    "description": "批量属性编辑与只读验证。先校验全部步骤再顺序执行，首个错误或断言失败即停止；不是事务，不自动回滚，已完成的属性编辑可在 Unity Undo。默认 dry_run=true 仅检查参数结构，不查询对象或验证属性存在。expect 对工具返回 JSON 的点分隔路径进行精确相等断言。",
    "inputSchema": {"type": "object", "additionalProperties": False, "properties": {
        "dry_run": {"type": "boolean", "default": True},
        "steps": {"type": "array", "minItems": 1, "maxItems": 50, "items": {
            "type": "object", "additionalProperties": False, "properties": {
                "tool": {"type": "string", "enum": sorted(BATCH_TOOLS)},
                "arguments": {"type": "object"},
                "expect": {"type": "object", "additionalProperties": False, "properties": {
                    "path": {"type": "string", "description": "JSON 路径，如 hit、roots.0；空串表示整个结果"},
                    "equals": {"description": "期望的 JSON 值"}
                }, "required": ["path", "equals"]}
            }, "required": ["tool", "arguments"]
        }}
    }, "required": ["steps"]}
})


def validate_arguments(value, schema, path="arguments"):
    kind = schema.get("type")
    valid = {
        "object": isinstance(value, dict), "array": isinstance(value, list),
        "string": isinstance(value, str), "boolean": type(value) is bool,
        "integer": type(value) is int,
        "number": type(value) in (int, float) and math.isfinite(value),
    }
    if kind and not valid.get(kind, False):
        raise ValueError(path + " 类型应为 " + kind)
    if "enum" in schema and value not in schema["enum"]:
        raise ValueError(path + " 不在允许值中")
    if isinstance(value, dict):
        props = schema.get("properties", {})
        for key in schema.get("required", []):
            if key not in value: raise ValueError(path + " 缺少 " + key)
        for key, item in value.items():
            if key not in props:
                if schema.get("additionalProperties") is False: raise ValueError(path + " 未知参数 " + key)
            else:
                validate_arguments(item, props[key], path + "." + key)
    elif isinstance(value, list):
        if not schema.get("minItems", 0) <= len(value) <= schema.get("maxItems", len(value)):
            raise ValueError(path + " 数量超出范围")
        for index, item in enumerate(value):
            validate_arguments(item, schema.get("items", {}), path + "." + str(index))
    elif type(value) in (int, float):
        if not math.isfinite(value) or value < schema.get("minimum", -math.inf) or value > schema.get("maximum", math.inf):
            raise ValueError(path + " 数值超出范围")


def json_equal(actual, expected):
    if type(actual) in (int, float) and type(expected) in (int, float):
        return actual == expected
    if type(actual) is not type(expected):
        return False
    if isinstance(actual, dict):
        return actual.keys() == expected.keys() and all(json_equal(actual[k], expected[k]) for k in actual)
    if isinstance(actual, list):
        return len(actual) == len(expected) and all(json_equal(a, b) for a, b in zip(actual, expected))
    return actual == expected


def run_batch(args):
    definitions = {t["name"]: t for t in TOOLS}
    validate_arguments(args, definitions["unity_batch"]["inputSchema"])
    for i, step in enumerate(args["steps"]):
        if step["tool"] not in definitions: raise ValueError("步骤工具未在当前分组启用: " + step["tool"])
        schema = dict(definitions[step["tool"]]["inputSchema"], additionalProperties=False)
        validate_arguments(step["arguments"], schema, "steps." + str(i) + ".arguments")
    if args.get("dry_run", True):
        return json.dumps({"ok": True, "dryRun": True, "validatedSteps": len(args["steps"]), "executedSteps": 0})
    project = bound_project_path()
    binding = cmd_use_project(project)
    if str(binding).startswith("❌"): raise RuntimeError(binding)
    results = []
    deadline = time.monotonic() + 120
    for i, step in enumerate(args["steps"]):
        if time.monotonic() >= deadline:
            return json.dumps({"ok": False, "error": "批处理达到时间上限，剩余步骤未执行", "projectPath": project, "results": results, "executedSteps": len(results)}, ensure_ascii=False)
        result = handle(step["tool"], step["arguments"])
        try: data = json.loads(result)
        except ValueError: data = str(result)
        item = {"index": i, "tool": step["tool"], "ok": not result.is_error, "result": data}
        if isinstance(data, dict) and data.get("pending"):
            item.update(ok=False, error="操作尚未完成，后续步骤未执行")
        if item["ok"] and "expect" in step:
            try:
                actual = data
                for segment in step["expect"]["path"].split(".") if step["expect"]["path"] else []:
                    actual = actual[int(segment)] if isinstance(actual, list) else actual[segment]
                # JSON bool 与 number 不视为相等。
                item["verified"] = json_equal(actual, step["expect"]["equals"])
                if not item["verified"]: item.update(ok=False, error="验证结果与期望不符", actual=actual)
            except (KeyError, IndexError, TypeError, ValueError):
                item.update(ok=False, verified=False, error="验证路径不存在或不可读取")
        results.append(item)
        if not item["ok"]: break
    return json.dumps({"ok": len(results) == len(args["steps"]) and all(r["ok"] for r in results),
                       "dryRun": False, "projectPath": project, "executedSteps": len(results),
                       "totalSteps": len(args["steps"]), "results": results}, ensure_ascii=False)


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
        "unity_pipeline_job",
    ],
    "assets": [  # 资源管理 — 6 tools
        "unity_select_asset",
        "unity_find_assets",
        "unity_import_assets",
        "unity_refresh_asset_database",
        "unity_recompile_scripts",
        "unity_get_prefab_structure",
    ],
    "scene": [  # 场景 & 运行时，以及批量编辑验证
        "unity_batch",
        "unity_execute_code",
        "unity_get_selection",
        "unity_get_scene_graph",
        "unity_get_scene_roots",
        "unity_play_mode",
        "unity_screenshot_to_file",
        "unity_get_component",
        "unity_set_property",
    ],
    "shader": ["unity_shadergraph"],
    "inspect": ["unity_physics", "unity_profiler", "unity_asset_preview", "unity_frame_debugger"],
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

def validate_registry():
    names = [tool["name"] for tool in ALL_TOOLS]
    if len(names) != len(set(names)):
        raise ValueError("MCP 工具名称重复")
    for group, members in TOOL_GROUPS.items():
        if not set(members) <= set(names):
            raise ValueError("分组引用未注册工具: " + group)
    if not set(ADVANCED_MESSAGES) <= set(names):
        raise ValueError("高级工具缺少 schema 注册")

validate_registry()


def handle(name: str, args: dict, close_after: bool = False) -> str:
    if name not in {t["name"] for t in TOOLS}:
        return PipelineToolResult(json.dumps({"ok": False, "error": "工具未注册或未在当前分组启用: " + str(name)}, ensure_ascii=False), True)
    if not isinstance(args, dict):
        return PipelineToolResult('{"ok":false,"error":"arguments must be an object"}', True)
    try:
        definition = next(t for t in TOOLS if t["name"] == name)
        validate_arguments(args, definition["inputSchema"])
    except (ValueError, TypeError) as e:
        return PipelineToolResult(json.dumps({"ok": False, "error": str(e)}, ensure_ascii=False), True)
    return tool_result(_handle_impl(name, args, close_after))


def _unity_call(*args, **kwargs):
    result = UP.call(*args, **kwargs)
    if not result.get("ok"):
        raise RuntimeError(result.get("error", result.get("message", "Unity 请求失败")))
    return result


def _handle_impl(name: str, args: dict, close_after: bool = False) -> str:
    # close_after 默认 False：stdio 进程常驻，复用同一条 TCP 连接，
    # 避免每次工具调用都断开重连（重连要扫实例目录 + 暖场 ping，最多 5s）。
    # 需要强制本次调用后关闭连接时显式传 True。
    try:
        if name == "unity_batch":
            return run_batch(args)
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
            return json.dumps({"ok": bool(r.get("ok")), "state": r.get("message"), "error": r.get("error"),
                               "projectPath": r.get("projectPath"), "bridge": bridge_health(r.get("projectPath"))}, ensure_ascii=False)
        elif name == "unity_execute_code":
            # 首次调用要冷启动 Roslyn（245 个引用），实测可能远超 30s；
            # 超时会被报成「结果未知」，所以这里单独放宽到 120s
            r = UP.call("execute_code", args["code"], timeout_s=120.0)
            if r.get("ok"):
                return f"✅ 执行成功:\n{r.get('message', '(无输出)')}"
            return f"❌ 失败:\n{r.get('error', r.get('message', str(r)))}"
        elif name == "unity_select_asset":
            _unity_call("select_asset", args["asset_path"])
            return f"✅ 已选中: {args['asset_path']}"
        elif name == "unity_import_assets":
            r = _unity_call("import_assets", args["paths"])
            return f"✅ {r.get('message', 'done')}"
        elif name == "unity_refresh_asset_database":
            _unity_call("refresh_asset_database")
            return "✅ 已刷新"
        elif name == "unity_recompile_scripts":
            _unity_call("request_recompile")
            return "✅ 已触发"
        elif name == "unity_get_console_logs":
            c = str(args.get("count", 50))
            r = _unity_call("get_console_logs", c)
            return f"{r.get('message', r)}"
        elif name == "unity_clear_console":
            r = _unity_call("clear_console")
            return f"✅ {r.get('message', '已清空')}"
        elif name == "unity_get_selection":
            r = _unity_call("get_selection")
            return f"当前选中:\n{r.get('message', r)}"
        elif name == "unity_find_assets":
            r = _unity_call("find_assets", args["filter"])
            return f"{r.get('message', r)}"
        elif name == "unity_get_scene_graph":
            d = str(args.get("depth", 0))
            r = _unity_call("get_scene_graph", d)
            return f"{r.get('message', r)}"
        elif name == "unity_get_scene_roots":
            r = _unity_call("get_scene_roots")
            return f"{r.get('message', r)}"
        elif name == "unity_play_mode":
            r = _unity_call("play_mode", "")
            return f"{r.get('message', r)}"
        elif name == "unity_screenshot":
            t = args.get("target", "game")
            r = _unity_call("screenshot", t)
            m = str(r.get("message", r))
            if len(m) > 200:
                return f"✅ 截图成功（{len(m)} 字符 base64，已弃用，建议 screenshot_to_file）"
            return f"❌ {r.get('error', m)}"
        elif name == "unity_screenshot_to_file":
            spec = args.get("spec", "game|")
            r = _unity_call("screenshot_to_file", spec)
            return f"{r.get('message', r)}"
        elif name == "unity_subscribe":
            types = args.get("types", "all")
            r = _unity_call("subscribe", types)
            return f"{r.get('message', r)}"
        elif name == "unity_subscribe_status":
            r = _unity_call("subscribe_status", "")
            return f"{r.get('message', r)}"
        elif name == "unity_poll_events":
            spec = args.get("spec", "500|10")
            r = _unity_call("poll_events", spec)
            return f"{r.get('message', r)}"
        elif name == "unity_unsubscribe":
            r = _unity_call("unsubscribe", "")
            return f"{r.get('message', r)}"
        elif name in ADVANCED_MESSAGES:
            return run_advanced_tool(name, args)
        elif name in ("unity_cli", "unity_pipeline", "unity_pipeline_commands", "unity_pipeline_job"):
            return run_pipeline_tool(name, args)
        elif name == "unity_get_component":
            r = _unity_call("get_component", args["target"])
            return f"{r.get('message', r)}"
        elif name == "unity_set_property":
            r = UP.call("set_property", args["spec"])
            if not r.get("ok"):
                return f"❌ {r.get('error', r.get('message', str(r)))}"
            return f"{r.get('message', 'ok')}"
        elif name == "unity_get_prefab_structure":
            r = _unity_call("get_prefab_structure", args["asset_path"])
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

    # 沿用已有 xybridge* 条目里的 command（用户可能指定了某个具体的解释器路径，
    # 例如 pyenv / venv 里的 python3），避免每次安装都把它改回 PATH 上的默认值
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
    # 启动时只做一次「报告」，不建立连接：Unity 可能还没起来，
    # 真正的连接推迟到第一次工具调用（UnitySocket.call 内自动 connect）。
    _inst, _err = resolve_target()
    if _inst:
        print(
            f"[YZJ-MCP:{server_name}] 启动, 目标实例: "
            f"{_inst.get('projectPath')} [{_endpoint(_inst)}], tools: {len(TOOLS)}",
            file=sys.stderr,
        )
    else:
        print(
            f"[YZJ-MCP:{server_name}] 启动, 未绑定目标"
            f"（存活实例 {len(list_instances())} 个）, tools: {len(TOOLS)}",
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
                            "serverInfo": {"name": server_name, "version": SERVER_VERSION},
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
    # MCP stdio 始终使用 UTF-8，避免 Windows 本地代码页使中文日志解码失败。
    for _stream in (sys.stdin, sys.stdout, sys.stderr):
        if hasattr(_stream, "reconfigure"):
            _stream.reconfigure(encoding="utf-8")
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
        "shader": "xybridge-shader",
        "inspect": "xybridge-inspect",
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
