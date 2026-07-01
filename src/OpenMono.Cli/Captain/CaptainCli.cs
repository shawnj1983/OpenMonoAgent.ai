using System.Text.Json;
using System.Diagnostics;
using OpenMono.Config;
using OpenMono.Mcp;
using OpenMono.Rendering;

namespace OpenMono.Captain;

public static class CaptainCli
{
    public static async Task<int> RunAsync(
        string[] args,
        AppConfig config,
        IRenderer renderer,
        CancellationToken ct)
    {
        if (args.Length == 0 || args[0] is "help" or "--help" or "-h")
        {
            PrintHelp(renderer);
            return 0;
        }

        var sub = args[0];
        var rest = args.Skip(1).ToArray();
        switch (sub)
        {
            case "init":
                return await InitAsync(rest, config, renderer, ct);
            case "start":
                return await StartAsync(rest, config, renderer, ct);
            case "run":
                return await RunForegroundAsync(rest, config, renderer, ct);
            case "stop":
                return await StopAsync(config, renderer);
            case "status":
                return await StatusAsync(config, renderer);
            case "undo":
                return await UndoAsync(config, renderer);
            case "scan":
                return await ScanAsync(config, renderer, ct);
            case "query":
                return await QueryAsync(rest, config, renderer);
            case "mcp-smoke":
                return await McpSmokeAsync(config, renderer, ct);
            default:
                renderer.WriteError($"Unknown captain subcommand: {sub}");
                PrintHelp(renderer);
                return 1;
        }
    }

    private static void PrintHelp(IRenderer renderer)
    {
        renderer.WriteMarkdown("""
            ## Captain — always-on organization engine

            Usage:

              `openmono captain init`        Initialize captain state + rules
              `openmono captain start`       Start captain in background (spawns `captain run`)
              `openmono captain run`         Run captain in the foreground (Ctrl+C to stop)
              `openmono captain status`      Show current state (pid, paths)
              `openmono captain stop`        Stop background captain (pid file)
              `openmono captain undo`        Undo the last successful move/rename
              `openmono captain scan`        Scan roots and build/update the local index
              `openmono captain query <q>`   Search the index (paths + snippets)
              `openmono captain mcp-smoke`   Smoke test Outlook + browser MCP connectivity (for demos)

            Safety defaults:
            - Moves/renames allowed.
            - Deleting/removing is never performed.
            - Operations are limited to configured roots in `rules.yml`.
            """);
    }

    private static async Task<int> InitAsync(string[] _args, AppConfig config, IRenderer renderer, CancellationToken ct)
    {
        var dir = CaptainPaths.CaptainDir(config);
        Directory.CreateDirectory(dir);

        var rulesPath = CaptainPaths.RulesPath(config);
        if (!File.Exists(rulesPath))
        {
            var rules = CaptainRulesStore.Default(config);
            CaptainRulesStore.Save(config, rules);
            renderer.WriteInfo($"Created rules: {rulesPath}");
        }
        else
        {
            renderer.WriteInfo($"Rules already exist: {rulesPath}");
        }

        var journal = CaptainPaths.ActionsJournalPath(config);
        if (!File.Exists(journal))
            await File.WriteAllTextAsync(journal, "", ct);

        var queue = CaptainPaths.QueuePath(config);
        if (!File.Exists(queue))
            await File.WriteAllTextAsync(queue, "", ct);

        renderer.WriteInfo($"Captain dir: {dir}");
        renderer.WriteInfo("Next: `openmono captain start`");
        return 0;
    }

    private static Task<int> StartAsync(string[] _args, AppConfig config, IRenderer renderer, CancellationToken ct)
    {
        Directory.CreateDirectory(CaptainPaths.CaptainDir(config));

        var pidPath = CaptainPaths.PidPath(config);
        if (File.Exists(pidPath))
        {
            var pidText = File.ReadAllText(pidPath).Trim();
            if (int.TryParse(pidText, out var existingPid) && ProcessExists(existingPid))
            {
                renderer.WriteWarning($"Captain already running (pid {existingPid}).");
                return Task.FromResult(0);
            }
            try { File.Delete(pidPath); } catch { }
        }

        var (fileName, args) = BuildSelfLaunch("captain run");
        var logPath = CaptainPaths.LogPath(config);

        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = args,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = config.WorkingDirectory,
        };

