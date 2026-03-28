using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace AiEvent;

public static class AiEventRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private static readonly object SyncRoot = new();
    private static bool _hasRewrittenReadableJson;
    private static AiEventPoolDatabase? _poolDatabase;

    private static Dictionary<AiEventSlot, AiGeneratedEventPayload> _activePayloads = new();

    public static string ActiveCachePath => AiEventStorage.GetActiveCachePath();

    public static string PoolPath => AiEventStorage.GetPoolPath();

    public static string PoolDatabasePath => AiEventStorage.GetPoolDatabasePath();

    public static string HistoryDirectoryPath => AiEventStorage.GetHistoryDirectoryPath();

    private static AiEventPoolDatabase PoolDatabase => _poolDatabase ??= new AiEventPoolDatabase(AiEventStorage.GetPoolDatabasePath());

    public static void Initialize()
    {
        lock (SyncRoot)
        {
            AiEventStorage.MigrateLegacyFiles(AiEventConfigService.GetModDirectory());
            PoolDatabase.EnsureInitialized();
            LoadActiveCache();
            PromoteInactiveDynamicEntriesToCacheInternal(GetActiveRunSeedFromSessionState());
            NormalizeActivePayloads();
            EnsureAllSlots();
            RewriteReadableJsonIfNeeded();
        }
    }

    public static AiGeneratedEventPayload Get(AiEventSlot slot)
    {
        lock (SyncRoot)
        {
            if (!_activePayloads.TryGetValue(slot, out AiGeneratedEventPayload? payload))
            {
                payload = AiEventFallbacks.Create(slot);
                _activePayloads[slot] = payload;
            }

            return ClonePayload(payload);
        }
    }

    public static IReadOnlyDictionary<AiEventSlot, AiGeneratedEventPayload> GetAll()
    {
        lock (SyncRoot)
        {
            EnsureAllSlots();
            return _activePayloads.ToDictionary(pair => pair.Key, pair => ClonePayload(pair.Value));
        }
    }

    public static void SetActive(AiGeneratedEventPayload payload)
    {
        lock (SyncRoot)
        {
            payload = ClonePayload(payload);
            payload.EventKey = AiEventRegistry.GetEventKey(payload.Slot);
            _activePayloads[payload.Slot] = payload;
            SaveActiveCacheInternal();
        }
    }

    public static void ResetActiveToFallbacks()
    {
        lock (SyncRoot)
        {
            _activePayloads = AiEventRegistry.AllSlots.ToDictionary(slot => slot, AiEventFallbacks.Create);
            SaveActiveCacheInternal();
        }
    }

    public static AiEventPoolEntry CreatePoolEntry(AiGeneratedEventPayload payload, string source, string? seed = null)
    {
        AiGeneratedEventPayload clonedPayload = ClonePayload(payload);
        if (string.IsNullOrWhiteSpace(clonedPayload.EntryId))
        {
            clonedPayload.EntryId = Guid.NewGuid().ToString("N");
        }

        return new AiEventPoolEntry
        {
            EntryId = clonedPayload.EntryId,
            GeneratedAtUtc = DateTime.UtcNow,
            Source = source,
            Seed = seed ?? string.Empty,
            Theme = string.Empty,
            Payload = clonedPayload,
        };
    }

    public static void AddPoolEntry(AiEventPoolEntry entry)
    {
        lock (SyncRoot)
        {
            AiEventPoolEntry cloned = NormalizePoolEntry(CloneEntry(entry));
            PoolDatabase.Upsert(cloned, SerializePayload(cloned.Payload));
        }
    }

    public static IReadOnlyList<AiEventPoolEntry> GetLatestPoolEntries(IReadOnlyCollection<AiEventSlot> allowedSlots, int limit)
    {
        lock (SyncRoot)
        {
            return PoolDatabase.QueryLatest(allowedSlots.ToHashSet(), Math.Max(0, limit), DeserializePayload)
                .Select(CloneEntry)
                .ToList();
        }
    }

    public static IReadOnlyList<AiEventPoolEntry> GetPoolEntriesForSeed(string seed, string? source = null)
    {
        lock (SyncRoot)
        {
            return PoolDatabase.QueryBySeed(seed, string.IsNullOrWhiteSpace(source) ? null : source, DeserializePayload)
                .Select(CloneEntry)
                .ToList();
        }
    }

    public static IReadOnlyList<AiEventPoolEntry> GetPoolEntriesByIds(IEnumerable<string> entryIds)
    {
        lock (SyncRoot)
        {
            HashSet<string> idSet = entryIds
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            return PoolDatabase.QueryByIds(idSet, DeserializePayload)
                .Select(CloneEntry)
                .ToList();
        }
    }

    public static IReadOnlyList<AiEventPoolEntry> GetAllPoolEntries()
    {
        lock (SyncRoot)
        {
            return PoolDatabase.QueryAll(DeserializePayload)
                .Select(CloneEntry)
                .ToList();
        }
    }

    public static IReadOnlyList<AiEventPoolEntrySummary> GetAllPoolEntrySummaries()
    {
        lock (SyncRoot)
        {
            return PoolDatabase.QueryAllSummaries()
                .Select(CloneSummary)
                .ToList();
        }
    }

    public static int GetPoolEntrySummaryCount()
    {
        lock (SyncRoot)
        {
            return PoolDatabase.GetSummaryCount();
        }
    }

    public static IReadOnlyList<AiEventPoolEntrySummary> GetPoolEntrySummariesPage(int pageIndex, int pageSize)
    {
        lock (SyncRoot)
        {
            int safePageIndex = Math.Max(0, pageIndex);
            int safePageSize = Math.Max(1, pageSize);
            return PoolDatabase.QuerySummariesPage(safePageIndex * safePageSize, safePageSize)
                .Select(CloneSummary)
                .ToList();
        }
    }

    public static AiEventPoolEntry? GetPoolEntryById(string entryId)
    {
        lock (SyncRoot)
        {
            AiEventPoolEntry? entry = PoolDatabase.QueryEntryById(entryId, DeserializePayload);
            return entry == null ? null : CloneEntry(entry);
        }
    }

    public static void DeletePoolEntry(string entryId)
    {
        lock (SyncRoot)
        {
            PoolDatabase.Delete(entryId);
        }
    }

    public static void ClearPoolEntries()
    {
        lock (SyncRoot)
        {
            PoolDatabase.Clear();
            TryDeleteLegacyPoolArtifacts();
        }
    }

    public static int PromoteSeedDynamicEntriesToCache(string seed)
    {
        if (string.IsNullOrWhiteSpace(seed))
        {
            return 0;
        }

        lock (SyncRoot)
        {
            return PoolDatabase.PromoteSeedDynamicEntriesToCache(seed);
        }
    }

    public static string SerializePoolEntry(AiEventPoolEntry entry)
    {
        return JsonSerializer.Serialize(entry, JsonOptions);
    }

    public static AiEventPoolEntry DeserializePoolEntry(string json)
    {
        AiEventPoolEntry entry = JsonSerializer.Deserialize<AiEventPoolEntry>(json, JsonOptions) ?? new AiEventPoolEntry();
        return NormalizePoolEntry(entry);
    }

    public static void SaveHistorySnapshot(string? seed, string source, IEnumerable<AiGeneratedEventPayload> events)
    {
        try
        {
            AiEventStorage.EnsureDataDirectories();
            Directory.CreateDirectory(HistoryDirectoryPath);

            AiEventGenerationSnapshot snapshot = new()
            {
                GeneratedAtUtc = DateTime.UtcNow,
                Seed = seed ?? string.Empty,
                Source = source,
                Events = events.Select(ClonePayload).ToList(),
            };

            string safeSeed = string.IsNullOrWhiteSpace(seed) ? "no-seed" : string.Concat(seed.Where(ch => char.IsLetterOrDigit(ch) || ch == '-' || ch == '_'));
            string fileName = $"{snapshot.GeneratedAtUtc:yyyyMMdd-HHmmssfff}_{safeSeed}.json";
            string fullPath = Path.Combine(HistoryDirectoryPath, fileName);
            File.WriteAllText(fullPath, JsonSerializer.Serialize(snapshot, JsonOptions));
        }
        catch (Exception ex)
        {
            MainFile.Logger.Error($"Failed to save ai-event generation history: {ex}");
        }
    }

    private static string? GetActiveRunSeedFromSessionState()
    {
        try
        {
            foreach (string sessionStatePath in AiEventStorage.GetAllSessionStatePaths())
            {
                if (!File.Exists(sessionStatePath))
                {
                    continue;
                }

                JsonNode? node = JsonNode.Parse(File.ReadAllText(sessionStatePath));
                string? seed = node?["Seed"]?.GetValue<string>();
                if (!string.IsNullOrWhiteSpace(seed))
                {
                    return seed;
                }
            }

            return null;
        }
        catch (Exception ex)
        {
            MainFile.Logger.Error($"Failed to inspect ai-event run session state for stale dynamic cleanup: {ex}");
            return null;
        }
    }

    private static void PromoteInactiveDynamicEntriesToCacheInternal(string? activeSeed)
    {
        PoolDatabase.PromoteInactiveDynamicEntriesToCache(activeSeed);
    }

    private static void LoadActiveCache()
    {
        try
        {
            if (!File.Exists(ActiveCachePath))
            {
                _activePayloads = new Dictionary<AiEventSlot, AiGeneratedEventPayload>();
                return;
            }

            string json = File.ReadAllText(ActiveCachePath);
            List<AiGeneratedEventPayload>? payloads = JsonSerializer.Deserialize<List<AiGeneratedEventPayload>>(json, JsonOptions);
            _activePayloads = payloads?.ToDictionary(p => p.Slot) ?? new Dictionary<AiEventSlot, AiGeneratedEventPayload>();
        }
        catch (Exception ex)
        {
            MainFile.Logger.Error($"Failed to load ai-event active cache: {ex}");
            _activePayloads = new Dictionary<AiEventSlot, AiGeneratedEventPayload>();
        }
    }

    private static void TryDeleteLegacyPoolArtifacts()
    {
        try
        {
            AiEventStorage.DeleteFileIfExists(PoolPath);
        }
        catch (Exception ex)
        {
            MainFile.Logger.Error($"Failed to delete legacy ai-event pool json while clearing cache: {ex}");
        }
    }

    private static void NormalizeActivePayloads()
    {
        Dictionary<AiEventSlot, AiGeneratedEventPayload> normalized = new();

        foreach (AiEventSlot slot in AiEventRegistry.AllSlots)
        {
            AiGeneratedEventPayload fallback = AiEventFallbacks.Create(slot);

            if (!_activePayloads.TryGetValue(slot, out AiGeneratedEventPayload? payload))
            {
                normalized[slot] = fallback;
                continue;
            }

            normalized[slot] = NormalizePayload(payload, slot, fallback);
        }

        _activePayloads = normalized;
    }

    private static AiEventPoolEntry NormalizePoolEntry(AiEventPoolEntry entry)
    {
        if (entry.Payload == null)
        {
            entry.Payload = AiEventFallbacks.Create(AiEventSlot.Shared);
        }

        AiEventSlot slot = entry.Payload.Slot;
        AiGeneratedEventPayload fallback = AiEventFallbacks.Create(slot);
        AiGeneratedEventPayload payload = NormalizePayload(entry.Payload, slot, fallback);
        if (string.IsNullOrWhiteSpace(payload.EntryId))
        {
            payload.EntryId = string.IsNullOrWhiteSpace(entry.EntryId) ? Guid.NewGuid().ToString("N") : entry.EntryId;
        }

        return new AiEventPoolEntry
        {
            EntryId = string.IsNullOrWhiteSpace(entry.EntryId) ? payload.EntryId : entry.EntryId,
            GeneratedAtUtc = entry.GeneratedAtUtc == default ? DateTime.UtcNow : entry.GeneratedAtUtc,
            Source = string.IsNullOrWhiteSpace(entry.Source) ? "unknown" : entry.Source,
            Seed = entry.Seed ?? string.Empty,
            Theme = entry.Theme ?? string.Empty,
            Payload = payload,
        };
    }

    private static AiGeneratedEventPayload NormalizePayload(AiGeneratedEventPayload payload, AiEventSlot slot, AiGeneratedEventPayload fallback)
    {
        payload.Slot = slot;
        payload.EventKey = AiEventRegistry.GetEventKey(slot);
        payload.Eng ??= fallback.Eng;
        payload.Zhs ??= fallback.Zhs;
        payload.Options ??= fallback.Options;
        payload.Eng.Options ??= fallback.Eng.Options;
        payload.Zhs.Options ??= fallback.Zhs.Options;
        payload.EntryId = string.IsNullOrWhiteSpace(payload.EntryId) ? Guid.NewGuid().ToString("N") : payload.EntryId;
        return payload;
    }

    private static void EnsureAllSlots()
    {
        foreach (AiEventSlot slot in AiEventRegistry.AllSlots)
        {
            if (!_activePayloads.ContainsKey(slot))
            {
                _activePayloads[slot] = AiEventFallbacks.Create(slot);
            }
        }
    }

    private static void SaveActiveCacheInternal()
    {
        EnsureAllSlots();
        AiEventStorage.EnsureDirectoryForFile(ActiveCachePath);
        File.WriteAllText(ActiveCachePath, JsonSerializer.Serialize(_activePayloads.Values.OrderBy(p => p.Slot), JsonOptions));
    }

    private static void RewriteReadableJsonIfNeeded()
    {
        if (_hasRewrittenReadableJson)
        {
            return;
        }

        _hasRewrittenReadableJson = true;

        try
        {
            SaveActiveCacheInternal();
            RewriteHistorySnapshots();
        }
        catch (Exception ex)
        {
            MainFile.Logger.Error($"Failed to rewrite ai-event cache/history with readable unicode: {ex}");
        }
    }

    private static void RewriteHistorySnapshots()
    {
        if (!Directory.Exists(HistoryDirectoryPath))
        {
            return;
        }

        foreach (string path in Directory.GetFiles(HistoryDirectoryPath, "*.json"))
        {
            try
            {
                string json = File.ReadAllText(path);
                AiEventGenerationSnapshot? snapshot = JsonSerializer.Deserialize<AiEventGenerationSnapshot>(json, JsonOptions);
                if (snapshot == null)
                {
                    continue;
                }

                File.WriteAllText(path, JsonSerializer.Serialize(snapshot, JsonOptions));
            }
            catch (Exception ex)
            {
                MainFile.Logger.Error($"Failed to rewrite ai-event history snapshot {path}: {ex}");
            }
        }
    }

    private static string SerializePayload(AiGeneratedEventPayload payload)
    {
        return JsonSerializer.Serialize(payload, JsonOptions);
    }

    private static AiGeneratedEventPayload DeserializePayload(string json)
    {
        return JsonSerializer.Deserialize<AiGeneratedEventPayload>(json, JsonOptions) ?? new AiGeneratedEventPayload();
    }

    private static AiGeneratedEventPayload ClonePayload(AiGeneratedEventPayload payload)
    {
        string json = JsonSerializer.Serialize(payload, JsonOptions);
        return JsonSerializer.Deserialize<AiGeneratedEventPayload>(json, JsonOptions) ?? new AiGeneratedEventPayload();
    }

    private static AiEventPoolEntry CloneEntry(AiEventPoolEntry entry)
    {
        string json = JsonSerializer.Serialize(entry, JsonOptions);
        return JsonSerializer.Deserialize<AiEventPoolEntry>(json, JsonOptions) ?? new AiEventPoolEntry();
    }

    private static AiEventPoolEntrySummary CloneSummary(AiEventPoolEntrySummary summary)
    {
        string json = JsonSerializer.Serialize(summary, JsonOptions);
        return JsonSerializer.Deserialize<AiEventPoolEntrySummary>(json, JsonOptions) ?? new AiEventPoolEntrySummary();
    }
}

public sealed class AiEventGenerationSnapshot
{
    public DateTime GeneratedAtUtc { get; set; }

    public string Seed { get; set; } = string.Empty;

    public string Source { get; set; } = string.Empty;

    public List<AiGeneratedEventPayload> Events { get; set; } = new();
}
