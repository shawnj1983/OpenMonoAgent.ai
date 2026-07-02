using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace OpenMono.Acp;

public static class AcpCli
{
    private const int DefaultPort = 7475;

    public static async Task<int> RunAsync(string[] args, CancellationToken ct)
    {
        if (args.Length == 0 || args[0] is "help" or "--help" or "-h")
        {
            PrintHelp();
            return 0;
        }

        var port = DefaultPort;
        var model = (string?)null;

        // global flags (simple)
        var rest = new List<string>();
        for (var i = 0; i < args.Length; i++)
        {
            var a = args[i];
            var next = i + 1 < args.Length ? args[i + 1] : null;
            switch (a)
            {
                case "--port" when next is not null && int.TryParse(next, out var p):
                    port = p;
                    i++;
                    break;
                case "--model" when next is not null:
                    model = next;
                    i++;
                    break;
                default:
                    rest.Add(a);
                    break;
            }
        }

        if (rest.Count == 0)
        {
            PrintHelp();
            return 1;
        }

        var sub = rest[0];
        var subArgs = rest.Skip(1).ToArray();

        var baseUrl = $"http://127.0.0.1:{port}";
        using var http = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };

        try
        {
            return sub switch
            {
                "discovery" => await DiscoveryAsync(http, baseUrl, ct),
                "new-session" => await NewSessionAsync(http, baseUrl, model, ct),
                "send" => await SendAsync(http, baseUrl, subArgs, ct),
                "respond-permission" => await RespondPermissionAsync(http, baseUrl, subArgs, ct),
                "respond-input" => await RespondInputAsync(http, baseUrl, subArgs, ct),
                _ => Unknown(sub)
            };
        }
        catch (HttpRequestException ex)
        {
            Console.Error.WriteLine($"ACP HTTP error: {ex.Message}");
            return 1;
        }
    }

    private static int Unknown(string sub)
    {
        Console.Error.WriteLine($"Unknown acp subcommand: {sub}");
        PrintHelp();
        return 1;
    }

    private static void PrintHelp()
    {
        Console.WriteLine("OpenMono ACP client");
        Console.WriteLine();
        Console.WriteLine("Usage:");
        Console.WriteLine("  openmono acp discovery [--port 7475]");
        Console.WriteLine("  openmono acp new-session [--port 7475] [--model <name>]");
        Console.WriteLine("  openmono acp send <session_id> <message...> [--port 7475]");
        Console.WriteLine("  openmono acp respond-permission <session_id> <perm_id> <allow|deny> [--port 7475]");
        Console.WriteLine("  openmono acp respond-input <session_id> <ask_id> <value...> [--port 7475]");
        Console.WriteLine();
        Console.WriteLine("Notes:");
        Console.WriteLine("- Run the server with: openmono --acp-only");
        Console.WriteLine("- `send` streams SSE events; it prints assistant text as it arrives.");
        Console.WriteLine("- If the agent pauses for permission or user input, this command exits non-zero and prints the pause id.");
    }

    private static async Task<int> DiscoveryAsync(HttpClient http, string baseUrl, CancellationToken ct)
    {
        var json = await http.GetStringAsync($"{baseUrl}/api/v1/discovery", ct);
        Console.WriteLine(json);
        return 0;
    }

    private static async Task<int> NewSessionAsync(HttpClient http, string baseUrl, string? model, CancellationToken ct)
    {
        // Keep a stable anonymous type to satisfy the compiler/analyzers.
        var body = new { model = (string?)null };
        if (model is not null) body = new { model = (string?)model };
        var resp = await http.PostAsJsonAsync($"{baseUrl}/api/v1/sessions", body, cancellationToken: ct);
        resp.EnsureSuccessStatusCode();
        var raw = await resp.Content.ReadAsStringAsync(ct);
        Console.WriteLine(raw);
        return 0;
    }

    private static async Task<int> SendAsync(HttpClient http, string baseUrl, string[] args, CancellationToken ct)
    {
        if (args.Length < 2)
        {
            Console.Error.WriteLine("send requires: <session_id> <message...>");
            return 1;
        }

        var sessionId = args[0];
        var message = string.Join(' ', args.Skip(1));
        return await PostTurnAndStreamAsync(http, $"{baseUrl}/api/v1/sessions/{sessionId}/turn", new { message }, ct);
    }

    private static async Task<int> RespondPermissionAsync(HttpClient http, string baseUrl, string[] args, CancellationToken ct)
    {
        if (args.Length != 3)
        {
            Console.Error.WriteLine("respond-permission requires: <session_id> <perm_id> <allow|deny>");
            return 1;
        }

        var sessionId = args[0];
        var permId = args[1];
        var decision = args[2].Trim().ToLowerInvariant();
        if (decision is not ("allow" or "deny"))
        {
            Console.Error.WriteLine("decision must be allow or deny");
            return 1;
        }

        return await PostTurnAndStreamAsync(
            http,
            $"{baseUrl}/api/v1/sessions/{sessionId}/turn",
            new { permission = new { id = permId, decision } },
            ct);
    }

    private static async Task<int> RespondInputAsync(HttpClient http, string baseUrl, string[] args, CancellationToken ct)
    {
        if (args.Length < 3)
        {
            Console.Error.WriteLine("respond-input requires: <session_id> <ask_id> <value...>");
            return 1;
        }

        var sessionId = args[0];
        var askId = args[1];
        var value = string.Join(' ', args.Skip(2));

        return await PostTurnAndStreamAsync(
            http,
            $"{baseUrl}/api/v1/sessions/{sessionId}/turn",
            new { user_input = new { id = askId, value } },
            ct);
    }

    private static async Task<int> PostTurnAndStreamAsync(HttpClient http, string url, object body, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = JsonContent.Create(body)
        };

        using var resp = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!resp.IsSuccessStatusCode)
        {
            var text = await resp.Content.ReadAsStringAsync(ct);
            Console.Error.WriteLine($"ACP error: {(int)resp.StatusCode} {resp.ReasonPhrase}\n{text}");
            return 1;
        }

        await using var stream = await resp.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(stream, Encoding.UTF8);

        var sawText = false;
        while (!ct.IsCancellationRequested)
        {
            var ev = await ReadSseEventAsync(reader, ct);
            if (ev is null) break;

            var (name, data) = ev.Value;
            switch (name)
            {
                case "text_delta":
                {
                    sawText = true;
                    var json = JsonDocument.Parse(data);
                    var content = json.RootElement.TryGetProperty("content", out var c) ? c.GetString() : null;
                    if (!string.IsNullOrEmpty(content))
                        Console.Write(content);
                    break;
                }
                case "permission_request":
                {
                    Console.Error.WriteLine();
                    Console.Error.WriteLine($"ACP paused for permission: {data}");
                    return 2;
                }
                case "user_input_request":
                {
                    Console.Error.WriteLine();
                    Console.Error.WriteLine($"ACP paused for user input: {data}");
                    return 3;
                }
                case "error":
                    Console.Error.WriteLine();
                    Console.Error.WriteLine($"ACP error event: {data}");
                    return 1;
                case "done":
                    if (sawText) Console.WriteLine();
                    return 0;
            }
        }

        if (sawText) Console.WriteLine();
        return 0;
    }

    private static async Task<(string Name, string Data)?> ReadSseEventAsync(StreamReader reader, CancellationToken ct)
    {
        string? name = null;
        var data = new StringBuilder();

        while (!ct.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(ct);
            if (line is null) break;
            if (line.Length == 0)
            {
                if (name is null) return null;
                return (name, data.ToString());
            }

            if (line.StartsWith("event:", StringComparison.Ordinal))
            {
                name = line["event:".Length..].Trim();
                continue;
            }

            if (line.StartsWith("data:", StringComparison.Ordinal))
            {
                if (data.Length > 0) data.Append('\n');
                data.Append(line["data:".Length..].TrimStart());
            }
        }

        if (name is null) return null;
        return (name, data.ToString());
    }
}

