"""Bridge 可靠性测试（TCP 版，2.0 起）。

不依赖 Unity、不依赖 Windows：用线程里的假 Unity 服务端讲同一套 NDJSON 协议，
验证「发现实例 → 握手 → 请求 → 异常」全链路，重点是不变量：

  · 实例文件只是候选，能连上才算存活
  · token 不对必须在握手阶段就失败，不能等到每个工具调用都报错
  · 写入失败 / 超时 / 断开一律不自动重放（执行结果未知时重放会改两次）
  · 服务返回的错误要透传到 MCP 的 isError，不能被当成成功

运行：python test_bridge_reliability.py
"""

import importlib.util
import io
import json
import os
import shutil
import socket
import tempfile
import threading
import unittest
from pathlib import Path
from unittest.mock import patch

spec = importlib.util.spec_from_file_location(
    "xy_reliability", Path(__file__).with_name("xy_mcp_server.py")
)
m = importlib.util.module_from_spec(spec)
spec.loader.exec_module(m)


# ═══════════════════ 假 Unity 服务端 ═══════════════════
class FakeUnity:
    """最小可用的 Bridge 服务端：NDJSON、token 校验、可编排的异常行为。"""

    def __init__(self, token="tok", behavior=None):
        self.token = token
        self.behavior = behavior or "normal"  # normal | close | silent | error
        self.requests = []
        self.connections = 0
        self._srv = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
        self._srv.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
        self._srv.bind(("127.0.0.1", 0))
        self._srv.listen(8)
        self.port = self._srv.getsockname()[1]
        self._closed = False
        threading.Thread(target=self._accept_loop, daemon=True).start()

    def _accept_loop(self):
        while not self._closed:
            try:
                conn, _ = self._srv.accept()
            except OSError:
                return
            threading.Thread(target=self._serve, args=(conn,), daemon=True).start()

    def _serve(self, conn):
        self.connections += 1
        try:
            conn.sendall(
                (json.dumps({"type": "unity_connected", "message": "connected", "processId": 4242}) + "\n").encode()
            )
            buf = b""
            while True:
                chunk = conn.recv(65536)
                if not chunk:
                    return
                buf += chunk
                while b"\n" in buf:
                    line, buf = buf.split(b"\n", 1)
                    if not line.strip():
                        continue
                    msg = json.loads(line.decode())
                    self.requests.append(msg)
                    if msg.get("token") != self.token:
                        conn.sendall(
                            (json.dumps({"type": "response", "reply_to": msg.get("id"),
                                         "ok": False, "error": "认证失败：令牌不匹配。"}) + "\n").encode()
                        )
                        continue
                    # 异常编排只作用于业务请求：握手 ping 正常应答，
                    # 否则测到的是「连不上」，而不是「请求发出后断开/超时」。
                    if msg.get("type") != "ping":
                        if self.behavior == "close":
                            conn.close()
                            return
                        if self.behavior == "silent":
                            continue
                    conn.sendall((json.dumps(self._reply(msg)) + "\n").encode())
        except OSError:
            return
        finally:
            try:
                conn.close()
            except OSError:
                pass

    def _reply(self, msg):
        if self.behavior == "error":
            return {"type": "response", "reply_to": msg.get("id"), "ok": True,
                    "message": '{"ok":false,"error":"missing asset"}'}
        return {"type": "response", "reply_to": msg.get("id"), "ok": True,
                "message": "pong" if msg.get("type") == "ping" else "ok",
                "processId": 4242, "projectPath": "C:/FakeProject"}

    def types(self):
        return [r.get("type") for r in self.requests]

    def close(self):
        self._closed = True
        try:
            self._srv.close()
        except OSError:
            pass


class InstanceDirMixin:
    """把模块的实例目录换成临时目录，避免碰到真实的 ~/.xybridge。"""

    def setUp(self):
        self.dir = tempfile.mkdtemp(prefix="xybridge-test-")
        self.patch = patch.object(m, "INSTANCE_DIR", self.dir)
        self.patch.start()
        self.addCleanup(self.patch.stop)
        self.addCleanup(shutil.rmtree, self.dir, True)

    def write_instance(self, port, token="tok", project="C:/FakeProject", schema=2, name=None):
        data = {
            "schema": schema,
            "transport": "tcp",
            "host": "127.0.0.1",
            "port": port,
            "pid": 4242,
            "token": token,
            "projectPath": project,
            "unityVersion": "2022.3.53f1",
            "bridgeVersion": "2.0.0",
            "startedAt": "2026-10-23T00:00:00.0000000Z",
        }
        path = os.path.join(self.dir, name or "xy-test-4242.json")
        with open(path, "w", encoding="utf-8") as f:
            json.dump(data, f)
        return path

    def connect(self, inst):
        sock = m.UnitySocket(inst, explicit=True)
        self.addCleanup(sock.close)
        return sock


