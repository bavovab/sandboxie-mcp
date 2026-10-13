# Contributing

Install the .NET SDK specified by `global.json`. Work on Windows x64; the tests exercise Windows command-line parsing and directory junctions.

Run `./scripts/build.ps1` before submitting a change. It restores locked dependencies, verifies formatting, builds with warnings treated as errors, and runs tests. Run the live suite for changes to native integration, storage semantics or process behavior; see [validation](docs/validation.md).

Keep MCP endpoints thin. Authorization and domain behavior belong in Core services; native declarations and request layouts belong in Integration. Add a regression that demonstrates the failure being fixed. Test doubles belong only in the test project. Every advertised tool must perform its described operation or return a specific supported error.

Document changed inputs, permissions, side effects and compatibility. Changes to tools require updating `docs/tools-schema.json` and `docs/tools.md` from an actual `tools/list` response. Changes to dependencies require reviewed lock-file updates. Do not bundle Sandboxie binaries, certificates, personal configuration, sandbox content or credentials.

Submit a pull request describing the concrete behavior change and the checks actually executed. Avoid broad refactors unrelated to the issue. Report security issues through the channel described in [SECURITY.md](SECURITY.md).
