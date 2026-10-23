# Bridge reliability and batch editing

The canonical Python server is `MCP~/xy_mcp_server.py`. Edit this source, then
use the Unity Bridge installer to update the shared Hub and project-root copies.
The installer refuses a lower version or fewer tools, replaces each file
atomically, and keeps the previous file as `xy_mcp_server.py.previous`.
Each destination is replaced independently; the two copies are not a transaction.

`unity_status` returns `bridge.version`, `pid`, `scriptPath`, `runningSha256`,
`disk`, `installed`, `projectSource`, `registeredTools`, and `exposedTools`.
`restartRequired` means the running process loaded a different script from the
one now on disk. Reconnect the MCP client after installing. The Unity panel
checks the installed file against the source; it does not claim to inspect
every MCP client's running process. `projectSource` checks the local
`Assets/XYHubTools/MCP~` path; it may be unavailable in UPM installations.

Service JSON failures and transport failures produce MCP `isError=true`.
Write failures, timeouts, and dropped connections are never automatically
replayed: a write may have reached Unity even when the caller saw an error,
and a timeout does not prove that the operation was not applied. Inspect the
object before retrying.

## Transport (2.0)

The Bridge listens on `127.0.0.1` on an OS-assigned port and writes
`{schema, transport, host, port, pid, token, projectPath, ...}` to
`~/.xybridge/instances/xy-<project-fingerprint>-<pid>.json`. Discovery rules
that the tests pin down:

- An instance file is only a *candidate*. Liveness means "a TCP connection can
  be established", so files left behind by a crash are ignored and eventually
  cleaned up.
- The token is generated per Editor start / domain reload and validated on
  every envelope. A stale client gets an explicit auth error instead of silent
  failures on each tool call; reconnecting re-reads the instance file.
- The client watches the instance file's mtime before each call. A domain
  reload rewrites it with a new port and token, so the stale connection is
  dropped *before* the request is sent — otherwise the first call after every
  C# recompile would fail with "connection closed".
- The first message the server pushes (`unity_connected`) carries no token or
  project path, because the client is not authenticated yet. `ping`/`status`
  return those fields.
- One MCP process keeps one connection open and multiplexes request ids. The
  server accepts multiple concurrent connections (a probe connection from
  `unity_list_projects` must not block the main one).

## Batch and verification

`unity_batch` supports up to 50 sequential component property edits and read
queries. All step schemas are checked before any command is sent. By default,
`dry_run=true` checks parameter structure only; it does not resolve scene
objects or properties. Pass `dry_run=false` to execute against one explicitly
bound project. The first service error, pending result, failed assertion, or
time-budget boundary stops the remaining steps. Completed edits remain applied;
this wrapper does not promise rollback or one combined Undo group.

Example: disable a light and verify its serialized property:

```json
{
  "dry_run": false,
  "steps": [
    {
      "tool": "unity_set_property",
      "arguments": {"spec": "Directional Light|Light.m_Enabled|false"}
    },
    {
      "tool": "unity_get_component",
      "arguments": {"target": "Directional Light|UnityEngine.Light"},
      "expect": {"path": "components.0.properties.m_Enabled", "equals": false}
    }
  ]
}
```

`expect.path` selects a dot-separated JSON path; array indices are supported.
An empty path compares the entire result. Boolean and numeric values are
distinct; numeric `1` and `1.0` compare equal. No expression or C# is evaluated.
The response includes per-step results, execution counts, and assertion results.
Names follow the existing component tools' resolution rules; use unambiguous
targets. The 120-second budget is checked between steps, so an in-flight
command may finish after that boundary.

For registered Pipeline commands requiring transactional Undo, use the existing
`unity_pipeline` command `batch` and discover its live schema first. That is a
separate execution path with its own supported commands and restrictions.

## Tests

Run `python -m unittest discover -s Assets/XYHubTools/MCP~ -p "test_*.py"`
from the Unity project root. Only Python 3.8+ is required — no Unity, no
Windows, no pywin32. `test_bridge_reliability.py` starts a fake Bridge server
on a loopback socket and speaks the real NDJSON protocol, so it exercises
discovery, handshake, token rejection, no-replay on disconnect and timeout,
error propagation, batch preflight, failure stops, and read-back assertions.
`test_pipeline_http.py` covers the Pipeline HTTP integration.