class TransportTests(InstanceDirMixin, unittest.TestCase):
    def test_instance_file_is_discovered_and_handshake_binds_project(self):
        unity = FakeUnity()
        self.addCleanup(unity.close)
        path = self.write_instance(unity.port)
        live = m.list_instances()
        self.assertEqual(len(live), 1)
        self.assertEqual(live[0]["path"], path)

        sock = self.connect(m._load_instance(path))
        self.assertTrue(sock.connect())
        self.assertEqual(sock.project_path, "C:/FakeProject")
        self.assertIn("ping", unity.types())

    def test_dead_port_is_not_treated_as_alive(self):
        unity = FakeUnity()
        port = unity.port
        unity.close()
        self.write_instance(port)
        self.assertEqual(m.list_instances(), [])

    def test_legacy_or_malformed_instance_is_skipped(self):
        unity = FakeUnity()
        self.addCleanup(unity.close)
        self.write_instance(unity.port, schema=1)
        self.write_instance(unity.port, schema=2, name="broken.json")
        with open(os.path.join(self.dir, "broken.json"), "w", encoding="utf-8") as f:
            f.write("{not json")
        self.assertEqual(m.list_instances(), [])

    def test_token_mismatch_fails_at_handshake(self):
        unity = FakeUnity(token="right")
        self.addCleanup(unity.close)
        sock = self.connect(m._load_instance(self.write_instance(unity.port, token="wrong")))
        self.assertFalse(sock.connect())
        self.assertIn("令牌", sock._last_error or "")

    def test_disconnect_does_not_replay_the_write(self):
        unity = FakeUnity(behavior="close")
        self.addCleanup(unity.close)
        sock = self.connect(m._load_instance(self.write_instance(unity.port)))
        result = sock.call("set_property", "Cube|m_Name|NewName")
        self.assertFalse(result["ok"])
        self.assertIn("未自动重放", result["error"])
        self.assertEqual(unity.types().count("set_property"), 1)

    def test_timeout_does_not_replay_the_write(self):
        unity = FakeUnity(behavior="silent")
        self.addCleanup(unity.close)
        sock = self.connect(m._load_instance(self.write_instance(unity.port)))
        result = sock.call("set_property", "Cube|m_Name|NewName", timeout_s=0.4)
        self.assertFalse(result["ok"])
        self.assertIn("未自动重放", result["error"])
        self.assertEqual(unity.types().count("set_property"), 1)

    def test_instance_file_change_forces_reconnect(self):
        """域重载会换端口和令牌；实例文件变了就要重连，不能让第一次调用撞死。"""
        first, second = FakeUnity(token="tok"), FakeUnity(token="rotated")
        self.addCleanup(first.close)
        self.addCleanup(second.close)
        path = self.write_instance(first.port, token="tok")
        sock = self.connect(m._load_instance(path))
        self.assertTrue(sock.connect())
        self.assertEqual(sock.call("ping")["ok"], True)

        # 模拟 Unity 重载：同一路径重写，端口与令牌都变，mtime 必须往前走
        self.write_instance(second.port, token="rotated")
        os.utime(path, (os.path.getmtime(path) + 5, os.path.getmtime(path) + 5))

        result = sock.call("ping")
        self.assertTrue(result["ok"], result)
        self.assertEqual(second.connections, 1)
        self.assertEqual(first.types().count("ping"), 2)  # 握手 + 显式，之后不再打旧端口

    def test_connection_is_reused_between_calls(self):
        unity = FakeUnity()
        self.addCleanup(unity.close)
        sock = self.connect(m._load_instance(self.write_instance(unity.port)))
        sock.call("ping")
        sock.call("ping")
        # 两次调用共用一条连接：只握手一次（1 次握手 ping + 2 次显式 ping）
        self.assertEqual(unity.connections, 1)
        self.assertEqual(unity.types().count("ping"), 3)


