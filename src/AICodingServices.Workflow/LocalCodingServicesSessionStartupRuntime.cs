using System.Diagnostics;
using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace AICodingServices.Workflow;

public sealed class LocalCodingServicesSessionStartupRuntime : ICodingServicesSessionStartupRuntime
{
    private static readonly HttpClient HttpClient = CreateHttpClient();

    public Task<IReadOnlyList<CodingServicesProcessDescriptor>> FindRunningCodexUiProcessesAsync(
        CodingServicesStartupPaths paths,
        CancellationToken cancellationToken)
    {
        List<CodingServicesProcessDescriptor> results = [];
        Process[] processes = Process.GetProcessesByName("CodexUI");
        foreach (Process process in processes)
        {
            try
            {
                results.Add(new CodingServicesProcessDescriptor(
                    process.Id,
                    process.ProcessName,
                    paths.CodexUiExePath));
            }
            finally
            {
                process.Dispose();
            }
        }

        return Task.FromResult<IReadOnlyList<CodingServicesProcessDescriptor>>(results);
    }

    public Task StopProcessAsync(int processId, CancellationToken cancellationToken)
    {
        Process? process = null;
        try
        {
            process = Process.GetProcessById(processId);
            if (process.HasExited)
            {
                return Task.CompletedTask;
            }

            process.Kill(entireProcessTree: true);
            return process.WaitForExitAsync(cancellationToken);
        }
        catch (ArgumentException)
        {
            return Task.CompletedTask;
        }
        finally
        {
            if (process is not null)
            {
                process.Dispose();
            }
        }
    }

    public Task<int?> StartCodexUiAsync(CodingServicesStartupPaths paths, CancellationToken cancellationToken)
    {
        if (File.Exists(paths.CodexUiExePath))
        {
            ProcessStartInfo startInfo = new()
            {
                FileName = paths.CodexUiExePath,
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = Path.GetDirectoryName(paths.CodexUiExePath) ?? paths.RepositoryRoot
            };
            startInfo.ArgumentList.Add("--urls");
            startInfo.ArgumentList.Add(paths.SiteUrl.TrimEnd('/'));

            Process? process = Process.Start(startInfo);
            if (process is null)
            {
                return Task.FromResult<int?>(null);
            }

            int processId = process.Id;
            process.Dispose();
            return Task.FromResult<int?>(processId);
        }

        if (!File.Exists(paths.CodexUiDllPath))
        {
            return Task.FromResult<int?>(null);
        }

        ProcessStartInfo dllStartInfo = new()
        {
            FileName = "dotnet",
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(paths.CodexUiDllPath) ?? paths.RepositoryRoot
        };
        dllStartInfo.ArgumentList.Add(paths.CodexUiDllPath);
        dllStartInfo.ArgumentList.Add("--urls");
        dllStartInfo.ArgumentList.Add(paths.SiteUrl.TrimEnd('/'));

        Process? dllProcess = Process.Start(dllStartInfo);
        if (dllProcess is null)
        {
            return Task.FromResult<int?>(null);
        }

        int dllProcessId = dllProcess.Id;
        dllProcess.Dispose();
        return Task.FromResult<int?>(dllProcessId);
    }

    public async Task<CodingServicesProbeResult> ProbeSiteAsync(string siteUrl, CancellationToken cancellationToken)
    {
        Stopwatch stopwatch = Stopwatch.StartNew();
        string normalizedUrl = siteUrl.EndsWith("/", StringComparison.Ordinal)
            ? siteUrl
            : siteUrl + "/";

        for (int attempt = 0; attempt < 10; attempt++)
        {
            try
            {
                HttpRequestMessage request = new(HttpMethod.Get, normalizedUrl);
                try
                {
                    HttpResponseMessage response = await HttpClient.SendAsync(request, cancellationToken);
                    try
                    {
                        if (response.IsSuccessStatusCode)
                        {
                            return new CodingServicesProbeResult(
                                CodingServicesProbeState.Healthy,
                                $"CodexUI is reachable at {normalizedUrl}.",
                                stopwatch.ElapsedMilliseconds);
                        }
                    }
                    finally
                    {
                        response.Dispose();
                    }
                }
                finally
                {
                    request.Dispose();
                }

                await Task.Delay(500, cancellationToken);
            }
            catch (HttpRequestException)
            {
                await Task.Delay(500, cancellationToken);
            }
        }

        return new CodingServicesProbeResult(
            CodingServicesProbeState.Failed,
            $"CodexUI did not become reachable at {normalizedUrl}.",
            stopwatch.ElapsedMilliseconds);
    }

