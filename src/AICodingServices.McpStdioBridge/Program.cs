using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using AICodingServices.Core;
using AICodingServices.Logging;

namespace AICodingServices.McpStdioBridge;

internal static class Program
{
    private const int ProxyHubConnectTimeoutMilliseconds = 60000;
    private static readonly TimeSpan PendingResponseDrainTimeout = TimeSpan.FromSeconds(60);

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

            using (NamedPipeClientStream pipe = new(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous))
            {
                try
                {
                    await pipe.ConnectAsync(ProxyHubConnectTimeoutMilliseconds);
                }
                catch (TimeoutException)
                {
                    Console.Error.WriteLine($"AICodingServices MCP proxy hub did not accept pipe '{pipeName}' within {ProxyHubConnectTimeoutMilliseconds / 1000} seconds.");
                    Console.Error.WriteLine("Start or restart CodexUI, then restart the client/test MCP session.");
                    return 10;
                }
                catch (IOException ex)
                {
                    Console.Error.WriteLine($"Unable to connect to AICodingServices MCP proxy hub pipe '{pipeName}': {ex.Message}");
                    Console.Error.WriteLine("Start CodexUI and restart the client/test MCP session.");
                    return 11;
                }

                using (StreamReader pipeReader = new(pipe, Encoding.UTF8, leaveOpen: true))
                {
                    using (StreamWriter pipeWriter = new(pipe, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true })
                    {
                        Stream stdin = Console.OpenStandardInput();
                        Stream stdout = Console.OpenStandardOutput();
                        StdioProtocolState protocolState = new();

                        await pipeWriter.WriteLineAsync(JsonSerializer.Serialize(new
                        {
                            server = options.Server,
                            settingsPath = options.SettingsPath,
                            serverDll = options.ServerDll
                        }));

                        Task inputPump = PumpClientToHubAsync(stdin, pipeWriter, protocolState);
                        Task outputPump = PumpHubToClientAsync(pipeReader, stdout, protocolState);
                        Task completed = await Task.WhenAny(inputPump, outputPump);
                        if (completed.IsFaulted)
                        {
                            Exception ex = completed.Exception?.GetBaseException() ?? new InvalidOperationException("Unknown bridge pump failure.");
                            Console.Error.WriteLine($"AICodingServices MCP stdio bridge disconnected with an error: {ex.Message}");
                            return 20;
                        }

                        if (ReferenceEquals(completed, inputPump))
                        {
                            await protocolState.WaitForPendingResponsesAsync(PendingResponseDrainTimeout);
                            return 0;
                        }

                        if (ReferenceEquals(completed, outputPump))
                        {
                            Console.Error.WriteLine("AICodingServices MCP stdio bridge disconnected because the CodexUI proxy hub closed the server stream.");
                        }

                        return 0;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"AICodingServices MCP stdio bridge failed before startup completed: {ex.Message}");
            return 1;
        }
    }

    private static async Task PumpClientToHubAsync(Stream input, TextWriter hubWriter, StdioProtocolState protocolState)
    {
        StdioMessageReader reader = new(input);
        while (await reader.ReadNextAsync() is { } message)
        {
            protocolState.ObserveInputMode(message.Mode);
            protocolState.ObserveClientMessage(message.Json);
            await hubWriter.WriteLineAsync(message.Json);
            await hubWriter.FlushAsync();
        }
    }

    private static async Task PumpHubToClientAsync(TextReader hubReader, Stream output, StdioProtocolState protocolState)
    {
        while (await hubReader.ReadLineAsync() is { } line)
        {
            protocolState.ObserveHubMessage(line);
            if (protocolState.UseFramedOutput)
            {
                await WriteFramedMessageAsync(output, line);
            }
            else
            {
                await WriteLineMessageAsync(output, line);
            }
        }
    }

    private static async Task WriteFramedMessageAsync(Stream output, string json)
    {
        await StdioMessageWriter.WriteFramedAsync(output, json);
    }

    private static async Task WriteLineMessageAsync(Stream output, string json)
    {
        await StdioMessageWriter.WriteLineAsync(output, json);
    }

    private static void PrintUsage()
    {
        Console.Error.WriteLine("AICodingServices.McpStdioBridge [--server aicodingservices] [--repo-root <path>] [--config <path>] [--server-dll <path>]");
        Console.Error.WriteLine("AICodingServices.McpStdioBridge connects MCP stdio to the CodexUI-owned AICodingServices MCP proxy hub.");
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
            string server = "aicodingservices";
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