class ServerContractTests(InstanceDirMixin, unittest.TestCase):
    def test_service_error_reaches_mcp(self):
        with patch.object(m.UP, "call", return_value={"ok": True, "message": '{"ok":false,"error":"missing asset"}'}):
            self.assertTrue(m.handle("unity_asset_preview", {"op": "get", "path": "Assets/missing.png"}).is_error)

    def test_stdio_response_marks_service_error(self):
        request = {"jsonrpc": "2.0", "id": 9, "method": "tools/call", "params": {
            "name": "unity_asset_preview", "arguments": {"op": "get", "path": "Assets/missing.png"}}}
        output = io.StringIO()
        target = {"host": "127.0.0.1", "port": 1, "projectPath": "test", "token": "t"}
        with patch.object(m.sys, "stdin", io.StringIO(json.dumps(request) + "\n")), \
             patch.object(m.sys, "stdout", output), patch.object(m.sys, "stderr", io.StringIO()), \
             patch.object(m, "resolve_target", return_value=(target, None)), \
             patch.object(m, "list_instances", return_value=[target]), \
             patch.object(m.UP, "call", return_value={"ok": True, "message": '{"ok":false,"error":"missing"}'}), \
             patch.object(m.UP, "close"):
            m.main()
        response = json.loads(output.getvalue())
        self.assertEqual(response["id"], 9)
        self.assertTrue(response["result"]["isError"])

    def test_stdio_initialize_reports_version(self):
        request = {"jsonrpc": "2.0", "id": 1, "method": "initialize"}
        output = io.StringIO()
        with patch.object(m.sys, "stdin", io.StringIO(json.dumps(request) + "\n")), \
             patch.object(m.sys, "stdout", output), patch.object(m.sys, "stderr", io.StringIO()), \
             patch.object(m, "list_instances", return_value=[]), patch.object(m.UP, "close"):
            m.main()
        response = json.loads(output.getvalue())
        self.assertEqual(response["result"]["serverInfo"]["version"], m.SERVER_VERSION)

    def test_legacy_writes_do_not_claim_success_after_transport_failure(self):
        with patch.object(m.UP, "call", return_value={"ok": False, "error": "disconnected"}):
            for name, args in [("unity_select_asset", {"asset_path": "Assets/a"}),
                               ("unity_import_assets", {"paths": "Assets/a"}),
                               ("unity_refresh_asset_database", {}), ("unity_recompile_scripts", {})]:
                self.assertTrue(m.handle(name, args).is_error, name)

    def test_unknown_tool_and_bad_arguments(self):
        with patch.object(m.UP, "call") as call:
            self.assertTrue(m.handle("missing", {}).is_error)
            self.assertTrue(m.handle("unity_physics", {"op": "missing"}).is_error)
            self.assertTrue(m.handle("unity_set_property", {}).is_error)
            call.assert_not_called()

    def test_running_hash_detects_disk_change(self):
        with patch.object(m, "script_info", return_value={"sha256": "different"}):
            self.assertTrue(m.bridge_health(None)["restartRequired"])

    def test_json_assertions_distinguish_nested_booleans_from_numbers(self):
        self.assertFalse(m.json_equal({"values": [True]}, {"values": [1]}))
        self.assertTrue(m.json_equal({"value": 1.0}, {"value": 1}))


