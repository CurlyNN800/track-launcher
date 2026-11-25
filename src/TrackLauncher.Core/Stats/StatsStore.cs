using System.Globalization;
using System.Text;
using Microsoft.Data.Sqlite;

namespace TrackLauncher.Core.Stats;

public sealed record SessionRow(long Id, string TrackId, string TrackName, DateTime StartedUtc, DateTime EndedUtc, bool Open);

/// <summary>
/// Статистика в SQLite: одна строка на каждый запуск трека.
/// Время хранится в UTC в формате ISO 8601 — строки сравниваются как даты.
/// </summary>
public sealed class StatsStore
{
    private readonly string _connectionString;

    public StatsStore(string dbPath)
    {
        _connectionString = new SqliteConnectionStringBuilder { DataSource = dbPath }.ToString();
        using var connection = Open();
        Execute(connection, """
            CREATE TABLE IF NOT EXISTS sessions (
                id          INTEGER PRIMARY KEY AUTOINCREMENT,
                track_id    TEXT NOT NULL,
                track_name  TEXT NOT NULL,
                started_at  TEXT NOT NULL,
                ended_at    TEXT NULL,
                last_seen   TEXT NOT NULL,
                ended_by    TEXT NULL
            );
            CREATE INDEX IF NOT EXISTS ix_sessions_started ON sessions(started_at);
            """);
    }

    public long BeginSession(string trackId, string trackName, DateTime startedUtc)
    {
        using var connection = Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO sessions (track_id, track_name, started_at, last_seen)
            VALUES ($track, $name, $start, $start);
            SELECT last_insert_rowid();
            """;
        cmd.Parameters.AddWithValue("$track", trackId);
        cmd.Parameters.AddWithValue("$name", trackName);
        cmd.Parameters.AddWithValue("$start", Iso(startedUtc));
        return (long)cmd.ExecuteScalar()!;
    }

    /// <summary>«Ещё жив»: если комп выключат без «Завершить трек», сессия закончится на этой отметке.</summary>
    public void Touch(long id, DateTime nowUtc)
    {
        using var connection = Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "UPDATE sessions SET last_seen = $now WHERE id = $id AND ended_at IS NULL";
        cmd.Parameters.AddWithValue("$now", Iso(nowUtc));
        cmd.Parameters.AddWithValue("$id", id);
        cmd.ExecuteNonQuery();
    }

    public void EndSession(long id, DateTime endedUtc, string endedBy)
    {
        using var connection = Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "UPDATE sessions SET ended_at = $end, last_seen = $end, ended_by = $by WHERE id = $id AND ended_at IS NULL";
        cmd.Parameters.AddWithValue("$end", Iso(endedUtc));
        cmd.Parameters.AddWithValue("$by", endedBy);
        cmd.Parameters.AddWithValue("$id", id);
        cmd.ExecuteNonQuery();
    }

    /// <summary>Закрывает «зависшие» сессии (лаунчер упал или комп выключили) по последней отметке.</summary>
    public void CloseDangling(long? exceptId)
    {
        using var connection = Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "UPDATE sessions SET ended_at = last_seen, ended_by = 'crash' WHERE ended_at IS NULL AND id <> $except";
        cmd.Parameters.AddWithValue("$except", exceptId ?? -1);
        cmd.ExecuteNonQuery();
    }

    /// <summary>Сессии, которые пересекаются с промежутком [from, to).</summary>
    public List<SessionRow> GetSessions(DateTime fromUtc, DateTime toUtc)
    {
        using var connection = Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT id, track_id, track_name, started_at, COALESCE(ended_at, last_seen), ended_at IS NULL
            FROM sessions
            WHERE started_at < $to AND COALESCE(ended_at, last_seen) > $from
            ORDER BY started_at
            """;
        cmd.Parameters.AddWithValue("$from", Iso(fromUtc));
        cmd.Parameters.AddWithValue("$to", Iso(toUtc));

        var rows = new List<SessionRow>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            rows.Add(new SessionRow(
                reader.GetInt64(0),
                reader.GetString(1),
                reader.GetString(2),
                ParseIso(reader.GetString(3)),
                ParseIso(reader.GetString(4)),
                reader.GetInt64(5) == 1));
        }
        return rows;
    }

    public string ExportCsv()
    {
        var rows = GetSessions(DateTime.MinValue.AddYears(1), DateTime.UtcNow.AddDays(1));
        var sb = new StringBuilder("id;track_id;track_name;started;ended;minutes\r\n");
        foreach (var r in rows)
        {
            var minutes = (r.EndedUtc - r.StartedUtc).TotalMinutes;
            sb.Append(CultureInfo.InvariantCulture, $"{r.Id};{r.TrackId};{CsvEscape(r.TrackName)};")
              .Append(CultureInfo.InvariantCulture, $"{r.StartedUtc.ToLocalTime():yyyy-MM-dd HH:mm};{r.EndedUtc.ToLocalTime():yyyy-MM-dd HH:mm};{minutes:0}\r\n");
        }
        return sb.ToString();
    }

    private SqliteConnection Open()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        return connection;
    }

    private static void Execute(SqliteConnection connection, string sql)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    private static string Iso(DateTime utc) =>
        DateTime.SpecifyKind(utc.Kind == DateTimeKind.Local ? utc.ToUniversalTime() : utc, DateTimeKind.Utc)
            .ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);

    private static DateTime ParseIso(string value) =>
        DateTime.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal);

    private static string CsvEscape(string value) =>
        value.Contains(';') || value.Contains('"') ? "\"" + value.Replace("\"", "\"\"") + "\"" : value;
}
