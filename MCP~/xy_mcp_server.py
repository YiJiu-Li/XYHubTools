"""
YZJBridge MCP Server
AI 通过 MCP 协议操控 Unity Editor
只需: pip install pywin32
"""

import json, os, sys, time, threading
import win32file, win32pipe, pywintypes

ROOT = os.path.dirname(os.path.abspath(__file__))
SANITIZED = (
    ROOT.replace("\\", "_").replace("/", "_").replace(":", "_").replace(" ", "_")
)
PIPE = r"\\.\pipe\xybridge_unity_" + SANITIZED


# ═══════════════ Named Pipe 客户端（自动重连） ═══════════════
class UnityPipe:
    def __init__(self):
        self._h = None
        self._n = 0
        self._lock = threading.Lock()
        self._buf = b""

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
        deadline = time.time() + max(timeout_ms, 1) / 1000.0
        while time.time() < deadline:
            try:
                wait_ms = max(1, int((deadline - time.time()) * 1000))
                win32pipe.WaitNamedPipe(PIPE, min(wait_ms, 500))
                self._h = win32file.CreateFile(
                    PIPE,
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
                    if err is not None and isinstance(err, pywintypes.error) and err.winerror in (109, 233):
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
                return {"ok": False, "error": "Unity 未连接"}

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
                    if err is not None and isinstance(err, pywintypes.error) and err.winerror in (109, 233):
                        self._h = None
                        return {"ok": False, "error": "管道断开"}
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
]

