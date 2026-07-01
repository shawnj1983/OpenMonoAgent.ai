using OpenMono.Config;
using OpenMono.Rendering;

namespace OpenMono.Captain;

public sealed class CaptainIndexer
{
    private readonly AppConfig _config;
    private readonly CaptainRules _rules;
    private readonly CaptainLocalIndexStore _store;
    private readonly CaptainOpenSearchIndexStore? _openSearch;

    public CaptainIndexer(AppConfig config, CaptainRules rules)
    {
        _config = config;
        _rules = rules;
        _store = new CaptainLocalIndexStore(config);
        _store.EnsureSchema();

        if (config.OpenSearch.Enabled)
        {
            try
            {
                _openSearch = new CaptainOpenSearchIndexStore(config);
                _openSearch.EnsureIndex();
            }
            catch
            {
                _openSearch = null;
            }
        }
    }

    public int ScanAll(IRenderer renderer, CancellationToken ct)
    {
        var indexed = 0;

        foreach (var root in _rules.Roots)
        {
            if (ct.IsCancellationRequested) break;
            if (!Directory.Exists(root))
            {
                renderer.WriteWarning($"Missing root: {root}");
                continue;
            }

            foreach (var path in EnumerateFilesSafe(root))
            {
                if (ct.IsCancellationRequested) break;

                try
                {
                    IndexFile(path);
                    indexed++;
                    if (indexed % 500 == 0)
                        renderer.WriteInfo($"Indexed {indexed} files…");
                }
                catch (Exception ex)
                {
                    renderer.WriteWarning($"Index failed: {path} — {ex.Message}");
                }
            }
        }

        return indexed;
    }

    public void IndexFile(string path)
    {
        var resolved = Path.GetFullPath(path, _config.WorkingDirectory);
        if (CaptainPathPolicy.ValidatePath(resolved, _rules.Roots) is { } err)
            throw new InvalidOperationException(err);

        var fi = new FileInfo(resolved);
        if (!fi.Exists) return;

        var currentMtime = fi.LastWriteTimeUtc.ToString("o");
        var existing = _store.TryGetFileState(resolved);
        if (existing is not null && existing.Value.size == fi.Length && existing.Value.mtimeUtc == currentMtime)
            return;

        var content = CaptainLocalIndexStore.TryExtractText(resolved, fi.Length);
        var sha = CaptainLocalIndexStore.TrySha256(resolved, fi.Length);
        _store.UpsertFile(resolved, fi.Length, fi.LastWriteTimeUtc, sha, content);

        if (_openSearch is not null)
        {
            try
            {
                _openSearch.UpsertFile(resolved, fi.Length, currentMtime, sha, content);
            }
            catch
            {
                // Best-effort. Local index remains the fallback.
            }
        }
    }

    public IReadOnlyList<CaptainSearchHit> Search(string query, int limit = 20) =>
        _openSearch is not null
            ? TryOpenSearchFirst(query, limit)
            : _store.Search(query, limit);

    private IReadOnlyList<CaptainSearchHit> TryOpenSearchFirst(string query, int limit)
    {
        try
        {
            return _openSearch!.Search(query, limit);
        }
        catch
        {
            return _store.Search(query, limit);
        }
    }

    private IEnumerable<string> EnumerateFilesSafe(string root)
    {
        var stack = new Stack<string>();
        stack.Push(root);

        while (stack.Count > 0)
        {
            var dir = stack.Pop();
            if (ShouldIgnore(dir)) continue;

            string[] subDirs;
            try { subDirs = Directory.GetDirectories(dir); }
            catch { continue; }

            foreach (var sd in subDirs)
            {
                if (!ShouldIgnore(sd))
                    stack.Push(sd);
            }

            string[] files;
            try { files = Directory.GetFiles(dir); }
            catch { continue; }

            foreach (var f in files)
            {
                if (ShouldIgnore(f)) continue;
                yield return f;
            }
        }
    }

    private bool ShouldIgnore(string path)
    {
        foreach (var token in _rules.Ignore)
        {
            if (token.Length == 0) continue;
            if (path.Contains(token, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }
}