    public async Task<CodingServicesDirectBridgeProbeResult> ProbeDirectBridgeAsync(
        CodingServicesStartupPaths paths,
        CancellationToken cancellationToken)
    {
        Stopwatch stopwatch = Stopwatch.StartNew();
        if (!File.Exists(paths.DirectBridgeDllPath))
        {
            return new CodingServicesDirectBridgeProbeResult(
                CodingServicesProbeState.Missing,
                $"Direct bridge DLL was not found at {paths.DirectBridgeDllPath}.",
                null,
                stopwatch.ElapsedMilliseconds);
        }

        Process process = new();
        try
        {
            process.StartInfo = new ProcessStartInfo("dotnet")
            {
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                WorkingDirectory = paths.RepositoryRoot
            };
            process.StartInfo.ArgumentList.Add(paths.DirectBridgeDllPath);
            process.StartInfo.ArgumentList.Add("--repo-root");
            process.StartInfo.ArgumentList.Add(paths.RepositoryRoot);
            process.StartInfo.ArgumentList.Add("--config");
            process.StartInfo.ArgumentList.Add(paths.SettingsPath);

            if (!process.Start())
            {
                return new CodingServicesDirectBridgeProbeResult(
                    CodingServicesProbeState.Failed,
                    "Direct bridge process did not start.",
                    null,
                    stopwatch.ElapsedMilliseconds);
            }

            Stream outputStream = process.StandardOutput.BaseStream;
            Stream inputStream = process.StandardInput.BaseStream;
            string initializeJson = CodingServicesMcpBridgeProtocol.CreateInitializeRequestJson();
            await WriteFramedMessageAsync(inputStream, initializeJson, cancellationToken);

            string initializeResponse = await ReadFramedMessageAsync(outputStream, cancellationToken);
            if (!MessageHasId(initializeResponse, CodingServicesMcpBridgeProtocol.InitializeRequestId))
            {
                string stderrText = await ReadErrorTextAsync(process, cancellationToken);
                return new CodingServicesDirectBridgeProbeResult(
                    CodingServicesProbeState.Failed,
                    $"Direct bridge initialize did not return the expected response. {stderrText}".Trim(),
                    null,
                    stopwatch.ElapsedMilliseconds);
            }

            string initializedJson = CodingServicesMcpBridgeProtocol.CreateInitializedNotificationJson();
            await WriteFramedMessageAsync(inputStream, initializedJson, cancellationToken);

            string toolCallJson = CodingServicesMcpBridgeProtocol.CreateMonitorStatusToolCallJson();
            await WriteFramedMessageAsync(inputStream, toolCallJson, cancellationToken);

            string toolCallResponse = await ReadFramedMessageAsync(outputStream, cancellationToken);
            if (!MessageHasId(toolCallResponse, CodingServicesMcpBridgeProtocol.MonitorStatusRequestId))
            {
                string stderrText = await ReadErrorTextAsync(process, cancellationToken);
                return new CodingServicesDirectBridgeProbeResult(
                    CodingServicesProbeState.Failed,
                    $"Direct bridge tool call did not return the expected response. {stderrText}".Trim(),
                    null,
                    stopwatch.ElapsedMilliseconds);
            }

            string monitorSummary = ExtractMonitorSummary(toolCallResponse);
            return new CodingServicesDirectBridgeProbeResult(
                CodingServicesProbeState.Healthy,
                $"Direct bridge responded to get_monitor_status via {paths.ServerName}.",
                monitorSummary,
                stopwatch.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            return new CodingServicesDirectBridgeProbeResult(
                CodingServicesProbeState.Failed,
                $"Direct bridge probe failed: {ex.Message}",
                null,
                stopwatch.ElapsedMilliseconds);
        }
        finally
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                    await process.WaitForExitAsync(cancellationToken);
                }
            }
            catch (InvalidOperationException)
            {
            }
            finally
            {
                process.Dispose();
            }
        }
    }

    private static async Task WriteFramedMessageAsync(Stream output, string json, CancellationToken cancellationToken)
    {
        byte[] payload = Encoding.UTF8.GetBytes(json);
        byte[] header = Encoding.ASCII.GetBytes($"{CodingServicesMcpBridgeProtocol.ContentLengthHeaderName}: {payload.Length}\r\n\r\n");
        await output.WriteAsync(header.AsMemory(0, header.Length), cancellationToken);
        await output.WriteAsync(payload.AsMemory(0, payload.Length), cancellationToken);
        await output.FlushAsync(cancellationToken);
    }

    private static async Task<string> ReadFramedMessageAsync(Stream input, CancellationToken cancellationToken)
    {
        string headerLine = await ReadAsciiLineAsync(input, cancellationToken);
        if (!headerLine.StartsWith($"{CodingServicesMcpBridgeProtocol.ContentLengthHeaderName}:", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Unexpected MCP header '{headerLine}'.");
        }

        string lengthText = headerLine[(CodingServicesMcpBridgeProtocol.ContentLengthHeaderName.Length + 1)..].Trim();
        if (!int.TryParse(lengthText, out int contentLength))
        {
            throw new InvalidOperationException($"Invalid Content-Length header '{headerLine}'.");
        }

        while (true)
        {
            string line = await ReadAsciiLineAsync(input, cancellationToken);
            if (line.Length == 0)
            {
                break;
            }
        }

        byte[] buffer = new byte[contentLength];
        int offset = 0;
        while (offset < buffer.Length)
        {
            int read = await input.ReadAsync(buffer.AsMemory(offset, buffer.Length - offset), cancellationToken);
            if (read == 0)
            {
                throw new EndOfStreamException("Direct bridge closed before the MCP payload completed.");
            }

            offset += read;
        }

        return Encoding.UTF8.GetString(buffer);
    }

    private static async Task<string> ReadAsciiLineAsync(Stream input, CancellationToken cancellationToken)
    {
        List<byte> bytes = [];
        byte[] buffer = new byte[1];
        while (true)
        {
            int read = await input.ReadAsync(buffer.AsMemory(0, 1), cancellationToken);
            if (read == 0)
            {
                throw new EndOfStreamException("Direct bridge closed before a complete header line was read.");
            }

            if (buffer[0] == (byte)'\n')
            {
                if (bytes.Count > 0 && bytes[^1] == (byte)'\r')
                {
                    bytes.RemoveAt(bytes.Count - 1);
                }

                return Encoding.ASCII.GetString(bytes.ToArray());
            }

            bytes.Add(buffer[0]);
        }
    }

    private static bool MessageHasId(string json, int expectedId)
    {
        JsonDocument document = JsonDocument.Parse(json);
        try
        {
            if (!document.RootElement.TryGetProperty("id", out JsonElement idElement))
            {
                return false;
            }

            if (idElement.ValueKind != JsonValueKind.Number)
            {
                return false;
            }

            int actualId;
            if (!idElement.TryGetInt32(out actualId))
            {
                return false;
            }

            return actualId == expectedId;
        }
        finally
        {
            document.Dispose();
        }
    }

    private static string ExtractMonitorSummary(string json)
    {
        JsonDocument document = JsonDocument.Parse(json);
        try
        {
            if (!document.RootElement.TryGetProperty("result", out JsonElement resultElement))
            {
                return "No result payload was returned.";
            }

            if (!resultElement.TryGetProperty("content", out JsonElement contentElement)
                || contentElement.ValueKind != JsonValueKind.Array)
            {
                return "Bridge result did not include MCP content entries.";
            }

            foreach (JsonElement item in contentElement.EnumerateArray())
            {
                if (item.TryGetProperty("text", out JsonElement textElement)
                    && textElement.ValueKind == JsonValueKind.String
                    && !string.IsNullOrWhiteSpace(textElement.GetString()))
                {
                    string text = textElement.GetString()!;
                    return text.Length <= 240 ? text : text[..240];
                }
            }

            return "Bridge call returned content without text.";
        }
        finally
        {
            document.Dispose();
        }
    }

    private static async Task<string> ReadErrorTextAsync(Process process, CancellationToken cancellationToken)
    {
        string text = await process.StandardError.ReadToEndAsync(cancellationToken);
        return text.Trim();
    }

    private static HttpClient CreateHttpClient()
    {
        HttpClient client = new();
        client.Timeout = TimeSpan.FromSeconds(2);
        return client;
    }
}