# ═══════════════ 分组（每组 ≤9 工具，绕过 VS Code 截断） ═══════════════
TOOL_GROUPS = {
    "core": [  # 基础通讯 — 4 tools
        "unity_ping",
        "unity_status",
        "unity_get_console_logs",
        "unity_clear_console",
    ],
    "assets": [  # 资源管理 — 5 tools
        "unity_select_asset",
        "unity_find_assets",
        "unity_import_assets",
        "unity_refresh_asset_database",
        "unity_recompile_scripts",
    ],
    "scene": [  # 场景 & 运行时 — 7 tools
        "unity_execute_code",
        "unity_get_selection",
        "unity_get_scene_graph",
        "unity_get_scene_roots",
        "unity_play_mode",
        "unity_screenshot_to_file",
        "unity_get_component",
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


def handle(name: str, args: dict, close_after: bool = True) -> str:
    try:
        if name == "unity_ping":
            r = UP.call("ping")
            if not r.get("ok"):
                return f"❌ Unity 未连接: {r.get('error', r.get('message', r))}"
            return f"✅ pong | PID: {r.get('processId', '?')}"
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
        elif name == "unity_get_component":
            r = UP.call("get_component", args["target"])
            return f"{r.get('message', r)}"
        else:
            return f"未知: {name}"
    except Exception as e:
        return f"❌ {e}"
    finally:
        if close_after:
            UP.close()


# ═══════════════ SSE 传输（HTTP 端口，Unity 可直接启动）═══════════════
def run_sse_server(port: int, server_name: str):
    """基于内置 http.server 的 MCP SSE 传输，无需额外依赖"""
    import threading
    import uuid
    import queue
    from http.server import HTTPServer, BaseHTTPRequestHandler

    sessions = {}  # sessionId -> Queue
    sessions_lock = threading.Lock()

    def dispatch(sid, body):
        mid = None
        try:
            msg = json.loads(body)
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
            elif m == "notifications/initialized":
                return
            elif m == "tools/list":
                resp = json.dumps(
                    {"jsonrpc": "2.0", "id": mid, "result": {"tools": TOOLS}}
                )
            elif m == "tools/call":
                tn = msg["params"]["name"]
                ta = msg["params"].get("arguments", {})
                txt = handle(tn, ta, close_after=False)
                resp = json.dumps(
                    {
                        "jsonrpc": "2.0",
                        "id": mid,
                        "result": {"content": [{"type": "text", "text": txt}]},
                    }
                )
            else:
                resp = json.dumps(
                    {
                        "jsonrpc": "2.0",
                        "id": mid,
                        "error": {"code": -32601, "message": f"Method not found: {m}"},
                    }
                )
            if sid:
                with sessions_lock:
                    q = sessions.get(sid)
                if q:
                    q.put(resp)
        except Exception as e:
            if mid is not None and sid:
                with sessions_lock:
                    q = sessions.get(sid)
                if q:
                    q.put(
                        json.dumps(
                            {
                                "jsonrpc": "2.0",
                                "id": mid,
                                "error": {"code": -32603, "message": str(e)},
                            }
                        )
                    )

    class MCPHandler(BaseHTTPRequestHandler):
        def log_message(self, fmt, *a):
            print(f"[YZJ-MCP:{server_name}] HTTP {fmt % a}", file=sys.stderr)

        def _cors(self):
            self.send_header("Access-Control-Allow-Origin", "*")
            self.send_header("Access-Control-Allow-Methods", "GET, POST, OPTIONS")
            self.send_header("Access-Control-Allow-Headers", "Content-Type")

        def do_OPTIONS(self):
            self.send_response(200)
            self._cors()
            self.end_headers()

        def do_GET(self):
            if not self.path.startswith("/sse"):
                self.send_response(404)
                self.end_headers()
                return
            sid = str(uuid.uuid4())
            q = queue.Queue()
            with sessions_lock:
                sessions[sid] = q
            self.send_response(200)
            self.send_header("Content-Type", "text/event-stream")
            self.send_header("Cache-Control", "no-cache")
            self.send_header("Connection", "keep-alive")
            self._cors()
            self.end_headers()
            try:
                self.wfile.write(
                    f"event: endpoint\ndata: /message?sessionId={sid}\n\n".encode()
                )
                self.wfile.flush()
                while True:
                    try:
                        data = q.get(timeout=20)
                        if data is None:
                            break
                        self.wfile.write(f"event: message\ndata: {data}\n\n".encode())
                        self.wfile.flush()
                    except queue.Empty:
                        self.wfile.write(b": keepalive\n\n")
                        self.wfile.flush()
            except Exception:
                pass
            finally:
                with sessions_lock:
                    sessions.pop(sid, None)

        def do_POST(self):
            if not self.path.startswith("/message"):
                self.send_response(404)
                self.end_headers()
                return
            sid = None
            if "?" in self.path:
                for part in self.path.split("?", 1)[1].split("&"):
                    if part.startswith("sessionId="):
                        sid = part[10:]
            length = int(self.headers.get("Content-Length", 0))
            body = self.rfile.read(length).decode("utf-8")
            self.send_response(202)
            self.send_header("Content-Length", "0")
            self._cors()
            self.end_headers()
            threading.Thread(target=dispatch, args=(sid, body), daemon=True).start()

    httpd = HTTPServer(("127.0.0.1", port), MCPHandler)
    print(
        f"[YZJ-MCP:{server_name}] SSE 启动: http://127.0.0.1:{port}/sse",
        file=sys.stderr,
    )
    sys.stderr.flush()
    httpd.serve_forever()


def main(server_name: str = "yzjbridge"):
    global TOOLS
    print(
        f"[YZJ-MCP:{server_name}] 启动, 管道: {PIPE}, tools: {len(TOOLS)}",
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
                            "result": {"content": [{"type": "text", "text": txt}]},
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
    for a in sys.argv[1:]:
        if a.startswith("--group="):
            group = a.split("=", 1)[1]
        elif a.startswith("--transport="):
            transport = a.split("=", 1)[1]
        elif a.startswith("--port="):
            port = int(a.split("=", 1)[1])

    name_map = {
        "core": "xybridge-core",
        "assets": "xybridge-assets",
        "scene": "xybridge-scene",
        "events": "xybridge-events",
    }
    if group in TOOL_GROUPS:
        names = TOOL_GROUPS[group]
        TOOLS = [t for t in ALL_TOOLS if t["name"] in names]
        sname = name_map.get(group, "xybridge")
        print(
            f"[YZJ-MCP] 分组: {group} ({len(TOOLS)} tools), 传输: {transport}",
            file=sys.stderr,
        )
        if transport == "sse":
            run_sse_server(port, sname)
        else:
            main(sname)
    else:
        print(f"[YZJ-MCP] 全量模式, 传输: {transport}", file=sys.stderr)
        if transport == "sse":
            run_sse_server(port, "xybridge")
        else:
            main("xybridge")
