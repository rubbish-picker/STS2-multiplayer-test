using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Merchant;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Rewards;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Saves.Runs;

namespace CocoRelics;

public sealed class CocoObservedRunIdentity
{
    [JsonPropertyName("seed")]
    public string Seed { get; set; } = string.Empty;

    [JsonPropertyName("ascension")]
    public int Ascension { get; set; }

    [JsonPropertyName("current_act_index")]
    public int CurrentActIndex { get; set; }

    [JsonPropertyName("player_net_ids")]
    public List<ulong> PlayerNetIds { get; set; } = new();
}

public sealed class CocoObservedRunSnapshot
{
    [JsonPropertyName("run")]
    public SerializableRun Run { get; set; } = new();
}

public sealed class CocoObservedMerchantCardEntry
{
    [JsonPropertyName("card")]
    public SerializableCard Card { get; set; } = new();

    [JsonPropertyName("cost")]
    public int Cost { get; set; }

    [JsonPropertyName("is_on_sale")]
    public bool IsOnSale { get; set; }
}

public sealed class CocoObservedMerchantRelicEntry
{
    [JsonPropertyName("relic")]
    public SerializableRelic Relic { get; set; } = new();

    [JsonPropertyName("cost")]
    public int Cost { get; set; }
}

public sealed class CocoObservedMerchantPotionEntry
{
    [JsonPropertyName("potion")]
    public SerializablePotion Potion { get; set; } = new();

    [JsonPropertyName("cost")]
    public int Cost { get; set; }
}

public sealed class CocoObservedMerchantInventory
{
    [JsonPropertyName("character_cards")]
    public List<CocoObservedMerchantCardEntry> CharacterCards { get; set; } = new();

    [JsonPropertyName("colorless_cards")]
    public List<CocoObservedMerchantCardEntry> ColorlessCards { get; set; } = new();

    [JsonPropertyName("relics")]
    public List<CocoObservedMerchantRelicEntry> Relics { get; set; } = new();

    [JsonPropertyName("potions")]
    public List<CocoObservedMerchantPotionEntry> Potions { get; set; } = new();

    [JsonPropertyName("card_removal_used")]
    public bool CardRemovalUsed { get; set; }

    [JsonPropertyName("card_removal_cost")]
    public int CardRemovalCost { get; set; }
}

public sealed class CocoObservedTreasurePreviewSave
{
    [JsonPropertyName("gold_amount")]
    public int GoldAmount { get; set; }

    [JsonPropertyName("relic_ids")]
    public List<ModelId> RelicIds { get; set; } = new();

    [JsonPropertyName("extra_rewards")]
    public List<SerializableReward> ExtraRewards { get; set; } = new();
}

public sealed class CocoObservedRoomInfoSave
{
    [JsonPropertyName("act_index")]
    public int ActIndex { get; set; }

    [JsonPropertyName("coord")]
    public MapCoord Coord { get; set; }

    [JsonPropertyName("room_type")]
    public RoomType RoomType { get; set; }

    [JsonPropertyName("model_id")]
    public ModelId? ModelId { get; set; }

    [JsonPropertyName("shop_inventory")]
    public CocoObservedMerchantInventory? ShopInventory { get; set; }

    [JsonPropertyName("treasure_preview")]
    public CocoObservedTreasurePreviewSave? TreasurePreview { get; set; }

    [JsonPropertyName("snapshot_after_point")]
    public CocoObservedRunSnapshot? SnapshotAfterPoint { get; set; }
}

public sealed class CocoObservedSessionSave
{
    [JsonPropertyName("schema_version")]
    public int SchemaVersion { get; set; } = 1;

    [JsonPropertyName("identity")]
    public CocoObservedRunIdentity Identity { get; set; } = new();

    [JsonPropertyName("observed_rooms")]
    public List<CocoObservedRoomInfoSave> ObservedRooms { get; set; } = new();
}

