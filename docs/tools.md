# Tool reference

43 tools. This reference is generated from the running server's MCP schemas.

All calls enforce the server policy, even when a client displays the complete catalog. `Read-only` describes domain side effects; calls can still append audit metadata.

Results provide the same JSON envelope in `structuredContent` and text content. Success has `status`, `message` and `data`; errors additionally set MCP `isError=true` and provide `code`, `message` and optional `details`.

Required arguments are marked **required**. Defaults and nested shapes are recorded in [tools-schema.json](tools-schema.json). Setting/rule arrays replace their complete local lists; revisions must come from actual local configuration reads.

## audit_read

Read recent metadata-only MCP audit entries filtered by sandbox visibility. Arguments, command lines and secrets are never logged.

Read-only: true; destructive hint: true.

| Argument | Required | Shape |
| --- | --- | --- |
| `limit` | optional | integer |

## configuration_apply

Apply validated setting changes against an expected local revision. Empty values delete a setting. Produces a backup and reports partial failures.

Read-only: false; destructive hint: true.

| Argument | Required | Shape |
| --- | --- | --- |
| `section` | **required** | string |
| `changes` | **required** | array of object |
| `expectedRevision` | **required** | string |

## configuration_preview

Validate proposed setting changes and show the before/after values without writing. Requires the same permissions as applying them.

Read-only: true; destructive hint: true.

| Argument | Required | Shape |
| --- | --- | --- |
| `section` | **required** | string |
| `changes` | **required** | array of object |

## configuration_read

Read local or effective settings and a revision. Secret settings are omitted. Global and template sections require global_config permission.

Read-only: true; destructive hint: true.

| Argument | Required | Shape |
| --- | --- | --- |
| `section` | **required** | string |
| `effective` | optional | boolean |

## configuration_reload

Reload Sandboxie configuration. Existing sandboxed processes may need restarting to use changed rules.

Read-only: false; destructive hint: false.

No arguments.

## file_import

Copy an allowed external file into a stopped sandbox's drive/user/share storage. Existing files require overwrite=true.

Read-only: false; destructive hint: true.

| Argument | Required | Shape |
| --- | --- | --- |
| `sandbox` | **required** | string |
| `sourcePath` | **required** | string |
| `destinationPath` | **required** | string |
| `overwrite` | optional | boolean |

## file_list

List materialized files under a stopped sandbox's storage root. Paths are relative; results are paginated.

Read-only: true; destructive hint: true.

| Argument | Required | Shape |
| --- | --- | --- |
| `sandbox` | **required** | string |
| `path` | optional | string |
| `offset` | optional | integer |
| `limit` | optional | integer |

## file_read

Read a materialized sandbox file as strict UTF-8 or base64, within maxFileBytes. Requires a stopped sandbox.

Read-only: true; destructive hint: true.

| Argument | Required | Shape |
| --- | --- | --- |
| `sandbox` | **required** | string |
| `path` | **required** | string |
| `base64` | optional | boolean |

## file_recover

Copy a materialized file out of a stopped sandbox into an allowed absolute external path. This writes to the host.

Read-only: false; destructive hint: true.

| Argument | Required | Shape |
| --- | --- | --- |
| `sandbox` | **required** | string |
| `sourcePath` | **required** | string |
| `destinationPath` | **required** | string |
| `overwrite` | optional | boolean |

## image_create

Create an image for an empty stopped sandbox. Requires an active encryption entitlement. Passwords are referenced through user-configured environment-variable names.

Read-only: false; destructive hint: true.

| Argument | Required | Shape |
| --- | --- | --- |
| `sandbox` | **required** | string |
| `sizeMiB` | **required** | integer |
| `secretEnvironmentVariable` | optional | string / null |

## image_info

Query the mounted image associated with a sandbox through Sandboxie's mount manager.

Read-only: true; destructive hint: true.

| Argument | Required | Shape |
| --- | --- | --- |
| `sandbox` | **required** | string |

## image_mount

Mount a sandbox image with an optional configured secret reference. Password values are not placed on process command lines.

Read-only: false; destructive hint: true.

| Argument | Required | Shape |
| --- | --- | --- |
| `sandbox` | **required** | string |
| `secretEnvironmentVariable` | optional | string / null |
| `protect` | optional | boolean |

## image_unmount

Unmount a stopped sandbox image through the Sandboxie service.

Read-only: false; destructive hint: true.

| Argument | Required | Shape |
| --- | --- | --- |
| `sandbox` | **required** | string |

## network_rules_replace

Replace NetworkAccess rules. Actions are allow/block; protocols Any/TCP/UDP/ICMP. Numeric ports and IP addresses support comma sets and ranges. Read WFP status before relying on enforcement.

Read-only: false; destructive hint: true.

