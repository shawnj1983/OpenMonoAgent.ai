using System.Net;
using System.Text;

namespace OpenMono.Tests.Fakes;

/// <summary>
/// Deterministic <see cref="HttpMessageHandler"/> test double. Returns a queued
/// sequence of responses (the last one repeats once exhausted) and records the
/// serialized request bodies it receives, so LLM-client streaming, tool-call,
/// retry, and request-shape behaviour can be unit-tested without a live server.
/// </summary>
internal sealed class StubHttpMessageHandler : HttpMessageHandler
{
    private readonly Queue<Func<HttpRequestMessage, HttpResponseMessage>> _responders;

    public List<string> RequestBodies { get; } = new();
    public List<Uri?> RequestUris { get; } = new();
    public int CallCount { get; private set; }

    public StubHttpMessageHandler(params Func<HttpRequestMessage, HttpResponseMessage>[] responders)
    {
        if (responders.Length == 0)
            throw new ArgumentException("At least one responder is required.", nameof(responders));
        _responders = new Queue<Func<HttpRequestMessage, HttpResponseMessage>>(responders);
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        CallCount++;
        RequestUris.Add(request.RequestUri);
        if (request.Content is not null)
            RequestBodies.Add(await request.Content.ReadAsStringAsync(cancellationToken));

        var responder = _responders.Count > 1 ? _responders.Dequeue() : _responders.Peek();
        return responder(request);
    }

    /// <summary>Builds a 200 OK Server-Sent-Events response from raw SSE text.</summary>
    public static HttpResponseMessage Sse(string body) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(body, Encoding.UTF8, "text/event-stream"),
    };

    /// <summary>Builds a bare response with the given status code (for retry tests).</summary>
    public static HttpResponseMessage Status(HttpStatusCode code) => new(code)
    {
        Content = new StringContent(string.Empty),
    };

    /// <summary>Formats raw payloads as an SSE body: one `data: &lt;payload&gt;` frame each.</summary>
    public static string SseBody(params string[] dataPayloads)
    {
        var sb = new StringBuilder();
        foreach (var payload in dataPayloads)
            sb.Append("data: ").Append(payload).Append("\n\n");
        return sb.ToString();
    }
}
