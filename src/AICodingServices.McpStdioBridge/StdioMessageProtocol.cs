using System.Text;
using System.Text.Json;

namespace AICodingServices.McpStdioBridge;

internal enum StdioMessageMode
{
    Line,
    Framed
}

internal sealed record StdioMessage(string Json, StdioMessageMode Mode);

internal sealed class StdioProtocolState
{
    private const int Unknown = 0;
    private const int Line = 1;
    private const int Framed = 2;

    private readonly object pendingGate = new();
    private int observedMode;
    private int pendingResponses;
    private TaskCompletionSource<bool> pendingDrained = CreateCompletedTaskSource();

    public bool UseFramedOutput => Volatile.Read(ref observedMode) == Framed;

    public void ObserveInputMode(StdioMessageMode mode)
    {
        int value;
        if (mode == StdioMessageMode.Framed)
        {
            value = Framed;
        }
        else
        {
            value = Line;
        }

        Interlocked.CompareExchange(ref observedMode, value, Unknown);
    }

    public void ObserveClientMessage(string json)
    {
        if (!HasMessageId(json))
        {
            return;
        }

        lock (pendingGate)
        {
            if (pendingResponses == 0)
            {
                pendingDrained = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            }

            pendingResponses++;
        }
    }

    public void ObserveHubMessage(string json)
    {
        if (!HasMessageId(json))
        {
            return;
        }

        lock (pendingGate)
        {
            if (pendingResponses > 0)
            {
                pendingResponses--;
            }

            if (pendingResponses == 0)
            {
                pendingDrained.TrySetResult(true);
            }
        }
    }

    public async Task<bool> WaitForPendingResponsesAsync(TimeSpan timeout)
    {
        Task pendingTask;
        lock (pendingGate)
        {
            if (pendingResponses == 0)
            {
                return true;
            }

            pendingTask = pendingDrained.Task;
        }

        Task completed = await Task.WhenAny(pendingTask, Task.Delay(timeout));
        return ReferenceEquals(completed, pendingTask);
    }

    private static bool HasMessageId(string json)
    {
        try
        {
            using (JsonDocument document = JsonDocument.Parse(json))
            {
                if (!document.RootElement.TryGetProperty("id", out JsonElement id))
                {
                    return false;
                }

                return id.ValueKind != JsonValueKind.Null;
            }
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static TaskCompletionSource<bool> CreateCompletedTaskSource()
    {
        TaskCompletionSource<bool> source = new(TaskCreationOptions.RunContinuationsAsynchronously);
        source.SetResult(true);
        return source;
    }
}

internal sealed class StdioMessageReader
{
    private static readonly Encoding Utf8 = new UTF8Encoding(false);

    private readonly Stream input;

    public StdioMessageReader(Stream input)
    {
        this.input = input;
    }

    public async Task<StdioMessage?> ReadNextAsync()
    {
        while (true)
        {
            string? line = await ReadHeaderLineAsync();
            if (line is null)
            {
                return null;
            }

            if (line.Length == 0)
            {
                continue;
            }

            if (TryParseContentLength(line, out int contentLength))
            {
                await ReadRemainingHeadersAsync();
                byte[] payload = await ReadExactAsync(contentLength);
                string json = Utf8.GetString(payload);
                return new StdioMessage(json, StdioMessageMode.Framed);
            }

            return new StdioMessage(line, StdioMessageMode.Line);
        }
    }

    private async Task ReadRemainingHeadersAsync()
    {
        while (true)
        {
            string? line = await ReadHeaderLineAsync();
            if (line is null)
            {
                throw new EndOfStreamException("MCP message ended before the header block was complete.");
            }

            if (line.Length == 0)
            {
                return;
            }
        }
    }

    private async Task<string?> ReadHeaderLineAsync()
    {
        MemoryStream buffer = new();
        try
        {
            byte[] oneByte = new byte[1];
            while (true)
            {
                int read = await input.ReadAsync(oneByte, 0, oneByte.Length);
                if (read == 0)
                {
                    if (buffer.Length == 0)
                    {
                        return null;
                    }

                    return Encoding.ASCII.GetString(buffer.ToArray());
                }

                byte value = oneByte[0];
                if (value == (byte)'\n')
                {
                    byte[] bytes = buffer.ToArray();
                    int length = bytes.Length;
                    if (length > 0 && bytes[length - 1] == (byte)'\r')
                    {
                        length--;
                    }

                    return Encoding.ASCII.GetString(bytes, 0, length);
                }

                buffer.WriteByte(value);
            }
        }
        finally
        {
            buffer.Dispose();
        }
    }

    private async Task<byte[]> ReadExactAsync(int contentLength)
    {
        if (contentLength < 0)
        {
            throw new InvalidDataException("Content-Length cannot be negative.");
        }

        byte[] payload = new byte[contentLength];
        int offset = 0;
        while (offset < payload.Length)
        {
            int read = await input.ReadAsync(payload, offset, payload.Length - offset);
            if (read == 0)
            {
                throw new EndOfStreamException("MCP message ended before the declared payload was complete.");
            }

            offset += read;
        }

        return payload;
    }

    private static bool TryParseContentLength(string line, out int contentLength)
    {
        const string HeaderName = "Content-Length:";
        contentLength = 0;
        if (!line.StartsWith(HeaderName, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        string value = line.Substring(HeaderName.Length).Trim();
        if (!int.TryParse(value, out contentLength))
        {
            throw new InvalidDataException($"Invalid MCP Content-Length header: '{line}'.");
        }

        return true;
    }
}

internal static class StdioMessageWriter
{
    public static async Task WriteFramedAsync(Stream output, string json)
    {
        byte[] payload = Encoding.UTF8.GetBytes(json);
        byte[] header = Encoding.ASCII.GetBytes($"Content-Length: {payload.Length}\r\n\r\n");
        await output.WriteAsync(header, 0, header.Length);
        await output.WriteAsync(payload, 0, payload.Length);
        await output.FlushAsync();
    }

    public static async Task WriteLineAsync(Stream output, string json)
    {
        byte[] payload = Encoding.UTF8.GetBytes(json + Environment.NewLine);
        await output.WriteAsync(payload, 0, payload.Length);
        await output.FlushAsync();
    }
}
