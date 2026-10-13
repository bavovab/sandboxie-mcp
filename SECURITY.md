# Security policy

Security fixes target the latest release. The verified Sandboxie compatibility target is Plus 1.18.5 on Windows 11 x64.

For a hosted GitHub repository, use **Security → Report a vulnerability** when private reporting is enabled. If that control is unavailable, open an issue requesting a private reporting channel without including exploit details, credentials, certificates or private sandbox data. Repository owners should enable private vulnerability reporting before publication; the [release procedure](docs/releasing.md) includes that step.

Reports should identify the release, access profile, Windows/Sandboxie versions, violated boundary and a minimal reproduction using disposable data. Include the expected and observed behavior. Do not send real passwords or certificates.

The server's policy limits its own MCP requests. It is not a replacement for Windows access control, Sandboxie isolation, or the MCP client's independent tool permissions. See the [security model](docs/security.md).
