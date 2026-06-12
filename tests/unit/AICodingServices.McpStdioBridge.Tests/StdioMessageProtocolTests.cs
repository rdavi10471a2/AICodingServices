using System.Text;
using AICodingServices.McpStdioBridge;

namespace AICodingServices.McpStdioBridge.Tests;

public sealed class StdioMessageProtocolTests
{
    [Fact]
    public async Task ReadNextAsync_UnwrapsContentLengthFramedMessage()
    {
        const string Json = "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"initialize\"}";
        byte[] payload = Encoding.UTF8.GetBytes(Json);
        string framed = $"Content-Length: {payload.Length}\r\n\r\n{Json}";
        MemoryStream input = new(Encoding.UTF8.GetBytes(framed));
        StdioMessageReader reader = new(input);

        StdioMessage? message = await reader.ReadNextAsync();

        Assert.NotNull(message);
        Assert.Equal(StdioMessageMode.Framed, message.Mode);
        Assert.Equal(Json, message.Json);
    }

    [Fact]
    public async Task ReadNextAsync_ReadsLineDelimitedJsonMessage()
    {
        const string Json = "{\"jsonrpc\":\"2.0\",\"id\":2,\"method\":\"tools/list\"}";
        MemoryStream input = new(Encoding.UTF8.GetBytes(Json + "\n"));
        StdioMessageReader reader = new(input);

        StdioMessage? message = await reader.ReadNextAsync();

        Assert.NotNull(message);
        Assert.Equal(StdioMessageMode.Line, message.Mode);
        Assert.Equal(Json, message.Json);
    }

    [Fact]
    public async Task WriteFramedAsync_WritesContentLengthFrame()
    {
        const string Json = "{\"jsonrpc\":\"2.0\",\"id\":2,\"result\":{}}";
        MemoryStream output = new();

        await StdioMessageWriter.WriteFramedAsync(output, Json);

        string framed = Encoding.UTF8.GetString(output.ToArray());
        Assert.StartsWith("Content-Length: ", framed);
        Assert.EndsWith(Json, framed);
        Assert.Contains("\r\n\r\n", framed);
    }

    [Fact]
    public void ObserveInputMode_KeepsFirstObservedMode()
    {
        StdioProtocolState state = new();

        state.ObserveInputMode(StdioMessageMode.Framed);
        state.ObserveInputMode(StdioMessageMode.Line);

        Assert.True(state.UseFramedOutput);
    }

    [Fact]
    public async Task WaitForPendingResponsesAsync_WaitsUntilMatchingResponseIsObserved()
    {
        StdioProtocolState state = new();
        state.ObserveClientMessage("{\"jsonrpc\":\"2.0\",\"id\":7,\"method\":\"tools/call\"}");

        Task<bool> wait = state.WaitForPendingResponsesAsync(TimeSpan.FromSeconds(5));
        Assert.False(wait.IsCompleted);

        state.ObserveHubMessage("{\"jsonrpc\":\"2.0\",\"id\":7,\"result\":{}}");

        Assert.True(await wait);
    }

    [Fact]
    public async Task WaitForPendingResponsesAsync_IgnoresNotifications()
    {
        StdioProtocolState state = new();
        state.ObserveClientMessage("{\"jsonrpc\":\"2.0\",\"method\":\"notifications/initialized\",\"params\":{}}");

        bool drained = await state.WaitForPendingResponsesAsync(TimeSpan.FromSeconds(1));

        Assert.True(drained);
    }
}
