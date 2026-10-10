import importlib.util
import io
import json
from pathlib import Path
import tempfile
import unittest
from unittest.mock import Mock, patch
import urllib.error

spec = importlib.util.spec_from_file_location("xy_mcp", Path(__file__).with_name("xy_mcp_server.py"))
m = importlib.util.module_from_spec(spec)
spec.loader.exec_module(m)


class PipelineTests(unittest.TestCase):
    def setUp(self):
        self.directory = tempfile.TemporaryDirectory()
        self.addCleanup(self.directory.cleanup)
        self.project = Path(self.directory.name)
        self.descriptor = self.project / "Library/Pipeline/.unity-pipeline-port"
        self.descriptor.parent.mkdir(parents=True)
        self.write_descriptor()
        self.pipe = Mock(explicit=True)
        self.pipe.call.return_value = {"projectPath": str(self.project)}
        self.up = patch.object(m, "UP", self.pipe)
        self.up.start()
        self.addCleanup(self.up.stop)
        self.http = patch.object(m._PIPELINE_HTTP, "open")
        self.open = self.http.start()
        self.addCleanup(self.http.stop)
        self.reply({"success": True, "result": 2})

    def write_descriptor(self, **changes):
        value = {"port": 7800, "evalToken": "test-only-token", "projectPath": str(self.project)}
        value.update(changes)
        self.descriptor.write_text(json.dumps(value), encoding="utf-8")

    def reply(self, value):
        self.open.return_value.__enter__.return_value.read.return_value = json.dumps(value).encode()

    def test_legacy_command_line_is_not_retokenized(self):
        command = 'eval "return @\"C:\\A B\\中文\";"'
        result = m.handle("unity_cli", {"spec": command})
        self.assertFalse(result.is_error)
        request = self.open.call_args.args[0]
        self.assertEqual(request.full_url, "http://127.0.0.1:7800/api/exec")
        self.assertEqual(request.get_header("Authorization"), "Bearer test-only-token")
        self.assertEqual(json.loads(request.data)["commandLine"], command)

    def test_structured_parameters_keep_types(self):
        params = {"enabled": False, "count": 3, "items": [1, "中文"], "value": None}
        result = m.handle("unity_pipeline", {"command": "sample", "parameters": params})
        self.assertFalse(result.is_error)
        self.assertEqual(json.loads(self.open.call_args.args[0].data)["parameters"], params)

    def test_command_schema_and_compact_list(self):
        self.reply({"commands": [{"name": "editor_status", "parameters": []}, {"name": "eval"}]})
        result = m.handle("unity_pipeline_commands", {"command": "editor_status"})
        self.assertEqual(len(json.loads(result)["commands"]), 1)
        self.assertEqual(self.open.call_args.args[0].method, "GET")
        self.assertIn("editor_status", m.handle("unity_cli", {}))
        self.assertTrue(m.handle("unity_pipeline_commands", {"command": "unknown"}).is_error)

    def test_descriptor_refresh_after_restart(self):
        m.handle("unity_cli", {"spec": "editor_status"})
        self.write_descriptor(port=7801, evalToken="rotated-test-token")
        m.handle("unity_cli", {"spec": "editor_status"})
        request = self.open.call_args.args[0]
        self.assertIn(":7801/", request.full_url)
        self.assertEqual(request.get_header("Authorization"), "Bearer rotated-test-token")

    def test_switch_project_does_not_reuse_descriptor(self):
        m.handle("unity_cli", {"spec": "editor_status"})
        other = self.project / "other"
        descriptor = other / "Library/Pipeline/.unity-pipeline-port"
        descriptor.parent.mkdir(parents=True)
        descriptor.write_text(json.dumps({"port": 7802, "evalToken": "other", "projectPath": str(other)}))
        self.pipe.call.return_value = {"projectPath": str(other)}
        m.handle("unity_cli", {"spec": "editor_status"})
        self.assertIn(":7802/", self.open.call_args.args[0].full_url)

    def test_execution_failure_at_http_200(self):
        self.reply({"success": False, "error": "bad arguments", "retryable": False})
        result = m.handle("unity_pipeline", {"command": "sample"})
        self.assertTrue(result.is_error)
        self.assertFalse(json.loads(result)["retryable"])

    def test_http_error_preserves_server_details(self):
        self.open.side_effect = urllib.error.HTTPError("http://127.0.0.1", 409, "busy", {},
            io.BytesIO(b'{"error":"busy","retryable":true}'))
        result = m.handle("unity_cli", {"spec": "editor_status"})
        self.assertTrue(result.is_error)
        self.assertTrue(json.loads(result)["retryable"])
        self.assertEqual(self.open.call_count, 1)

    def test_timeout_never_replays_command(self):
        self.open.side_effect = TimeoutError()
        result = m.handle("unity_pipeline", {"command": "sample"})
        self.assertTrue(result.is_error)
        self.assertEqual(self.open.call_count, 1)

    def test_invalid_or_missing_descriptor_never_sends(self):
        for changes in ({"port": 70000}, {"projectPath": "wrong"}, {"evalToken": ""}):
            self.write_descriptor(**changes)
            self.assertTrue(m.handle("unity_cli", {"spec": "editor_status"}).is_error)
        for content in ("[]", "{truncated"):
            self.descriptor.write_text(content)
            self.assertTrue(m.handle("unity_cli", {"spec": "editor_status"}).is_error)
        self.descriptor.unlink()
        self.assertTrue(m.handle("unity_cli", {"spec": "editor_status"}).is_error)
        self.open.assert_not_called()

    def test_invalid_inputs(self):
        for timeout in (float("nan"), float("inf"), -1, 0, 601):
            self.assertTrue(m.handle("unity_pipeline", {"command": "sample", "timeout": timeout}).is_error)
        self.assertTrue(m.handle("unity_pipeline", {"command": "sample", "parameters": []}).is_error)
        self.assertTrue(m.handle("unity_cli", {"spec": 12}).is_error)
        self.open.assert_not_called()

    def test_redirects_disabled(self):
        self.assertIsNone(m._NoRedirect().redirect_request(None, None, 302, "", {}, "https://example.com"))


if __name__ == "__main__":
    unittest.main()