        var proc = Process.Start(psi);
        if (proc is null)
        {
            renderer.WriteError("Failed to start captain process.");
            return Task.FromResult(1);
        }

        File.WriteAllText(pidPath, proc.Id.ToString());

        _ = Task.Run(async () =>
        {
            try
            {
                await using var fs = new FileStream(logPath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
                await using var sw = new StreamWriter(fs) { AutoFlush = true };
                await sw.WriteLineAsync($"[{DateTime.UtcNow:o}] captain started pid={proc.Id}");

                var stdoutTask = PumpAsync(proc.StandardOutput, sw, ct);
                var stderrTask = PumpAsync(proc.StandardError, sw, ct);
                await Task.WhenAll(stdoutTask, stderrTask);
            }
            catch { }
        }, ct);

        renderer.WriteInfo($"Captain started in background (pid {proc.Id}).");
        renderer.WriteInfo($"Log: {logPath}");
        return Task.FromResult(0);
    }

    private static async Task<int> RunForegroundAsync(string[] _args, AppConfig config, IRenderer renderer, CancellationToken ct)
    {
        renderer.WriteInfo("Captain running (foreground). Ctrl+C to stop.");
        var rules = CaptainRulesStore.LoadOrDefault(config);
        await CaptainDaemon.RunAsync(config, rules, renderer, ct);
        return 0;
    }

    private static Task<int> StopAsync(AppConfig config, IRenderer renderer)
    {
        var pidPath = CaptainPaths.PidPath(config);
        if (!File.Exists(pidPath))
        {
            renderer.WriteInfo("Captain not running (no pid file).");
            return Task.FromResult(0);
        }

        var pidText = File.ReadAllText(pidPath).Trim();
        if (!int.TryParse(pidText, out var pid))
        {
            renderer.WriteWarning("Invalid pid file; removing.");
            try { File.Delete(pidPath); } catch { }
            return Task.FromResult(0);
        }

        try
        {
            var proc = Process.GetProcessById(pid);
            proc.Kill(entireProcessTree: true);
            proc.WaitForExit(3000);
            renderer.WriteInfo($"Stopped captain (pid {pid}).");
        }
        catch (Exception ex)
        {
            renderer.WriteWarning($"Failed to stop pid {pid}: {ex.Message}");
        }
        finally
        {
            try { File.Delete(pidPath); } catch { }
        }

        return Task.FromResult(0);
    }

    private static Task<int> StatusAsync(AppConfig config, IRenderer renderer)
    {
        var dir = CaptainPaths.CaptainDir(config);
        var pidPath = CaptainPaths.PidPath(config);
        var rulesPath = CaptainPaths.RulesPath(config);
        var logPath = CaptainPaths.LogPath(config);

        renderer.WriteInfo($"Captain dir: {dir}");
        renderer.WriteInfo($"Rules: {rulesPath}");
        renderer.WriteInfo($"Log: {logPath}");

        if (!File.Exists(pidPath))
        {
            renderer.WriteInfo("Status: stopped");
            return Task.FromResult(0);
        }

        var pidText = File.ReadAllText(pidPath).Trim();
        if (!int.TryParse(pidText, out var pid) || !ProcessExists(pid))
        {
            renderer.WriteWarning("Status: stale pid file (not running)");
            return Task.FromResult(0);
        }

        renderer.WriteInfo($"Status: running (pid {pid})");
        return Task.FromResult(0);
    }

    private static Task<int> UndoAsync(AppConfig config, IRenderer renderer)
    {
        var rules = CaptainRulesStore.LoadOrDefault(config);
        var ops = new CaptainFileOps(config, rules);
        try
        {
            if (!ops.TryUndoLast(out var msg))
            {
                renderer.WriteWarning(msg);
                return Task.FromResult(1);
            }
            renderer.WriteInfo(msg);
            return Task.FromResult(0);
        }
        catch (Exception ex)
        {
            renderer.WriteError(ex.Message);
            return Task.FromResult(1);
        }
    }

    private static Task<int> ScanAsync(AppConfig config, IRenderer renderer, CancellationToken ct)
    {
        var rules = CaptainRulesStore.LoadOrDefault(config);
        var indexer = new CaptainIndexer(config, rules);
        renderer.WriteInfo("Scanning roots…");
        var count = indexer.ScanAll(renderer, ct);
        renderer.WriteInfo($"Indexed {count} files.");
        return Task.FromResult(0);
    }

