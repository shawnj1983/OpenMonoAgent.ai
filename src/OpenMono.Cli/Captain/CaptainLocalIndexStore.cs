using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;
using OpenMono.Config;

namespace OpenMono.Captain;

public sealed record CaptainSearchHit(
    string Path,
    string? Snippet,
    long Size,
    string MtimeUtc);

public sealed class CaptainLocalIndexStore
{
    private readonly string _dbPath;

    public CaptainLocalIndexStore(AppConfig config)
    {
        _dbPath = CaptainPaths.DbPath(config);
        Directory.CreateDirectory(Path.GetDirectoryName(_dbPath)!);
    }

    public void EnsureSchema()
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS meta(
              key TEXT PRIMARY KEY,
              value TEXT NOT NULL
            );

            INSERT OR IGNORE INTO meta(key, value) VALUES('schema_version', '1');

            CREATE TABLE IF NOT EXISTS files(
              path TEXT PRIMARY KEY,
              size INTEGER NOT NULL,
              mtime_utc TEXT NOT NULL,
              sha256 TEXT NULL,
              ext TEXT NULL,
              bucket TEXT NULL,
              indexed_at_utc TEXT NOT NULL
            );

            CREATE VIRTUAL TABLE IF NOT EXISTS files_fts
            USING fts5(path, content, tokenize='unicode61');
            """;
        cmd.ExecuteNonQuery();
    }

    public void UpsertFile(string path, long size, DateTime mtimeUtc, string? sha256, string? content)
    {
        using var conn = Open();
        using var tx = conn.BeginTransaction();

        var ext = Path.GetExtension(path).TrimStart('.').ToLowerInvariant();
        var bucket = BucketForExtension(ext);
        var now = DateTime.UtcNow.ToString("o");

        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = """
                INSERT INTO files(path, size, mtime_utc, sha256, ext, bucket, indexed_at_utc)
                VALUES ($path, $size, $mtime, $sha, $ext, $bucket, $indexedAt)
                ON CONFLICT(path) DO UPDATE SET
                  size=excluded.size,
                  mtime_utc=excluded.mtime_utc,
                  sha256=excluded.sha256,
                  ext=excluded.ext,
                  bucket=excluded.bucket,
                  indexed_at_utc=excluded.indexed_at_utc;
                """;
            cmd.Parameters.AddWithValue("$path", path);
            cmd.Parameters.AddWithValue("$size", size);
            cmd.Parameters.AddWithValue("$mtime", mtimeUtc.ToString("o"));
            cmd.Parameters.AddWithValue("$sha", (object?)sha256 ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$ext", string.IsNullOrEmpty(ext) ? DBNull.Value : ext);
            cmd.Parameters.AddWithValue("$bucket", bucket);
            cmd.Parameters.AddWithValue("$indexedAt", now);
            cmd.ExecuteNonQuery();
        }

        // FTS: store content only for safe text files. If null, just index path.
        using (var del = conn.CreateCommand())
        {
            del.Transaction = tx;
            del.CommandText = "DELETE FROM files_fts WHERE path = $path;";
            del.Parameters.AddWithValue("$path", path);
            del.ExecuteNonQuery();
        }

        using (var ins = conn.CreateCommand())
        {
            ins.Transaction = tx;
            ins.CommandText = "INSERT INTO files_fts(path, content) VALUES ($path, $content);";
            ins.Parameters.AddWithValue("$path", path);
            ins.Parameters.AddWithValue("$content", (object?)content ?? "");
            ins.ExecuteNonQuery();
        }

        tx.Commit();
    }

    public void RemoveFile(string path)
    {
        using var conn = Open();
        using var tx = conn.BeginTransaction();

        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = "DELETE FROM files WHERE path = $path;";
            cmd.Parameters.AddWithValue("$path", path);
            cmd.ExecuteNonQuery();
        }

        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = "DELETE FROM files_fts WHERE path = $path;";
            cmd.Parameters.AddWithValue("$path", path);
            cmd.ExecuteNonQuery();
        }

        tx.Commit();
    }

    public (long size, string mtimeUtc)? TryGetFileState(string path)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT size, mtime_utc FROM files WHERE path = $path LIMIT 1;";
        cmd.Parameters.AddWithValue("$path", path);
        using var reader = cmd.ExecuteReader();
        if (!reader.Read()) return null;
        return (reader.GetInt64(0), reader.GetString(1));
    }

    public IReadOnlyList<CaptainSearchHit> Search(string query, int limit = 20)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT
              f.path,
              f.size,
              f.mtime_utc,
              snippet(files_fts, 1, '[', ']', '…', 10) AS snippet
            FROM files_fts
            JOIN files f ON f.path = files_fts.path
            WHERE files_fts MATCH $q
            LIMIT $limit;
            """;
        cmd.Parameters.AddWithValue("$q", BuildSafeFtsQuery(query));
        cmd.Parameters.AddWithValue("$limit", limit);

