using System;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using BaseLib.Config;
using Godot;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.CardPools;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;

namespace MultiplayerCard;

public enum MultiplayerCardMode
{
    VanillaMode,
    MultiplayerMode,
    UniversalMode,
}

public enum MultiplayerCardAppearanceMode
{
    VanillaLike,
    HighProbability,
    DebugReward,
}

public enum MultiplayerCardDebugForcedCard
{
    None,
    ZeroSum,
    YouSoSelfish,
    BorrowTeammateTime,
    DropHandkerchief,
    FriendFee,
}

public sealed class MultiplayerCardRuntimeConfig
{
    [JsonPropertyName("mode")]
    public string Mode { get; set; } = "multiplayer";

    [JsonPropertyName("appearance_mode")]
    public string AppearanceMode { get; set; } = "vanilla";

    [JsonPropertyName("high_probability_reward_chance")]
    public double HighProbabilityRewardChance { get; set; } = 0.25;

    [JsonPropertyName("high_probability_reward_card")]
    public string HighProbabilityRewardCard { get; set; } = "none";

    [JsonPropertyName("debug_forced_reward_card")]
    public string DebugForcedRewardCard { get; set; } = "none";
}

public static class MultiplayerCardConfigService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = null,
    };

    public static MultiplayerCardRuntimeConfig Current { get; private set; } = new();

    public static MultiplayerCardRuntimeConfig? SyncedFromHost { get; private set; }

    public static MultiplayerCardRuntimeConfig? LockedForRun { get; private set; }

    public static MultiplayerCardModConfig? UiConfig { get; private set; }

    public static string ConfigPath => Path.Combine(GetModDirectory(), "MultiplayerCard.runtime.config");

    public static void Initialize()
    {
        Reload();
        InitializeUiConfig();
    }

    public static void Reload()
    {
        try
        {
            if (!File.Exists(ConfigPath))
            {
                Current = new MultiplayerCardRuntimeConfig();
                Save();
                MainFile.Logger.Info($"Created runtime config at {ConfigPath}");
                return;
            }

            string json = File.ReadAllText(ConfigPath);
            Current = JsonSerializer.Deserialize<MultiplayerCardRuntimeConfig>(json, JsonOptions) ?? new MultiplayerCardRuntimeConfig();
        }
        catch (Exception ex)
        {
            Current = new MultiplayerCardRuntimeConfig();
            MainFile.Logger.Error($"Failed to load MultiplayerCard runtime config: {ex}");
        }
    }

    public static void Save()
    {
        Directory.CreateDirectory(GetModDirectory());
        File.WriteAllText(ConfigPath, JsonSerializer.Serialize(Current, JsonOptions));
    }

    public static void SaveFromUiConfig()
    {
        if (UiConfig == null)
        {
            Save();
            return;
        }

        Current = UiConfig.ToRuntimeConfig();
        Save();
    }

    public static MultiplayerCardRuntimeConfig GetEffectiveConfig()
    {
        if (LockedForRun != null)
        {
            return LockedForRun;
        }

        return RunManager.Instance.NetService?.Type == NetGameType.Client && SyncedFromHost != null
            ? SyncedFromHost
            : Current;
    }

    public static void ApplyHostConfig(MultiplayerCardRuntimeConfig config)
    {
        SyncedFromHost = CloneConfig(config);
        bool isMultiplayer = RunManager.Instance.NetService?.Type.IsMultiplayer() ?? true;
        LockForRun(SyncedFromHost, persistToDisk: false, isMultiplayer);
    }

    public static void ClearHostConfig()
    {
        SyncedFromHost = null;
    }

    public static void PrepareForNewRun(bool isMultiplayer)
    {
        LockForRun(Current, persistToDisk: true, isMultiplayer);
    }

    public static void EnsureRunConfigLoaded()
    {
        if (LockedForRun != null)
        {
            return;
        }

        bool isMultiplayer = RunManager.Instance.NetService?.Type.IsMultiplayer() ?? false;
        MultiplayerCardRuntimeConfig? persisted = TryLoadRunConfigFromDisk(isMultiplayer);
        if (persisted != null)
        {
            LockForRun(persisted, persistToDisk: false, isMultiplayer);
            MainFile.Logger.Info($"[MultiplayerCard] loaded persisted run config: mode={persisted.Mode}, appearance={persisted.AppearanceMode}, chance={persisted.HighProbabilityRewardChance:0.##}.");
            return;
        }

        if (RunManager.Instance.NetService?.Type != NetGameType.Client)
        {
            LockForRun(Current, persistToDisk: true, isMultiplayer);
            MainFile.Logger.Info($"[MultiplayerCard] no persisted run config found; using current config for this run: mode={Current.Mode}, appearance={Current.AppearanceMode}, chance={Current.HighProbabilityRewardChance:0.##}.");
        }
    }

    public static void ClearRunLockInMemory()
    {
        LockedForRun = null;
    }

    public static void ClearPersistedRunConfig(bool isMultiplayer)
    {
        string path = GetRunSessionConfigPath(isMultiplayer);

        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
                MainFile.Logger.Info($"[MultiplayerCard] cleared persisted run config at {path}");
            }
        }
        catch (Exception ex)
        {
            MainFile.Logger.Error($"Failed to clear persisted MultiplayerCard run config: {ex}");
        }
    }

    public static string GetModDirectory()
    {
        string? location = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
        return string.IsNullOrWhiteSpace(location) ? AppContext.BaseDirectory : location;
    }

    public static MultiplayerCardMode GetMode()
    {
        return ParseMode(GetEffectiveConfig().Mode);
    }

    public static MultiplayerCardAppearanceMode GetAppearanceMode()
    {
        return ParseAppearanceMode(GetEffectiveConfig().AppearanceMode);
    }

    public static double GetHighProbabilityRewardChance()
    {
        return Math.Clamp(GetEffectiveConfig().HighProbabilityRewardChance, 0d, 1d);
    }

    public static bool IsSingleplayerUniversalFallbackEnabled(IRunState? runState)
    {
        return GetMode() == MultiplayerCardMode.UniversalMode && runState?.Players.Count <= 1;
    }

    public static bool ShouldAllowModCardsForRun(IRunState? runState)
    {
        return GetMode() switch
        {
            MultiplayerCardMode.VanillaMode => false,
            MultiplayerCardMode.MultiplayerMode => runState?.Players.Count > 1,
            MultiplayerCardMode.UniversalMode => true,
            _ => false,
        };
    }

    public static bool ShouldAllowModCardsForConstraint(CardMultiplayerConstraint constraint)
    {
        return GetMode() switch
        {
            MultiplayerCardMode.VanillaMode => false,
            MultiplayerCardMode.MultiplayerMode => constraint == CardMultiplayerConstraint.MultiplayerOnly,
            MultiplayerCardMode.UniversalMode => true,
            _ => false,
        };
    }

    public static bool ShouldInjectHighProbabilityReward(Player player, CardCreationOptions options)
    {
        if (!ShouldAllowModCardsForRun(player.RunState))
        {
            return false;
        }

        if (GetAppearanceMode() != MultiplayerCardAppearanceMode.HighProbability)
        {
            return false;
        }

        if (options.Source != CardCreationSource.Encounter)
        {
            return false;
        }

        return GetHighProbabilityRewardChance() > 0d
            && GetHighProbabilityRewardCard() != MultiplayerCardDebugForcedCard.None;
    }

    public static bool ShouldForceDebugRewardCard(Player player, CardCreationOptions options)
    {
        if (!ShouldAllowModCardsForRun(player.RunState))
        {
            return false;
        }

        if (GetAppearanceMode() != MultiplayerCardAppearanceMode.DebugReward)
        {
            return false;
        }

        if (options.Source != CardCreationSource.Encounter)
        {
            return false;
        }

        return GetDebugForcedRewardCard() != MultiplayerCardDebugForcedCard.None;
    }

    public static bool IsOurColorlessCard(CardModel card)
    {
        return card is ZeroSum or YouSoSelfish or DropHandkerchief;
    }

    public static bool IsOurNecrobinderCard(CardModel card)
    {
        return card is BorrowTeammateTime;
    }

    public static bool IsOurRegentCard(CardModel card)
    {
        return card is FriendFee;
    }

    public static bool IsOurCard(CardModel card)
    {
        return IsOurColorlessCard(card) || IsOurNecrobinderCard(card) || IsOurRegentCard(card);
    }

    public static IReadOnlyList<CardModel> GetModColorlessCards()
    {
        return ModelDb.CardPool<ColorlessCardPool>()
            .AllCards
            .Where(IsOurColorlessCard)
            .ToList();
    }

    public static IReadOnlyList<CardModel> GetModNecrobinderCards()
    {
        return ModelDb.CardPool<NecrobinderCardPool>()
            .AllCards
            .Where(IsOurNecrobinderCard)
            .ToList();
    }

    public static IReadOnlyList<CardModel> GetModRegentCards()
    {
        return ModelDb.CardPool<RegentCardPool>()
            .AllCards
            .Where(IsOurRegentCard)
            .ToList();
    }

    public static MultiplayerCardDebugForcedCard GetDebugForcedRewardCard()
    {
        return ParseDebugForcedRewardCard(GetEffectiveConfig().DebugForcedRewardCard);
    }

    public static MultiplayerCardDebugForcedCard GetHighProbabilityRewardCard()
    {
        return ParseDebugForcedRewardCard(GetEffectiveConfig().HighProbabilityRewardCard);
    }

    public static CardModel? GetConfiguredHighProbabilityRewardCard()
    {
        return GetHighProbabilityRewardCard() switch
        {
            MultiplayerCardDebugForcedCard.ZeroSum => ModelDb.Card<ZeroSum>(),
            MultiplayerCardDebugForcedCard.YouSoSelfish => ModelDb.Card<YouSoSelfish>(),
            MultiplayerCardDebugForcedCard.BorrowTeammateTime => ModelDb.Card<BorrowTeammateTime>(),
            MultiplayerCardDebugForcedCard.DropHandkerchief => ModelDb.Card<DropHandkerchief>(),
            MultiplayerCardDebugForcedCard.FriendFee => ModelDb.Card<FriendFee>(),
            _ => null,
        };
    }

    public static CardModel? GetConfiguredDebugRewardCard()
    {
        return GetDebugForcedRewardCard() switch
        {
            MultiplayerCardDebugForcedCard.ZeroSum => ModelDb.Card<ZeroSum>(),
            MultiplayerCardDebugForcedCard.YouSoSelfish => ModelDb.Card<YouSoSelfish>(),
            MultiplayerCardDebugForcedCard.BorrowTeammateTime => ModelDb.Card<BorrowTeammateTime>(),
            MultiplayerCardDebugForcedCard.DropHandkerchief => ModelDb.Card<DropHandkerchief>(),
            MultiplayerCardDebugForcedCard.FriendFee => ModelDb.Card<FriendFee>(),
            _ => null,
        };
    }

    public static MultiplayerCardMode ParseMode(string? raw)
    {
        return raw?.Trim().ToLowerInvariant() switch
        {
            "vanilla" => MultiplayerCardMode.VanillaMode,
            "multiplayer" => MultiplayerCardMode.MultiplayerMode,
            "universal" => MultiplayerCardMode.UniversalMode,
            _ => MultiplayerCardMode.MultiplayerMode,
        };
    }

    public static MultiplayerCardAppearanceMode ParseAppearanceMode(string? raw)
    {
        return raw?.Trim().ToLowerInvariant() switch
        {
            "vanilla" => MultiplayerCardAppearanceMode.VanillaLike,
            "high_probability" => MultiplayerCardAppearanceMode.HighProbability,
            "debug" => MultiplayerCardAppearanceMode.DebugReward,
            _ => MultiplayerCardAppearanceMode.VanillaLike,
        };
    }

    public static MultiplayerCardDebugForcedCard ParseDebugForcedRewardCard(string? raw)
    {
        return raw?.Trim().ToLowerInvariant() switch
        {
            "zero_sum" => MultiplayerCardDebugForcedCard.ZeroSum,
            "you_so_selfish" => MultiplayerCardDebugForcedCard.YouSoSelfish,
            "borrow_teammate_time" => MultiplayerCardDebugForcedCard.BorrowTeammateTime,
            "drop_handkerchief" => MultiplayerCardDebugForcedCard.DropHandkerchief,
            "friend_fee" => MultiplayerCardDebugForcedCard.FriendFee,
            _ => MultiplayerCardDebugForcedCard.None,
        };
    }

    private static void InitializeUiConfig()
    {
        if (UiConfig != null)
        {
            return;
        }

        UiConfig = new MultiplayerCardModConfig(Current);
        UiConfig.Load();
        UiConfig.ConfigChanged += (_, _) => SaveFromUiConfig();
        ModConfigRegistry.Register(MainFile.ModId, UiConfig);
        SaveFromUiConfig();
    }

    private static void LockForRun(MultiplayerCardRuntimeConfig config, bool persistToDisk, bool isMultiplayer)
    {
        LockedForRun = CloneConfig(config);

        if (!persistToDisk)
        {
            return;
        }

        try
        {
            string path = GetRunSessionConfigPath(isMultiplayer);
            string? directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(path, JsonSerializer.Serialize(LockedForRun, JsonOptions));
        }
        catch (Exception ex)
        {
            MainFile.Logger.Error($"Failed to persist MultiplayerCard run config: {ex}");
        }
    }

    private static MultiplayerCardRuntimeConfig? TryLoadRunConfigFromDisk(bool isMultiplayer)
    {
        string path = GetRunSessionConfigPath(isMultiplayer);

        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            string json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<MultiplayerCardRuntimeConfig>(json, JsonOptions);
        }
        catch (Exception ex)
        {
            MainFile.Logger.Error($"Failed to load persisted MultiplayerCard run config: {ex}");
            return null;
        }
    }

    private static string GetRunSessionConfigPath(bool isMultiplayer)
    {
        string fileName = isMultiplayer
            ? "MultiplayerCard.current_run_mp.config"
            : "MultiplayerCard.current_run.config";

        string godotPath = SaveManager.Instance.GetProfileScopedPath(Path.Combine(UserDataPathProvider.SavesDir, fileName));
        return godotPath.StartsWith("user://", StringComparison.OrdinalIgnoreCase)
            ? ProjectSettings.GlobalizePath(godotPath)
            : godotPath;
    }

    private static MultiplayerCardRuntimeConfig CloneConfig(MultiplayerCardRuntimeConfig config)
    {
        return new MultiplayerCardRuntimeConfig
        {
            Mode = config.Mode,
            AppearanceMode = config.AppearanceMode,
            HighProbabilityRewardChance = config.HighProbabilityRewardChance,
            HighProbabilityRewardCard = config.HighProbabilityRewardCard,
            DebugForcedRewardCard = config.DebugForcedRewardCard,
        };
    }
}

