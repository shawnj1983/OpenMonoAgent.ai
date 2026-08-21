using System.Net;
using FluentAssertions;
using OpenMono.Config;
using OpenMono.Llm;
using OpenMono.Session;
using OpenMono.Tests.Fakes;

namespace OpenMono.Tests.Llm;

public sealed class OpenAiCompatClientTests
{
    // NOTE: MaxConcurrentRequests is left at the app default (2). OpenAiCompatClient
    // shares a *static* request-gate semaphore that is disposed and recreated whenever
    // a client is built with a different capacity; using a non-default value here would
    // race with other tests (and the app) that use the default. Keeping it aligned keeps
    // the shared gate stable under xunit's parallel execution.
    private static OpenAiCompatClient CreateClient(StubHttpMessageHandler handler) =>
        new(new LlmConfig { Endpoint = "http://test-endpoint", Model = "test-model" },
            new HttpClient(handler));

    private static async Task<List<StreamChunk>> CollectAsync(ILlmClient client)
    {
        var messages = new List<Message> { new() { Role = MessageRole.User, Content = "hi" } };
        var options = new LlmOptions { Model = "test-model", MaxTokens = 128 };
        var chunks = new List<StreamChunk>();
        await foreach (var chunk in client.StreamChatAsync(messages, tools: null, options, CancellationToken.None))
            chunks.Add(chunk);
        return chunks;
    }

    [Fact]
    public async Task StreamChat_AggregatesTextDeltas_AndCompletes()
    {
        var body = StubHttpMessageHandler.SseBody(
            """{"choices":[{"index":0,"delta":{"content":"Hello"},"finish_reason":null}]}""",
            """{"choices":[{"index":0,"delta":{"content":" world"},"finish_reason":null}]}""",
            """{"choices":[{"index":0,"delta":{},"finish_reason":"stop"}]}""",
            "[DONE]");
        using var client = CreateClient(new StubHttpMessageHandler(_ => StubHttpMessageHandler.Sse(body)));

        var chunks = await CollectAsync(client);

        string.Concat(chunks.Select(c => c.TextDelta)).Should().Be("Hello world");
        chunks.Should().Contain(c => c.IsComplete);
    }

    [Fact]
    public async Task StreamChat_AssemblesToolCallAcrossDeltas()
    {
        var body = StubHttpMessageHandler.SseBody(
            """{"choices":[{"index":0,"delta":{"tool_calls":[{"index":0,"id":"call_1","type":"function","function":{"name":"FileRead","arguments":"{\"file"}}]},"finish_reason":null}]}""",
            """{"choices":[{"index":0,"delta":{"tool_calls":[{"index":0,"function":{"arguments":"_path\":\"/x\"}"}}]},"finish_reason":null}]}""",
            """{"choices":[{"index":0,"delta":{},"finish_reason":"tool_calls"}]}""",
            "[DONE]");
        using var client = CreateClient(new StubHttpMessageHandler(_ => StubHttpMessageHandler.Sse(body)));

        var chunks = await CollectAsync(client);

        var toolCalls = chunks.Where(c => c.ToolCallDelta is not null).Select(c => c.ToolCallDelta!).ToList();
        toolCalls.Should().ContainSingle();
        toolCalls[0].Id.Should().Be("call_1");
        toolCalls[0].Name.Should().Be("FileRead");
        toolCalls[0].Arguments.Should().Be("""{"file_path":"/x"}""");
    }

    [Fact]
    public async Task StreamChat_ParsesUsage()
    {
        var body = StubHttpMessageHandler.SseBody(
            """{"choices":[{"index":0,"delta":{"content":"hi"}}]}""",
            """{"choices":[],"usage":{"prompt_tokens":10,"completion_tokens":5}}""",
            "[DONE]");
        using var client = CreateClient(new StubHttpMessageHandler(_ => StubHttpMessageHandler.Sse(body)));

        var chunks = await CollectAsync(client);

        var usage = chunks.Select(c => c.Usage).FirstOrDefault(u => u is not null);
        usage.Should().NotBeNull();
        usage!.PromptTokens.Should().Be(10);
        usage.CompletionTokens.Should().Be(5);
        usage.TotalTokens.Should().Be(15);
    }

    [Fact]
    public async Task StreamChat_ThrowsOnErrorFrame()
    {
        var body = StubHttpMessageHandler.SseBody("""{"error":{"message":"boom"}}""");
        using var client = CreateClient(new StubHttpMessageHandler(_ => StubHttpMessageHandler.Sse(body)));

        var act = async () => await CollectAsync(client);

        await act.Should().ThrowAsync<HttpRequestException>().WithMessage("*boom*");
    }

    [Fact]
    public async Task StreamChat_RetriesOnServiceUnavailable_ThenSucceeds()
    {
        var body = StubHttpMessageHandler.SseBody(
            """{"choices":[{"index":0,"delta":{"content":"ok"},"finish_reason":null}]}""",
            """{"choices":[{"index":0,"delta":{},"finish_reason":"stop"}]}""",
            "[DONE]");
        var handler = new StubHttpMessageHandler(
            _ => StubHttpMessageHandler.Status(HttpStatusCode.ServiceUnavailable),
            _ => StubHttpMessageHandler.Sse(body));
        using var client = CreateClient(handler);

        var chunks = await CollectAsync(client);

        handler.CallCount.Should().Be(2, "the client should retry once after a 503");
        string.Concat(chunks.Select(c => c.TextDelta)).Should().Be("ok");
    }

    [Fact]
    public async Task StreamChat_PostsToChatCompletionsEndpoint_WithStreamEnabled()
    {
        var body = StubHttpMessageHandler.SseBody(
            """{"choices":[{"index":0,"delta":{},"finish_reason":"stop"}]}""", "[DONE]");
        var handler = new StubHttpMessageHandler(_ => StubHttpMessageHandler.Sse(body));
        using var client = CreateClient(handler);

        await CollectAsync(client);

        handler.RequestUris[0]!.ToString().Should().Be("http://test-endpoint/v1/chat/completions");
        handler.RequestBodies[0].Should().Contain("\"stream\":true");
    }
}
