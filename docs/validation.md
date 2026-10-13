# Validation record

Release 1.0.0 targets Windows 11 x64 and installed Sandboxie Plus 1.18.5. Validation is performed against actual behavior, not successful compilation alone.

## Automated regressions

The Windows unit suite covers permission denials and visibility, full-profile denial precedence, forbidden settings and launch-hook bypasses, stale revisions, partial writes/backups, root separation during copies, all-session busy checks, strict configuration, feature gating, Windows quoting, traversal/device aliases/junctions, network grammar, snapshots, invalid snapshot graphs, archives, protected paths and retained process waiting.

The release passed all 93 Windows unit tests. Run `scripts/build.ps1` for locked restore, format verification, a warning-free Release build and tests. `TreatWarningsAsErrors` applies to production and test projects.

## Live Sandboxie regression

`scripts/integration.py` communicates with the actual server over stdio. The executed scenarios include:

1. Discovery of 43 tools, schemas and three resources, with runtime status, Sandboxie Windows event reads and native popup-journal restriction reporting.
2. Creating a disposable sandbox, changing Unicode settings and rejecting a stale revision.
3. Native process launch, identity, suspension/resumption, busy checks and confirmed termination.
4. Creating a snapshot branch, restoring a layer, deleting an unused leaf and reading the restored file from inside an actual sandboxed program through the driver overlay.
5. Passing custom environment data while excluding an unrelated host secret variable.
6. Rejecting traversal and unavailable image entitlement requests.
7. Creating/assigning a temporary template, protecting referenced templates and writing typed rule lists.
8. Exporting/importing configuration, content and snapshots, verifying SHA-256, preserving storage during rename and recovering a file.
9. A separate live read-only MCP session that filters box visibility, rejects writes/shared settings and returns Russian errors.
10. Removing the test definitions and temporary template.

The suite's report explicitly records that encrypted image operations were not live-tested and that host services were not stopped. Test boxes use random names scoped to a unique prefix. Cleanup operates only on those names; it does not use personal/default boxes.

## Coverage limits

Encrypted image workflows require a matching entitlement and components. Password-protected configuration requires a protected test installation. Actual service/driver stop transitions, multi-user sessions and comprehensive network traffic enforcement require separate controlled environments. Their absence from the live record is not represented as a passing test. CI on hosted Windows runners executes unit/build/package checks; it does not claim driver integration without installing and exercising Sandboxie.

`validation.json` records the executed live checks. The self-contained distribution executable passed the same ten-scenario suite before the local release was handed off. After publication, GitHub CI/workflow outcomes should be evaluated from actual runs; local checks do not imply a hosted workflow already ran.
