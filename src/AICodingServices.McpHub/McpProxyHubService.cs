using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using AICodingServices.Core;
using AICodingServices.Logging;

namespace AICodingServices.McpHub;

public sealed class McpProxyHubService : IDisposable
{
    public const string ServerName = "aicodingservices";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false
    };

    private readonly MonitorSettings settings;
    private readonly IMonitorLogger logger;
    private readonly string pipeName;
    private readonly CancellationTokenSource shutdown = new();
    private readonly Task acceptLoop;

    public McpProxyHubService(MonitorSettings settings, IMonitorLogger logger)
    {
        this.settings = settings;
        this.logger = logger;
        pipeName = MonitorMcpProxyPipeNames.GetDefaultPipeName(settings);
        acceptLoop = Task.Run(() => AcceptLoopAsync(shutdown.Token));
    }

    public string PipeName => pipeName;

    public void Dispose()
    {
        shutdown.Cancel();
        try
        {
            acceptLoop.Wait(TimeSpan.FromSeconds(2));
        }
        catch
        {
            // Shutdown is best-effort; active MCP clients commonly close stdio first.
        }

        shutdown.Dispose();
    }

    private async Task AcceptLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            NamedPipeServerStream pipe = new(
                pipeName,
                PipeDirection.InOut,
                NamedPipeServerStream.MaxAllowedServerInstances,
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous);

            try
            {
                await pipe.WaitForConnectionAsync(cancellationToken);
                _ = Task.Run(() => HandleClientAsync(pipe, cancellationToken), cancellationToken);
            }
            catch (OperationCanceledException)
            {
                await pipe.DisposeAsync();
                break;
            }
            catch (Exception ex)
            {
                await pipe.DisposeAsync();
                logger.Write(
                    MonitorLogLevel.Warning,
                    "AICodingServices.McpProxyHub",
                    "adapter.mcp.proxy.accept.failed",
                    "MCP proxy hub failed to accept a client.",
                    new Dictionary<string, string>
                    {
                        ["error"] = ex.Message
                    });
            }
        }
    }

    private async Task HandleClientAsync(NamedPipeServerStream pipe, CancellationToken cancellationToken)
    {
        try
        {
            using (StreamReader clientReader = new(pipe, Encoding.UTF8, leaveOpen: true))
            using (StreamWriter clientWriter = new(pipe, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true })
            {
                string? handshakeLine = await clientReader.ReadLineAsync(cancellationToken);
                if (string.IsNullOrWhiteSpace(handshakeLine))
                {
                    return;
                }

                HubHandshake handshake = HubHandshake.Parse(handshakeLine);
                if (!handshake.Server.Equals(ServerName, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException($"Unknown AICodingServices MCP proxy server '{handshake.Server}'. Expected '{ServerName}'.");
                }

                string sessionId = Guid.NewGuid().ToString("N");
                string serverDll = ResolveServerDll(handshake);
                using (Process child = StartServer(serverDll, handshake))
                {
                    ConcurrentDictionary<string, PendingRequest> pending = new(StringComparer.Ordinal);

                    logger.Write(
                        MonitorLogLevel.Information,
                        "AICodingServices.McpProxyHub",
                        "adapter.mcp.bridge.session.started",
                        "MCP proxy hub accepted a bridge session.",
                        new Dictionary<string, string>
                        {
                            ["sessionId"] = sessionId,
                            ["clientServer"] = handshake.Server,
                            ["childProcessId"] = child.Id.ToString(),
                            ["proxyPipeName"] = pipeName
                        });

                    try
                    {
                        Task clientToChild = PumpClientToChildAsync(sessionId, clientReader, child, pending, cancellationToken);
                        Task childToClient = PumpChildToClientAsync(sessionId, child, clientWriter, pending, cancellationToken);
                        Task stderr = PumpChildStderrAsync(sessionId, child, cancellationToken);
                        Task exit = child.WaitForExitAsync(cancellationToken);

                        await Task.WhenAny(clientToChild, childToClient, exit);
                        await StopChildAsync(child, cancellationToken);
                        await Task.WhenAll(SwallowAsync(clientToChild), SwallowAsync(childToClient), SwallowAsync(stderr));
                    }
                    finally
                    {
                        logger.Write(
                            MonitorLogLevel.Information,
                            "AICodingServices.McpProxyHub",
                            "adapter.mcp.bridge.session.completed",
                            "MCP proxy hub closed a bridge session.",
                            new Dictionary<string, string>
                            {
                                ["sessionId"] = sessionId,
                                ["childProcessId"] = child.Id.ToString(),
                                ["exitCode"] = child.HasExited ? child.ExitCode.ToString() : string.Empty
                            });
                    }
                }
            }
        }
        finally
        {
            await pipe.DisposeAsync();
        }
    }

    private async Task PumpClientToChildAsync(
        string sessionId,
        StreamReader clientReader,
        Process child,
        ConcurrentDictionary<string, PendingRequest> pending,
        CancellationToken cancellationToken)
    {
        while (await clientReader.ReadLineAsync(cancellationToken) is { } line)
        {
            MessageMetadata metadata = MessageMetadata.FromJsonLine(line);
            if (!string.IsNullOrWhiteSpace(metadata.Id))
            {
                pending[metadata.Id] = new PendingRequest(
                    DateTimeOffset.UtcNow,
                    metadata.Method,
                    metadata.ToolName,
                    metadata.ArgumentsText,
                    Encoding.UTF8.GetByteCount(line));
            }

            logger.Write(
                MonitorLogLevel.Information,
                "AICodingServices.McpProxyHub",
                "adapter.mcp.request.started",
                "MCP proxy hub forwarded a request to AICodingServices server.",
                BuildRequestProperties(sessionId, metadata, line));

            await child.StandardInput.WriteLineAsync(line);
            await child.StandardInput.FlushAsync(cancellationToken);
        }
    }

    private async Task PumpChildToClientAsync(
        string sessionId,
        Process child,
        StreamWriter clientWriter,
        ConcurrentDictionary<string, PendingRequest> pending,
        CancellationToken cancellationToken)
    {
        while (await child.StandardOutput.ReadLineAsync(cancellationToken) is { } line)
        {
            MessageMetadata metadata = MessageMetadata.FromJsonLine(line);
            PendingRequest? request = null;
            long? durationMs = null;
            if (!string.IsNullOrWhiteSpace(metadata.Id)
                && pending.TryRemove(metadata.Id, out request))
            {
                durationMs = (long)(DateTimeOffset.UtcNow - request.StartedUtc).TotalMilliseconds;
                metadata = metadata with
                {
                    Method = metadata.Method ?? request.Method,
                    ToolName = metadata.ToolName ?? request.ToolName,
                    ArgumentsText = metadata.ArgumentsText ?? request.ArgumentsText
                };
            }

            logger.Write(
                MonitorLogLevel.Information,
                "AICodingServices.McpProxyHub",
                "adapter.mcp.request.completed",
                "MCP proxy hub received a response from AICodingServices server.",
                BuildResponseProperties(sessionId, metadata, request, line, durationMs));

            await clientWriter.WriteLineAsync(line);
        }
    }

    private async Task PumpChildStderrAsync(string sessionId, Process child, CancellationToken cancellationToken)
    {
        while (await child.StandardError.ReadLineAsync(cancellationToken) is { } line)
        {
            logger.Write(
                MonitorLogLevel.Warning,
                "AICodingServices.McpProxyHub",
                "adapter.mcp.bridge.stderr",
                "AICodingServices MCP server wrote stderr.",
                new Dictionary<string, string>
                {
                    ["sessionId"] = sessionId,
                    ["stderr"] = Truncate(line, 4000)
                });
        }
    }

    private Dictionary<string, string> BuildRequestProperties(string sessionId, MessageMetadata metadata, string line)
    {
        Dictionary<string, string> properties = BaseProperties(sessionId, metadata);
        properties["requestBytes"] = Encoding.UTF8.GetByteCount(line).ToString();
        properties["contentTextPreview"] = Truncate(metadata.ArgumentsText ?? line, 500);
        properties["contentText"] = metadata.ArgumentsText ?? line;
        return properties;
    }

    private Dictionary<string, string> BuildResponseProperties(
        string sessionId,
        MessageMetadata metadata,
        PendingRequest? request,
        string line,
        long? durationMs)
    {
        string responseText = ExtractResultText(line) ?? line;
        Dictionary<string, string> properties = BaseProperties(sessionId, metadata);
        properties["isError"] = metadata.IsError.ToString().ToLowerInvariant();
        properties["messageBytes"] = Encoding.UTF8.GetByteCount(line).ToString();
        properties["contentTextPreview"] = Truncate(responseText, 500);
        properties["contentText"] = responseText;
        if (durationMs is not null)
        {
            properties["durationMs"] = durationMs.Value.ToString();
        }

        if (request is not null)
        {
            properties["requestBytes"] = request.RequestBytes.ToString();
        }

        return properties;
    }

    private static Dictionary<string, string> BaseProperties(string sessionId, MessageMetadata metadata)
    {
        Dictionary<string, string> properties = new()
        {
            ["sessionId"] = sessionId,
            ["requestId"] = metadata.Id ?? string.Empty,
            ["method"] = metadata.Method ?? string.Empty
        };
        if (!string.IsNullOrWhiteSpace(metadata.ToolName))
        {
            properties["toolName"] = metadata.ToolName;
        }

        if (!string.IsNullOrWhiteSpace(metadata.ArgumentsText))
        {
            properties["arguments"] = metadata.ArgumentsText;
        }

        return properties;
    }

    private string ResolveServerDll(HubHandshake handshake)
    {
        if (!string.IsNullOrWhiteSpace(handshake.ServerDll))
        {
            return Path.GetFullPath(handshake.ServerDll);
        }

        string debugServer = Path.Combine(settings.RepositoryRoot, "src", "AICodingServices.McpServer", "bin", "Debug", "net10.0", "AICodingServices.McpServer.dll");
        if (File.Exists(debugServer))
        {
            return debugServer;
        }

        string releaseServer = Path.Combine(settings.RepositoryRoot, "src", "AICodingServices.McpServer", "bin", "Release", "net10.0", "AICodingServices.McpServer.dll");
        if (File.Exists(releaseServer))
        {
            return releaseServer;
        }

        throw new FileNotFoundException("AICodingServices MCP server build output was not found.", debugServer);
    }

    private Process StartServer(string serverDll, HubHandshake handshake)
    {
        ProcessStartInfo startInfo = new()
        {
            FileName = "dotnet",
            WorkingDirectory = settings.RepositoryRoot,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add(serverDll);
        startInfo.ArgumentList.Add("--repo-root");
        startInfo.ArgumentList.Add(settings.RepositoryRoot);
        startInfo.ArgumentList.Add("--config");
        startInfo.ArgumentList.Add(string.IsNullOrWhiteSpace(handshake.SettingsPath)
            ? Path.Combine(settings.RepositoryRoot, "config", "appsettings.json")
            : handshake.SettingsPath);

        return Process.Start(startInfo)
            ?? throw new InvalidOperationException("Unable to start AICodingServices MCP server.");
    }

    private static async Task StopChildAsync(Process child, CancellationToken cancellationToken)
    {
        if (child.HasExited)
        {
            return;
        }

        try
        {
            child.StandardInput.Close();
        }
        catch (InvalidOperationException)
        {
        }

        Task exited = child.WaitForExitAsync(cancellationToken);
        Task completed = await Task.WhenAny(exited, Task.Delay(750, cancellationToken));
        if (!ReferenceEquals(completed, exited) && !child.HasExited)
        {
            child.Kill(entireProcessTree: true);
        }
    }

    private static async Task SwallowAsync(Task task)
    {
        try
        {
            await task;
        }
        catch
        {
            // Paired stdio/pipe streams commonly close in either order.
        }
    }

    private static string? ExtractResultText(string line)
    {
        try
        {
            JsonObject root = JsonNode.Parse(line)?.AsObject() ?? [];
            if (root["result"]?["content"] is JsonArray content)
            {
                JsonNode? first = content.FirstOrDefault();
                if (first?["text"] is JsonNode textNode)
                {
                    return textNode.GetValue<string>();
                }
            }

            return root["result"]?.ToJsonString(JsonOptions) ?? root["error"]?.ToJsonString(JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string Truncate(string value, int maxLength)
    {
        return value.Length <= maxLength ? value : value[..maxLength];
    }

    private sealed record HubHandshake(string Server, string? SettingsPath, string? ServerDll)
    {
        public static HubHandshake Parse(string line)
        {
            JsonObject obj = JsonNode.Parse(line)?.AsObject()
                ?? throw new InvalidOperationException("Invalid MCP proxy hub handshake.");
            return new HubHandshake(
                obj["server"]?.GetValue<string>() ?? ServerName,
                obj["settingsPath"]?.GetValue<string>(),
                obj["serverDll"]?.GetValue<string>());
        }
    }

    private sealed record PendingRequest(
        DateTimeOffset StartedUtc,
        string? Method,
        string? ToolName,
        string? ArgumentsText,
        long RequestBytes);

    private sealed record MessageMetadata(
        string? Id,
        string? Method,
        string? ToolName,
        string? ArgumentsText,
        bool IsError)
    {
        public static MessageMetadata FromJsonLine(string line)
        {
            try
            {
                JsonObject root = JsonNode.Parse(line)?.AsObject() ?? [];
                string? id = root["id"]?.ToString();
                string? method = root["method"]?.GetValue<string>();
                string? toolName = null;
                string? argumentsText = null;
                if (string.Equals(method, "tools/call", StringComparison.Ordinal)
                    && root["params"] is JsonObject parameters)
                {
                    toolName = parameters["name"]?.GetValue<string>();
                    argumentsText = parameters["arguments"]?.ToJsonString(JsonOptions);
                }

                bool isError = root["error"] is not null
                    || root["result"]?["isError"]?.GetValue<bool?>() == true;
                return new MessageMetadata(id, method, toolName, argumentsText, isError);
            }
            catch (JsonException)
            {
                return new MessageMetadata(null, null, null, null, false);
            }
        }
    }
}
