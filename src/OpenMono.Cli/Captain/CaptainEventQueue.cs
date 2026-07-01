using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using OpenMono.Config;

namespace OpenMono.Captain;

public sealed record CaptainEvent
{
    public required string Id { get; init; }
    public required string Type { get; init; } // fs_created|fs_changed|fs_renamed|fs_deleted
    public required string Path { get; init; }
    public string? OldPath { get; init; }
    public required string TimestampUtc { get; init; }
}

public sealed class CaptainEventQueue
{
    private readonly string _queuePath;
    private readonly string _cursorPath;
    private readonly object _appendLock = new();

    public CaptainEventQueue(AppConfig config)
    {
        _queuePath = CaptainPaths.QueuePath(config);
        _cursorPath = CaptainPaths.QueueCursorPath(config);
        Directory.CreateDirectory(Path.GetDirectoryName(_queuePath)!);
        if (!File.Exists(_queuePath))
            File.WriteAllText(_queuePath, "");
        if (!File.Exists(_cursorPath))
            File.WriteAllText(_cursorPath, "0");
    }

    public void Enqueue(CaptainEvent ev)
    {
        var json = JsonSerializer.Serialize(ev);
        lock (_appendLock)
        {
            File.AppendAllText(_queuePath, json + "\n", Encoding.UTF8);
        }
    }

    public IReadOnlyList<CaptainEvent> DequeueBatch(int maxItems)
    {
        var cursor = ReadCursor();
        if (cursor < 0) cursor = 0;

        var batch = new List<CaptainEvent>(capacity: Math.Max(1, maxItems));
        long newCursor = cursor;

        using var fs = new FileStream(_queuePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        if (cursor > fs.Length)
            cursor = 0;
        fs.Seek(cursor, SeekOrigin.Begin);

        using var reader = new StreamReader(fs, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
        while (batch.Count < maxItems)
        {
            var line = reader.ReadLine();
            if (line is null) break;

            // Approximate cursor update: compute bytes read (line + newline).
            newCursor += Encoding.UTF8.GetByteCount(line) + 1;

            var trimmed = line.Trim();
            if (trimmed.Length == 0) continue;
            try
            {
                var ev = JsonSerializer.Deserialize<CaptainEvent>(trimmed);
                if (ev is not null) batch.Add(ev);
            }
            catch { }
        }

        if (batch.Count > 0)
            WriteCursor(newCursor);

        return batch;
    }

    private long ReadCursor()
    {
        try
        {
            var txt = File.ReadAllText(_cursorPath).Trim();
            return long.TryParse(txt, out var val) ? val : 0;
        }
        catch { return 0; }
    }

    private void WriteCursor(long cursor)
    {
        try { File.WriteAllText(_cursorPath, cursor.ToString()); }
        catch { }
    }
}

