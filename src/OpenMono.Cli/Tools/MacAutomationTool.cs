using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using OpenMono.Permissions;
using OpenMono.Utils;

namespace OpenMono.Tools;

/// <summary>
/// macOS-only desktop automation via `osascript` / AppleScript.
/// Intended for "Jarvis" local control: opening/activating/quitting apps,
/// clicking menu items, and sending keystrokes.
/// </summary>
public sealed class MacAutomationTool : ToolBase
{
    public override string Name => "MacAutomation";
    public override string Description =>
        "macOS-only computer control via AppleScript (osascript): open/activate/quit apps, click menu items, " +
        "and send keystrokes/keycodes. Requires macOS Accessibility permission for System Events actions.";

    public override bool IsConcurrencySafe => false;
    public override bool IsReadOnly => false;
    public override PermissionLevel DefaultPermission => PermissionLevel.Ask;

    protected override SchemaBuilder DefineSchema() => new SchemaBuilder()
        .AddEnum("operation", "Operation to perform",
            "open_app",
            "activate_app",
            "quit_app",
            "click_menu_item",
            "keystroke",
            "key_code")
        .AddString("app", "Application name (e.g. 'Google Chrome', 'Finder'). Required for open/activate/quit/click_menu_item.")
        .AddString("process", "Process name as shown in System Events (defaults to app). Used for click_menu_item.")
        .AddString("menu", "Menu name (e.g. 'File', 'View'). Required for click_menu_item.")
        .AddString("menu_item", "Menu item name (e.g. 'Close Tab', 'Print…'). Required for click_menu_item.")
        .AddString("text", "Text to type for keystroke (required when operation=keystroke).")
        .AddInteger("keycode", "AppleScript key code (required when operation=key_code). Example: 53=Escape.", minimum: 0, maximum: 255)
        .AddArray("modifiers", "Modifier keys for keystroke/key_code. Values: command, shift, option, control.", new
        {
            type = "string",
            @enum = new[] { "command", "shift", "option", "control" }
        })
        .AddBoolean("dry_run", "If true, return the AppleScript without executing.")
        .AddInteger("timeout_ms", "Timeout in ms (default 15000, max 120000).", minimum: 1, maximum: 120000)
        .Require("operation");

    public IReadOnlyList<Capability> RequiredCapabilities(JsonElement input)
    {
        var op = input.TryGetProperty("operation", out var o) ? o.GetString() : null;
        return op switch
        {
            "open_app" => [new ProcessExecCap("open", ["-a", "<app>"])],
            "activate_app" or "quit_app" or "click_menu_item" or "keystroke" or "key_code"
                => [new ProcessExecCap("osascript", ["-e", "<applescript>"])],
            _ => []
        };
    }

    protected override async Task<ToolResult> ExecuteCoreAsync(JsonElement input, ToolContext context, CancellationToken ct)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            return ToolResult.Error("MacAutomation is only available on macOS.");

        var op = input.GetProperty("operation").GetString();
        var dryRun = input.TryGetProperty("dry_run", out var d) && d.ValueKind == JsonValueKind.True;
        var timeoutMs = input.TryGetProperty("timeout_ms", out var t) ? t.GetInt32() : 15_000;
        timeoutMs = Math.Clamp(timeoutMs, 1, 120_000);

