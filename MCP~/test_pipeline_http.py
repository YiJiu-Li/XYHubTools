import importlib.util
import io
import json
from pathlib import Path
import tempfile
import subprocess
import sys
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
        self.pipe = m.UnityPipe("test-pipe", explicit=True)
        self.pipe.call = Mock(return_value={"projectPath": str(self.project)})
        self.up = patch.object(m, "UP", self.pipe)
        self.up.start()
        self.addCleanup(self.up.stop)
        self.http = patch.object(m._PIPELINE_HTTP, "open")
        self.open = self.http.start()
        self.addCleanup(self.http.stop)
        self.reply({"success": True, "result": 2})

    def write_descriptor(self, **changes):
        value = {"port": 7800, "evalToken": "test-only-token", "projectPath": str(self.project), "startedAt": "2026-10-10T00:00:00Z"}
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
        self.assertIn("detail=full&query=editor_status", self.open.call_args.args[0].full_url)
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
        self.pipe.set_pipe("other-pipe")
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

    def test_discovery_defaults_and_pagination(self):
        self.reply({"tags": [{"tag": "assets", "count": 2}], "count": 1, "total": 2, "offset": 0})
        result = json.loads(m.handle("unity_pipeline_commands", {}))
        self.assertEqual(result["nextOffset"], 1)
        self.assertIn("detail=tags", self.open.call_args.args[0].full_url)
        m.handle("unity_pipeline_commands", {"query": "a & b", "tag": "assets/import", "limit": 2, "offset": 1})
        query = m.urllib.parse.parse_qs(m.urllib.parse.urlparse(self.open.call_args.args[0].full_url).query)
        self.assertEqual(query, {"query": ["a & b"], "tag": ["assets/import"], "detail": ["compact"], "limit": ["2"], "offset": ["1"]})
        for args in ({"limit": True}, {"limit": 201}, {"offset": -1}, {"detail": "invalid"}, {"command": "eval", "limit": 1}):
            self.assertTrue(m.handle("unity_pipeline_commands", args).is_error)

    def test_project_binding_survives_pipe_loss_without_reprobing(self):
        m.handle("unity_pipeline", {"command": "editor_status"})
        self.pipe.call.side_effect = AssertionError("must not probe pipe again")
        self.write_descriptor(port=7803)
        self.assertFalse(m.handle("unity_pipeline", {"command": "editor_status"}).is_error)
        self.assertIn(":7803/", self.open.call_args.args[0].full_url)

    def test_absolute_project_binding_needs_no_pipe(self):
        self.pipe.call.side_effect = AssertionError("pipe unavailable")
        with patch.object(m, "list_live_pipes", side_effect=AssertionError("must not enumerate")):
            result = m.cmd_use_project(str(self.project))
        self.assertIn(str(self.project), result)
        self.assertFalse(m.handle("unity_pipeline", {"command": "editor_status"}).is_error)

    def submit_job(self):
        self.reply({"success": True, "result": {"jobId": "job-1", "state": "queued"}})
        result = m.handle("unity_pipeline", {"command": "editor_status", "job": True})
        self.assertFalse(result.is_error)
        self.assertTrue(json.loads(self.open.call_args.args[0].data)["job"])
        handle = json.loads(result)["xyJob"]
        self.assertNotIn("token", json.dumps(handle).lower())
        return handle

    def test_job_submission_status_and_cancel(self):
        job = self.submit_job()
        self.reply({"jobId": "job-1", "state": "completed", "result": {"value": 2}})
        self.assertFalse(m.handle("unity_pipeline_job", {"job": job}).is_error)
        self.assertTrue(self.open.call_args.args[0].full_url.endswith("/api/job?id=job-1"))
        self.reply({"state": "running"})
        self.assertFalse(m.handle("unity_pipeline_job", {"job": job, "action": "cancel"}).is_error)
        self.assertEqual(json.loads(self.open.call_args.args[0].data), {"id": "job-1"})

    def test_job_rejects_wrong_project_or_restarted_server_without_http(self):
        job = self.submit_job()
        self.open.reset_mock()
        wrong = dict(job, projectPath=str(self.project / "other"))
        result = m.handle("unity_pipeline_job", {"job": wrong, "action": "cancel"})
        self.assertEqual(json.loads(result)["code"], "job_project_mismatch")
        self.write_descriptor(startedAt="2026-10-10T01:00:00Z")
        result = m.handle("unity_pipeline_job", {"job": job})
        self.assertEqual(json.loads(result)["code"], "job_session_expired")
        self.open.assert_not_called()

    def test_job_failures_and_missing_retained_result(self):
        job = self.submit_job()
        self.reply({"state": "failed"})
        self.assertTrue(m.handle("unity_pipeline_job", {"job": job}).is_error)
        self.open.side_effect = urllib.error.HTTPError("http://127.0.0.1", 404, "Not Found", {}, io.BytesIO(b'{"error":"Job Not Found"}'))
        result = m.handle("unity_pipeline_job", {"job": job})
        self.assertEqual(json.loads(result)["code"], "job_unavailable")

    def test_read_timeout_is_retryable_but_writes_are_unknown(self):
        self.open.side_effect = TimeoutError()
        result = json.loads(m.handle("unity_pipeline_commands", {}))
        self.assertTrue(result["retryable"])
        self.assertFalse(result["outcomeUnknown"])
        result = json.loads(m.handle("unity_pipeline", {"command": "sample"}))
        self.assertFalse(result["retryable"])
        self.assertTrue(result["outcomeUnknown"])

    def test_invalid_job_inputs_never_send(self):
        for args in ({"job": {}}, {"job": {"id": "1"}}, {"job": None}):
            self.assertTrue(m.handle("unity_pipeline_job", args).is_error)
        self.assertTrue(m.handle("unity_pipeline", {"command": "sample", "job": "true"}).is_error)
        self.open.assert_not_called()

    def test_nested_execution_failure_sets_mcp_error(self):
        self.reply({"success": True, "result": {"success": False, "diagnostics": ["compile failed"]}})
        result = m.handle("unity_pipeline", {"command": "eval", "parameters": {"code": "bad"}})
        self.assertTrue(result.is_error)
        self.assertEqual(json.loads(result)["result"]["diagnostics"], ["compile failed"])

    def test_new_tools_are_exposed_over_stdio_core_group(self):
        messages = [
            {"jsonrpc": "2.0", "id": 1, "method": "initialize", "params": {}},
            {"jsonrpc": "2.0", "id": 2, "method": "tools/list"},
            {"jsonrpc": "2.0", "id": 3, "method": "tools/call", "params": {"name": "unity_pipeline_job", "arguments": {"job": {}}}},
        ]
        process = subprocess.run([sys.executable, str(Path(m.__file__)), "--group=core"],
            input="\n".join(json.dumps(v) for v in messages) + "\n", text=True, capture_output=True, timeout=10)
        self.assertEqual(process.returncode, 0, process.stderr)
        replies = {r["id"]: r for r in map(json.loads, process.stdout.splitlines())}
        tools = {t["name"]: t for t in replies[2]["result"]["tools"]}
        self.assertIn("unity_pipeline_job", tools)
        self.assertIn("query", tools["unity_pipeline_commands"]["inputSchema"]["properties"])
        self.assertTrue(replies[3]["result"]["isError"])


if __name__ == "__main__":
    unittest.main()