class BatchTests(unittest.TestCase):
    def setUp(self):
        self.patch = patch.object(m.UP, "call", return_value={"ok": True, "message": '{"hit":false}'})
        self.call = self.patch.start()
        self.addCleanup(self.patch.stop)
        self.steps = [{"tool": "unity_physics", "arguments": {"op": "raycast", "origin": "0,1,0", "direction": "0,-1,0"},
                       "expect": {"path": "hit", "equals": False}}]

    def test_dry_run_never_calls_unity(self):
        result = m.handle("unity_batch", {"steps": self.steps})
        self.assertFalse(result.is_error)
        self.assertEqual(json.loads(result)["executedSteps"], 0)
        self.call.assert_not_called()

    def test_entire_batch_preflight_before_writes(self):
        steps = [{"tool": "unity_set_property", "arguments": {"spec": "Cube|m_Name|NewName"}},
                 {"tool": "unity_physics", "arguments": {"op": "typo"}}]
        result = m.handle("unity_batch", {"steps": steps, "dry_run": False})
        self.assertTrue(result.is_error)
        self.call.assert_not_called()

    def test_unknown_arguments_and_disallowed_tools_rejected(self):
        for step in [{"tool": "unity_execute_code", "arguments": {"code": ""}},
                     {"tool": "unity_set_property", "arguments": {"spec": "a|b|c", "typo": True}}]:
            self.assertTrue(m.handle("unity_batch", {"steps": [step]}).is_error)
        self.call.assert_not_called()

    def execute(self, steps):
        with patch.object(m, "bound_project_path", return_value="project"), patch.object(m, "cmd_use_project", return_value="bound"):
            return m.handle("unity_batch", {"steps": steps, "dry_run": False})

    def test_assertion_success(self):
        result = self.execute(self.steps)
        self.assertFalse(result.is_error)
        self.assertTrue(json.loads(result)["results"][0]["verified"])

    def test_property_edit_then_readback(self):
        self.call.side_effect = [
            {"ok": True, "message": '{"ok":true}'},
            {"ok": True, "message": '{"components":[{"properties":{"m_Enabled":false}}]}'},
        ]
        result = self.execute([
            {"tool": "unity_set_property", "arguments": {"spec": "Light|Light.m_Enabled|false"}},
            {"tool": "unity_get_component", "arguments": {"target": "Light|UnityEngine.Light"},
             "expect": {"path": "components.0.properties.m_Enabled", "equals": False}},
        ])
        self.assertFalse(result.is_error)
        self.assertTrue(json.loads(result)["results"][1]["verified"])
        self.assertEqual(self.call.call_args_list[0].args[0], "set_property")
        self.assertEqual(self.call.call_args_list[1].args[0], "get_component")

    def test_assertion_failure_stops_later_edits(self):
        self.steps[0]["expect"]["equals"] = True
        result = self.execute(self.steps + [{"tool": "unity_set_property", "arguments": {"spec": "Cube|m_Name|Oops"}}])
        self.assertTrue(result.is_error)
        self.assertEqual(json.loads(result)["executedSteps"], 1)
        self.call.assert_called_once()

    def test_service_failure_stops_later_edits(self):
        self.call.return_value = {"ok": False, "error": "disconnected"}
        result = self.execute(self.steps * 2)
        self.assertTrue(result.is_error)
        self.assertEqual(json.loads(result)["executedSteps"], 1)


