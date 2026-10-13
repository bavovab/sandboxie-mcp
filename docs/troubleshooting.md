# Troubleshooting

| Symptom/code | Action |
| --- | --- |
| Startup cannot find `SbieDll.dll` | Set `sandboxiePath` to the installed x64 Plus directory. Check the installation before starting MCP. |
| `native_error` | Inspect the hexadecimal status, installed version and service/driver state. Do not automatically retry a mutation. |
| `access_denied` | Check the operation profile, explicit denials, sandbox pattern, setting, executable and external-root permissions. Windows/Sandboxie restrictions may additionally deny a native action. |
| `configuration_conflict` | Read local settings again, review the new state and apply with its actual revision. |
| `sandbox_busy` | Stop the entire sandbox, including helper processes and processes in other sessions, before storage operations. |
| `not_initialized` | Run a program once in the sandbox to create its registry hive before taking a snapshot. |
| `not_empty` | Inspect content and choose an explicit cleanup operation before deleting a definition or creating an image. |
| `feature_unavailable` | Check the driver flags and the Sandboxie entitlement required for that feature. Core workflows do not require a certificate. |
| `snapshot_in_use` | Change current/default references and remove children before deleting a leaf. |
| `unsafe_path` | Use a local path without traversal, device names, alternate streams, trailing aliases or reparse points. |
| `partial_failure` | Preserve the box and backup/recovery directory, inspect reported completed/attempted steps and current settings before recovery. |
| `timeout` / `cancelled` | Check actual state; the underlying native call or previously committed changes may have completed. |
| UTF-8 read fails | Request `base64=true` or recover the binary file to an allowed external destination. |
| Codex cuts off long calls | Increase `tool_timeout_sec` and, when appropriate, `operationTimeoutSeconds`. Client and server timeouts are independent. |
| Audit warning on stderr | Check state-directory ownership, permissions, free space and whether another process holds the journal. |

Use `--check-config` to validate JSON without loading Sandboxie. Use `--diagnose` for runtime status. Both commands write human-readable output and exit; do not register them as the normal stdio MCP command.

PowerShell, browsers and other applications can leave sandboxed service/helper processes after their main process exits. `process_wait` waits for the requested process only. `process_stop` without a process ID terminates the current-session box processes and verifies termination.

An early launch can report `verified=false` if the process exits before membership is observed. This is not proof of successful application work. Use `process_wait` and inspect the application's output. The retained exit-handle cache covers the latest 256 server launches until shutdown.
