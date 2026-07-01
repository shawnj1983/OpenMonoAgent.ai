using System.Text.Json;
using Microsoft.Playwright;
using OpenMono.Permissions;

namespace OpenMono.Tools;

/// <summary>
/// Local-only browser automation for "agent-mode" workflows.
/// Safety:
/// - Always permission-gated (Ask)
/// - Requires explicit opt-in to potentially destructive clicks (submit/pay)
/// </summary>
public sealed class BrowserControlTool : ToolBase
{
    public override string Name => "BrowserControl";
    public override string Description =>
        "Full browser control (local-only): connect to a Chromium browser via remote debugging (CDP), " +
        "navigate, click, type, press keys, extract page text, take screenshots, and print-to-PDF. " +
        "Use for agent-mode browser automation with safety gates (no submit/pay without explicit opt-in).";

    public override bool IsConcurrencySafe => false;
    public override bool IsReadOnly => false;
    public override PermissionLevel DefaultPermission => PermissionLevel.Ask;

    private IPlaywright? _pw;
    private IBrowser? _browser;
    private IBrowserContext? _context;
    private IPage? _page;

    protected override SchemaBuilder DefineSchema() => new SchemaBuilder()
        .AddEnum("operation", "Operation to perform",
            "connect_cdp",
            "launch",
            "list_pages",
            "select_page",
            "navigate",
            "click",
            "type",
            "press",
            "wait_for",
            "extract_text",
            "screenshot",
            "pdf",
            "close")
        .AddString("cdp_url", "CDP endpoint URL (e.g. http://localhost:9222) for connect_cdp")
        .AddBoolean("headless", "For launch: run headless (default true)")
        .AddString("url", "For navigate: destination URL")
        .AddString("selector", "CSS selector for click/type/wait_for")
        .AddString("text", "Text to type into selector")
        .AddString("key", "Keyboard key to press (e.g. Enter, Tab, Control+L)")
        .AddInteger("timeout_ms", "Timeout in ms (default 10000)", minimum: 1, maximum: 120000)
        .AddInteger("page_index", "Index from list_pages to select (0-based)", minimum: 0, maximum: 1000)
        .AddString("output_path", "Where to write screenshot/pdf (relative or absolute; must be within working dir)")
        .AddBoolean("full_page", "For screenshot: capture full page (default true)")
        .AddBoolean("allow_submit", "Allow clicking likely submit/pay buttons (default false)")
        .Require("operation");

    public IReadOnlyList<Capability> RequiredCapabilities(JsonElement input)
    {
        var op = input.TryGetProperty("operation", out var o) ? o.GetString() : null;
        var caps = new List<Capability>();

        switch (op)
        {
            case "connect_cdp":
            {
                var cdp = input.TryGetProperty("cdp_url", out var u) ? u.GetString() : null;
                if (!string.IsNullOrWhiteSpace(cdp))
                {
                    caps.Add(NetworkEgressCap.FromUrl(cdp!));
                }
                break;
            }
            case "navigate":
            {
                var url = input.TryGetProperty("url", out var u) ? u.GetString() : null;
                if (!string.IsNullOrWhiteSpace(url))
                {
                    caps.Add(NetworkEgressCap.FromUrl(url!));
                }
                break;
            }
            case "screenshot":
            case "pdf":
            {
                var outPath = input.TryGetProperty("output_path", out var p) ? p.GetString() : null;
                if (!string.IsNullOrWhiteSpace(outPath))
                {
                    // ToolContext will resolve and enforce it's inside working dir.
                    caps.Add(new FileWriteCap(outPath!, "create"));
                }
                break;
            }
        }

        return caps;
    }

    protected override async Task<ToolResult> ExecuteCoreAsync(JsonElement input, ToolContext context, CancellationToken ct)
    {
        var op = input.GetProperty("operation").GetString();
        var timeoutMs = input.TryGetProperty("timeout_ms", out var t) ? t.GetInt32() : 10_000;
        timeoutMs = Math.Clamp(timeoutMs, 1, 120_000);

        try
        {
            switch (op)
            {
                case "connect_cdp":
                    return await ConnectCdpAsync(input, timeoutMs, ct);
                case "launch":
                    return await LaunchAsync(input, timeoutMs, ct);
                case "list_pages":
                    return ListPages();
                case "select_page":
                    return SelectPage(input);
                case "navigate":
                    return await NavigateAsync(input, timeoutMs, ct);
                case "click":
                    return await ClickAsync(input, timeoutMs, ct);
                case "type":
                    return await TypeAsync(input, timeoutMs, ct);
                case "press":
                    return await PressAsync(input, timeoutMs, ct);
                case "wait_for":
                    return await WaitForAsync(input, timeoutMs, ct);
                case "extract_text":
                    return await ExtractTextAsync(timeoutMs, ct);
                case "screenshot":
                    return await ScreenshotAsync(input, context, timeoutMs, ct);
                case "pdf":
                    return await PdfAsync(input, context, timeoutMs, ct);
                case "close":
                    return await CloseAsync();
                default:
                    return ToolResult.InvalidInput($"Unknown operation: {op}", "Use a valid operation");
            }
        }
        catch (Exception ex)
        {
            return ToolResult.Error($"BrowserControl failed: {ex.Message}");
        }
    }

