# Compatibility

| Component | Release target |
| --- | --- |
| Operating system | Windows 11 x64 |
| Sandboxie | Plus 1.18.5 x64 |
| Runtime | Self-contained .NET 10 Windows x64 |
| Build SDK | `global.json`, 10.0.401 with latestFeature roll-forward |
| MCP SDK | ModelContextProtocol 2.2.0 |
| Transport | Local stdio; no network listener |
| Messages | English and Russian envelopes; English tool/schema names |

Other Sandboxie versions are reported as outside the verified version matrix. Discovery does not make a compatibility claim for them. This release does not support Sandboxie Classic, ARM64, x86, Linux execution, portable service installation or HTTP transport.

## Supporter Certificate

Sandboxie Plus is open source. A Supporter Certificate is Sandboxie's entitlement mechanism for selected Plus features; open-source availability and enabled features in a distributed installation are separate matters. The MCP server does not require a certificate for the core workflows verified in this release and does not issue, import, bypass or redistribute certificates.

Feature checks use the installed driver's feature flags, including its certified flag. Gated settings and image operations return `feature_unavailable` when the required entitlement is absent. Disabling a gated setting remains possible. Windows administrator rights do not create a Sandboxie entitlement.

The tested installation had no certified/encryption entitlement. Thus ordinary sandbox operations, configuration, processes, files, snapshots, archives and restrictions have live evidence without a certificate. Encrypted image creation/mounting and password-protected configuration have implementation and protocol review but lack live coverage on a matching licensed/protected installation.

Use `sandboxie_status` to inspect detected flags. Consult [Sandboxie's Plus features](https://sandboxie-plus.github.io/sandboxie-docs/PlusContent/Plus-Features/) and the installed version's documentation for entitlement terms.

## Behavior boundaries

- Process inventory and process controls target the current Windows session; storage safety checks examine all sessions.
- Message reads default to recorded `SbieDrv`/`SbieSvc` events in the Windows System log. The separate native popup journal is restricted to Sandboxie's recognized session leader; an ordinary MCP process receives `feature_unavailable` for that source. Windows events do not contain every popup notification.
- Materialized file access requires stopped boxes. It does not expose a merged view of current and inherited snapshot layers.
- Reparse points are rejected. Storage roots mounted through reparse points, including some image-backed layouts, are not available through generic file/snapshot/archive tools in this release. Image control uses Sandboxie's native mount manager separately.
- Snapshot deletion supports unused leaves. Automatic merging is not advertised.
- Archives use `sandboxie-mcp/1`; Sandboxie's GUI archive formats are separate.
- Component transitions require Windows service-control rights. Reads and idempotent already-running starts need query rights; the server does not elevate itself.
- Network rule writes do not certify enforcement for every network stack or traffic type. WFP and user-mode coverage differ.

These boundaries are part of the public contract; unsupported operations are not represented by successful no-op responses.
