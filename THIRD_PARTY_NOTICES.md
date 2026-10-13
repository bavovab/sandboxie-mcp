# Third-party notices

## Installed prerequisite

Sandboxie Plus is developed by the Sandboxie contributors. Its source repository and license are available at [sandboxie-plus/Sandboxie](https://github.com/sandboxie-plus/Sandboxie). This repository contains original C# interoperability code and does not redistribute Sandboxie binaries, certificates or upstream implementation files. The project's MIT license does not change Sandboxie's license or entitlement terms.

Native ABI and snapshot-format references are pinned to [Sandboxie v1.18.5](https://github.com/sandboxie-plus/Sandboxie/tree/v1.18.5). Constants and layouts are documented in [architecture](docs/architecture.md).

## Distributed dependencies

The server uses the official [MCP C# SDK](https://github.com/modelcontextprotocol/csharp-sdk), Microsoft .NET and Microsoft.Extensions packages. Exact package versions and integrity hashes are recorded in version-controlled `packages.lock.json` files and the release's `DEPENDENCIES.json`. The inventory describes the server's restored NuGet graph, including build-time packages; an entry does not imply that every package assembly is distributed.

The self-contained release includes the distributed runtime's `.NET-LICENSE.txt` and `.NET-ThirdPartyNotices.txt`, package notice files under `licenses/`, and the MCP SDK's [license](licenses/ModelContextProtocol-LICENSE.txt), preserved from its package source commit `6fa3825973949a9c4f0cd8af344e15a8db09dc35`. Its NuGet declaration is Apache-2.0; the upstream license also describes the project's licensing transition. Package metadata in `DEPENDENCIES.json` identifies the declared license, copyright and upstream project URL for each NuGet package and the included runtime pack. Tests additionally use xUnit and Microsoft.NET.Test.Sdk; test assemblies are excluded from the release.

Third-party license terms apply to the corresponding dependencies. Preserve their notices when redistributing the release.