    private async Task EnsurePlaywrightAsync()
    {
        _pw ??= await Playwright.CreateAsync();
    }

    private void EnsurePageSelected()
    {
        if (_page is null)
            throw new InvalidOperationException("No page selected. Run connect_cdp/launch then list_pages/select_page.");
    }

    private async Task<ToolResult> ConnectCdpAsync(JsonElement input, int timeoutMs, CancellationToken ct)
    {
        var cdpUrl = input.TryGetProperty("cdp_url", out var u) ? u.GetString() : null;
        if (string.IsNullOrWhiteSpace(cdpUrl))
            return ToolResult.InvalidInput("cdp_url is required for connect_cdp", "Provide cdp_url like http://localhost:9222");

        await EnsurePlaywrightAsync();

        _browser = await _pw!.Chromium.ConnectOverCDPAsync(cdpUrl!, new BrowserTypeConnectOverCDPOptions
        {
            Timeout = timeoutMs
        });

        _context = _browser.Contexts.FirstOrDefault() ?? await _browser.NewContextAsync();
        _page = _context.Pages.FirstOrDefault() ?? await _context.NewPageAsync();

        return ToolResult.Success("Connected to browser via CDP and selected a page.");
    }

    private async Task<ToolResult> LaunchAsync(JsonElement input, int timeoutMs, CancellationToken ct)
    {
        var headless = !(input.TryGetProperty("headless", out var h) && h.ValueKind == JsonValueKind.False);
        await EnsurePlaywrightAsync();

        _browser = await _pw!.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = headless,
            Timeout = timeoutMs,
        });

        _context = await _browser.NewContextAsync();
        _page = await _context.NewPageAsync();

        return ToolResult.Success($"Launched Chromium (headless={headless}).");
    }

    private ToolResult ListPages()
    {
        if (_context is null)
            return ToolResult.Error("Not connected. Run connect_cdp or launch first.");

        var pages = _context.Pages
            .Select((p, i) => new { index = i, url = p.Url })
            .ToList();

        var lines = new List<string> { $"Pages: {pages.Count}" };
        foreach (var p in pages)
            lines.Add($"- [{p.index}] {p.url}");

        return ToolResult.SuccessWithPayload(string.Join('\n', lines), new { pages });
    }

    private ToolResult SelectPage(JsonElement input)
    {
        if (_context is null)
            return ToolResult.Error("Not connected. Run connect_cdp or launch first.");

        var idx = input.TryGetProperty("page_index", out var p) ? p.GetInt32() : -1;
        if (idx < 0 || idx >= _context.Pages.Count)
            return ToolResult.InvalidInput("page_index out of range", "Call list_pages to get valid indexes");

        _page = _context.Pages[idx];
        return ToolResult.Success($"Selected page index {idx}: {_page.Url}");
    }

    private async Task<ToolResult> NavigateAsync(JsonElement input, int timeoutMs, CancellationToken ct)
    {
        EnsurePageSelected();
        var url = input.TryGetProperty("url", out var u) ? u.GetString() : null;
        if (string.IsNullOrWhiteSpace(url))
            return ToolResult.InvalidInput("url is required for navigate", "Provide url");

        await _page!.GotoAsync(url!, new PageGotoOptions
        {
            Timeout = timeoutMs,
            WaitUntil = WaitUntilState.NetworkIdle
        });
        return ToolResult.Success($"Navigated to: {_page.Url}");
    }

    private async Task<ToolResult> ClickAsync(JsonElement input, int timeoutMs, CancellationToken ct)
    {
        EnsurePageSelected();
        var selector = input.TryGetProperty("selector", out var s) ? s.GetString() : null;
        if (string.IsNullOrWhiteSpace(selector))
            return ToolResult.InvalidInput("selector is required for click", "Provide selector");

        var allowSubmit = input.TryGetProperty("allow_submit", out var a) && a.GetBoolean();
        if (!allowSubmit && LooksLikeSubmitOrPay(selector!))
        {
            return ToolResult.PermissionDenied(
                "Refusing to click a potentially destructive submit/pay selector without allow_submit=true.",
                "Set allow_submit=true only if the user explicitly approved the action.");
        }

        await _page!.ClickAsync(selector!, new PageClickOptions { Timeout = timeoutMs });
        return ToolResult.Success($"Clicked: {selector}");
    }

    private async Task<ToolResult> TypeAsync(JsonElement input, int timeoutMs, CancellationToken ct)
    {
        EnsurePageSelected();
        var selector = input.TryGetProperty("selector", out var s) ? s.GetString() : null;
        var text = input.TryGetProperty("text", out var t) ? t.GetString() : null;
        if (string.IsNullOrWhiteSpace(selector) || text is null)
            return ToolResult.InvalidInput("selector and text are required for type", "Provide selector and text");

        await _page!.FillAsync(selector!, text, new PageFillOptions { Timeout = timeoutMs });
        return ToolResult.Success($"Typed into: {selector}");
    }

    private async Task<ToolResult> PressAsync(JsonElement input, int timeoutMs, CancellationToken ct)
    {
        EnsurePageSelected();
        var key = input.TryGetProperty("key", out var k) ? k.GetString() : null;
        if (string.IsNullOrWhiteSpace(key))
            return ToolResult.InvalidInput("key is required for press", "Provide key");

        await _page!.Keyboard.PressAsync(key!);
        return ToolResult.Success($"Pressed: {key}");
    }

    private async Task<ToolResult> WaitForAsync(JsonElement input, int timeoutMs, CancellationToken ct)
    {
        EnsurePageSelected();
        var selector = input.TryGetProperty("selector", out var s) ? s.GetString() : null;
        if (string.IsNullOrWhiteSpace(selector))
            return ToolResult.InvalidInput("selector is required for wait_for", "Provide selector");

        await _page!.WaitForSelectorAsync(selector!, new PageWaitForSelectorOptions { Timeout = timeoutMs });
        return ToolResult.Success($"Selector appeared: {selector}");
    }

    private async Task<ToolResult> ExtractTextAsync(int timeoutMs, CancellationToken ct)
    {
        EnsurePageSelected();
        var payload = await _page!.EvaluateAsync<JsonElement>(
            "() => ({ title: document.title, url: location.href, text: document.body ? document.body.innerText : '' })");

        var title = payload.TryGetProperty("title", out var t) ? t.GetString() : "";
        var url = payload.TryGetProperty("url", out var u) ? u.GetString() : "";
        return ToolResult.SuccessWithPayload(
            $"Extracted: {title} ({url})",
            payload);
    }

    private async Task<ToolResult> ScreenshotAsync(JsonElement input, ToolContext context, int timeoutMs, CancellationToken ct)
    {
        EnsurePageSelected();
        var outPathRaw = input.TryGetProperty("output_path", out var p) ? p.GetString() : null;
        if (string.IsNullOrWhiteSpace(outPathRaw))
            return ToolResult.InvalidInput("output_path is required for screenshot", "Provide output_path");

        var fullPage = !(input.TryGetProperty("full_page", out var f) && f.ValueKind == JsonValueKind.False);
        var resolved = ResolveOutputPath(outPathRaw!, context.WorkingDirectory);

        await _page!.ScreenshotAsync(new PageScreenshotOptions
        {
            Path = resolved,
            FullPage = fullPage,
            Timeout = timeoutMs,
        });

        return ToolResult.Success($"Saved screenshot: {resolved}");
    }

    private async Task<ToolResult> PdfAsync(JsonElement input, ToolContext context, int timeoutMs, CancellationToken ct)
    {
        EnsurePageSelected();
        var outPathRaw = input.TryGetProperty("output_path", out var p) ? p.GetString() : null;
        if (string.IsNullOrWhiteSpace(outPathRaw))
            return ToolResult.InvalidInput("output_path is required for pdf", "Provide output_path");

        var resolved = ResolveOutputPath(outPathRaw!, context.WorkingDirectory);

        await _page!.PdfAsync(new PagePdfOptions
        {
            Path = resolved,
            PrintBackground = true,
        });

        return ToolResult.Success($"Saved PDF: {resolved}");
    }

    private async Task<ToolResult> CloseAsync()
    {
        try
        {
            if (_context is not null)
                await _context.CloseAsync();
            if (_browser is not null)
                await _browser.CloseAsync();
        }
        finally
        {
            _page = null;
            _context = null;
            _browser = null;
            _pw = null;
        }

        return ToolResult.Success("BrowserControl closed.");
    }

    private static string ResolveOutputPath(string input, string workingDirectory)
    {
        // Constrain output to working directory for safety.
        var resolved = Path.GetFullPath(input, workingDirectory);
        var wd = Path.GetFullPath(workingDirectory)
            .TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!resolved.StartsWith(wd, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            throw new InvalidOperationException("output_path must be within the working directory");
        return resolved;
    }

    private static bool LooksLikeSubmitOrPay(string selector)
    {
        var s = selector.ToLowerInvariant();
        return s.Contains("submit", StringComparison.Ordinal) ||
               s.Contains("payment", StringComparison.Ordinal) ||
               s.Contains("checkout", StringComparison.Ordinal) ||
               s.Contains("buy", StringComparison.Ordinal) ||
               s.Contains("order", StringComparison.Ordinal);
    }
}

