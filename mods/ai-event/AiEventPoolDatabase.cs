using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Microsoft.Data.Sqlite;

namespace AiEvent;

public sealed class AiEventPoolDatabase
{
    private readonly string _databaseDirectoryPath;
    private readonly string _databasePath;
    private readonly string _connectionString;
    private bool _initialized;

    public AiEventPoolDatabase(string databaseDirectoryPath)
    {
        _databaseDirectoryPath = databaseDirectoryPath;
        _databasePath = Path.Combine(databaseDirectoryPath, "pool.db");
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = _databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
        }.ToString();
    }

    public void EnsureInitialized()
    {
        if (_initialized)
        {
            return;
        }

        Directory.CreateDirectory(_databaseDirectoryPath);
        using SqliteConnection connection = OpenConnection();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            """
            PRAGMA journal_mode = WAL;
            PRAGMA synchronous = NORMAL;
            CREATE TABLE IF NOT EXISTS event_pool (
                entry_id TEXT PRIMARY KEY,
                generated_at_utc TEXT NOT NULL,
                source TEXT NOT NULL,
                seed TEXT NOT NULL,
                theme TEXT NOT NULL,
                slot INTEGER NOT NULL,
                eng_title TEXT NOT NULL,
                zhs_title TEXT NOT NULL,
                eng_initial_description TEXT NOT NULL,
                zhs_initial_description TEXT NOT NULL,
                event_key TEXT NOT NULL,
                payload_json TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS ix_event_pool_generated_at ON event_pool(generated_at_utc DESC);
            CREATE INDEX IF NOT EXISTS ix_event_pool_seed_source ON event_pool(seed, source);
            CREATE INDEX IF NOT EXISTS ix_event_pool_slot_generated_at ON event_pool(slot, generated_at_utc DESC);
            """;
        command.ExecuteNonQuery();
        _initialized = true;
    }

    public void Upsert(AiEventPoolEntry entry, string payloadJson)
    {
        EnsureInitialized();

        using SqliteConnection connection = OpenConnection();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO event_pool (
                entry_id, generated_at_utc, source, seed, theme, slot,
                eng_title, zhs_title, eng_initial_description, zhs_initial_description,
                event_key, payload_json
            )
            VALUES (
                $entry_id, $generated_at_utc, $source, $seed, $theme, $slot,
                $eng_title, $zhs_title, $eng_initial_description, $zhs_initial_description,
                $event_key, $payload_json
            )
            ON CONFLICT(entry_id) DO UPDATE SET
                generated_at_utc = excluded.generated_at_utc,
                source = excluded.source,
                seed = excluded.seed,
                theme = excluded.theme,
                slot = excluded.slot,
                eng_title = excluded.eng_title,
                zhs_title = excluded.zhs_title,
                eng_initial_description = excluded.eng_initial_description,
                zhs_initial_description = excluded.zhs_initial_description,
                event_key = excluded.event_key,
                payload_json = excluded.payload_json;
            """;

        BindEntry(command, entry, payloadJson);
        command.ExecuteNonQuery();
    }

    public List<AiEventPoolEntry> QueryLatest(HashSet<AiEventSlot> allowedSlots, int limit, Func<string, AiGeneratedEventPayload> payloadParser)
    {
        EnsureInitialized();
        if (allowedSlots.Count == 0 || limit <= 0)
        {
            return new List<AiEventPoolEntry>();
        }

        using SqliteConnection connection = OpenConnection();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            $"SELECT * FROM event_pool WHERE slot IN ({BuildInClause(command, allowedSlots)}) ORDER BY generated_at_utc DESC LIMIT $limit;";
        command.Parameters.AddWithValue("$limit", limit);
        return ReadEntries(command, payloadParser);
    }

    public List<AiEventPoolEntry> QueryBySeed(string seed, string? source, Func<string, AiGeneratedEventPayload> payloadParser)
    {
        EnsureInitialized();

        using SqliteConnection connection = OpenConnection();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            string.IsNullOrWhiteSpace(source)
                ? "SELECT * FROM event_pool WHERE seed = $seed ORDER BY generated_at_utc DESC;"
                : "SELECT * FROM event_pool WHERE seed = $seed AND source = $source ORDER BY generated_at_utc DESC;";
        command.Parameters.AddWithValue("$seed", seed);
        if (!string.IsNullOrWhiteSpace(source))
        {
            command.Parameters.AddWithValue("$source", source);
        }

        return ReadEntries(command, payloadParser);
    }

    public List<AiEventPoolEntry> QueryByIds(HashSet<string> entryIds, Func<string, AiGeneratedEventPayload> payloadParser)
    {
        EnsureInitialized();
        if (entryIds.Count == 0)
        {
            return new List<AiEventPoolEntry>();
        }

        using SqliteConnection connection = OpenConnection();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = $"SELECT * FROM event_pool WHERE entry_id IN ({BuildInClause(command, entryIds)}) ORDER BY generated_at_utc DESC;";
        return ReadEntries(command, payloadParser);
    }

    public List<AiEventPoolEntry> QueryAll(Func<string, AiGeneratedEventPayload> payloadParser)
    {
        EnsureInitialized();

        using SqliteConnection connection = OpenConnection();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM event_pool ORDER BY generated_at_utc DESC;";
        return ReadEntries(command, payloadParser);
    }

    public int GetSummaryCount()
    {
        EnsureInitialized();

        using SqliteConnection connection = OpenConnection();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM event_pool;";
        return Convert.ToInt32(command.ExecuteScalar() ?? 0, CultureInfo.InvariantCulture);
    }

    public List<AiEventPoolEntrySummary> QuerySummariesPage(int offset, int limit)
    {
        EnsureInitialized();
        if (limit <= 0)
        {
            return new List<AiEventPoolEntrySummary>();
        }

        using SqliteConnection connection = OpenConnection();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT
                entry_id, generated_at_utc, source, seed, theme, slot,
                eng_title, zhs_title, eng_initial_description, zhs_initial_description, event_key
            FROM event_pool
            ORDER BY generated_at_utc DESC
            LIMIT $limit OFFSET $offset;
            """;
        command.Parameters.AddWithValue("$limit", limit);
        command.Parameters.AddWithValue("$offset", Math.Max(0, offset));

        List<AiEventPoolEntrySummary> results = new();
        using SqliteDataReader reader = command.ExecuteReader();
        while (reader.Read())
        {
            results.Add(ReadSummary(reader));
        }

        return results;
    }

    public List<AiEventPoolEntrySummary> QueryAllSummaries()
    {
        EnsureInitialized();

        using SqliteConnection connection = OpenConnection();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT
                entry_id, generated_at_utc, source, seed, theme, slot,
                eng_title, zhs_title, eng_initial_description, zhs_initial_description, event_key
            FROM event_pool
            ORDER BY generated_at_utc DESC;
            """;

        List<AiEventPoolEntrySummary> results = new();
        using SqliteDataReader reader = command.ExecuteReader();
        while (reader.Read())
        {
            results.Add(ReadSummary(reader));
        }

        return results;
    }

    public AiEventPoolEntry? QueryEntryById(string entryId, Func<string, AiGeneratedEventPayload> payloadParser)
    {
        EnsureInitialized();
        if (string.IsNullOrWhiteSpace(entryId))
        {
            return null;
        }

        using SqliteConnection connection = OpenConnection();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM event_pool WHERE entry_id = $entry_id LIMIT 1;";
        command.Parameters.AddWithValue("$entry_id", entryId);
        return ReadSingleEntry(command, payloadParser);
    }

    public void Delete(string entryId)
    {
        EnsureInitialized();

        using SqliteConnection connection = OpenConnection();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "DELETE FROM event_pool WHERE entry_id = $entry_id;";
        command.Parameters.AddWithValue("$entry_id", entryId);
        command.ExecuteNonQuery();
    }

    public void Clear()
    {
        EnsureInitialized();

        using SqliteConnection connection = OpenConnection();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "DELETE FROM event_pool;";
        command.ExecuteNonQuery();
        Vacuum(connection);
    }

    public int PromoteSeedDynamicEntriesToCache(string seed)
    {
        EnsureInitialized();

        using SqliteConnection connection = OpenConnection();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            """
            UPDATE event_pool
            SET source = 'llm_cache'
            WHERE seed = $seed AND LOWER(source) = 'llm_dynamic';
            """;
        command.Parameters.AddWithValue("$seed", seed);
        return command.ExecuteNonQuery();
    }

    public int PromoteInactiveDynamicEntriesToCache(string? activeSeed)
    {
        EnsureInitialized();

        using SqliteConnection connection = OpenConnection();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = string.IsNullOrWhiteSpace(activeSeed)
            ? "UPDATE event_pool SET source = 'llm_cache' WHERE LOWER(source) = 'llm_dynamic';"
            : "UPDATE event_pool SET source = 'llm_cache' WHERE LOWER(source) = 'llm_dynamic' AND seed <> $active_seed;";
        if (!string.IsNullOrWhiteSpace(activeSeed))
        {
            command.Parameters.AddWithValue("$active_seed", activeSeed);
        }

        return command.ExecuteNonQuery();
    }

    public bool HasAnyEntries()
    {
        return GetSummaryCount() > 0;
    }

    public void ReplaceAll(IEnumerable<AiEventPoolEntry> entries, Func<AiEventPoolEntry, string> payloadSerializer)
    {
        EnsureInitialized();

        using SqliteConnection connection = OpenConnection();
        using SqliteTransaction transaction = connection.BeginTransaction();

        using (SqliteCommand clearCommand = connection.CreateCommand())
        {
            clearCommand.Transaction = transaction;
            clearCommand.CommandText = "DELETE FROM event_pool;";
            clearCommand.ExecuteNonQuery();
        }

        foreach (AiEventPoolEntry entry in entries)
        {
            using SqliteCommand insertCommand = connection.CreateCommand();
            insertCommand.Transaction = transaction;
            insertCommand.CommandText =
                """
                INSERT INTO event_pool (
                    entry_id, generated_at_utc, source, seed, theme, slot,
                    eng_title, zhs_title, eng_initial_description, zhs_initial_description,
                    event_key, payload_json
                )
                VALUES (
                    $entry_id, $generated_at_utc, $source, $seed, $theme, $slot,
                    $eng_title, $zhs_title, $eng_initial_description, $zhs_initial_description,
                    $event_key, $payload_json
                );
                """;
            BindEntry(insertCommand, entry, payloadSerializer(entry));
            insertCommand.ExecuteNonQuery();
        }

        transaction.Commit();
        Vacuum(connection);
    }

    private SqliteConnection OpenConnection()
    {
        SqliteConnection connection = new(_connectionString);
        connection.Open();
        return connection;
    }

    private static void BindEntry(SqliteCommand command, AiEventPoolEntry entry, string payloadJson)
    {
        command.Parameters.AddWithValue("$entry_id", entry.EntryId);
        command.Parameters.AddWithValue("$generated_at_utc", entry.GeneratedAtUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$source", entry.Source ?? string.Empty);
        command.Parameters.AddWithValue("$seed", entry.Seed ?? string.Empty);
        command.Parameters.AddWithValue("$theme", entry.Theme ?? string.Empty);
        command.Parameters.AddWithValue("$slot", (int)entry.Payload.Slot);
        command.Parameters.AddWithValue("$eng_title", entry.Payload.Eng?.Title ?? string.Empty);
        command.Parameters.AddWithValue("$zhs_title", entry.Payload.Zhs?.Title ?? string.Empty);
        command.Parameters.AddWithValue("$eng_initial_description", entry.Payload.Eng?.InitialDescription ?? string.Empty);
        command.Parameters.AddWithValue("$zhs_initial_description", entry.Payload.Zhs?.InitialDescription ?? string.Empty);
        command.Parameters.AddWithValue("$event_key", entry.Payload.EventKey ?? string.Empty);
        command.Parameters.AddWithValue("$payload_json", payloadJson);
    }

    private static string BuildInClause<T>(SqliteCommand command, IEnumerable<T> values)
    {
        List<string> parameterNames = new();
        int index = 0;
        foreach (T value in values)
        {
            string parameterName = $"$p{index++}";
            command.Parameters.AddWithValue(parameterName, value?.ToString() ?? string.Empty);
            parameterNames.Add(parameterName);
        }

        return parameterNames.Count == 0 ? "NULL" : string.Join(", ", parameterNames);
    }

    private static List<AiEventPoolEntry> ReadEntries(SqliteCommand command, Func<string, AiGeneratedEventPayload> payloadParser)
    {
        List<AiEventPoolEntry> entries = new();
        using SqliteDataReader reader = command.ExecuteReader();
        while (reader.Read())
        {
            entries.Add(ReadEntry(reader, payloadParser));
        }

        return entries;
    }

    private static AiEventPoolEntry? ReadSingleEntry(SqliteCommand command, Func<string, AiGeneratedEventPayload> payloadParser)
    {
        using SqliteDataReader reader = command.ExecuteReader();
        return reader.Read() ? ReadEntry(reader, payloadParser) : null;
    }

    private static AiEventPoolEntry ReadEntry(SqliteDataReader reader, Func<string, AiGeneratedEventPayload> payloadParser)
    {
        string payloadJson = reader.GetString(reader.GetOrdinal("payload_json"));
        AiGeneratedEventPayload payload = payloadParser(payloadJson);
        return new AiEventPoolEntry
        {
            EntryId = reader.GetString(reader.GetOrdinal("entry_id")),
            GeneratedAtUtc = DateTime.Parse(reader.GetString(reader.GetOrdinal("generated_at_utc")), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
            Source = reader.GetString(reader.GetOrdinal("source")),
            Seed = reader.GetString(reader.GetOrdinal("seed")),
            Theme = reader.GetString(reader.GetOrdinal("theme")),
            Payload = payload,
        };
    }

    private static AiEventPoolEntrySummary ReadSummary(SqliteDataReader reader)
    {
        return new AiEventPoolEntrySummary
        {
            EntryId = reader.GetString(reader.GetOrdinal("entry_id")),
            GeneratedAtUtc = DateTime.Parse(reader.GetString(reader.GetOrdinal("generated_at_utc")), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
            Source = reader.GetString(reader.GetOrdinal("source")),
            Seed = reader.GetString(reader.GetOrdinal("seed")),
            Theme = reader.GetString(reader.GetOrdinal("theme")),
            Slot = (AiEventSlot)reader.GetInt32(reader.GetOrdinal("slot")),
            EngTitle = reader.GetString(reader.GetOrdinal("eng_title")),
            ZhsTitle = reader.GetString(reader.GetOrdinal("zhs_title")),
            EngInitialDescription = reader.GetString(reader.GetOrdinal("eng_initial_description")),
            ZhsInitialDescription = reader.GetString(reader.GetOrdinal("zhs_initial_description")),
            EventKey = reader.GetString(reader.GetOrdinal("event_key")),
        };
    }

    private static void Vacuum(SqliteConnection connection)
    {
        using SqliteCommand vacuum = connection.CreateCommand();
        vacuum.CommandText = "VACUUM;";
        vacuum.ExecuteNonQuery();
    }
}
