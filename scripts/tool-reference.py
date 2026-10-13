"""Generate tool documentation from an actual running server's tools/list response."""
import argparse
import json
from pathlib import Path
import tempfile
from integration import Session


def generate(arguments):
    server = Path(arguments.server).resolve(strict=True)
    command = [str(server)] if server.suffix.lower() != ".dll" else [arguments.dotnet, str(server)]
    with tempfile.TemporaryDirectory(prefix="sandboxie-mcp-reference-") as directory:
        temporary = Path(directory)
        config = temporary / "config.json"
        config.write_text(json.dumps({"stateDirectory": str(temporary / "state"),
                                      "policy": {"profile": "read-only"}}), encoding="utf-8")
        session = Session(command, config, temporary / "stderr.log")
        try:
            tools = session.rpc("tools/list", {})["tools"]
        finally:
            session.close()
    docs = Path(arguments.output).resolve()
    docs.mkdir(parents=True, exist_ok=True)
    (docs / "tools-schema.json").write_text(json.dumps(tools, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
    lines = ["# Tool reference", "", f"{len(tools)} tools. This reference is generated from the running server's MCP schemas.", "",
             "All calls enforce the server policy, even when a client displays the complete catalog. `Read-only` describes domain side effects; calls can still append audit metadata.", "",
             "Results provide the same JSON envelope in `structuredContent` and text content. Success has `status`, `message` and `data`; errors additionally set MCP `isError=true` and provide `code`, `message` and optional `details`.", "",
             "Required arguments are marked **required**. Defaults and nested shapes are recorded in [tools-schema.json](tools-schema.json). Setting/rule arrays replace their complete local lists; revisions must come from actual local configuration reads.", ""]
    for tool in sorted(tools, key=lambda item: item["name"]):
        annotations = tool.get("annotations", {})
        lines += ["## " + tool["name"], "", tool.get("description", ""), "",
                  "Read-only: " + str(annotations.get("readOnlyHint", False)).lower() +
                  "; destructive hint: " + str(annotations.get("destructiveHint", True)).lower() + ".", ""]
        schema = tool["inputSchema"]
        properties = schema.get("properties", {})
        if properties:
            lines += ["| Argument | Required | Shape |", "| --- | --- | --- |"]
            for key, value in properties.items():
                shape = value.get("type", "schema")
                if isinstance(shape, list):
                    shape = " / ".join(shape)
                if shape == "array":
                    shape += " of " + str(value.get("items", {}).get("type", "objects"))
                lines.append(f"| `{key}` | {'**required**' if key in schema.get('required', []) else 'optional'} | {shape} |")
            lines.append("")
        else:
            lines += ["No arguments.", ""]
    lines += ["## Resources", "", "| URI | Content |", "| --- | --- |",
              "| `sandboxie://status` | Runtime status envelope; requires diagnostics |",
              "| `sandboxie://sandboxes` | Visible sandbox list envelope; requires sandbox_read |",
              "| `sandboxie://policy` | Configured policy without secrets |", "",
              "## Error codes", "", "`access_denied`, `not_found`, `already_exists`, `sandbox_busy`, `not_initialized`, `invalid_argument`, `native_error`, `feature_unavailable`, `configuration_conflict`, `verification_failed`, `unsafe_path`, `snapshot_in_use`, `timeout`, `io_error`, `partial_failure`, `not_empty`, `cancelled`.", "",
              "Schema/JSON-RPC errors rejected by the MCP SDK may use protocol errors before a tool runs. See [configuration](configuration.md), [workflows](workflows.md) and [troubleshooting](troubleshooting.md) for permissions and recovery semantics.", ""]
    (docs / "tools.md").write_text("\n".join(lines), encoding="utf-8")
    print(f"Generated {len(tools)} tool schemas and reference entries.")


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--server", required=True)
    parser.add_argument("--dotnet", default="dotnet")
    parser.add_argument("--output", default=str(Path(__file__).resolve().parents[1] / "docs"))
    generate(parser.parse_args())
