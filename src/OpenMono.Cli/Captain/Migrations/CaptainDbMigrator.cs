using Microsoft.Data.Sqlite;

namespace OpenMono.Captain.Migrations;

public sealed class CaptainDbMigrator
{
    private readonly SqliteConnection _conn;

    public CaptainDbMigrator(SqliteConnection conn)
    {
        _conn = conn;
    }

    public int EnsureLatest()
    {
        using var tx = _conn.BeginTransaction();

        EnsureMetaTable(tx);
        var current = GetSchemaVersion(tx);

        // Apply sequential migrations. Keep each migration additive and backfillable.
        if (current < 1)
        {
            ApplyV1(tx);
            SetSchemaVersion(tx, 1);
            current = 1;
        }

        tx.Commit();
        return current;
    }

    private void EnsureMetaTable(SqliteTransaction tx)
    {
        using var cmd = _conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS meta(
              key TEXT PRIMARY KEY,
              value TEXT NOT NULL
            );
            """;
        cmd.ExecuteNonQuery();
    }

    private int GetSchemaVersion(SqliteTransaction tx)
    {
        using var cmd = _conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT value FROM meta WHERE key='schema_version' LIMIT 1;";
        var result = cmd.ExecuteScalar();
        if (result is null) return 0;
        return int.TryParse(result.ToString(), out var v) ? v : 0;
    }

    private void SetSchemaVersion(SqliteTransaction tx, int version)
    {
        using var cmd = _conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
            INSERT INTO meta(key, value) VALUES('schema_version', $v)
            ON CONFLICT(key) DO UPDATE SET value=excluded.value;
            """;
        cmd.Parameters.AddWithValue("$v", version.ToString());
        cmd.ExecuteNonQuery();
    }

    private void ApplyV1(SqliteTransaction tx)
    {
        using var cmd = _conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
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
}

