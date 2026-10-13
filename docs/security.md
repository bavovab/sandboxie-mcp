# Security boundaries

The server accepts instructions from a local MCP client and applies a trusted user-owned configuration. It runs with its launching Windows account's rights. Full access is intentionally broad: configuration, destructive storage operations and component controls are available within Windows and Sandboxie permissions.

## Enforced controls

- Every tool reaches a service-side operation check. Explicit denials override full access and custom allow lists.
- Sandbox visibility is checked before per-box operations. Global sections, messages and template management require a separate permission.
- Restricted profiles require explicit executable and external-directory allow lists. Dangerous settings cannot be enabled merely by naming them in `allowedSettings`.
- Paths are local, absolute where required and confined where relative. Traversal, ambiguous Windows aliases, device names, alternate streams and reparse points are rejected.
- The server's configuration, runtime directory and state directory are protected from its file operations.
- Archive entries are validated before creating a sandbox. Declared and actual decompressed sizes are bounded; duplicate canonical paths and symbolic-link entries are rejected. Snapshot graphs are checked before imported metadata can reach the driver.
- Filesystem changes require a stopped sandbox across sessions. Deletion protection is honored for cleanup and definition deletion.
- Configuration writes use revisions, backups and read-back verification. Partial changes are explicitly reported.
- Image secrets use configured local references. Passwords, argument arrays, environment values and certificate contents are excluded from audit entries.

## Trust assumptions

The policy file, installed Sandboxie directory, executable allow lists and state directory must remain under trusted Windows access control. The server is not a boundary against hostile code running as the same Windows user or with administrator rights. Such code can alter Sandboxie configuration directly, replace paths between validation and use, read process memory or control the client independently.

An executable allow list authorizes that program with caller-selected arguments. Allowing an interpreter, scripting engine, shell, compiler or application with extension mechanisms can authorize broader behavior through that program. The server does not analyze arbitrary program semantics or claim an executable allow list confines all resulting filesystem/network access. Sandboxie rules remain responsible for sandbox isolation.

Policy enforcement covers this MCP server. A Codex terminal with direct host access can invoke Sandboxie separately. Limit the client's other tools and Windows account privileges when that boundary matters.

Snapshots and imports contain application-controlled data. Treat file contents, log strings and archive metadata as data, not trusted instructions for the model. The server does not execute instructions embedded in those files on its own.

## Operational limits

One process serializes its mutations; external writers and other server instances are not transaction participants. Avoid concurrent changes to the same box. No Windows ACL, firewall or Sandboxie rule is silently weakened after a failed operation. No automatic certificate installation, UAC bypass or arbitrary host shell endpoint is provided.

Native calls cannot be forcibly cancelled. After a timeout or partial failure, inspect state before retrying. A failed audit write produces a stderr warning; it does not relabel a completed mutation as failed and trigger unsafe retries. Protect and monitor stderr/state permissions if audit retention is required.

The release executable is unsigned. SHA-256 establishes package integrity against a trusted published checksum; it does not establish publisher identity by itself.