| Argument | Required | Shape |
| --- | --- | --- |
| `sandbox` | **required** | string |
| `values` | **required** | array of object |
| `expectedRevision` | **required** | string |

## process_info

Read process identity, parent and suspended state after verifying sandbox membership. Command lines are not exposed.

Read-only: true; destructive hint: true.

| Argument | Required | Shape |
| --- | --- | --- |
| `sandbox` | **required** | string |
| `processId` | **required** | integer |

## process_list

List current-session processes belonging to the specified sandbox.

Read-only: true; destructive hint: true.

| Argument | Required | Shape |
| --- | --- | --- |
| `sandbox` | **required** | string |

## process_start

Run an absolute executable inside a sandbox with an argument array and optional working directory/environment. Reports whether membership was verified.

Read-only: false; destructive hint: true.

| Argument | Required | Shape |
| --- | --- | --- |
| `sandbox` | **required** | string |
| `executable` | **required** | string |
| `arguments` | **required** | array of string |
| `workingDirectory` | optional | string / null |
| `environment` | optional | object / null |
| `hidden` | optional | boolean |

## process_stop

Stop a verified process or all current-session processes in a sandbox and verify termination. Omit processId to stop the sandbox's processes.

Read-only: false; destructive hint: true.

| Argument | Required | Shape |
| --- | --- | --- |
| `sandbox` | **required** | string |
| `processId` | optional | integer / null |

## process_suspend

Suspend or resume a process, or all current-session sandbox processes. Read process_info to verify the returned execution state.

Read-only: false; destructive hint: true.

| Argument | Required | Shape |
| --- | --- | --- |
| `sandbox` | **required** | string |
| `suspended` | **required** | boolean |
| `processId` | optional | integer / null |

## process_wait

Wait 1..300 seconds for a verified process to exit. Timing out does not terminate it.

Read-only: true; destructive hint: true.

| Argument | Required | Shape |
| --- | --- | --- |
| `sandbox` | **required** | string |
| `processId` | **required** | integer |
| `seconds` | optional | integer |

## program_rules_replace

Replace forced programs/folders, breakout programs, leader/linger programs or process groups against a revision. Some rules affect isolation.

Read-only: false; destructive hint: true.

| Argument | Required | Shape |
| --- | --- | --- |
| `sandbox` | **required** | string |
| `setting` | **required** | string |
| `values` | **required** | array of string |
| `expectedRevision` | **required** | string |

## resource_rules_replace

Replace one file, registry, IPC, window-class or COM rule list. Uses Sandboxie's native rule syntax and an expected revision.

Read-only: false; destructive hint: true.

| Argument | Required | Shape |
| --- | --- | --- |
| `sandbox` | **required** | string |
| `setting` | **required** | string |
| `values` | **required** | array of string |
| `expectedRevision` | **required** | string |

## rules_read

Read local resource, network and program rules and the revision used to replace them.

Read-only: true; destructive hint: true.

| Argument | Required | Shape |
| --- | --- | --- |
| `sandbox` | **required** | string |

## sandbox_clean

Permanently delete all content and snapshots of a stopped sandbox while retaining its definition. Respects NeverDelete.

Read-only: false; destructive hint: true.

| Argument | Required | Shape |
| --- | --- | --- |
| `sandbox` | **required** | string |

## sandbox_create

Create a named sandbox. Optionally copy another sandbox's configuration; the copy is subject to setting permissions.

Read-only: false; destructive hint: false.

| Argument | Required | Shape |
| --- | --- | --- |
| `sandbox` | **required** | string |
| `copyFrom` | optional | string / null |

## sandbox_delete

Delete an empty, stopped sandbox definition. Content cleanup is a separate operation; deletion protection is respected.

Read-only: false; destructive hint: true.

| Argument | Required | Shape |
| --- | --- | --- |
| `sandbox` | **required** | string |

## sandbox_export

Export local configuration and optional materialized content/snapshots to a new ZIP in sandboxie-mcp/1 format. Requires a stopped sandbox; secret settings and root paths are omitted.

Read-only: false; destructive hint: true.

| Argument | Required | Shape |
| --- | --- | --- |
| `sandbox` | **required** | string |
| `destinationPath` | **required** | string |
| `includeContent` | optional | boolean |

## sandbox_import

Import a sandboxie-mcp/1 ZIP into a new sandbox. Rejects traversal, links and extraction-limit violations. Does not overwrite an existing sandbox.

Read-only: false; destructive hint: true.

| Argument | Required | Shape |
| --- | --- | --- |
| `sandbox` | **required** | string |
| `sourcePath` | **required** | string |

## sandbox_info

Read a sandbox's resolved storage roots, enabled state, process count and local configuration revision.

Read-only: true; destructive hint: true.