    private static Task<int> QueryAsync(string[] args, AppConfig config, IRenderer renderer)
    {
        if (args.Length == 0)
        {
            renderer.WriteError("Usage: openmono captain query <query>");
            return Task.FromResult(1);
        }

        var q = string.Join(' ', args);
        var rules = CaptainRulesStore.LoadOrDefault(config);
        var indexer = new CaptainIndexer(config, rules);
        var hits = indexer.Search(q, limit: 20);

        if (hits.Count == 0)
        {
            renderer.WriteInfo("No matches.");
            return Task.FromResult(0);
        }

        renderer.WriteInfo($"Matches: {hits.Count}");
        foreach (var h in hits)
        {
            renderer.WriteInfo($"{h.Path}  ({h.Size} bytes, mtime {h.MtimeUtc})");
            if (!string.IsNullOrWhiteSpace(h.Snippet))
                renderer.WriteInfo($"  {h.Snippet}");
        }

        return Task.FromResult(0);
    }

    private static async Task<int> McpSmokeAsync(AppConfig config, IRenderer renderer, CancellationToken ct)
    {
        if (!config.McpServers.TryGetValue("ms365", out var ms365) || !ms365.Enabled)
        {
            renderer.WriteWarning("MCP smoke: missing enabled MCP server config named 'ms365'.");
            return 1;
        }

        if (!config.McpServers.TryGetValue("chrome-devtools", out var chrome) || !chrome.Enabled)
        {
            renderer.WriteWarning("MCP smoke: missing enabled MCP server config named 'chrome-devtools'.");
            return 1;
        }

        renderer.WriteInfo("MCP smoke: connecting to 'ms365'…");
        using (var client = await McpClient.ConnectAsync(ToMcpConfig("ms365", ms365), ct))
        {
            var tools = await client.ListToolsAsync(ct);
            renderer.WriteInfo($"MCP ms365 tools: {CountTools(tools)}");

            var inbox = await client.CallToolAsync("list_inbox", JsonSerializer.SerializeToElement(new { }), ct);
            var messages = FirstContentData(inbox).GetProperty("messages");
            if (messages.ValueKind == JsonValueKind.Array && messages.GetArrayLength() > 0)
            {
                var first = messages[0];
                var id = first.GetProperty("id").GetString() ?? "";
                var subject = first.TryGetProperty("subject", out var s) ? s.GetString() ?? "" : "";
                renderer.WriteInfo($"MCP ms365 inbox[0]: {id} — {subject}");

                _ = await client.CallToolAsync(
                    "add_category",
                    JsonSerializer.SerializeToElement(new { messageId = id, category = "captain_demo" }),
                    ct);

                var inbox2 = await client.CallToolAsync("list_inbox", JsonSerializer.SerializeToElement(new { }), ct);
                var messages2 = FirstContentData(inbox2).GetProperty("messages");
                var updated = messages2.EnumerateArray().FirstOrDefault(m => (m.GetProperty("id").GetString() ?? "") == id);
                if (updated.ValueKind != JsonValueKind.Undefined &&
                    updated.TryGetProperty("categories", out var cats) &&
                    cats.ValueKind == JsonValueKind.Array)
                {
                    renderer.WriteInfo($"MCP ms365 labeled: {id} categories=[{string.Join(", ", cats.EnumerateArray().Select(c => c.GetString() ?? ""))}]");
                }
            }
            else
            {
                renderer.WriteWarning("MCP ms365 inbox is empty (or tool returned no messages).");
            }
        }

        renderer.WriteInfo("MCP smoke: connecting to 'chrome-devtools'…");
        string? capturedPath = null;
        using (var client = await McpClient.ConnectAsync(ToMcpConfig("chrome-devtools", chrome), ct))
        {
            var tools = await client.ListToolsAsync(ct);
            renderer.WriteInfo($"MCP chrome-devtools tools: {CountTools(tools)}");

            var pagesRes = await client.CallToolAsync("list_pages", JsonSerializer.SerializeToElement(new { }), ct);
            var pages = FirstContentData(pagesRes).GetProperty("pages");
            if (pages.ValueKind != JsonValueKind.Array || pages.GetArrayLength() == 0)
            {
                renderer.WriteWarning("MCP chrome-devtools returned no pages.");
                return 1;
            }

            var first = pages[0];
            var pageId = first.GetProperty("id").GetString() ?? "1";
            _ = await client.CallToolAsync("select_page", JsonSerializer.SerializeToElement(new { id = pageId }), ct);

            var evalRes = await client.CallToolAsync(
                "evaluate_script",
                JsonSerializer.SerializeToElement(new
                {
                    script = "() => ({ title: document.title, url: location.href, text: document.body.innerText })"
                }),
                ct);
            var eval = FirstContentData(evalRes);
            var title = eval.TryGetProperty("title", out var t) ? t.GetString() ?? "captured_page" : "captured_page";
            var url = eval.TryGetProperty("url", out var u) ? u.GetString() ?? "" : "";
            var text = eval.TryGetProperty("text", out var body) ? body.GetString() ?? "" : "";

            var capturesDir = Path.Combine(config.WorkingDirectory, ".captain_captures");
            Directory.CreateDirectory(capturesDir);
            capturedPath = Path.Combine(capturesDir, $"{DateTime.UtcNow:yyyyMMdd_HHmmss}_{SafeSlug(title)}.md");

            var md = $"""
                # {title}

                URL: {url}
                CapturedAtUtc: {DateTime.UtcNow:o}

                ## Summary
                - MCP smoke capture

                ## Text

                {text}
                """;

            await File.WriteAllTextAsync(capturedPath, md, ct);
            renderer.WriteInfo($"MCP chrome-devtools saved capture: {capturedPath}");
        }

        if (!string.IsNullOrWhiteSpace(capturedPath))
        {
            var rules = CaptainRulesStore.LoadOrDefault(config);
            var indexer = new CaptainIndexer(config, rules);
            indexer.IndexFile(capturedPath);
            var hits = indexer.Search("Example Domain", limit: 5);
            renderer.WriteInfo($"MCP smoke: indexed capture hits={hits.Count}");
        }

        renderer.WriteInfo("MCP smoke: OK");
        return 0;
    }