public static class CocoRelicsStorage
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = null,
    };

    public static string GetSessionStatePath(bool isMultiplayer)
    {
        string fileName = isMultiplayer
            ? "coco_relics.current_run_mp.session.json"
            : "coco_relics.current_run.session.json";

        return ToFileSystemPath(SaveManager.Instance.GetProfileScopedPath(Path.Combine(UserDataPathProvider.SavesDir, fileName)));
    }

    public static string GetCurrentSessionStatePath()
    {
        bool isMultiplayer = RunManager.Instance.NetService?.Type.IsMultiplayer() ?? false;
        return GetSessionStatePath(isMultiplayer);
    }

    public static CocoObservedRunIdentity BuildIdentity(RunState runState)
    {
        return new CocoObservedRunIdentity
        {
            Seed = runState.Rng.StringSeed,
            Ascension = runState.AscensionLevel,
            CurrentActIndex = runState.CurrentActIndex,
            PlayerNetIds = runState.Players.Select(static player => player.NetId).OrderBy(static id => id).ToList(),
        };
    }

    public static bool MatchesCurrentRun(CocoObservedRunIdentity? identity, RunState runState)
    {
        if (identity == null)
        {
            return false;
        }

        CocoObservedRunIdentity current = BuildIdentity(runState);
        return identity.Seed == current.Seed
            && identity.Ascension == current.Ascension
            && identity.CurrentActIndex == current.CurrentActIndex
            && identity.PlayerNetIds.SequenceEqual(current.PlayerNetIds);
    }

    public static CocoObservedSessionSave? LoadCurrentSession(RunState runState)
    {
        string path = GetCurrentSessionStatePath();
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            CocoObservedSessionSave? loaded = JsonSerializer.Deserialize<CocoObservedSessionSave>(File.ReadAllText(path), JsonOptions);
            if (loaded == null)
            {
                return null;
            }

            if (MatchesCurrentRun(loaded.Identity, runState))
            {
                return loaded;
            }

            DeleteFileIfExists(path);
        }
        catch (Exception ex)
        {
            MainFile.Logger.Error($"[CocoRelics] failed to load observed-room session {path}: {ex}");
        }

        return null;
    }

    public static void SaveCurrentSession(RunState runState, IReadOnlyDictionary<ObservedRoomKey, ObservedRoomInfo> observedRooms)
    {
        try
        {
            string path = GetCurrentSessionStatePath();
            EnsureDirectoryForFile(path);
            CocoObservedSessionSave save = new()
            {
                Identity = BuildIdentity(runState),
                ObservedRooms = observedRooms.Select(static pair => ToSave(pair.Key, pair.Value)).ToList(),
            };

            File.WriteAllText(path, JsonSerializer.Serialize(save, JsonOptions));
        }
        catch (Exception ex)
        {
            MainFile.Logger.Error($"[CocoRelics] failed to save observed-room session: {ex}");
        }
    }

    public static void ClearCurrentSession()
    {
        DeleteFileIfExists(GetCurrentSessionStatePath());
    }

    public static ObservedRoomInfo FromSave(CocoObservedRoomInfoSave save, RunState runState)
    {
        if (!IsRoomSaveValid(save))
        {
            throw new InvalidOperationException($"Observed room cache contains missing model references for coord {save.Coord}.");
        }

        return new ObservedRoomInfo
        {
            RoomType = save.RoomType,
            ModelId = save.ModelId,
            ShopInventory = save.ShopInventory == null ? null : RestoreMerchantInventory(save.ShopInventory, runState),
            TreasurePreview = save.TreasurePreview == null ? null : new ObservedTreasurePreview
            {
                GoldAmount = save.TreasurePreview.GoldAmount,
                RelicIds = save.TreasurePreview.RelicIds,
                ExtraRewards = save.TreasurePreview.ExtraRewards
                    .Select(serialized => Reward.FromSerializable(serialized, LocalPlayer(runState)))
                    .ToList(),
            },
            SnapshotAfterPoint = save.SnapshotAfterPoint,
        };
    }

    public static bool TryFromSave(CocoObservedRoomInfoSave save, RunState runState, out ObservedRoomInfo? info)
    {
        info = null;
        try
        {
            if (!IsRoomSaveValid(save))
            {
                MainFile.Logger.Warn($"[CocoRelics] discarded observed-room cache at {save.Coord} because one or more referenced models are missing.");
                return false;
            }

            info = FromSave(save, runState);
            return true;
        }
        catch (Exception ex)
        {
            MainFile.Logger.Warn($"[CocoRelics] discarded observed-room cache at {save.Coord} because it could not be restored: {ex.Message}");
            return false;
        }
    }

    public static CocoObservedRoomInfoSave CreateRoomSave(ObservedRoomKey key, ObservedRoomInfo info)
    {
        return ToSave(key, info);
    }

    public static RunState RestoreSnapshot(CocoObservedRunSnapshot snapshot, RunState liveRunState)
    {
        RunState restored = RunState.FromSerializable(snapshot.Run);
        restored.Map = liveRunState.Map;
        return restored;
    }

    public static CocoObservedRunSnapshot CaptureSnapshot(RunState runState)
    {
        return new CocoObservedRunSnapshot
        {
            Run = CreateSerializableRun(runState),
        };
    }

    private static CocoObservedRoomInfoSave ToSave(ObservedRoomKey key, ObservedRoomInfo info)
    {
        return new CocoObservedRoomInfoSave
        {
            ActIndex = key.ActIndex,
            Coord = key.Coord,
            RoomType = info.RoomType,
            ModelId = info.ModelId,
            ShopInventory = info.ShopInventory == null ? null : CaptureMerchantInventory(info.ShopInventory),
            TreasurePreview = info.TreasurePreview == null ? null : new CocoObservedTreasurePreviewSave
            {
                GoldAmount = info.TreasurePreview.GoldAmount,
                RelicIds = info.TreasurePreview.RelicIds.ToList(),
                ExtraRewards = info.TreasurePreview.ExtraRewards.Select(static reward => reward.ToSerializable()).ToList(),
            },
            SnapshotAfterPoint = info.SnapshotAfterPoint,
        };
    }

    private static bool IsRoomSaveValid(CocoObservedRoomInfoSave save)
    {
        if (save.ModelId != null)
        {
            bool primaryModelExists = save.RoomType switch
            {
                RoomType.Monster or RoomType.Elite or RoomType.Boss => ModelDb.GetByIdOrNull<EncounterModel>(save.ModelId) != null,
                RoomType.Event => ModelDb.GetByIdOrNull<EventModel>(save.ModelId) != null,
                _ => true,
            };

            if (!primaryModelExists)
            {
                return false;
            }
        }

        if (save.TreasurePreview != null && save.TreasurePreview.RelicIds.Any(id => ModelDb.GetByIdOrNull<RelicModel>(id) == null))
        {
            return false;
        }

        return true;
    }

    private static SerializableRun CreateSerializableRun(RunState runState)
    {
        return new SerializableRun
        {
            SchemaVersion = SaveManager.Instance.GetLatestSchemaVersion<SerializableRun>(),
            Acts = runState.Acts.Select(static act => act.ToSave()).ToList(),
            Modifiers = runState.Modifiers.Select(static modifier => modifier.ToSerializable()).ToList(),
            CurrentActIndex = runState.CurrentActIndex,
            EventsSeen = runState.VisitedEventIds.ToList(),
            SerializableOdds = runState.Odds.ToSerializable(),
            SerializableSharedRelicGrabBag = runState.SharedRelicGrabBag.ToSerializable(),
            Players = runState.Players.Select(static player => player.ToSerializable()).ToList(),
            SerializableRng = runState.Rng.ToSerializable(),
            VisitedMapCoords = runState.VisitedMapCoords.ToList(),
            MapPointHistory = runState.MapPointHistory.Select(static history => history.ToList()).ToList(),
            SaveTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            StartTime = 0L,
            RunTime = 0L,
            WinTime = 0L,
            Ascension = runState.AscensionLevel,
            PlatformType = RunManager.Instance.NetService?.Platform ?? default,
            ExtraFields = runState.ExtraFields.ToSerializable(),
        };
    }

    private static CocoObservedMerchantInventory CaptureMerchantInventory(MerchantInventory inventory)
    {
        return new CocoObservedMerchantInventory
        {
            CharacterCards = inventory.CharacterCardEntries
                .Where(static entry => entry.CreationResult != null)
                .Select(static entry => new CocoObservedMerchantCardEntry
                {
                    Card = entry.CreationResult!.Card.ToSerializable(),
                    Cost = entry.Cost,
                    IsOnSale = entry.IsOnSale,
                })
                .ToList(),
            ColorlessCards = inventory.ColorlessCardEntries
                .Where(static entry => entry.CreationResult != null)
                .Select(static entry => new CocoObservedMerchantCardEntry
                {
                    Card = entry.CreationResult!.Card.ToSerializable(),
                    Cost = entry.Cost,
                    IsOnSale = entry.IsOnSale,
                })
                .ToList(),
            Relics = inventory.RelicEntries
                .Where(static entry => entry.Model != null)
                .Select(static entry => new CocoObservedMerchantRelicEntry
                {
                    Relic = entry.Model!.ToSerializable(),
                    Cost = entry.Cost,
                })
                .ToList(),
            Potions = inventory.PotionEntries
                .Where(static entry => entry.Model != null)
                .Select((entry, index) => new CocoObservedMerchantPotionEntry
                {
                    Potion = entry.Model!.ToSerializable(index),
                    Cost = entry.Cost,
                })
                .ToList(),
            CardRemovalUsed = inventory.CardRemovalEntry?.Used ?? false,
            CardRemovalCost = inventory.CardRemovalEntry?.Cost ?? 0,
        };
    }

    private static MerchantInventory RestoreMerchantInventory(CocoObservedMerchantInventory save, RunState runState)
    {
        Player player = LocalPlayer(runState);
        MerchantInventory inventory = new(player);

        foreach (CocoObservedMerchantCardEntry entrySave in save.CharacterCards)
        {
            GetCharacterCardEntries(inventory).Add(CreateMerchantCardEntry(entrySave, player));
        }

        foreach (CocoObservedMerchantCardEntry entrySave in save.ColorlessCards)
        {
            GetColorlessCardEntries(inventory).Add(CreateMerchantCardEntry(entrySave, player));
        }

        foreach (CocoObservedMerchantRelicEntry entrySave in save.Relics)
        {
            GetRelicEntries(inventory).Add(CreateMerchantRelicEntry(entrySave, player));
        }

        foreach (CocoObservedMerchantPotionEntry entrySave in save.Potions)
        {
            GetPotionEntries(inventory).Add(CreateMerchantPotionEntry(entrySave, player));
        }

        MerchantCardRemovalEntry removalEntry = new(player);
        if (save.CardRemovalUsed)
        {
            removalEntry.SetUsed();
        }

        SetMerchantCost(removalEntry, save.CardRemovalCost);
        SetCardRemovalEntry(inventory, removalEntry);
        return inventory;
    }

    private static MerchantCardEntry CreateMerchantCardEntry(CocoObservedMerchantCardEntry save, Player player)
    {
        MerchantCardEntry entry = new(player, inventory: null, Array.Empty<CardModel>(), CardType.Attack);
        CardModel card = CardModel.FromSerializable(save.Card);
        player.RunState.AddCard(card, player);
        SetMerchantCardCreationResult(entry, new CardCreationResult(card));
        SetMerchantCardIsOnSale(entry, save.IsOnSale);
        SetMerchantCost(entry, save.Cost);
        return entry;
    }

    private static MerchantRelicEntry CreateMerchantRelicEntry(CocoObservedMerchantRelicEntry save, Player player)
    {
        MerchantRelicEntry entry = new(RelicModel.FromSerializable(save.Relic), player);
        SetMerchantCost(entry, save.Cost);
        return entry;
    }

    private static MerchantPotionEntry CreateMerchantPotionEntry(CocoObservedMerchantPotionEntry save, Player player)
    {
        MerchantPotionEntry entry = new(PotionModel.FromSerializable(save.Potion), player);
        SetMerchantCost(entry, save.Cost);
        return entry;
    }

    private static List<MerchantCardEntry> GetCharacterCardEntries(MerchantInventory inventory) =>
        AccessTools.Field(typeof(MerchantInventory), "_characterCardEntries").GetValue(inventory) as List<MerchantCardEntry> ?? throw new InvalidOperationException("Failed to access merchant character cards.");

    private static List<MerchantCardEntry> GetColorlessCardEntries(MerchantInventory inventory) =>
        AccessTools.Field(typeof(MerchantInventory), "_colorlessCardEntries").GetValue(inventory) as List<MerchantCardEntry> ?? throw new InvalidOperationException("Failed to access merchant colorless cards.");

    private static List<MerchantRelicEntry> GetRelicEntries(MerchantInventory inventory) =>
        AccessTools.Field(typeof(MerchantInventory), "_relicEntries").GetValue(inventory) as List<MerchantRelicEntry> ?? throw new InvalidOperationException("Failed to access merchant relics.");

    private static List<MerchantPotionEntry> GetPotionEntries(MerchantInventory inventory) =>
        AccessTools.Field(typeof(MerchantInventory), "_potionEntries").GetValue(inventory) as List<MerchantPotionEntry> ?? throw new InvalidOperationException("Failed to access merchant potions.");

    private static void SetCardRemovalEntry(MerchantInventory inventory, MerchantCardRemovalEntry entry)
    {
        typeof(MerchantInventory)
            .GetProperty(nameof(MerchantInventory.CardRemovalEntry))?
            .SetValue(inventory, entry);
    }

    private static void SetMerchantCost(MerchantEntry entry, int cost)
    {
        AccessTools.Field(typeof(MerchantEntry), "_cost").SetValue(entry, cost);
    }

    private static void SetMerchantCardCreationResult(MerchantCardEntry entry, CardCreationResult creationResult)
    {
        typeof(MerchantCardEntry)
            .GetProperty(nameof(MerchantCardEntry.CreationResult))?
            .SetValue(entry, creationResult);
    }

    private static void SetMerchantCardIsOnSale(MerchantCardEntry entry, bool isOnSale)
    {
        typeof(MerchantCardEntry)
            .GetProperty(nameof(MerchantCardEntry.IsOnSale))?
            .SetValue(entry, isOnSale);
    }

    private static Player LocalPlayer(RunState runState)
    {
        return MegaCrit.Sts2.Core.Context.LocalContext.GetMe(runState) ?? runState.Players.First();
    }

    private static void EnsureDirectoryForFile(string path)
    {
        string? directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }

    private static void DeleteFileIfExists(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private static string ToFileSystemPath(string path)
    {
        return path.StartsWith("user://", StringComparison.OrdinalIgnoreCase)
            ? Godot.ProjectSettings.GlobalizePath(path)
            : path;
    }
}
