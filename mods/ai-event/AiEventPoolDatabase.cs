using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace AiEvent;

public sealed class AiEventPoolDatabase
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = null,
    };

    private readonly string _databaseDirectoryPath;
    private readonly string _entriesDirectoryPath;
    private readonly string _indexPath;

    private bool _loaded;
    private Dictionary<string, AiEventPoolIndexEntry> _indexEntries = new(StringComparer.OrdinalIgnoreCase);

    public AiEventPoolDatabase(string databaseDirectoryPath)
    {
        _databaseDirectoryPath = databaseDirectoryPath;
        _entriesDirectoryPath = Path.Combine(databaseDirectoryPath, "entries");
        _indexPath = Path.Combine(databaseDirectoryPath, "index.json");
    }

    public void EnsureInitialized()
    {
        Directory.CreateDirectory(_databaseDirectoryPath);
        Directory.CreateDirectory(_entriesDirectoryPath);
        EnsureLoaded();
    }

    public void Upsert(AiEventPoolEntry entry, string payloadJson)
    {
        EnsureInitialized();

        AiEventPoolIndexEntry indexEntry = AiEventPoolIndexEntry.FromPoolEntry(entry);
        WritePayload(entry.EntryId, payloadJson);
        _indexEntries[entry.EntryId] = indexEntry;
        SaveIndex();
    }

    public List<AiEventPoolEntry> QueryLatest(HashSet<AiEventSlot> allowedSlots, int limit, Func<string, AiGeneratedEventPayload> payloadParser)
    {
        EnsureInitialized();
        if (allowedSlots.Count == 0 || limit <= 0)
        {
            return new List<AiEventPoolEntry>();
        }

        return _indexEntries.Values
            .Where(entry => allowedSlots.Contains(entry.Slot))
            .OrderByDescending(entry => entry.GeneratedAtUtc)
            .Take(limit)
            .Select(entry => Materialize(entry, payloadParser))
            .ToList();
    }

    public List<AiEventPoolEntry> QueryBySeed(string seed, string? source, Func<string, AiGeneratedEventPayload> payloadParser)
    {
        EnsureInitialized();

        IEnumerable<AiEventPoolIndexEntry> query = _indexEntries.Values
            .Where(entry => string.Equals(entry.Seed, seed, StringComparison.Ordinal));
        if (!string.IsNullOrWhiteSpace(source))
        {
            query = query.Where(entry => string.Equals(entry.Source, source, StringComparison.OrdinalIgnoreCase));
        }

        return query
            .OrderByDescending(entry => entry.GeneratedAtUtc)
            .Select(entry => Materialize(entry, payloadParser))
            .ToList();
    }

    public List<AiEventPoolEntry> QueryByIds(HashSet<string> entryIds, Func<string, AiGeneratedEventPayload> payloadParser)
    {
        EnsureInitialized();
        if (entryIds.Count == 0)
        {
            return new List<AiEventPoolEntry>();
        }

        return _indexEntries.Values
            .Where(entry => entryIds.Contains(entry.EntryId))
            .Select(entry => Materialize(entry, payloadParser))
            .ToList();
    }

    public List<AiEventPoolEntry> QueryAll(Func<string, AiGeneratedEventPayload> payloadParser)
    {
        EnsureInitialized();

        return _indexEntries.Values
            .OrderByDescending(entry => entry.GeneratedAtUtc)
            .Select(entry => Materialize(entry, payloadParser))
            .ToList();
    }

    public List<AiEventPoolEntrySummary> QueryAllSummaries()
    {
        EnsureInitialized();

        return _indexEntries.Values
            .OrderByDescending(entry => entry.GeneratedAtUtc)
            .Select(entry => entry.ToSummary())
            .ToList();
    }

    public AiEventPoolEntry? QueryEntryById(string entryId, Func<string, AiGeneratedEventPayload> payloadParser)
    {
        EnsureInitialized();
        if (string.IsNullOrWhiteSpace(entryId))
        {
            return null;
        }

        return !_indexEntries.TryGetValue(entryId, out AiEventPoolIndexEntry? entry)
            ? null
            : Materialize(entry, payloadParser);
    }

    public void Delete(string entryId)
    {
        EnsureInitialized();
        _indexEntries.Remove(entryId);

        string payloadPath = GetPayloadPath(entryId);
        if (File.Exists(payloadPath))
        {
            File.Delete(payloadPath);
        }

        SaveIndex();
    }

    public int PromoteSeedDynamicEntriesToCache(string seed)
    {
        EnsureInitialized();

        int changed = 0;
        foreach (AiEventPoolIndexEntry entry in _indexEntries.Values)
        {
            if (!string.Equals(entry.Seed, seed, StringComparison.Ordinal))
            {
                continue;
            }

            if (!string.Equals(entry.Source, "llm_dynamic", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            entry.Source = "llm_cache";
            changed++;
        }

        if (changed > 0)
        {
            SaveIndex();
        }

        return changed;
    }

    public int PromoteInactiveDynamicEntriesToCache(string? activeSeed)
    {
        EnsureInitialized();

        int changed = 0;
        foreach (AiEventPoolIndexEntry entry in _indexEntries.Values)
        {
            if (!string.Equals(entry.Source, "llm_dynamic", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(activeSeed) && string.Equals(entry.Seed, activeSeed, StringComparison.Ordinal))
            {
                continue;
            }

            entry.Source = "llm_cache";
            changed++;
        }

        if (changed > 0)
        {
            SaveIndex();
        }

        return changed;
    }

    public bool HasAnyEntries()
    {
        EnsureInitialized();
        return _indexEntries.Count > 0;
    }

    public void ReplaceAll(IEnumerable<AiEventPoolEntry> entries, Func<AiEventPoolEntry, string> payloadSerializer)
    {
        EnsureInitialized();

        if (Directory.Exists(_entriesDirectoryPath))
        {
            Directory.Delete(_entriesDirectoryPath, recursive: true);
        }

        Directory.CreateDirectory(_entriesDirectoryPath);
        _indexEntries = new Dictionary<string, AiEventPoolIndexEntry>(StringComparer.OrdinalIgnoreCase);

        foreach (AiEventPoolEntry entry in entries)
        {
            AiEventPoolIndexEntry indexEntry = AiEventPoolIndexEntry.FromPoolEntry(entry);
            WritePayload(entry.EntryId, payloadSerializer(entry));
            _indexEntries[entry.EntryId] = indexEntry;
        }

        SaveIndex();
    }

    private void EnsureLoaded()
    {
        if (_loaded)
        {
            return;
        }

        _loaded = true;
        if (!File.Exists(_indexPath))
        {
            _indexEntries = new Dictionary<string, AiEventPoolIndexEntry>(StringComparer.OrdinalIgnoreCase);
            SaveIndex();
            return;
        }

        string json = File.ReadAllText(_indexPath);
        List<AiEventPoolIndexEntry>? entries = JsonSerializer.Deserialize<List<AiEventPoolIndexEntry>>(json, JsonOptions);
        _indexEntries = (entries ?? new List<AiEventPoolIndexEntry>())
            .Where(entry => !string.IsNullOrWhiteSpace(entry.EntryId))
            .ToDictionary(entry => entry.EntryId, StringComparer.OrdinalIgnoreCase);
    }

    private void SaveIndex()
    {
        List<AiEventPoolIndexEntry> orderedEntries = _indexEntries.Values
            .OrderByDescending(entry => entry.GeneratedAtUtc)
            .ToList();
        File.WriteAllText(_indexPath, JsonSerializer.Serialize(orderedEntries, JsonOptions));
    }

    private void WritePayload(string entryId, string payloadJson)
    {
        File.WriteAllText(GetPayloadPath(entryId), payloadJson);
    }

    private string ReadPayload(string entryId)
    {
        return File.ReadAllText(GetPayloadPath(entryId));
    }

    private string GetPayloadPath(string entryId)
    {
        return Path.Combine(_entriesDirectoryPath, $"{entryId}.json");
    }

    private AiEventPoolEntry Materialize(AiEventPoolIndexEntry entry, Func<string, AiGeneratedEventPayload> payloadParser)
    {
        AiGeneratedEventPayload payload = payloadParser(ReadPayload(entry.EntryId));
        return new AiEventPoolEntry
        {
            EntryId = entry.EntryId,
            GeneratedAtUtc = entry.GeneratedAtUtc,
            Source = entry.Source,
            Seed = entry.Seed,
            Theme = entry.Theme,
            Payload = payload,
        };
    }

    private sealed class AiEventPoolIndexEntry
    {
        public string EntryId { get; set; } = string.Empty;

        public DateTime GeneratedAtUtc { get; set; }

        public string Source { get; set; } = string.Empty;

        public string Seed { get; set; } = string.Empty;

        public string Theme { get; set; } = string.Empty;

        public AiEventSlot Slot { get; set; }

        public string EngTitle { get; set; } = string.Empty;

        public string ZhsTitle { get; set; } = string.Empty;

        public string EngInitialDescription { get; set; } = string.Empty;

        public string ZhsInitialDescription { get; set; } = string.Empty;

        public string EventKey { get; set; } = string.Empty;

        public static AiEventPoolIndexEntry FromPoolEntry(AiEventPoolEntry entry)
        {
            return new AiEventPoolIndexEntry
            {
                EntryId = entry.EntryId,
                GeneratedAtUtc = entry.GeneratedAtUtc,
                Source = entry.Source,
                Seed = entry.Seed,
                Theme = entry.Theme,
                Slot = entry.Payload.Slot,
                EngTitle = entry.Payload.Eng?.Title ?? string.Empty,
                ZhsTitle = entry.Payload.Zhs?.Title ?? string.Empty,
                EngInitialDescription = entry.Payload.Eng?.InitialDescription ?? string.Empty,
                ZhsInitialDescription = entry.Payload.Zhs?.InitialDescription ?? string.Empty,
                EventKey = entry.Payload.EventKey ?? string.Empty,
            };
        }

        public AiEventPoolEntrySummary ToSummary()
        {
            return new AiEventPoolEntrySummary
            {
                EntryId = EntryId,
                GeneratedAtUtc = GeneratedAtUtc,
                Source = Source,
                Seed = Seed,
                Theme = Theme,
                Slot = Slot,
                EngTitle = EngTitle,
                ZhsTitle = ZhsTitle,
                EngInitialDescription = EngInitialDescription,
                ZhsInitialDescription = ZhsInitialDescription,
                EventKey = EventKey,
            };
        }
    }
}
