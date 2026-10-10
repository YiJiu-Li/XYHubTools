"""Verify the MCP/official HTTP contract against an open Unity project.

Only discovers commands, reads editor status and submits an editor_status job.
Does not alter scenes, assets or packages. Nonzero exit means upgrade not verified.
"""
import argparse
import json
from pathlib import Path
import time
import xy_mcp_server as m


def verify(project):
    m._PROJECT_ARG = str(Path(project).resolve())
    checks = []

    def call(name, args):
        result = m.run_pipeline_tool(name, args)
        if result.is_error:
            raise RuntimeError(str(result))
        return json.loads(result)

    tags = call("unity_pipeline_commands", {})
    assert isinstance(tags.get("tags"), list), "Missing tag discovery support"
    checks.append("tag_discovery")
    page = call("unity_pipeline_commands", {"query": "editor", "limit": 1})
    assert len(page["commands"]) <= 1 and isinstance(page.get("total"), int), "Missing filter/pagination support"
    assert all("schema" not in c for c in page["commands"]), "Compact discovery returned full schemas"
    checks.append("compact_discovery_and_pagination")
    command = call("unity_pipeline_commands", {"command": "editor_status"})
    assert len(command["commands"]) == 1 and command["commands"][0]["name"] == "editor_status"
    assert "schema" in command["commands"][0], "Missing command schema"
    checks.append("exact_command_schema")
    status = call("unity_pipeline", {"command": "editor_status"})
    assert isinstance(status.get("result"), dict), "Status is not a JSON object"
    checks.append("structured_execution")
    unknown = m.run_pipeline_tool("unity_pipeline", {"command": "__xy_contract_missing_command__"})
    assert unknown.is_error, "Failed command did not set MCP isError"
    checks.append("error_propagation")
    job = call("unity_pipeline", {"command": "editor_status", "job": True})["xyJob"]
    deadline = time.monotonic() + 30
    while True:
        state = call("unity_pipeline_job", {"job": job, "timeout": 5})
        if state.get("state") == "completed":
            break
        if time.monotonic() >= deadline:
            raise TimeoutError("Read-only job did not complete within 30 seconds")
        time.sleep(0.25)
    checks.append("detached_job_result")
    return {"success": True, "checks": checks, "server": tags.get("server"),
            "projectPath": str(Path(project).resolve()), "tagResponseBytes": len(json.dumps(tags).encode())}


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--project", required=True)
    parser.add_argument("--report")
    args = parser.parse_args()
    try:
        report = verify(args.project)
    except Exception as exc:
        report = {"success": False, "error": str(exc)}
    if args.report:
        Path(args.report).write_text(json.dumps(report, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(json.dumps(report, ensure_ascii=False, indent=2))
    raise SystemExit(0 if report["success"] else 1)
