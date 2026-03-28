using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Acts;

namespace AiEvent;

public enum AiEventMode
{
    Vanilla,
    VanillaPlusCache,
    LlmDynamic,
    LlmDebug,
}

public enum AiEventSlot
{
    Overgrowth,
    Hive,
    Glory,
    Underdocks,
    Shared,
}

public sealed class AiLocalizedEventText
{
    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("initial_description")]
    public string InitialDescription { get; set; } = string.Empty;

    [JsonPropertyName("options")]
    public List<AiLocalizedOptionText> Options { get; set; } = new();
}

public sealed class AiLocalizedOptionText
{
    [JsonPropertyName("key")]
    public string Key { get; set; } = string.Empty;

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("result_description")]
    public string ResultDescription { get; set; } = string.Empty;
}

public sealed class AiGeneratedEventPayload
{
    [JsonPropertyName("entry_id")]
    public string EntryId { get; set; } = string.Empty;

    [JsonPropertyName("slot")]
    public AiEventSlot Slot { get; set; }

    [JsonPropertyName("event_key")]
    public string EventKey { get; set; } = string.Empty;

    [JsonPropertyName("options")]
    public List<AiEventOptionPayload> Options { get; set; } = new();

    [JsonPropertyName("eng")]
    public AiLocalizedEventText Eng { get; set; } = new();

    [JsonPropertyName("zhs")]
    public AiLocalizedEventText Zhs { get; set; } = new();
}

public sealed class AiEventPoolEntry
{
    [JsonPropertyName("entry_id")]
    public string EntryId { get; set; } = string.Empty;

    [JsonPropertyName("generated_at_utc")]
    public DateTime GeneratedAtUtc { get; set; }

    [JsonPropertyName("source")]
    public string Source { get; set; } = string.Empty;

    [JsonPropertyName("seed")]
    public string Seed { get; set; } = string.Empty;

    [JsonPropertyName("theme")]
    public string Theme { get; set; } = string.Empty;

    [JsonPropertyName("payload")]
    public AiGeneratedEventPayload Payload { get; set; } = new();
}

public sealed class AiEventPoolEntrySummary
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
}

public sealed class AiEventThemePlan
{
    [JsonPropertyName("slot")]
    public AiEventSlot Slot { get; set; }

    [JsonPropertyName("theme")]
    public string Theme { get; set; } = string.Empty;

    [JsonPropertyName("option_count")]
    public int OptionCount { get; set; } = 3;

    [JsonPropertyName("reward_profile")]
    public string RewardProfile { get; set; } = "favored";
}

public sealed class AiEventOptionPayload
{
    [JsonPropertyName("key")]
    public string Key { get; set; } = string.Empty;

    [JsonPropertyName("effects")]
    public List<AiEventEffectPayload> Effects { get; set; } = new();
}

public sealed class AiEventEffectPayload
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("amount")]
    public int Amount { get; set; }

    [JsonPropertyName("count")]
    public int Count { get; set; } = 1;

    [JsonPropertyName("card_id")]
    public string CardId { get; set; } = string.Empty;

    [JsonPropertyName("relic_rarity")]
    public string RelicRarity { get; set; } = string.Empty;
}

public static class AiEventRegistry
{
    public const string EventsTableName = "events";

    private static readonly IReadOnlyDictionary<AiEventSlot, string> EventKeys = new Dictionary<AiEventSlot, string>
    {
        [AiEventSlot.Overgrowth] = "AI_OVERGROWTH_EVENT",
        [AiEventSlot.Hive] = "AI_HIVE_EVENT",
        [AiEventSlot.Glory] = "AI_GLORY_EVENT",
        [AiEventSlot.Underdocks] = "AI_UNDERDOCKS_EVENT",
        [AiEventSlot.Shared] = "AI_SHARED_EVENT",
    };

    public static IReadOnlyList<AiEventSlot> AllSlots { get; } = new[]
    {
        AiEventSlot.Overgrowth,
        AiEventSlot.Hive,
        AiEventSlot.Glory,
        AiEventSlot.Underdocks,
        AiEventSlot.Shared,
    };

    public static string GetEventKey(AiEventSlot slot)
    {
        return EventKeys[slot];
    }

    public static string GetImageFileName(AiEventSlot slot)
    {
        return GetEventKey(slot).ToLowerInvariant() + ".png";
    }

    public static string GetActName(AiEventSlot slot)
    {
        return slot switch
        {
            AiEventSlot.Overgrowth => "Overgrowth",
            AiEventSlot.Hive => "Hive",
            AiEventSlot.Glory => "Glory",
            AiEventSlot.Underdocks => "Underdocks",
            AiEventSlot.Shared => "Shared",
            _ => throw new ArgumentOutOfRangeException(nameof(slot), slot, null),
        };
    }

    public static AiEventSlot? TryGetSlotForAct(ActModel act)
    {
        return act switch
        {
            Overgrowth => AiEventSlot.Overgrowth,
            Hive => AiEventSlot.Hive,
            Glory => AiEventSlot.Glory,
            Underdocks => AiEventSlot.Underdocks,
            _ => null,
        };
    }

    public static EventModel GetModelForSlot(AiEventSlot slot)
    {
        return slot switch
        {
            AiEventSlot.Overgrowth => ModelDb.Event<AiOvergrowthEvent>(),
            AiEventSlot.Hive => ModelDb.Event<AiHiveEvent>(),
            AiEventSlot.Glory => ModelDb.Event<AiGloryEvent>(),
            AiEventSlot.Underdocks => ModelDb.Event<AiUnderdocksEvent>(),
            AiEventSlot.Shared => ModelDb.Event<AiSharedEvent>(),
            _ => throw new ArgumentOutOfRangeException(nameof(slot), slot, null),
        };
    }
}
