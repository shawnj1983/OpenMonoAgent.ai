using System.Globalization;
using OpenMono.Config;
using OpenMono.Rendering;

namespace OpenMono.Captain;

public static class CaptainDaemon
{
    public static async Task RunAsync(
        AppConfig config,
        CaptainRules rules,
        IRenderer renderer,
        CancellationToken ct)
    {
        // Minimal first pass: keep process alive and prove lifecycle works.
        // SafeFileOps is wired so the daemon can move/rename without deletions.
        renderer.WriteInfo($"Captain roots: {string.Join(", ", rules.Roots)}");
        renderer.WriteInfo($"Captain inbox: {rules.Organization.InboxRoot ?? "(none)"}");
        renderer.WriteInfo($"Captain organized root: {rules.Organization.OrganizedRoot ?? "(none)"}");

        var ops = new CaptainFileOps(config, rules);
        var journal = new CaptainActionJournal(config);
        var indexer = new CaptainIndexer(config, rules);
        var queue = new CaptainEventQueue(config);
        using var watcher = new FileWatchService(queue, rules, renderer);
        watcher.Start();

        while (!ct.IsCancellationRequested)
        {
            var batch = queue.DequeueBatch(maxItems: 50);
            if (batch.Count > 0)
            {
                foreach (var ev in batch)
                {
                    if (ct.IsCancellationRequested) break;
                    try
                    {
                        HandleEvent(ev, rules, ops, indexer, journal, renderer);
                    }
                    catch (Exception ex)
                    {
                        renderer.WriteWarning($"Event failed ({ev.Type}): {ex.Message}");
                    }
                }
            }

            await Task.Delay(batch.Count > 0 ? TimeSpan.FromMilliseconds(50) : TimeSpan.FromMilliseconds(250), ct);
        }
    }

    private static void HandleEvent(CaptainEvent ev, CaptainRules rules, CaptainFileOps ops, CaptainIndexer indexer, CaptainActionJournal journal, IRenderer renderer)
    {
        // Ignore directory events; we index files only.
        if (Directory.Exists(ev.Path))
            return;

        if (ev.Type is "fs_deleted")
        {
            renderer.WriteInfo($"Index remove: {ev.Path}");
            indexer.RemoveFile(ev.Path);
            return;
        }

        var path = ev.Path;
        if (!File.Exists(path))
            return;

        if (rules.Organization.Enabled &&
            rules.Organization.InboxRoot is { Length: > 0 } inbox &&
            rules.Organization.OrganizedRoot is { Length: > 0 } organizedRoot &&
            IsUnder(path, inbox))
        {
            if (ShouldSuppressInboxReorg(path, journal))
            {
                renderer.WriteInfo($"Skip organize (recent undo): {path}");
                renderer.WriteInfo($"Index: {path}");
                indexer.IndexFile(path);
                return;
            }

            var fileName = Path.GetFileName(path);
            if (string.IsNullOrWhiteSpace(fileName)) return;

            var ext = Path.GetExtension(fileName).TrimStart('.').ToLowerInvariant();
            var bucket = BucketForExtension(ext);
            var safeName = SanitizeFileName(Path.GetFileNameWithoutExtension(fileName));
            var stamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
            var newName = $"{stamp}_{safeName}{(ext.Length > 0 ? "." + ext : "")}";

            var destDir = Path.Combine(organizedRoot, bucket);
            var dest = Path.Combine(destDir, newName);
            renderer.WriteInfo($"Organize inbox: {path} -> {dest}");
            ops.Move(path, dest);
            renderer.WriteInfo($"Index: {dest}");
            indexer.IndexFile(dest);
            return;
        }

        // Outside inbox: keep in place, but index (incremental).
        renderer.WriteInfo($"Index: {path}");
        indexer.IndexFile(path);
    }

    private static bool ShouldSuppressInboxReorg(string inboxPath, CaptainActionJournal journal)
    {
        var last = journal.ReadLastSuccessful();
        if (last is null) return false;
        if (!string.Equals(last.Kind, "undo", StringComparison.Ordinal)) return false;

        var pathEquals = OperatingSystem.IsWindows()
            ? string.Equals(last.ToPath, inboxPath, StringComparison.OrdinalIgnoreCase)
            : string.Equals(last.ToPath, inboxPath, StringComparison.Ordinal);

        if (!pathEquals) return false;

        if (!DateTime.TryParse(last.TimestampUtc, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var ts))
            return false;

        // Avoid immediately re-organizing a file the user just undid.
        return (DateTime.UtcNow - ts) < TimeSpan.FromSeconds(30);
    }

    private static string BucketForExtension(string ext) => ext switch
    {
        "jpg" or "jpeg" or "png" or "gif" or "webp" or "svg" => "images",
        "pdf" or "doc" or "docx" or "ppt" or "pptx" or "xls" or "xlsx" => "docs",
        "zip" or "rar" or "7z" or "tar" or "gz" => "archives",
        "cs" or "ts" or "tsx" or "js" or "py" or "go" or "rs" or "java" => "code",
        _ => "other",
    };

    private static string SanitizeFileName(string name)
    {
        var cleaned = string.Concat(name.Select(c =>
            char.IsLetterOrDigit(c) || c == '-' || c == '_' ? c : '_'));
        while (cleaned.Contains("__", StringComparison.Ordinal))
            cleaned = cleaned.Replace("__", "_", StringComparison.Ordinal);
        return cleaned.Trim('_').ToLowerInvariant();
    }

    private static bool IsUnder(string path, string root)
    {
        var normRoot = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var normPath = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return normPath.StartsWith(normRoot, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    }
}

