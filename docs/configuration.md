# Configuration and permissions

Configuration is a UTF-8 JSON object passed through `--config`. Unknown JSON fields and unknown operation names are rejected. Configuration is read once at startup. Relative installation and state directories are rejected; Windows environment-variable expressions in paths are not expanded.

| Field | Default | Meaning |
| --- | --- | --- |
| `sandboxiePath` | Program Files / Sandboxie-Plus | Installed x64 Sandboxie directory |
| `language` | `en` | `en` or `ru` result messages |
| `stateDirectory` | LocalAppData / SandboxieMcp | Audit journal and configuration backups |
| `operationTimeoutSeconds` | 120 | Cooperative operation timeout, 1–3600 seconds |
| `maxFileBytes` | 1048576 | Maximum inline read, up to 16 MiB |
| `maxArchiveBytes` | 2147483648 | Archive, extraction and individual copy limit, up to 1 TiB |
| `policy` | read-only | Server-side request authorization |
| `secretEnvironmentVariables` | empty | Trusted environment-variable names available to image/password operations |
| `configurationPasswordEnvironmentVariable` | absent | Configured secret reference for password-protected Sandboxie configuration |

## Profiles

| Profile | Permissions |
| --- | --- |
| `full` | All implemented operations, subject to explicit denials, sandbox visibility and Windows/Sandboxie rights |
| `read-only` | Diagnostics, sandbox/config/process/file/snapshot/image reads and audit reads |
| `operator` | Read operations plus ordinary lifecycle, process, file, snapshot and archive operations; excludes rename, global configuration, maintenance, image writes and isolation changes |
| `custom` | Only names in `allowedOperations`; this list must be nonempty |

The operator's implicit writable settings are `Enabled`, `BoxAlias`, `BorderColor`, `BoxNameTitle`, `BlockNetworkFiles` and `BlockNetParam`. Use explicit `allowedSettings` for additional restricted-profile settings. Settings that change isolation also require `isolation_change`. Templates, root paths, hooks and command settings require the full profile; the setting allow list cannot override that restriction. Archive imports and configuration copies remain subject to every included setting's permissions, so an operator can reject an archive created with unrestricted settings.

`deniedOperations` overrides a profile or allow list. Sandbox visibility is the intersection of `allowedSandboxes` and the complement of `deniedSandboxes`. Patterns are case-insensitive exact names or prefixes ending in `*`. No other wildcards are accepted. Global, template and user sections additionally require `global_config`.

Executable allow lists match exact absolute paths. External-root allow lists cover files inside the specified absolute directories and apply to restricted profiles. Full access does not use executable/external-root allow lists; use explicit operation denials or a restricted profile to narrow it. All profiles reject unsafe paths and protect the server executable, its directory, configuration and state files from MCP file operations.

## Operation names

| Permission | Used for |
| --- | --- |
| `diagnostics` | Runtime status |
| `sandbox_read` | List and inspect sandboxes |
| `sandbox_create`, `sandbox_rename`, `sandbox_delete` | Definitions and lifecycle |
| `config_read`, `config_write` | Configuration and rule access |
| `global_config` | Shared sections, reload, global message visibility and template management |
| `isolation_change` | Open-access, force/breakout, network and selected security settings; launch hooks |
| `process_read`, `process_start`, `process_stop`, `process_suspend` | Process operations; waits use `process_read` |
| `file_read`, `file_import`, `file_recover`, `sandbox_clean` | Materialized storage |
| `snapshot_read`, `snapshot_create`, `snapshot_restore`, `snapshot_delete`, `snapshot_write` | Snapshot layers and metadata |
| `archive_export`, `archive_import` | Archive operations, with their additional lifecycle/config/file permissions |
| `message_read`, `audit_read` | Sandboxie messages and MCP audit entries |
| `image_read`, `image_write` | Image query and image changes |
| `maintenance` | SbieSvc/SbieDrv start and stop |

Creating a sandbox through a new configuration section still requires `sandbox_create`. Launch hooks cannot bypass a `process_start` denial. Client tool filtering is optional; these checks are enforced inside the server for every call.

## Secrets

Configure names, never passwords, in JSON:

```json
{
  "secretEnvironmentVariables": ["SANDBOXIE_CONFIG_PASSWORD", "SANDBOXIE_IMAGE_PASSWORD"],
  "configurationPasswordEnvironmentVariable": "SANDBOXIE_CONFIG_PASSWORD",
  "policy": { "profile": "full" }
}
```

Set these variables in the process environment used to launch the MCP server. Pass `secretEnvironmentVariable: "SANDBOXIE_IMAGE_PASSWORD"` to an image tool. The server reads the value locally; it does not put it on a process command line or in the audit journal. Password request buffers are cleared after native calls. Managed strings and inherited server environment remain accessible to processes with sufficient Windows rights; this is not a secret vault.

Sandboxed launches inherit a curated set of Windows runtime/path variables instead of the entire server environment. Caller-supplied environment values are explicitly merged. The live regression verifies that an unrelated host secret variable is excluded.

The MCP policy does not restrict the client's independent terminal or other tools. Configure those separately if the model must lack direct access to Sandboxie files or commands.
