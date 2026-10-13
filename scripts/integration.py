"""Live MCP regression against installed Sandboxie Plus. Uses only random test boxes."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import queue
import subprocess
import tempfile
import threading
import time
import uuid


class Session:
    def __init__(self, command, config, log, environment=None):
        self.log = log.open("w", encoding="utf-8")
        self.process = subprocess.Popen(command + ["--config", str(config)], stdin=subprocess.PIPE,
                                        stdout=subprocess.PIPE, stderr=self.log, text=True,
                                        encoding="utf-8", env=environment)
        self.messages = queue.Queue()
        self.sequence = 0
        self.reader = threading.Thread(target=self._read, daemon=True)
        self.reader.start()
        self.server_info = self.rpc("initialize", {"protocolVersion": "2025-11-25", "capabilities": {},
                                "clientInfo": {"name": "sandboxie-mcp-integration", "version": "1.0.0"}})["serverInfo"]
        self.send({"jsonrpc": "2.0", "method": "notifications/initialized"})

    def _read(self):
        try:
            for line in self.process.stdout:
                self.messages.put(json.loads(line))
        except (ValueError, OSError) as error:
            self.messages.put(error)
        finally:
            self.messages.put(EOFError("Server stdout closed"))

    def send(self, message):
        self.process.stdin.write(json.dumps(message, ensure_ascii=False) + "\n")
        self.process.stdin.flush()

    def rpc(self, method, params):
        self.sequence += 1
        self.send({"jsonrpc": "2.0", "id": self.sequence, "method": method, "params": params})
        deadline = time.monotonic() + 150
        while True:
            response = self.messages.get(timeout=max(0.01, deadline - time.monotonic()))
            if isinstance(response, Exception):
                raise response
            if response.get("id") == self.sequence:
                if "error" in response:
                    raise RuntimeError(response["error"])
                return response["result"]

    def call(self, tool, expected_error=None, **arguments):
        response = self.rpc("tools/call", {"name": tool, "arguments": arguments})
        data = response.get("structuredContent") or json.loads(response["content"][0]["text"])
        if expected_error is not None:
            assert response.get("isError") and data["code"] == expected_error, (tool, data)
            return data
        assert not response.get("isError"), (tool, data)
        return data["data"]

    def close(self):
        self.process.stdin.close()
        try:
            self.process.wait(timeout=10)
        except subprocess.TimeoutExpired:
            self.process.kill()
            self.process.wait(timeout=10)
        self.reader.join(timeout=2)
        self.process.stdout.close()
        self.log.close()


def run(arguments):
    if os.name != "nt":
        raise RuntimeError("Live tests require Windows x64 and installed Sandboxie Plus.")
    server = Path(arguments.server).resolve(strict=True)
    command = [str(server)] if server.suffix.lower() != ".dll" else [arguments.dotnet, str(server)]
    token = uuid.uuid4().hex[:10]
    prefix = "MCPTest_" + token
    names = [prefix + "_A", prefix + "_B", prefix + "_C"]
    template = prefix + "_Template"
    checks = []
    sandboxie = Path(arguments.sandboxie)
    environment = os.environ.copy()
    environment["MCP_HOST_SECRET_SENTINEL"] = uuid.uuid4().hex
    with tempfile.TemporaryDirectory(prefix="sandboxie-mcp-live-") as directory:
        root = Path(directory)
        configuration = {"sandboxiePath": str(sandboxie), "stateDirectory": str(root / "state"),
                         "secretEnvironmentVariables": ["MCP_HOST_SECRET_SENTINEL"],
                         "policy": {"profile": "full", "allowedSandboxes": [prefix + "*"]}}
        config = root / "config.json"
        config.write_text(json.dumps(configuration), encoding="utf-8")
        session = Session(command, config, root / "stderr.log", environment)
        def passed(check):
            checks.append(check)
            print("PASS", check, flush=True)
        def apply(section, values):
            current = session.call("configuration_read", section=section)
            return session.call("configuration_apply", section=section, expectedRevision=current["revision"],
                                changes=[{"setting": key, "values": value} for key, value in values.items()])
        try:
            tools = session.rpc("tools/list", {})["tools"]
            assert len(tools) == 43 and len({tool["name"] for tool in tools}) == 43
            resources = session.rpc("resources/list", {})["resources"]
            assert len(resources) == 3
            status = session.call("sandboxie_status")
            assert status["sandboxieVersion"] == "1.18.5", "This regression targets Plus 1.18.5."
            resource_status = session.rpc("resources/read", {"uri": "sandboxie://status"})
            assert json.loads(resource_status["contents"][0]["text"])["status"] == "success"
            message_page = session.call("sandboxie_messages", limit=5)
            assert message_page["source"] == "event-log" and isinstance(message_page["messages"], list)
            session.call("sandboxie_messages", source="native", expected_error="feature_unavailable")
            passed("MCP discovery, schemas, resources, diagnostics, Windows events and native journal restrictions")
            session.call("sandbox_create", sandbox=names[0])
            old = session.call("configuration_read", section=names[0])
            apply(names[0], {"BoxAlias": ["Проверка Unicode"], "BlockNetworkFiles": ["y"]})
            session.call("configuration_apply", expected_error="configuration_conflict", section=names[0],
                         expectedRevision=old["revision"], changes=[{"setting": "BoxAlias", "values": ["Stale"]}])
            assert session.call("sandbox_list")[0]["name"] == names[0]
            passed("Sandbox creation, Unicode settings and stale revision rejection")
            process = session.call("process_start", sandbox=names[0], executable=r"C:\Windows\System32\ping.exe",
                                   arguments=["-n", "30", "127.0.0.1"])
            pid = process["process"]["processId"]
            session.call("file_list", expected_error="sandbox_busy", sandbox=names[0])
            initial_wait = session.call("process_wait", sandbox=names[0], processId=pid, seconds=1)
            assert not initial_wait["exited"], initial_wait
            session.call("process_suspend", sandbox=names[0], suspended=True)
            assert session.call("process_info", sandbox=names[0], processId=pid)["details"]["suspended"]
            session.call("process_suspend", sandbox=names[0], processId=pid, suspended=False)
            assert not session.call("process_info", sandbox=names[0], processId=pid)["details"]["suspended"]
            session.call("process_stop", sandbox=names[0])
            passed("Native launch, process identity, suspend/resume, busy guard and termination")
            source = root / "fixture.txt"
            source.write_bytes(b"First snapshot value\n")
            relative = rf"drive\C\Windows\Temp\mcp_snapshot_{token}.txt"
            session.call("file_import", sandbox=names[0], sourcePath=str(source), destinationPath=relative)
            first = session.call("snapshot_create", sandbox=names[0], name="Первый снимок")["snapshot"]["id"]
            source.write_bytes(b"Second snapshot value\n")
            session.call("file_import", sandbox=names[0], sourcePath=str(source), destinationPath=relative)
            second = session.call("snapshot_create", sandbox=names[0], name="Second")["snapshot"]["id"]
            session.call("snapshot_restore", sandbox=names[0], snapshotId=first)
            session.call("snapshot_delete", sandbox=names[0], snapshotId=second)
            session.call("snapshot_update", sandbox=names[0], snapshotId=first, description="Проверка", makeDefault=True)
            virtual_input = rf"C:\Windows\Temp\mcp_snapshot_{token}.txt"
            virtual_output = rf"C:\Windows\Temp\mcp_overlay_{token}.txt"
            script = ("Start-Sleep -Milliseconds 700; [IO.File]::WriteAllText('" + virtual_output +
                      "',[IO.File]::ReadAllText('" + virtual_input + "')); exit 0")
            process = session.call("process_start", sandbox=names[0],
                                   executable=r"C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe",
                                   arguments=["-NoProfile", "-NonInteractive", "-Command", script])
            completion = session.call("process_wait", sandbox=names[0], processId=process["process"]["processId"], seconds=30)
            assert completion.get("exitCode") == 0, completion
            session.call("process_stop", sandbox=names[0])
            assert session.call("file_read", sandbox=names[0], path=rf"drive\C\Windows\Temp\mcp_overlay_{token}.txt")["content"] == "First snapshot value\n"
            passed("Native snapshot branches, restored driver overlay and UTF-8 metadata")
            virtual_output = rf"C:\Windows\Temp\mcp_environment_{token}.txt"
            script = ("Start-Sleep -Milliseconds 700; [IO.File]::WriteAllText('" + virtual_output +
                      "',[Environment]::GetEnvironmentVariable('MCP_TEST_VALUE')+'|'+[Environment]::GetEnvironmentVariable('MCP_HOST_SECRET_SENTINEL')); exit 0")
            process = session.call("process_start", sandbox=names[0],
                                   executable=r"C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe",
                                   arguments=["-NoProfile", "-NonInteractive", "-Command", script],
                                   environment={"MCP_TEST_VALUE": "Unicode Русский"})
            assert session.call("process_wait", sandbox=names[0], processId=process["process"]["processId"], seconds=30)["exitCode"] == 0
            session.call("process_stop", sandbox=names[0])
            assert session.call("file_read", sandbox=names[0], path=rf"drive\C\Windows\Temp\mcp_environment_{token}.txt")["content"] == "Unicode Русский|"
            passed("Custom environment and exclusion of inherited host secrets")
            session.call("file_read", expected_error="unsafe_path", sandbox=names[0], path="../outside.txt")
            if not status["capabilities"]["encryption"]:
                session.call("image_create", expected_error="feature_unavailable", sandbox=names[0], sizeMiB=64)
            passed("Traversal rejection and unavailable premium feature reporting")
            apply("Template_" + template, {"Tmpl.Title": ["MCP integration template"], "BlockNetParam": ["y"]})
            assert any(item["name"] == template for item in session.call("template_list"))
            revision = session.call("configuration_read", section=names[0])["revision"]
            session.call("template_assign", sandbox=names[0], values=[template], expectedRevision=revision)
            session.call("template_delete", expected_error="access_denied", template=template)
            revision = session.call("rules_read", sandbox=names[0])["revision"]
            session.call("network_rules_replace", sandbox=names[0], values=[{"action": "block", "ports": "1-65535", "protocol": "TCP"}], expectedRevision=revision)
            revision = session.call("rules_read", sandbox=names[0])["revision"]
            session.call("resource_rules_replace", sandbox=names[0], setting="ClosedFilePath", values=[r"C:\McpNonexistentPrivate"], expectedRevision=revision)
            revision = session.call("rules_read", sandbox=names[0])["revision"]
            session.call("program_rules_replace", sandbox=names[0], setting="LingerProcess", values=["mcp_nonexistent.exe"], expectedRevision=revision)
            passed("Template assignment, dependency protection and typed rule writes")
            archive = root / "export.zip"
            exported = session.call("sandbox_export", sandbox=names[0], destinationPath=str(archive))
            assert exported["sha256"].lower() == hashlib.sha256(archive.read_bytes()).hexdigest()
            session.call("sandbox_import", sandbox=names[1], sourcePath=str(archive))
            assert session.call("snapshot_list", sandbox=names[1])["current"] == first
            assert session.call("file_read", sandbox=names[1], path=rf"drive\C\Windows\Temp\mcp_environment_{token}.txt")["content"] == "Unicode Русский|"
            before_root = session.call("sandbox_info", sandbox=names[1])["paths"]["fileRoot"]
            session.call("sandbox_rename", sandbox=names[1], newName=names[2])
            assert session.call("sandbox_info", sandbox=names[2])["paths"]["fileRoot"] == before_root
            assert session.call("sandbox_size", sandbox=names[2])["files"] > 0
            recovered = root / "recovered.txt"
            session.call("file_recover", sandbox=names[2], sourcePath="snapshot-" + first + "/" + relative.replace("\\", "/"), destinationPath=str(recovered))
            assert recovered.read_bytes() == b"First snapshot value\n"
            passed("Archive export/import, checksum, snapshots, rename with data preservation and recovery")
            readonly_config = root / "readonly.json"
            readonly_config.write_text(json.dumps({"sandboxiePath": str(sandboxie), "language": "ru",
                "stateDirectory": str(root / "readonly-state"), "policy": {"profile": "read-only", "allowedSandboxes": [names[0]]}}), encoding="utf-8")
            restricted = Session(command, readonly_config, root / "readonly-stderr.log")
            try:
                assert [box["name"] for box in restricted.call("sandbox_list")] == [names[0]]
                denied = restricted.call("sandbox_clean", expected_error="access_denied", sandbox=names[0])
                assert denied["message"] == "Операция запрещена политикой доступа."
                restricted.call("sandbox_info", expected_error="access_denied", sandbox=names[2])
                restricted.call("configuration_read", expected_error="access_denied", section="GlobalSettings")
                restricted.call("sandboxie_messages", expected_error="access_denied")
            finally:
                restricted.close()
            passed("Live read-only isolation, sandbox visibility and Russian responses")
            assert session.call("audit_read", limit=200)
        except Exception:
            try:
                own_messages = [item for item in session.call("sandboxie_messages")
                                if prefix in item.get("text", "") or item.get("processId") == locals().get("pid")]
                print("Test sandbox diagnostics:", own_messages, flush=True)
            except Exception:
                pass
            raise
        finally:
            cleanup_errors = []
            for name in [names[0], names[2], names[1]]:
                try:
                    visible = [box["name"] for box in session.call("sandbox_list")]
                    if name not in visible:
                        continue
                    session.call("process_stop", sandbox=name)
                    # Sandboxie's own helper releases registry handles before removing the test root.
                    subprocess.run([str(sandboxie / "Start.exe"), "/box:" + name, "delete_sandbox_silent"],
                                   check=True, timeout=40, creationflags=subprocess.CREATE_NO_WINDOW)
                    time.sleep(0.3)
                    session.call("sandbox_clean", sandbox=name)
                    session.call("sandbox_delete", sandbox=name)
                except Exception as error:
                    cleanup_errors.append({"sandbox": name, "error": str(error)})
            try:
                if any(item["name"] == template for item in session.call("template_list")):
                    session.call("template_delete", template=template)
            except Exception as error:
                cleanup_errors.append({"template": template, "error": str(error)})
            session.close()
            if cleanup_errors:
                raise RuntimeError({"cleanupFailed": cleanup_errors})
        passed("All generated sandbox definitions and templates removed")
    report = {"serverVersion": session.server_info["version"], "sandboxieVersion": "1.18.5", "checks": checks,
              "passed": True, "testedWindowsEventLog": True, "testedNativePopupJournal": False,
              "testedEncryptedImages": False, "stoppedHostServices": False}
    if arguments.report:
        report_path = Path(arguments.report).resolve()
        report_path.parent.mkdir(parents=True, exist_ok=True)
        report_path.write_text(json.dumps(report, indent=2), encoding="utf-8")
    print(json.dumps(report, indent=2))


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--server", required=True, help="Built executable or .dll")
    parser.add_argument("--dotnet", default="dotnet", help="dotnet executable when testing a .dll")
    parser.add_argument("--sandboxie", default=r"C:\Program Files\Sandboxie-Plus")
    parser.add_argument("--report", help="Optional JSON evidence file")
    run(parser.parse_args())
