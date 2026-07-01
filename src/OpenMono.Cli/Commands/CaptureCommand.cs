using OpenMono.Session;

namespace OpenMono.Commands;

public sealed class CaptureCommand : ICommand
{
    private readonly ConversationLoop _loop;

    public CaptureCommand(ConversationLoop loop) => _loop = loop;

    public string Name => "capture";
    public string Description => "Capture current browser tab via MCP into a markdown file for Captain indexing";
    public CommandType Type => CommandType.Local;

    public async Task ExecuteAsync(string[] args, CommandContext context, CancellationToken ct)
    {
        var note = args.Length > 0 ? string.Join(' ', args) : null;

        var prompt = """
            Capture the *current browser tab* via the configured browser MCP server.

            Requirements:
            - Extract: URL, title, and the main readable page text (prefer article/readability style when possible).
            - Save a markdown capture file under `.captain_captures/` in the current working directory.
            - File name: `<yyyyMMdd_HHmmss>_<safe_title>.md`.
            - Include a short summary + key bullets at the top of the markdown file.
            - After saving, run `openmono captain scan` (or index just that file) so it is searchable via `openmono captain query`.

            If multiple browser MCP servers exist, prefer `chrome-devtools` (tools: `mcp__chrome-devtools__*`).
            If no browser MCP tools are available, explain how to set up `chrome-devtools-mcp` and then stop.
            """;

        if (!string.IsNullOrWhiteSpace(note))
            prompt += $"\n\nUser note/context: {note}";

        await _loop.RunTurnAsync(prompt, null, ct);
    }
}

