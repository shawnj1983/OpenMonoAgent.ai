using System.Text.Json;
using OpenMono.Config;

namespace OpenMono.Captain;

public sealed record CaptainActionRecord
{
    public required string Id { get; init; }
    public required string Kind { get; init; } // move|rename
    public required string FromPath { get; init; }
    public required string ToPath { get; init; }
    public required string Status { get; init; } // ok|error|undone
    public required string TimestampUtc { get; init; }
    public string? Error { get; init; }
}

public sealed class CaptainActionJournal
{
    private readonly string _path;

    public CaptainActionJournal(AppConfig config)
    {
        _path = CaptainPaths.ActionsJournalPath(config);
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        if (!File.Exists(_path))
            File.WriteAllText(_path, "");
    }

    public void Append(CaptainActionRecord record)
    {
        var json = JsonSerializer.Serialize(record);
        File.AppendAllText(_path, json + "\n");
    }

    public CaptainActionRecord? ReadLastSuccessful()
    {
        if (!File.Exists(_path)) return null;
        var lines = File.ReadAllLines(_path);
        for (var i = lines.Length - 1; i >= 0; i--)
        {
            var line = lines[i].Trim();
            if (line.Length == 0) continue;
            try
            {
                var rec = JsonSerializer.Deserialize<CaptainActionRecord>(line);
                if (rec is null) continue;
                if (rec.Status == "ok") return rec;
            }
            catch { }
        }
        return null;
    }
}

