# Release procedure

## Build the reviewed source

Run `scripts/build.ps1`, review source and dependency changes, then run `scripts/publish.ps1` from a fresh checkout. The package directory must be absent or empty; the publisher refuses to silently mix old binaries with new ones. The release contains the executable, configuration profiles, documentation, license/notices and an exact dependency inventory.

Run the live integration suite against the published executable on the verified target and retain its report. Confirm that test definitions were removed. Review compatibility/validation documentation whenever coverage or behavior changes. The package includes .NET native libraries which may be extracted by the runtime during first startup.

Check the package version, SHA-256 and files before delivery. Package checksums are in `artifacts/SHA256SUMS.txt`. Do not include build caches, test data, sandbox content, personal paths, certificates or credentials in the source archive.

## Publish to GitHub

Choose the repository owner and name, create an empty public repository under that account, and push the reviewed source. Enable Issues and private vulnerability reporting. Set an accurate description and the topics `mcp`, `sandboxie`, `sandboxie-plus`, `codex`, `windows`, `csharp`. Protect the main branch with the build/test check after that workflow has run.

The CI workflow restores locked dependencies, verifies formatting, runs tests and packages the server on Windows. It uploads build artifacts. The release workflow is triggered by a `v` version tag, builds/packages the corresponding commit, checks the executable version against the tag and creates a GitHub release with ZIP/checksum assets. Its default token has only the permissions required for the relevant job.

Tag `v1.0.0` only after review, live validation and an authorized publication destination. Change the version in `Directory.Build.props`; CLI/protocol versions and package naming follow the assembly version. Update the changelog and release notes for the resulting release. A local build does not constitute publication.

## Release notes

State the concrete capabilities, compatibility target, installation procedure and coverage limits. Link to the validation record. Do not claim encrypted-image or hosted-CI validation when that environment has not been exercised. Signing can be added by a repository owner with an actual signing identity; the current package is unsigned.