| Argument | Required | Shape |
| --- | --- | --- |
| `sandbox` | **required** | string |

## sandbox_list

List sandboxes visible under the current access policy, with enabled state and process counts.

Read-only: true; destructive hint: true.

No arguments.

## sandbox_rename

Rename a stopped sandbox while preserving its current file storage root. Read the returned backup and root.

Read-only: false; destructive hint: true.

| Argument | Required | Shape |
| --- | --- | --- |
| `sandbox` | **required** | string |
| `newName` | **required** | string |

## sandbox_size

Count materialized files and bytes, including snapshots, in a stopped sandbox. Rejects reparse points.

Read-only: true; destructive hint: true.

| Argument | Required | Shape |
| --- | --- | --- |
| `sandbox` | **required** | string |

## sandboxie_component

Start or stop Sandboxie's service or driver. Requires maintenance permission and Windows rights; stopping is refused while sandboxed processes are running.

Read-only: false; destructive hint: true.

| Argument | Required | Shape |
| --- | --- | --- |
| `component` | **required** | string |
| `action` | **required** | string |

## sandboxie_messages

Read recorded SbieDrv/SbieSvc Windows System events after a numeric cursor. source=native requests the current-session popup journal, which requires Sandboxie's session leader. Sources have separate cursors. Requires global_config because messages may contain cross-sandbox paths.

Read-only: true; destructive hint: true.

| Argument | Required | Shape |
| --- | --- | --- |
| `after` | optional | integer |
| `limit` | optional | integer |
| `source` | optional | string |

## sandboxie_status

Read Sandboxie version, component states, current access profile and detected feature flags. Does not expose certificates or secrets.

Read-only: true; destructive hint: true.

No arguments.

## snapshot_create

Freeze a stopped, initialized sandbox into a native snapshot layer. The new snapshot becomes current.

Read-only: false; destructive hint: true.

| Argument | Required | Shape |
| --- | --- | --- |
| `sandbox` | **required** | string |
| `name` | **required** | string |

## snapshot_delete

Delete an unused leaf snapshot. Current/default snapshots and ancestors cannot be deleted; automatic layer merging is not performed.

Read-only: false; destructive hint: true.

| Argument | Required | Shape |
| --- | --- | --- |
| `sandbox` | **required** | string |
| `snapshotId` | **required** | string |

## snapshot_list

List native Sandboxie snapshots and their current/default relationships.

Read-only: true; destructive hint: true.

| Argument | Required | Shape |
| --- | --- | --- |
| `sandbox` | **required** | string |

## snapshot_restore

Discard current changes and select a native snapshot. Empty snapshotId selects an empty base. Requires a stopped sandbox.

Read-only: false; destructive hint: true.

| Argument | Required | Shape |
| --- | --- | --- |
| `sandbox` | **required** | string |
| `snapshotId` | **required** | string |

## snapshot_update

Update snapshot name/description or designate its default. makeDefault=true with an empty ID clears the default.

Read-only: false; destructive hint: false.

| Argument | Required | Shape |
| --- | --- | --- |
| `sandbox` | **required** | string |
| `snapshotId` | **required** | string |
| `name` | optional | string / null |
| `description` | optional | string / null |
| `makeDefault` | optional | boolean |

## template_assign

Replace template assignments against a revision. Templates must exist. Manage custom template sections with configuration_read/apply.

Read-only: false; destructive hint: true.

| Argument | Required | Shape |
| --- | --- | --- |
| `sandbox` | **required** | string |
| `values` | **required** | array of string |
| `expectedRevision` | **required** | string |

## template_delete

Delete an unused local template with a configuration backup. Built-in templates are immutable. Requires global_config and config_write.

Read-only: false; destructive hint: true.

| Argument | Required | Shape |
| --- | --- | --- |
| `template` | **required** | string |

## template_list

List shared configuration templates. Requires global_config and config_read.

Read-only: true; destructive hint: true.

No arguments.

## Resources

| URI | Content |
| --- | --- |
| `sandboxie://status` | Runtime status envelope; requires diagnostics |
| `sandboxie://sandboxes` | Visible sandbox list envelope; requires sandbox_read |
| `sandboxie://policy` | Configured policy without secrets |

## Error codes

`access_denied`, `not_found`, `already_exists`, `sandbox_busy`, `not_initialized`, `invalid_argument`, `native_error`, `feature_unavailable`, `configuration_conflict`, `verification_failed`, `unsafe_path`, `snapshot_in_use`, `timeout`, `io_error`, `partial_failure`, `not_empty`, `cancelled`.

Schema/JSON-RPC errors rejected by the MCP SDK may use protocol errors before a tool runs. See [configuration](configuration.md), [workflows](workflows.md) and [troubleshooting](troubleshooting.md) for permissions and recovery semantics.