public sealed class MultiplayerCardModConfig : SimpleModConfig
{
    public MultiplayerCardModConfig(MultiplayerCardRuntimeConfig source)
    {
        Mode = MultiplayerCardConfigService.ParseMode(source.Mode);
        AppearanceMode = MultiplayerCardConfigService.ParseAppearanceMode(source.AppearanceMode);
        HighProbabilityRewardChancePercent = Math.Clamp(source.HighProbabilityRewardChance * 100d, 0d, 100d);
        HighProbabilityRewardCard = MultiplayerCardConfigService.ParseDebugForcedRewardCard(source.HighProbabilityRewardCard);
        DebugForcedRewardCard = MultiplayerCardConfigService.ParseDebugForcedRewardCard(source.DebugForcedRewardCard);
    }

    [ConfigSection("Card Mode")]
    public static MultiplayerCardMode Mode { get; set; } = MultiplayerCardMode.MultiplayerMode;

    [ConfigSection("Spawn Frequency")]
    public static MultiplayerCardAppearanceMode AppearanceMode { get; set; } = MultiplayerCardAppearanceMode.VanillaLike;

    [ConfigSection("Spawn Frequency")]
    [SliderRange(0, 100)]
    public static double HighProbabilityRewardChancePercent { get; set; } = 25d;

