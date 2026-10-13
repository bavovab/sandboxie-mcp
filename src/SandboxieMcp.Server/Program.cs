using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;
using SandboxieMcp.Core.Configuration;
using SandboxieMcp.Core.Integration;
using SandboxieMcp.Core.Security;
using SandboxieMcp.Core.Services;
using SandboxieMcp.Server.Tools;

namespace SandboxieMcp.Server;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        try
        {
            if (args.Contains("--help"))
            {
                Console.WriteLine("Sandboxie MCP\nUsage: sandboxie-mcp [--config <absolute-json-path>] [--diagnose | --check-config | --version]");
                return 0;
            }
            if (args.Contains("--version")) { Console.WriteLine(Core.BuildInfo.Version); return 0; }
            string? configPath = null;
            for (var i = 0; i < args.Length; i++)
            {
                if (args[i] == "--config" && i + 1 < args.Length) configPath = args[++i];
                else if (args[i] is not ("--diagnose" or "--check-config"))
                    throw new ArgumentException($"Unknown or incomplete argument: {args[i]}");
            }
            var configuration = ServerConfiguration.Load(configPath);
            if (args.Contains("--check-config")) { Console.WriteLine("Configuration is valid."); return 0; }
            using var api = new NativeSandboxieApi(configuration.SandboxiePath, configuration.ConfigurationPasswordEnvironmentVariable);
            if (args.Contains("--diagnose"))
            {
                var guard = new PolicyGuard(configuration.Policy);
                var boxes = new SandboxManager(api, configuration, guard);
                Console.WriteLine(JsonSerializer.Serialize(new DiagnosticsManager(boxes).Status(), ServerConfiguration.JsonOptions));
                return 0;
            }
            var builder = Host.CreateApplicationBuilder();
            builder.Logging.ClearProviders();
            builder.Logging.AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Trace);
            builder.Services.AddSingleton(configuration);
            builder.Services.AddSingleton<Core.ISandboxieApi>(api);
            builder.Services.AddSingleton(new PolicyGuard(configuration.Policy));
            builder.Services.AddSingleton(new AuditJournal(configuration.StateDirectory));
            builder.Services.AddSingleton<SandboxManager>();
            builder.Services.AddSingleton<ProcessManager>();
            builder.Services.AddSingleton<FileManager>();
            builder.Services.AddSingleton<SnapshotManager>();
            builder.Services.AddSingleton<ArchiveManager>();
            builder.Services.AddSingleton<DiagnosticsManager>();
            builder.Services.AddSingleton<ImageManager>();
            builder.Services.AddSingleton<RuleManager>();
            builder.Services.AddSingleton<TemplateManager>();
            builder.Services.AddSingleton<ToolExecutor>();
            builder.Services.AddMcpServer(options =>
            {
                options.ServerInfo = new Implementation { Name = "sandboxie-mcp", Version = Core.BuildInfo.Version };
                options.ServerInstructions = "Manage Sandboxie Plus on Windows. Read state before changes; respect access policy. " +
                    "Configuration changes may require restarting sandboxed processes. Cleanup and restore can destroy current data.";
            }).WithStdioServerTransport().WithTools<SandboxTools>().WithTools<ProcessTools>()
                .WithTools<FileTools>().WithTools<SnapshotTools>().WithTools<ArchiveTools>()
                .WithTools<SystemTools>().WithTools<ImageTools>().WithTools<RuleTools>().WithTools<TemplateTools>()
                .WithResources<ServerResources>();
            using var host = builder.Build();
            await host.RunAsync();
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine($"Sandboxie MCP could not start: {error.Message}");
            if (error is Core.SandboxieException sandboxError)
                Console.Error.WriteLine(JsonSerializer.Serialize(new { sandboxError.Code, sandboxError.Details }));
            return 1;
        }
    }
}
