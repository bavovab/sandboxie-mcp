# Sandboxie MCP

[![Build and test](https://github.com/bavovab/sandboxie-mcp/actions/workflows/ci.yml/badge.svg?branch=main)](https://github.com/bavovab/sandboxie-mcp/actions/workflows/ci.yml)

A local MCP server for managing Sandboxie Plus from Codex and other stdio MCP clients on Windows x64.

43 tools cover sandbox lifecycle, configuration, templates, resource and network rules, processes, files, native snapshots, archives, diagnostics and image control. The server uses the installed Sandboxie API and service; it does not automate SandMan's interface.

## Requirements

- Windows 11 x64.
- Sandboxie Plus x64 installed, with its service and driver running. Version 1.18.5 is the verified target.
- A local MCP client supporting stdio. Codex setup is documented below.
- The release executable includes .NET. Building from source requires the .NET SDK selected by `global.json`.

A Supporter Certificate is **not required for the tested core workflows**. Certificate-dependent Plus features remain subject to Sandboxie's own entitlement checks. Image creation and mounting require the corresponding entitlement and image components. See [compatibility](docs/compatibility.md).

## Install and connect

Download the Windows x64 ZIP from [GitHub Releases](https://github.com/bavovab/sandboxie-mcp/releases/latest). Each release includes the executable, configuration profiles, documentation, dependency notices and a SHA-256 checksum.

Extract the Windows release ZIP to `C:\Tools\sandboxie-mcp-win-x64`. Validate the selected policy before connecting:

```powershell
& 'C:\Tools\sandboxie-mcp-win-x64\sandboxie-mcp.exe' --config 'C:\Tools\sandboxie-mcp-win-x64\config\full.json' --check-config
& 'C:\Tools\sandboxie-mcp-win-x64\sandboxie-mcp.exe' --config 'C:\Tools\sandboxie-mcp-win-x64\config\full.json' --diagnose
codex mcp add sandboxie -- 'C:\Tools\sandboxie-mcp-win-x64\sandboxie-mcp.exe' --config 'C:\Tools\sandboxie-mcp-win-x64\config\full.json'
```

`full.json` enables the full implemented management surface. Windows and Sandboxie permissions still apply. Without a configuration file, the server starts with a read-only policy. To restrict the model, select [read-only](config/read-only.json), [operator](config/operator.json), or [custom](config/custom.json), then restart the MCP connection. Policy is configured by the user outside MCP; tools cannot modify it.

Use `"language": "ru"` for Russian result messages. Tool names, schemas and repository documentation remain English.

## Features

| Area | Implemented behavior |
| --- | --- |
| Sandboxes | List, inspect, create, copy configuration, rename with file-root preservation, delete empty definitions |
| Configuration | Local/effective reads, preview, revision checks, verified writes, backups, reload |
| Rules | File/registry/IPC/window/COM rules, network rules, forced programs/folders, process groups and leader/linger rules |
| Templates | Inventory, assignment, custom sections, dependency checks, local deletion |
| Processes | Launch with argument arrays and a curated environment, inspect, wait, suspend/resume and terminate |
| Files | Paginated inventory, UTF-8/base64 reads, import, recovery, storage size and complete cleanup |
| Snapshots | Native layers, branches, restore, names/descriptions/defaults and unused-leaf deletion |
| Archives | Versioned ZIP export/import, SHA-256, rebound storage roots and bounded extraction |
| System | Versions, service/driver states, feature flags, Sandboxie messages and a metadata-only audit journal |
| Plus images | Native image queries, creation, mounting and unmounting, with configured secret references |

Tools return structured results and stable error codes. They distinguish confirmed completion, short-lived launches that escaped membership verification, and partial changes. Filesystem operations reject traversal, Windows device names, alternate data streams and reparse points.

## Build and test

```powershell
./scripts/build.ps1
./scripts/publish.ps1
```

The package appears under `artifacts/`, with a checksum and dependency inventory. For live tests against your installed Sandboxie, use Python 3.11 or newer:

```powershell
python ./scripts/integration.py --server ./artifacts/sandboxie-mcp-win-x64/sandboxie-mcp.exe --report ./artifacts/integration-report.json
```

The live suite creates random `MCPTest_` sandboxes and a temporary template, validates real driver behavior, then removes its own test definitions. It does not stop host services. The [validation record](docs/validation.md) separates executed checks from unavailable test environments.

## Documentation

- [Codex setup](docs/codex.md)
- [Configuration and permission profiles](docs/configuration.md)
- [Tool reference](docs/tools.md) and [machine-readable schemas](docs/tools-schema.json)
- [Workflows and storage semantics](docs/workflows.md)
- [Architecture and native integration](docs/architecture.md)
- [Security boundaries](docs/security.md)
- [Compatibility and certificate-dependent features](docs/compatibility.md)
- [Testing and contribution](CONTRIBUTING.md)
- [Troubleshooting](docs/troubleshooting.md)
- [Release procedure](docs/releasing.md)

## License

Original project code is licensed under [MIT](LICENSE). Sandboxie is a separate prerequisite with its own license. The release package includes dependency and .NET notices; see [third-party notices](THIRD_PARTY_NOTICES.md).
