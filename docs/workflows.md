# Workflows and storage semantics

## Create and configure

Call `sandbox_create` with an ASCII name of 1–32 letters, digits or underscores. Reserved Windows and shared-section names are rejected. `copyFrom` copies permitted configuration while omitting secrets and custom storage roots, so the copy does not share the source's explicitly configured roots.

Read `configuration_read` with `effective=false`, obtain its revision, call `configuration_preview`, then pass the same revision to `configuration_apply`. Values are complete ordered lists. An empty list removes that setting. If a conflict occurs, read again and reconsider the proposed change. Existing programs may require restarting to use new settings.

Example change body:

```json
{
  "section": "Work_Browser",
  "changes": [
    { "setting": "BoxAlias", "values": ["Browser workspace"] },
    { "setting": "BlockNetworkFiles", "values": ["y"] }
  ]
}
```

Apply also requires `expectedRevision` from the actual local read; examples do not supply a fabricated revision.

## Templates and rules

Create custom template sections with `configuration_read/apply` using a `Template_` prefix. Dotted metadata keys such as `Tmpl.Title` are supported. `template_assign` accepts names without the prefix and requires existing templates. `template_delete` refuses built-in templates and referenced custom templates, and saves a backup before deletion.

Use `rules_read` before a typed replacement. `resource_rules_replace` and `program_rules_replace` replace one supported native rule list. `network_rules_replace` accepts structured actions and endpoint selectors; general configuration still provides access to other validated settings under the policy.

Network example:

```json
[
  { "action": "block", "process": "*", "protocol": "Any" },
  { "action": "allow", "process": "browser.exe", "ports": "443", "addresses": "192.0.2.10", "protocol": "TCP" }
]
```

Network rule enforcement depends on Sandboxie's configuration and active enforcement path. `wfpLoaded` reports the driver feature flag. Refer to [Sandboxie's NetworkAccess documentation](https://sandboxie-plus.github.io/sandboxie-docs/Content/NetworkAccess/) and [WFP configuration](https://sandboxie-plus.github.io/sandboxie-docs/PlusContent/WFPSupport/). Writing a rule alone does not prove traffic was blocked; the live suite verifies rule writes, not comprehensive firewall enforcement.

## Processes

`process_start` requires an absolute executable and an argument array. Optional working directory and environment are validated. `verified=true` means the membership query observed the process in the requested sandbox. A program that exits before that query reports `exited_before_verification`. Use `process_wait` to obtain a retained launch's exit code, including very short-lived programs.

Wait timeouts do not stop a program. `process_stop` checks termination. Suspend/resume returns the number of verified and already-exited targets. Sandboxed helpers can outlive the main program; stop the entire sandbox before file/snapshot operations. Suspending a program during its initial Sandboxie setup can interfere with initialization.

## Files and cleanup

Paths are relative to the materialized box root. For a copied virtual `C:\Windows\Temp\example.txt`, the common storage path is `drive\C\Windows\Temp\example.txt`. User and network mappings vary; inspect `file_list` instead of guessing.

Imports are restricted to `drive/`, `user/` and `share/`. Recovery writes to an allowed absolute host destination. Existing destinations need `overwrite=true`. Reads are strict UTF-8 unless `base64=true`; use recovery for larger or binary files. Inline reads, imports and archive operations have configured limits.

`sandbox_clean` permanently removes all materialized content, snapshots and retained recovery directories while keeping the definition. It honors `NeverDelete`. `sandbox_delete` requires a stopped, empty root and removes the definition separately. Cleanup is destructive and does not promise a recoverable recycle-bin copy.

Rename preserves the resolved file root, so the resulting name can continue using a directory containing its old name. Read the returned root rather than inferring a new directory.

## Snapshots and archives

Run a program once to initialize the registry hive before `snapshot_create`. Each snapshot becomes current and can reference the previous layer as its parent. Restore discards current modifications and selects a layer; an empty ID selects the empty base. Frozen data is still stored under `snapshot-ID`; file tools do not merge inherited layers into the current `drive` tree.

Only unused leaf snapshots can be deleted. Switch current/default references and remove descendants first. Snapshot name, description and default selection are mutable. A returned `retainedRecoveryDirectory` means the metadata commit succeeded but old staging data could not be removed; inspect it before cleanup.

Export produces `manifest.json` plus `content/` entries in `sandboxie-mcp/1` ZIP format. Import creates a new named sandbox, validates settings before writing and rejects existing definitions. Configuration changes and extraction are separate steps; a partial failure leaves the new box for inspection and reports extracted files. Preserve its state until the failure is understood.

## Images and component maintenance

Image password values are referenced through configured environment-variable names. Enable image settings through revision-checked configuration, create an image for an empty box, then mount/query/unmount through the image tools. These operations require Sandboxie's matching entitlement and components; an unavailable feature is a real error.

`sandboxie_component` accepts only `service`/`driver` and `start`/`stop`. Stopping is refused while sandboxed processes exist. Windows service-control rights are required for an actual state transition. Stopping the driver can make subsequent driver-dependent queries unavailable; start it again through the maintenance tool or Windows/Sandboxie controls.

## Message journals

`sandboxie_messages` defaults to `source="event-log"`, reading recorded `SbieDrv` and `SbieSvc` entries from the Windows System log. Its result identifies the source, includes `messages` and returns `nextCursor` for the next `after` value. Each entry includes its record number, event code, provider, time, insertion data and formatting status. A missing message resource preserves the actual insertion data and formatting error.

`source="native"` addresses the current-session popup ring buffer with its own independent cursor. Sandboxie 1.18.5 restricts this journal to its recognized session leader; an ordinary MCP server returns `feature_unavailable`. The server's API access preserves the existing SandMan session ownership. Windows events and native popup messages are different journals; Windows events cover only notifications Sandboxie records there. Both sources require `message_read` and `global_config` because entries can reveal paths across sandboxes.