        try
        {
            return op switch
            {
                "open_app" => await OpenAppAsync(input, dryRun, timeoutMs, ct),
                "activate_app" => await RunAppleScriptAsync(BuildActivateScript(input), dryRun, timeoutMs, ct),
                "quit_app" => await RunAppleScriptAsync(BuildQuitScript(input), dryRun, timeoutMs, ct),
                "click_menu_item" => await RunAppleScriptAsync(BuildClickMenuScript(input), dryRun, timeoutMs, ct),
                "keystroke" => await RunAppleScriptAsync(BuildKeystrokeScript(input), dryRun, timeoutMs, ct),
                "key_code" => await RunAppleScriptAsync(BuildKeyCodeScript(input), dryRun, timeoutMs, ct),
                _ => ToolResult.InvalidInput($"Unknown operation: {op}", "Use a supported operation.")
            };
        }
        catch (Exception ex)
        {
            return ToolResult.Error($"MacAutomation failed: {ex.Message}");
        }
    }

    private static async Task<ToolResult> OpenAppAsync(JsonElement input, bool dryRun, int timeoutMs, CancellationToken ct)
    {
        var app = input.TryGetProperty("app", out var a) ? a.GetString() : null;
        if (string.IsNullOrWhiteSpace(app))
            return ToolResult.InvalidInput("app is required for open_app", "Provide app like 'Google Chrome' or 'Finder'.");

        var cmd = $"open -a {ShellQuote(app!)}";
        if (dryRun)
            return ToolResult.Success($"[dry_run]\n{cmd}");

        var (code, stdout, stderr) = await ProcessRunner.RunAsync(cmd, timeoutMs: timeoutMs, ct: ct);
        if (code != 0)
            return ToolResult.Error($"open_app failed (exit {code})\n{JoinStd(stdout, stderr)}");
        return ToolResult.Success($"Opened app: {app}");
    }

    private static string BuildActivateScript(JsonElement input)
    {
        var app = RequireString(input, "app", "activate_app");
        return $"tell application {AppleScriptString(app)} to activate";
    }

    private static string BuildQuitScript(JsonElement input)
    {
        var app = RequireString(input, "app", "quit_app");
        return $"tell application {AppleScriptString(app)} to quit";
    }

    private static string BuildClickMenuScript(JsonElement input)
    {
        var app = RequireString(input, "app", "click_menu_item");
        var process = input.TryGetProperty("process", out var p) ? (p.GetString() ?? "").Trim() : "";
        if (string.IsNullOrWhiteSpace(process)) process = app;

        var menu = RequireString(input, "menu", "click_menu_item");
        var item = RequireString(input, "menu_item", "click_menu_item");

        // NOTE: Requires macOS Accessibility permission for the calling process.
        return $"""
               tell application "System Events"
                 tell process {AppleScriptString(process)}
                   click menu item {AppleScriptString(item)} of menu {AppleScriptString(menu)} of menu bar 1
                 end tell
               end tell
               """;
    }

    private static string BuildKeystrokeScript(JsonElement input)
    {
        var text = RequireString(input, "text", "keystroke");
        var modifiers = ReadModifiers(input);
        var usingClause = modifiers.Count > 0 ? $" using {{{string.Join(", ", modifiers)}}}" : "";

        return $"""
               tell application "System Events"
                 keystroke {AppleScriptString(text)}{usingClause}
               end tell
               """;
    }

    private static string BuildKeyCodeScript(JsonElement input)
    {
        if (!input.TryGetProperty("keycode", out var kc) || kc.ValueKind != JsonValueKind.Number)
            throw new ArgumentException("keycode is required for key_code");
        var keycode = kc.GetInt32();
        var modifiers = ReadModifiers(input);
        var usingClause = modifiers.Count > 0 ? $" using {{{string.Join(", ", modifiers)}}}" : "";

        return $"""
               tell application "System Events"
                 key code {keycode}{usingClause}
               end tell
               """;
    }

    private static List<string> ReadModifiers(JsonElement input)
    {
        if (!input.TryGetProperty("modifiers", out var m) || m.ValueKind != JsonValueKind.Array)
            return [];

        var result = new List<string>();
        foreach (var el in m.EnumerateArray())
        {
            if (el.ValueKind != JsonValueKind.String) continue;
            var v = (el.GetString() ?? "").Trim().ToLowerInvariant();
            result.Add(v switch
            {
                "command" => "command down",
                "shift" => "shift down",
                "option" => "option down",
                "control" => "control down",
                _ => ""
            });
        }
        return result.Where(s => !string.IsNullOrWhiteSpace(s)).Distinct().ToList();
    }

    private static async Task<ToolResult> RunAppleScriptAsync(string script, bool dryRun, int timeoutMs, CancellationToken ct)
    {
        if (dryRun)
            return ToolResult.Success($"[dry_run]\n{script}");

        // Use bash -c so we can pass a here-doc safely (no argument length surprises).
        var cmd = new StringBuilder();
        cmd.AppendLine("osascript <<'OPENMONO_APPLESCRIPT'");
        cmd.AppendLine(script.TrimEnd());
        cmd.AppendLine("OPENMONO_APPLESCRIPT");

        var (code, stdout, stderr) = await ProcessRunner.RunAsync(cmd.ToString(), timeoutMs: timeoutMs, ct: ct);
        if (code != 0)
            return ToolResult.Error($"osascript failed (exit {code})\n{JoinStd(stdout, stderr)}");

        var outText = string.IsNullOrWhiteSpace(stdout) ? "OK" : stdout;
        return ToolResult.Success(outText);
    }

    private static string RequireString(JsonElement input, string prop, string op)
    {
        var value = input.TryGetProperty(prop, out var el) ? el.GetString() : null;
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException($"{prop} is required for {op}");
        return value.Trim();
    }

    private static string AppleScriptString(string value)
    {
        // AppleScript string literal with escaped quotes/backslashes.
        var escaped = value.Replace("\\", "\\\\").Replace("\"", "\\\"");
        return $"\"{escaped}\"";
    }

    private static string ShellQuote(string value)
    {
        // Single-quote shell escaping.
        return "'" + value.Replace("'", "'\"'\"'") + "'";
    }

    private static string JoinStd(string stdout, string stderr)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(stdout)) parts.Add(stdout.TrimEnd());
        if (!string.IsNullOrWhiteSpace(stderr)) parts.Add("[stderr]\n" + stderr.TrimEnd());
        return parts.Count > 0 ? string.Join("\n", parts) : "(no output)";
    }
}

