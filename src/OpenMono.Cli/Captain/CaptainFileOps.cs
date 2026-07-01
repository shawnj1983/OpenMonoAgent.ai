using OpenMono.Config;

namespace OpenMono.Captain;

public sealed class CaptainFileOps
{
    private readonly CaptainRules _rules;
    private readonly CaptainActionJournal _journal;

    public CaptainFileOps(AppConfig config, CaptainRules rules)
    {
        _rules = rules;
        _journal = new CaptainActionJournal(config);
    }

    public void Mkdir(string dirPath)
    {
        var resolved = Path.GetFullPath(dirPath);
        if (CaptainPathPolicy.ValidateDirectory(resolved, _rules.Roots) is { } err)
            throw new InvalidOperationException(err);
        Directory.CreateDirectory(resolved);
    }

    public void Move(string fromPath, string toPath)
    {
        var resolvedFrom = Path.GetFullPath(fromPath);
        var resolvedTo = Path.GetFullPath(toPath);

        if (CaptainPathPolicy.ValidatePath(resolvedFrom, _rules.Roots) is { } errFrom)
            throw new InvalidOperationException(errFrom);
        if (CaptainPathPolicy.ValidatePath(resolvedTo, _rules.Roots) is { } errTo)
            throw new InvalidOperationException(errTo);

        if (!File.Exists(resolvedFrom))
            throw new FileNotFoundException("Source file not found", resolvedFrom);

        try
        {
            var destDir = Path.GetDirectoryName(resolvedTo);
            if (!string.IsNullOrEmpty(destDir))
                Directory.CreateDirectory(destDir);

            File.Move(resolvedFrom, resolvedTo, overwrite: true);

            _journal.Append(new CaptainActionRecord
            {
                Id = Guid.NewGuid().ToString("n"),
                Kind = "move",
                FromPath = resolvedFrom,
                ToPath = resolvedTo,
                Status = "ok",
                TimestampUtc = DateTime.UtcNow.ToString("o"),
            });
        }
        catch (Exception ex)
        {
            _journal.Append(new CaptainActionRecord
            {
                Id = Guid.NewGuid().ToString("n"),
                Kind = "move",
                FromPath = resolvedFrom,
                ToPath = resolvedTo,
                Status = "error",
                TimestampUtc = DateTime.UtcNow.ToString("o"),
                Error = ex.Message,
            });
            throw;
        }
    }

    public void Rename(string filePath, string newName)
    {
        if (newName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            throw new InvalidOperationException("new_name contains invalid filename characters");

        var resolved = Path.GetFullPath(filePath);
        if (CaptainPathPolicy.ValidatePath(resolved, _rules.Roots) is { } err)
            throw new InvalidOperationException(err);

        if (!File.Exists(resolved))
            throw new FileNotFoundException("File not found", resolved);

        var dir = Path.GetDirectoryName(resolved) ?? Directory.GetCurrentDirectory();
        var dest = Path.Combine(dir, newName);

        if (CaptainPathPolicy.ValidatePath(dest, _rules.Roots) is { } errTo)
            throw new InvalidOperationException(errTo);

        try
        {
            File.Move(resolved, dest, overwrite: true);

            _journal.Append(new CaptainActionRecord
            {
                Id = Guid.NewGuid().ToString("n"),
                Kind = "rename",
                FromPath = resolved,
                ToPath = dest,
                Status = "ok",
                TimestampUtc = DateTime.UtcNow.ToString("o"),
            });
        }
        catch (Exception ex)
        {
            _journal.Append(new CaptainActionRecord
            {
                Id = Guid.NewGuid().ToString("n"),
                Kind = "rename",
                FromPath = resolved,
                ToPath = dest,
                Status = "error",
                TimestampUtc = DateTime.UtcNow.ToString("o"),
                Error = ex.Message,
            });
            throw;
        }
    }

    public bool TryUndoLast(out string message)
    {
        var last = _journal.ReadLastSuccessful();
        if (last is null)
        {
            message = "No successful move/rename found to undo.";
            return false;
        }

        var from = last.ToPath;
        var to = last.FromPath;

        if (CaptainPathPolicy.ValidatePath(from, _rules.Roots) is { } errFrom)
            throw new InvalidOperationException(errFrom);
        if (CaptainPathPolicy.ValidatePath(to, _rules.Roots) is { } errTo)
            throw new InvalidOperationException(errTo);

        if (!File.Exists(from))
        {
            message = $"Cannot undo: missing current path: {from}";
            return false;
        }

        try
        {
            var destDir = Path.GetDirectoryName(to);
            if (!string.IsNullOrEmpty(destDir))
                Directory.CreateDirectory(destDir);

            File.Move(from, to, overwrite: true);

            _journal.Append(new CaptainActionRecord
            {
                Id = Guid.NewGuid().ToString("n"),
                Kind = "undo",
                FromPath = from,
                ToPath = to,
                Status = "ok",
                TimestampUtc = DateTime.UtcNow.ToString("o"),
            });

            message = $"Undid last action: {from} -> {to}";
            return true;
        }
        catch (Exception ex)
        {
            _journal.Append(new CaptainActionRecord
            {
                Id = Guid.NewGuid().ToString("n"),
                Kind = "undo",
                FromPath = from,
                ToPath = to,
                Status = "error",
                TimestampUtc = DateTime.UtcNow.ToString("o"),
                Error = ex.Message,
            });
            throw;
        }
    }
}