    [ConfigSection("Spawn Frequency")]
    public static MultiplayerCardDebugForcedCard HighProbabilityRewardCard { get; set; } = MultiplayerCardDebugForcedCard.None;

    [ConfigSection("Debug")]
    public static MultiplayerCardDebugForcedCard DebugForcedRewardCard { get; set; } = MultiplayerCardDebugForcedCard.None;

    public MultiplayerCardRuntimeConfig ToRuntimeConfig()
    {
        return new MultiplayerCardRuntimeConfig
        {
            Mode = NormalizeMode(Mode),
            AppearanceMode = NormalizeAppearanceMode(AppearanceMode),
            HighProbabilityRewardChance = Math.Clamp(HighProbabilityRewardChancePercent / 100d, 0d, 1d),
            HighProbabilityRewardCard = NormalizeDebugForcedRewardCard(HighProbabilityRewardCard),
            DebugForcedRewardCard = NormalizeDebugForcedRewardCard(DebugForcedRewardCard),
        };
    }

    private static string NormalizeMode(MultiplayerCardMode mode)
    {
        return mode switch
        {
            MultiplayerCardMode.VanillaMode => "vanilla",
            MultiplayerCardMode.MultiplayerMode => "multiplayer",
            MultiplayerCardMode.UniversalMode => "universal",
            _ => "multiplayer",
        };
    }

    private static string NormalizeAppearanceMode(MultiplayerCardAppearanceMode mode)
    {
        return mode switch
        {
            MultiplayerCardAppearanceMode.VanillaLike => "vanilla",
            MultiplayerCardAppearanceMode.HighProbability => "high_probability",
            MultiplayerCardAppearanceMode.DebugReward => "debug",
            _ => "vanilla",
        };
    }

    private static string NormalizeDebugForcedRewardCard(MultiplayerCardDebugForcedCard card)
    {
        return card switch
        {
            MultiplayerCardDebugForcedCard.ZeroSum => "zero_sum",
            MultiplayerCardDebugForcedCard.YouSoSelfish => "you_so_selfish",
            MultiplayerCardDebugForcedCard.BorrowTeammateTime => "borrow_teammate_time",
            MultiplayerCardDebugForcedCard.DropHandkerchief => "drop_handkerchief",
            MultiplayerCardDebugForcedCard.FriendFee => "friend_fee",
            _ => "none",
        };
    }
}
