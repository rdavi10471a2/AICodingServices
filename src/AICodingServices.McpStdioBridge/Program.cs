using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using AICodingServices.Core;
using AICodingServices.Logging;

namespace AICodingServices.McpStdioBridge;

internal static class Program
{
    private const int ProxyHubConnectTimeoutMilliseconds = 60000;

    public static async Task<int> Main(string[] args)
    {
        try
        {
            BridgeOptions options = BridgeOptions.Parse(args);
            if (options.ShowHelp)
            {
                PrintUsage();
                return 0;
            }

            MonitorSettings settings = MonitorSettingsLoader.Load(options.RepositoryRoot, options.SettingsPath);
            string pipeName = MonitorMcpProxyPipeNames.GetDefaultPipeName(settings);

            using NamedPipeClientStream pipe = new(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            try
            {
                await pipe.ConnectAsync(ProxyHubConnectTimeoutMilliseconds);
            }
            catch (TimeoutException)
            {
                Console.Error.WriteLine($"AICodingServices MCP proxy hub did not accept pipe '{pipeName}' within {ProxyHubConnectTimeoutMilliseconds / 1000} seconds.");
                Console.Error.WriteLine("Start or restart AICodingServices.exe, then restart the client/test MCP session.");
                return 10;
            }
            catch (IOException ex)
            {
                Console.Error.WriteLine($"Unable to connect to AICodingServices MCP proxy hub pipe '{pipeName}': {ex.Message}");
                Console.Error.WriteLine("Start AICodingServices.exe and restart the client/test MCP session.");
                return 11;
            }

            using StreamReader pipeReader = new(pipe, Encoding.UTF8, leaveOpen: true);
            await using StreamWriter pipeWriter = new(pipe, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true };
            using StreamReader stdin = new(Console.OpenStandardInput(), Encoding.UTF8);
            await using StreamWriter stdout = new(Console.OpenStandardOutput(), new UTF8Encoding(false)) { AutoFlush = true };

            await pipeWriter.WriteLineAsync(JsonSerializer.Serialize(new
            {
                server = options.Server,
                settingsPath = options.SettingsPath,
                serverDll = options.ServerDll
            }));

            Task inputPump = PumpAsync(stdin, pipeWriter);
            Task outputPump = PumpAsync(pipeReader, stdout);
            Task completed = await Task.WhenAny(inputPump, outputPump);
            if (completed.IsFaulted)
            {
                Exception ex = completed.Exception?.GetBaseException() ?? new InvalidOperationException("Unknown bridge pump failure.");
                Console.Error.WriteLine($"AICodingServices MCP stdio bridge disconnected with an error: {ex.Message}");
                return 20;
            }

            if (ReferenceEquals(completed, outputPump))
            {
                Console.Error.WriteLine("AICodingServices MCP stdio bridge disconnected because the WinForms proxy hub closed the server stream.");
            }

            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"AICodingServices MCP stdio bridge failed before startup completed: {ex.Message}");
            return 1;
        }
    }

    private static async Task PumpAsync(TextReader reader, TextWriter writer)
    {
        while (await reader.ReadLineAsync() is { } line)
        {
            await writer.WriteLineAsync(line);
            await writer.FlushAsync();
        }
    }

    private static void PrintUsage()
    {
        Console.Error.WriteLine("AICodingServices.McpStdioBridge [--server monitor] [--repo-root <path>] [--config <path>] [--server-dll <path>]");
        Console.Error.WriteLine("AICodingServices.McpStdioBridge connects MCP stdio to the WinForms-owned AICodingServices MCP proxy hub.");
    }

    private sealed record BridgeOptions(
        string Server,
        string RepositoryRoot,
        string? SettingsPath,
        string? ServerDll,
        bool ShowHelp)
    {
        public static BridgeOptions Parse(string[] args)
        {
            string server = "monitor";
            string repositoryRoot = Directory.GetCurrentDirectory();
            string? settingsPath = null;
            string? serverDll = null;
            bool showHelp = false;

            for (int index = 0; index < args.Length; index++)
            {
                string arg = args[index];
                if (arg is "--help" or "-h" or "/?")
                {
                    showHelp = true;
                    continue;
                }

                if (arg.Equals("--server", StringComparison.OrdinalIgnoreCase) && index + 1 < args.Length)
                {
                    server = args[++index];
                    continue;
                }

                if (arg.Equals("--repo-root", StringComparison.OrdinalIgnoreCase) && index + 1 < args.Length)
                {
                    repositoryRoot = args[++index];
                    continue;
                }

                if (arg.Equals("--config", StringComparison.OrdinalIgnoreCase) && index + 1 < args.Length)
                {
                    settingsPath = args[++index];
                    continue;
                }

                if (arg.Equals("--server-dll", StringComparison.OrdinalIgnoreCase) && index + 1 < args.Length)
                {
                    serverDll = args[++index];
                }
            }

            return new BridgeOptions(server, Path.GetFullPath(repositoryRoot), settingsPath, serverDll, showHelp);
        }
    }
}