    private static McpServerConfig ToMcpConfig(string name, McpServerSettings settings) => new()
    {
        Name = name,
        Command = settings.Command,
        Args = settings.Args,
        Env = settings.Env,
        WorkingDirectory = settings.WorkingDirectory,
        Enabled = settings.Enabled,
    };

    private static int CountTools(JsonElement listToolsResult)
    {
        if (listToolsResult.TryGetProperty("tools", out var tools) && tools.ValueKind == JsonValueKind.Array)
            return tools.GetArrayLength();
        return 0;
    }

    private static JsonElement FirstContentData(JsonElement toolCallResult)
    {
        if (toolCallResult.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in content.EnumerateArray())
            {
                if (item.TryGetProperty("data", out var data))
                    return data;
            }
        }

        return toolCallResult;
    }

    private static string SafeSlug(string input)
    {
        var sb = new System.Text.StringBuilder(input.Length);
        foreach (var ch in input)
        {
            if (char.IsLetterOrDigit(ch)) sb.Append(char.ToLowerInvariant(ch));
            else if (ch is ' ' or '-' or '_' or '.') sb.Append('_');
        }
        var s = sb.ToString().Trim('_');
        return string.IsNullOrWhiteSpace(s) ? "capture" : s[..Math.Min(s.Length, 64)];
    }

    private static bool ProcessExists(int pid)
    {
        try { _ = Process.GetProcessById(pid); return true; }
        catch { return false; }
    }

    private static async Task PumpAsync(StreamReader reader, StreamWriter writer, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(ct);
            if (line is null) break;
            await writer.WriteLineAsync(line);
        }
    }

    private static (string fileName, string args) BuildSelfLaunch(string commandLine)
    {
        var processPath = Environment.ProcessPath;
        if (!string.IsNullOrWhiteSpace(processPath) &&
            Path.GetFileName(processPath).StartsWith("dotnet", StringComparison.OrdinalIgnoreCase))
        {
            var dllPath = Path.Combine(AppContext.BaseDirectory, "openmono.dll");
            return ("dotnet", $"\"{dllPath}\" {commandLine}");
        }

        if (!string.IsNullOrWhiteSpace(processPath))
            return (processPath, commandLine);

        // Last resort: rely on PATH.
        return ("openmono", commandLine);
    }
}

