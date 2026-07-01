using System.Diagnostics;
using OpenMono.Config;
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

