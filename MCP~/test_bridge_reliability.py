import importlib.util
import io
import json
from pathlib import Path
import unittest
from unittest.mock import Mock, patch

spec = importlib.util.spec_from_file_location("xy_reliability", Path(__file__).with_name("xy_mcp_server.py"))
m = importlib.util.module_from_spec(spec)
spec.loader.exec_module(m)


class ReliabilityTests(unittest.TestCase):
    def test_service_error_reaches_mcp(self):
        with patch.object(m.UP, "call", return_value={"ok": True, "message": '{"ok":false,"error":"missing asset"}'}):
            self.assertTrue(m.handle("unity_asset_preview", {"op": "get", "path": "Assets/missing.png"}).is_error)

    def test_stdio_response_marks_service_error(self):
        request = {"jsonrpc": "2.0", "id": 9, "method": "tools/call", "params": {
            "name": "unity_asset_preview", "arguments": {"op": "get", "path": "Assets/missing.png"}}}
        output = io.StringIO()
        with patch.object(m.sys, "stdin", io.StringIO(json.dumps(request) + "\n")), \
             patch.object(m.sys, "stdout", output), patch.object(m.sys, "stderr", io.StringIO()), \
             patch.object(m, "resolve_pipe", return_value=("test", None)), \
             patch.object(m.UP, "call", return_value={"ok": True, "message": '{"ok":false,"error":"missing"}'}), \
             patch.object(m.UP, "close"):
            m.main()
        response = json.loads(output.getvalue())
        self.assertEqual(response["id"], 9)
        self.assertTrue(response["result"]["isError"])

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

    def test_failed_write_is_not_replayed(self):
        pipe = m.UnityPipe("test", explicit=True)
        pipe._h = 1
        with patch.object(m.win32file, "WriteFile", side_effect=OSError("broken")) as write, patch.object(pipe, "close") as close:
            result = pipe.call("set_property", "target|property|value")
            self.assertFalse(result["ok"])
            self.assertIn("未自动重放", result["error"])
            write.assert_called_once()
            close.assert_called_once()

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


if __name__ == "__main__":
    unittest.main()