        var hits = new List<CaptainSearchHit>();
        try
        {
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                hits.Add(new CaptainSearchHit(
                    Path: reader.GetString(0),
                    Size: reader.GetInt64(1),
                    MtimeUtc: reader.GetString(2),
                    Snippet: reader.IsDBNull(3) ? null : reader.GetString(3)
                ));
            }
        }
        catch (SqliteException)
        {
            // Fallback: if the query cannot be expressed safely in FTS syntax, do a path substring scan.
            return SearchByPathLike(query, limit);
        }
        return hits;
    }

    private IReadOnlyList<CaptainSearchHit> SearchByPathLike(string query, int limit)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT path, size, mtime_utc
            FROM files
            WHERE path LIKE $q
            LIMIT $limit;
            """;
        cmd.Parameters.AddWithValue("$q", $"%{query}%");
        cmd.Parameters.AddWithValue("$limit", limit);

        var hits = new List<CaptainSearchHit>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            hits.Add(new CaptainSearchHit(
                Path: reader.GetString(0),
                Size: reader.GetInt64(1),
                MtimeUtc: reader.GetString(2),
                Snippet: null
            ));
        }
        return hits;
    }

    private static string BuildSafeFtsQuery(string raw)
    {
        var normalized = string.Concat(raw.Select(c => char.IsLetterOrDigit(c) ? c : ' '));
        var parts = normalized
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Take(16)
            .ToList();

        if (parts.Count == 0) return "\"\"";
        if (parts.Count == 1) return $"\"{parts[0]}\"";
        return string.Join(" AND ", parts.Select(p => $"\"{p}\""));
    }

    public static string? TryExtractText(string path, long sizeBytes)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        if (!IsSafeTextExtension(ext))
            return null;

        // Defensive ceiling: don't read huge files into memory.
        if (sizeBytes > 1_000_000)
            return null;

        try
        {
            // Try UTF-8 first, fall back to default if needed.
            var bytes = File.ReadAllBytes(path);
            return Encoding.UTF8.GetString(bytes);
        }
        catch
        {
            return null;
        }
    }

    public static string? TrySha256(string path, long sizeBytes)
    {
        // Hash only moderately-sized files to keep scans responsive.
        if (sizeBytes > 50_000_000)
            return null;

        try
        {
            using var sha = SHA256.Create();
            using var fs = File.OpenRead(path);
            var hash = sha.ComputeHash(fs);
            return Convert.ToHexString(hash).ToLowerInvariant();
        }
        catch
        {
            return null;
        }
    }

    private SqliteConnection Open()
    {
        var conn = new SqliteConnection($"Data Source={_dbPath}");
        conn.Open();
        return conn;
    }

    private static bool IsSafeTextExtension(string ext) => ext is
        ".txt" or ".md" or ".json" or ".yml" or ".yaml" or ".toml" or ".xml" or ".csv" or
        ".cs" or ".fs" or ".vb" or ".ts" or ".tsx" or ".js" or ".jsx" or
        ".py" or ".go" or ".rs" or ".java" or ".kt" or ".swift" or
        ".html" or ".css" or ".scss" or ".sql";

    private static string BucketForExtension(string ext) => ext switch
    {
        "jpg" or "jpeg" or "png" or "gif" or "webp" or "svg" => "images",
        "pdf" or "doc" or "docx" or "ppt" or "pptx" or "xls" or "xlsx" => "docs",
        "zip" or "rar" or "7z" or "tar" or "gz" => "archives",
        "cs" or "ts" or "tsx" or "js" or "py" or "go" or "rs" or "java" => "code",
        _ => "other",
    };
}

