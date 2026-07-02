using System.Collections.Concurrent;
using OpenMono.Rendering;

namespace OpenMono.Captain;

public sealed class FileWatchService : IDisposable
{
    private readonly List<FileSystemWatcher> _watchers = [];
    private readonly CaptainEventQueue _queue;
    private readonly CaptainRules _rules;
    private readonly IRenderer _renderer;
    private readonly ConcurrentDictionary<string, DateTime> _lastEventUtc = new(StringComparer.OrdinalIgnoreCase);

    public FileWatchService(CaptainEventQueue queue, CaptainRules rules, IRenderer renderer)
    {
        _queue = queue;
        _rules = rules;
        _renderer = renderer;
    }

    public void Start()
    {
        foreach (var root in _rules.Roots)
        {
            if (!Directory.Exists(root))
            {
                _renderer.WriteWarning($"Watch root missing: {root}");
                continue;
            }

            var w = new FileSystemWatcher(root)
            {
                IncludeSubdirectories = true,
                EnableRaisingEvents = false,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size
            };

            w.Created += (_, e) => OnEvent("fs_created", e.FullPath, null);
            w.Changed += (_, e) => OnEvent("fs_changed", e.FullPath, null);
            w.Deleted += (_, e) => OnEvent("fs_deleted", e.FullPath, null);
            w.Renamed += (_, e) => OnEvent("fs_renamed", e.FullPath, e.OldFullPath);
            w.EnableRaisingEvents = true;

            _watchers.Add(w);
        }

        if (_watchers.Count > 0)
            _renderer.WriteInfo($"Watching {_watchers.Count} root(s) for file changes.");
    }

    private void OnEvent(string type, string path, string? oldPath)
    {
        if (ShouldIgnore(path)) return;
        if (IsDebounced(path)) return;

        _queue.Enqueue(new CaptainEvent
        {
            Id = Guid.NewGuid().ToString("n"),
            Type = type,
            Path = path,
            OldPath = oldPath,
            TimestampUtc = DateTime.UtcNow.ToString("o"),
        });
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

    private bool IsDebounced(string path)
    {
        var now = DateTime.UtcNow;
        var last = _lastEventUtc.GetOrAdd(path, _ => DateTime.MinValue);
        if ((now - last).TotalMilliseconds < 500)
            return true;
        _lastEventUtc[path] = now;
        return false;
    }

    public void Dispose()
    {
        foreach (var w in _watchers)
        {
            try { w.EnableRaisingEvents = false; } catch { }
            try { w.Dispose(); } catch { }
        }
        _watchers.Clear();
    }
}