class AuditTests(unittest.TestCase):
    """体检器：Unity 出事实、Python 按规则包求值。用合成事实，不依赖 Unity。"""

    FACTS = {
        "ok": True,
        "scope": "scene",
        "scene": "TestScene",
        "scenePath": "Assets/TestScene.unity",
        "objectCount": 3,
        "truncated": False,
        "warnings": [],
        "rulePacks": [{
            "name": "unity-core",
            "path": "Agent~/rules/unity-core/rules.json",
            "json": json.dumps({
                "pack": "unity-core",
                "rules": [{
                    "id": "unity.missing-script",
                    "severity": "error",
                    "title": "对象引用了已丢失的脚本",
                    "scope": "object",
                    "when": [{"fact": "missingScripts", "gt": 0}],
                    "evidence": ["path", "missingScripts", "components.0.type"],
                    "expected": "missingScripts == 0",
                }],
            }),
        }],
        "objects": [
            {"path": "Root/Clean", "layer": 0, "componentCount": 2, "missingScripts": 0,
             "components": [{"type": "UnityEngine.Transform", "missing": False, "index": 0}]},
            {"path": "Root/Broken", "layer": 5, "componentCount": 2, "missingScripts": 1,
             "components": [{"type": "<missing>", "missing": True, "index": 0}]},
            {"path": "Root/AlsoBroken", "layer": 0, "componentCount": 3, "missingScripts": 2,
             "components": [{"type": "UnityEngine.Transform", "missing": False, "index": 0}]},
        ],
    }

    def run_audit(self, args=None, facts=None):
        with patch.object(m.UP, "call", return_value={"ok": True, "message": json.dumps(facts or self.FACTS)}):
            result = m.handle("unity_audit_module", args or {})
        return json.loads(result), result

    def test_missing_script_rule_fires_only_on_affected_objects(self):
        out, result = self.run_audit()
        self.assertFalse(result.is_error)
        self.assertEqual(out["findingCount"], 2)
        paths = [f["target"]["path"] for f in out["findings"]]
        self.assertEqual(paths, ["Root/Broken", "Root/AlsoBroken"])
        first = out["findings"][0]
        self.assertEqual(first["ruleId"], "unity.missing-script")
        self.assertEqual(first["severity"], "error")
        self.assertEqual(first["evidence"]["missingScripts"], 1)
        self.assertEqual(first["evidence"]["components.0.type"], "<missing>")
        self.assertEqual(first["actual"], {"missingScripts": 1})

    def test_rule_pack_filter_and_rule_id_filter(self):
        out, _ = self.run_audit({"rulePack": "unity-core", "ruleIds": ["unity.missing-script"]})
        self.assertEqual(out["rulePacks"], ["unity-core"])
        self.assertEqual(out["rulesEvaluated"], 1)
        out, result = self.run_audit({"rulePack": "not-exist"})
        self.assertTrue(result.is_error)
        self.assertIn("not-exist", json.loads(result)["error"])

    def test_max_findings_truncates_and_warns(self):
        out, _ = self.run_audit({"maxFindings": 1})
        self.assertEqual(out["findingCount"], 1)
        self.assertTrue(any("maxFindings" in w for w in out["warnings"]))

    def test_unity_failure_is_not_reported_as_clean(self):
        with patch.object(m.UP, "call", return_value={"ok": False, "error": "scope 不支持"}):
            result = m.handle("unity_audit_module", {"scope": "scene"})
        self.assertTrue(result.is_error)
        self.assertIn("scope 不支持", str(result))

    def test_project_rule_without_project_facts_is_skipped(self):
        rule = {"id": "x.project", "scope": "project", "when": [{"fact": "objectCount", "gt": 0}]}
        pack = [{"name": "p", "json": json.dumps({"rules": [rule]})}]

        # 情形一：Unity 压根没返回 project 字段 —— 静默跳过，不刷告警
        facts = dict(self.FACTS)
        facts["rulePacks"] = pack
        out, result = self.run_audit(facts=facts)
        self.assertFalse(result.is_error)
        self.assertEqual(out["rulesEvaluated"], 0)
        self.assertEqual(out["warnings"], [])

        # 情形二：Unity 的 JsonUtility 把空 class 序列化成了空壳对象，
        # present=false 必须被当成「没扫过」，不能当成「扫了、很干净」
        facts["project"] = {"present": False, "objectCount": 99, "metaCount": 0}
        out, result = self.run_audit(facts=facts)
        self.assertFalse(result.is_error)
        self.assertEqual(out["rulesEvaluated"], 0)
        self.assertEqual(out["findingCount"], 0)
        self.assertIsNone(out["metaCount"])

        # 情形三：调用方显式点名要这条规则，就必须说清楚为什么没跑
        out, result = self.run_audit({"ruleIds": ["x.project"]}, facts=facts)
        self.assertFalse(result.is_error)
        self.assertTrue(any("x.project" in w and "project" in w for w in out["warnings"]))

    def test_project_rule_evaluates_once_against_project_facts(self):
        facts = dict(self.FACTS)
        facts["scope"] = "project"
        facts["objects"] = []
        facts["project"] = {
            "present": True,
            "projectPath": "D:/Proj",
            "metaCount": 150,
            "duplicateGuidCount": 2,
            "duplicateGuids": [
                {"guid": "aaaa", "paths": ["Assets/A.mat.meta", "Assets/B.mat.meta"]},
                {"guid": "bbbb", "paths": ["Assets/C.mat.meta", "Assets/D.mat.meta"]},
            ],
            "asmdefCount": 1,
            "brokenAsmdefRefCount": 0,
            "asmdefs": [],
        }
        facts["rulePacks"] = [{"name": "p", "json": json.dumps({"rules": [{
            "id": "x.dup", "severity": "error", "title": "重复 GUID", "scope": "project",
            "when": [{"fact": "duplicateGuidCount", "gt": 0}],
            "evidence": ["duplicateGuidCount", "duplicateGuids.0.guid"],
            "expected": "duplicateGuidCount == 0",
        }]})}]
        out, result = self.run_audit(facts=facts)
        self.assertFalse(result.is_error)
        self.assertEqual(out["rulesEvaluated"], 1)
        # project 规则只出一条，不按对象数重复
        self.assertEqual(out["findingCount"], 1)
        finding = out["findings"][0]
        self.assertEqual(finding["target"]["path"], "D:/Proj")
        self.assertEqual(finding["evidence"]["duplicateGuids.0.guid"], "aaaa")
        self.assertEqual(out["duplicateGuidCount"], 2)
        self.assertEqual(out["metaCount"], 150)

    def test_project_scope_clean_project_produces_no_findings(self):
        # 用真正随包分发的规则包，避免"测试里自己编的规则"和线上不一致
        shipped = (Path(m.__file__).parent.parent / "Agent~" / "rules" / "unity-core" / "rules.json")
        facts = dict(self.FACTS)
        facts["scope"] = "project"
        facts["objects"] = []
        facts["rulePacks"] = [{"name": "unity-core", "json": shipped.read_text(encoding="utf-8")}]
        facts["project"] = {"present": True, "projectPath": "D:/Proj", "metaCount": 10,
                            "duplicateGuidCount": 0, "duplicateGuids": [],
                            "asmdefCount": 1, "brokenAsmdefRefCount": 0, "asmdefs": []}
        out, result = self.run_audit(facts=facts)
        self.assertFalse(result.is_error)
        self.assertEqual(out["findingCount"], 0)
        # 1 条 object 规则 + 2 条 project 规则，object 规则在空对象列表上求值也算一次
        self.assertEqual(out["rulesEvaluated"], 3)
        self.assertEqual(out["warnings"], [])

    def test_unknown_operator_fails_loudly(self):
        facts = dict(self.FACTS)
        facts["rulePacks"] = [{"name": "p", "json": json.dumps({"rules": [
            {"id": "x.bad", "when": [{"fact": "layer", "roughly": 0}]}]})}]
        with patch.object(m.UP, "call", return_value={"ok": True, "message": json.dumps(facts)}):
            result = m.handle("unity_audit_module", {})
        self.assertTrue(result.is_error)
        self.assertIn("算子", str(result))

    def test_operators_and_missing_fact(self):
        obj = {"layer": 5, "tag": "Player", "nested": {"v": 2}}
        self.assertTrue(m._rule_match({"when": [{"fact": "layer", "gte": 5}]}, obj)[0])
        self.assertFalse(m._rule_match({"when": [{"fact": "layer", "lt": 5}]}, obj)[0])
        self.assertTrue(m._rule_match({"when": [{"fact": "tag", "in": ["Player", "Enemy"]}]}, obj)[0])
        self.assertTrue(m._rule_match({"when": [{"fact": "nested.v", "equals": 2}]}, obj)[0])
        # 事实缺失应当不命中，而不是抛异常或误命中
        self.assertFalse(m._rule_match({"when": [{"fact": "nope.deep", "equals": 1}]}, obj)[0])

    def test_shipped_rule_pack_is_valid_json_with_required_fields(self):
        path = Path(m.__file__).parent.parent / "Agent~" / "rules" / "unity-core" / "rules.json"
        body = json.loads(path.read_text(encoding="utf-8"))
        self.assertEqual(body["pack"], "unity-core")
        for rule in body["rules"]:
            for key in ("id", "severity", "title", "when", "expected", "fixHint"):
                self.assertIn(key, rule, rule.get("id"))
            for condition in rule["when"]:
                ops = [k for k in condition if k != "fact"]
                self.assertEqual(len(ops), 1, rule["id"])
                self.assertIn(ops[0], m.AUDIT_OPS, rule["id"])


class NoLegacyApiTests(unittest.TestCase):
    """防止 2.0 迁移留下死引用：管道时代的符号不应再被任何代码路径调用。"""

    def test_pipe_symbols_are_gone(self):
        for symbol in ("resolve_pipe", "list_live_pipes", "UnityPipe"):
            self.assertFalse(hasattr(m, symbol), symbol)

    def test_no_windows_only_imports(self):
        source = Path(m.__file__).read_text(encoding="utf-8")
        for needle in ("import win32file", "import win32pipe", "import pywintypes"):
            self.assertNotIn(needle, source)


if __name__ == "__main__":
    unittest.main(verbosity=2)
