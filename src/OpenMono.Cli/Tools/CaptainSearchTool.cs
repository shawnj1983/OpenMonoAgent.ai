using System.Text.Json;
using OpenMono.Captain;

namespace OpenMono.Tools;

public sealed class CaptainSearchTool : ToolBase
{
    public override string Name => "CaptainSearch";
    public override string Description =>
        "Search Captain's local index (including PDF text) and return the best matching paths + snippets. " +
        "Use this to answer questions based on the user's files without needing a full rescan.";

    public override bool IsConcurrencySafe => true;
    public override bool IsReadOnly => true;
    public override PermissionLevel DefaultPermission => PermissionLevel.Ask;

    protected override SchemaBuilder DefineSchema() => new SchemaBuilder()
        .AddString("query", "Search query (keywords)")
        .AddInteger("limit", "Max results (default: 10)", minimum: 1, maximum: 50)
        .Require("query");

    protected override Task<ToolResult> ExecuteCoreAsync(JsonElement input, ToolContext context, CancellationToken ct)
    {
        var q = input.GetProperty("query").GetString();
        if (string.IsNullOrWhiteSpace(q))
            return Task.FromResult(ToolResult.InvalidInput("query is required", "Provide query"));

        var limit = input.TryGetProperty("limit", out var l) ? l.GetInt32() : 10;
        limit = Math.Clamp(limit, 1, 50);

        try
        {
            var rules = CaptainRulesStore.LoadOrDefault(context.Config);
            var indexer = new CaptainIndexer(context.Config, rules);
            var hits = indexer.Search(q!, limit);

            if (hits.Count == 0)
                return Task.FromResult(ToolResult.Success("No matches."));

            var lines = new List<string> { $"Matches: {hits.Count}" };
            foreach (var h in hits)
            {
                lines.Add($"- {h.Path}");
                if (!string.IsNullOrWhiteSpace(h.Snippet))
                    lines.Add($"  {h.Snippet}");
            }

            return Task.FromResult(ToolResult.SuccessWithPayload(
                string.Join('\n', lines),
                new { query = q, limit, hits }));
        }
        catch (Exception ex)
        {
            return Task.FromResult(ToolResult.Error($"Captain search failed: {ex.Message}"));
        }
    }
}

