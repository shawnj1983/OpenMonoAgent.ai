using System.Text.Json;
using OpenMono.Captain;
using OpenMono.Permissions;

namespace OpenMono.Tools;

public sealed class CaptainFileOpsTool : ToolBase
{
    public override string Name => "CaptainFileOps";
    public override string Description =>
        "Safe file operations for the Captain runtime: move/rename/mkdir and undo last move/rename. " +
        "Never deletes. Restricted to captain roots in ~/.openmono/captain/rules.yml.";

    protected override SchemaBuilder DefineSchema() => new SchemaBuilder()
        .AddEnum("operation", "Operation: move, rename, mkdir, undo_last", "move", "rename", "mkdir", "undo_last")
        .AddString("from_path", "Source path for move (required when operation=move)")
        .AddString("to_path", "Destination path for move (required when operation=move)")
        .AddString("file_path", "Path for rename (required when operation=rename)")
        .AddString("new_name", "New file name (required when operation=rename)")
        .AddString("dir_path", "Directory path to create (required when operation=mkdir)")
        .Require("operation");

    public override PermissionLevel RequiredPermission(JsonElement input) => PermissionLevel.Ask;

    public IReadOnlyList<Capability> RequiredCapabilities(JsonElement input)
    {
        var op = input.TryGetProperty("operation", out var o) ? o.GetString() : null;
        switch (op)
        {
            case "move":
            {
                var from = input.TryGetProperty("from_path", out var f) ? f.GetString() : null;
                var to = input.TryGetProperty("to_path", out var t) ? t.GetString() : null;
                if (string.IsNullOrWhiteSpace(from) || string.IsNullOrWhiteSpace(to)) return [];
                return [new FileWriteCap(from!, "modify"), new FileWriteCap(to!, "create")];
            }
            case "rename":
            {
                var path = input.TryGetProperty("file_path", out var p) ? p.GetString() : null;
                if (string.IsNullOrWhiteSpace(path)) return [];
                return [new FileWriteCap(path!, "modify")];
            }
            case "mkdir":
            {
                var dir = input.TryGetProperty("dir_path", out var d) ? d.GetString() : null;
                if (string.IsNullOrWhiteSpace(dir)) return [];
                return [new FileWriteCap(dir!, "create")];
            }
            default:
                return [];
        }
    }

    protected override Task<ToolResult> ExecuteCoreAsync(JsonElement input, ToolContext context, CancellationToken ct)
    {
        var rules = CaptainRulesStore.LoadOrDefault(context.Config);
        var ops = new CaptainFileOps(context.Config, rules);

        var op = input.GetProperty("operation").GetString();
        return op switch
        {
            "move" => Task.FromResult(Move(input, context, ops)),
            "rename" => Task.FromResult(Rename(input, context, ops)),
            "mkdir" => Task.FromResult(Mkdir(input, context, ops)),
            "undo_last" => Task.FromResult(UndoLast(ops)),
            _ => Task.FromResult(ToolResult.Error($"Unknown operation: {op}"))
        };
    }

    private static ToolResult Mkdir(JsonElement input, ToolContext context, CaptainFileOps ops)
    {
        var dirPath = input.TryGetProperty("dir_path", out var d) ? d.GetString() : null;
        if (string.IsNullOrWhiteSpace(dirPath))
            return ToolResult.Error("dir_path is required when operation=mkdir");

        var resolved = ResolvePath(dirPath!, context.WorkingDirectory);
        try
        {
            ops.Mkdir(resolved);
            return ToolResult.Success($"Created directory: {resolved}");
        }
        catch (Exception ex)
        {
            return ToolResult.Error(ex.Message);
        }
    }

    private static ToolResult Move(JsonElement input, ToolContext context, CaptainFileOps ops)
    {
        var from = input.TryGetProperty("from_path", out var f) ? f.GetString() : null;
        var to = input.TryGetProperty("to_path", out var t) ? t.GetString() : null;
        if (string.IsNullOrWhiteSpace(from) || string.IsNullOrWhiteSpace(to))
            return ToolResult.Error("from_path and to_path are required when operation=move");

        var resolvedFrom = ResolvePath(from!, context.WorkingDirectory);
        var resolvedTo = ResolvePath(to!, context.WorkingDirectory);

        try
        {
            ops.Move(resolvedFrom, resolvedTo);
            return ToolResult.Success($"Moved: {resolvedFrom} -> {resolvedTo}");
        }
        catch (Exception ex)
        {
            return ToolResult.Error($"Move failed: {ex.Message}");
        }
    }

    private static ToolResult Rename(JsonElement input, ToolContext context, CaptainFileOps ops)
    {
        var path = input.TryGetProperty("file_path", out var p) ? p.GetString() : null;
        var newName = input.TryGetProperty("new_name", out var n) ? n.GetString() : null;
        if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(newName))
            return ToolResult.Error("file_path and new_name are required when operation=rename");

        var resolved = ResolvePath(path!, context.WorkingDirectory);

        try
        {
            ops.Rename(resolved, newName!);
            var dest = Path.Combine(Path.GetDirectoryName(resolved) ?? context.WorkingDirectory, newName!);
            return ToolResult.Success($"Renamed: {resolved} -> {dest}");
        }
        catch (Exception ex)
        {
            return ToolResult.Error($"Rename failed: {ex.Message}");
        }
    }

    private static ToolResult UndoLast(CaptainFileOps ops)
    {
        try
        {
            if (!ops.TryUndoLast(out var msg))
                return ToolResult.Error(msg);
            return ToolResult.Success(msg);
        }
        catch (Exception ex)
        {
            return ToolResult.Error($"Undo failed: {ex.Message}");
        }
    }

    private static string ResolvePath(string input, string workingDirectory)
    {
        var expanded = ExpandHome(input);
        return Path.GetFullPath(expanded, workingDirectory);
    }

    private static string ExpandHome(string path)
    {
        if (!path.StartsWith('~'))
            return path;
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (path == "~") return home;
        if (path.StartsWith("~/") || path.StartsWith("~\\"))
            return Path.Combine(home, path[2..]);
        return path;
    }
}

