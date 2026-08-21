using System.Text.Json;
using FluentAssertions;
using OpenMono.Llm;
using OpenMono.Session;
using OpenMono.Tests.Fakes;

namespace OpenMono.Tests.Llm;

public sealed class AnthropicClientTests
{
    private static AnthropicClient CreateClient(StubHttpMessageHandler handler) =>
        new(new ProviderConfig { Name = "anthropic", Endpoint = "http://test-endpoint", ApiKey = "test-key", Model = "claude-test" },
            new HttpClient(handler));

    private static async Task<List<StreamChunk>> CollectAsync(
        ILlmClient client, IReadOnlyList<Message>? messages = null, JsonElement? tools = null)
    {
        messages ??= new List<Message> { new() { Role = MessageRole.User, Content = "hi" } };
        var options = new LlmOptions { Model = "claude-test", MaxTokens = 128 };
        var chunks = new List<StreamChunk>();
        await foreach (var chunk in client.StreamChatAsync(messages, tools, options, CancellationToken.None))
            chunks.Add(chunk);
        return chunks;
    }

    [Fact]
    public async Task StreamChat_AggregatesTextDeltas_AndCompletes()
    {
        var body = StubHttpMessageHandler.SseBody(
            """{"type":"content_block_delta","delta":{"type":"text_delta","text":"Hello"}}""",
            """{"type":"content_block_delta","delta":{"type":"text_delta","text":" there"}}""",
            """{"type":"message_stop"}""");
        using var client = CreateClient(new StubHttpMessageHandler(_ => StubHttpMessageHandler.Sse(body)));

        var chunks = await CollectAsync(client);

        string.Concat(chunks.Select(c => c.TextDelta)).Should().Be("Hello there");
        chunks.Should().Contain(c => c.IsComplete);
    }

    [Fact]
    public async Task StreamChat_AssemblesToolUseFromInputJsonDeltas()
    {
        var body = StubHttpMessageHandler.SseBody(
            """{"type":"content_block_start","content_block":{"type":"tool_use","id":"tu_1","name":"get_weather"}}""",
            """{"type":"content_block_delta","delta":{"type":"input_json_delta","partial_json":"{\"city\":"}}""",
            """{"type":"content_block_delta","delta":{"type":"input_json_delta","partial_json":"\"Tokyo\"}"}}""",
            """{"type":"content_block_stop"}""",
            """{"type":"message_stop"}""");
        using var client = CreateClient(new StubHttpMessageHandler(_ => StubHttpMessageHandler.Sse(body)));

        var chunks = await CollectAsync(client);

        var tool = chunks.Select(c => c.ToolCallDelta).FirstOrDefault(t => t is not null);
        tool.Should().NotBeNull();
        tool!.Id.Should().Be("tu_1");
        tool.Name.Should().Be("get_weather");
        tool.Arguments.Should().Be("""{"city":"Tokyo"}""");
    }

    [Fact]
    public async Task StreamChat_ParsesUsageFromMessageDelta()
    {
        var body = StubHttpMessageHandler.SseBody(
            """{"type":"message_delta","usage":{"output_tokens":7}}""",
            """{"type":"message_stop"}""");
        using var client = CreateClient(new StubHttpMessageHandler(_ => StubHttpMessageHandler.Sse(body)));

        var chunks = await CollectAsync(client);

        var usage = chunks.Select(c => c.Usage).FirstOrDefault(u => u is not null);
        usage.Should().NotBeNull();
        usage!.CompletionTokens.Should().Be(7);
    }

    [Fact]
    public async Task StreamChat_ThrowsOnErrorEvent()
    {
        var body = StubHttpMessageHandler.SseBody("""{"type":"error","error":{"message":"rate limited"}}""");
        using var client = CreateClient(new StubHttpMessageHandler(_ => StubHttpMessageHandler.Sse(body)));

        var act = async () => await CollectAsync(client);

        await act.Should().ThrowAsync<HttpRequestException>().WithMessage("*rate limited*");
    }

    [Fact]
    public async Task StreamChat_MapsSystemPrompt_ToolsAndEndpoint()
    {
        var tools = JsonDocument.Parse("""
        [{"type":"function","function":{"name":"get_weather","description":"Get weather","parameters":{"type":"object","properties":{"city":{"type":"string"}}}}}]
        """).RootElement;
        var messages = new List<Message>
        {
            new() { Role = MessageRole.System, Content = "You are helpful." },
            new() { Role = MessageRole.User, Content = "weather in Tokyo?" },
        };
        var handler = new StubHttpMessageHandler(_ => StubHttpMessageHandler.Sse(
            StubHttpMessageHandler.SseBody("""{"type":"message_stop"}""")));
        using var client = CreateClient(handler);

        await CollectAsync(client, messages, tools);

        handler.RequestUris[0]!.ToString().Should().Be("http://test-endpoint/v1/messages");
        var request = handler.RequestBodies[0];
        request.Should().Contain("You are helpful.");   // system prompt hoisted out of messages
        request.Should().Contain("\"stream\":true");
        request.Should().Contain("input_schema");        // OpenAI tool schema mapped to Anthropic shape
    }
}
