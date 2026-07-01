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
        var indexer = new CaptainIndexer(config, rules);
        var lastSweep = DateTime.MinValue;

        while (!ct.IsCancellationRequested)
        {
            if (rules.Organization.Enabled &&
                rules.Organization.InboxRoot is { Length: > 0 } inbox &&
                rules.Organization.OrganizedRoot is { Length: > 0 } organized &&
                (DateTime.UtcNow - lastSweep).TotalSeconds >= 2)
            {
                lastSweep = DateTime.UtcNow;
                try
                {
                    OrganizeInboxOnce(inbox, organized, ops, indexer);
                }
                catch (Exception ex)
                {
                    renderer.WriteWarning($"Captain sweep failed: {ex.Message}");
                }
            }
            await Task.Delay(TimeSpan.FromSeconds(1), ct);
        }
    }

    private static void OrganizeInboxOnce(string inbox, string organizedRoot, CaptainFileOps ops, CaptainIndexer indexer)
    {
        if (!Directory.Exists(inbox)) return;

        foreach (var path in Directory.GetFiles(inbox))
        {
            var fileName = Path.GetFileName(path);
            if (string.IsNullOrWhiteSpace(fileName)) continue;

            var ext = Path.GetExtension(fileName).TrimStart('.').ToLowerInvariant();
            var bucket = BucketForExtension(ext);
            var safeName = SanitizeFileName(Path.GetFileNameWithoutExtension(fileName));
            var stamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
            var newName = $"{stamp}_{safeName}{(ext.Length > 0 ? "." + ext : "")}";

            var destDir = Path.Combine(organizedRoot, bucket);
            var dest = Path.Combine(destDir, newName);
            ops.Move(path, dest);
            indexer.IndexFile(dest);
        }
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
}

