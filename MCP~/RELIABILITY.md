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

Service JSON failures and pipe failures produce MCP `isError=true`.
Pipe write failures are not automatically replayed: a write may have reached
Unity even when the caller saw an error. A timeout similarly does not prove
that the operation was not applied; inspect the object before retrying.

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
from the Unity project root with Python and pywin32 installed. These tests
exercise error propagation, no-replay behavior, batch preflight, failure stops,
read-back assertions, and the existing Pipeline integration.
