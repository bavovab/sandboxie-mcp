# Codex connection

Extract the release directory to `C:\Tools\sandboxie-mcp-win-x64`. Register it through the Codex CLI:

```powershell
codex mcp add sandboxie -- 'C:\Tools\sandboxie-mcp-win-x64\sandboxie-mcp.exe' --config 'C:\Tools\sandboxie-mcp-win-x64\config\full.json'
codex mcp list
```

Alternatively, add this entry to the Codex configuration file:

```toml
[mcp_servers.sandboxie]
command = 'C:\Tools\sandboxie-mcp-win-x64\sandboxie-mcp.exe'
args = ['--config', 'C:\Tools\sandboxie-mcp-win-x64\config\full.json']
startup_timeout_sec = 30
tool_timeout_sec = 180
```

Use `read-only.json`, `operator.json` or your own configuration to restrict access. Restart the MCP connection after changing its policy or language. Codex's own permissions may impose additional restrictions.

Validate installation before connecting:

```powershell
& 'C:\Tools\sandboxie-mcp-win-x64\sandboxie-mcp.exe' --config 'C:\Tools\sandboxie-mcp-win-x64\config\full.json' --check-config
& 'C:\Tools\sandboxie-mcp-win-x64\sandboxie-mcp.exe' --config 'C:\Tools\sandboxie-mcp-win-x64\config\full.json' --diagnose
```

Ask Codex to call `sandboxie_status` and `sandbox_list` first. For configuration edits, read local settings, preview changes, then apply using the returned revision. For cleanup or restore, explicitly identify the target sandbox and intended data removal.

The server uses stdio exclusively. JSON-RPC goes to stdout; logs go to stderr. Closing the client connection exits the server without terminating already-launched sandboxed programs.

CLI registration syntax was checked with the installed Codex CLI. Configuration keys are described in [OpenAI's MCP documentation](https://developers.openai.com/codex/mcp/).
