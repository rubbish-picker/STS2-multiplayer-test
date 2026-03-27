using System;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using BaseLib.Config;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.CardPools;
using MegaCrit.Sts2.Core.Runs;

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
    DebugShop,
}

public sealed class MultiplayerCardRuntimeConfig
{
    [JsonPropertyName("mode")]
    public string Mode { get; set; } = "multiplayer";

    [JsonPropertyName("appearance_mode")]
    public string AppearanceMode { get; set; } = "vanilla";

    [JsonPropertyName("high_probability_reward_chance")]
    public double HighProbabilityRewardChance { get; set; } = 0.25;
}

public static class MultiplayerCardConfigService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = null,
    };

    public static MultiplayerCardRuntimeConfig Current { get; private set; } = new();

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

    public static string GetModDirectory()
    {
        string? location = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
        return string.IsNullOrWhiteSpace(location) ? AppContext.BaseDirectory : location;
    }

    public static MultiplayerCardMode GetMode()
    {
        return ParseMode(Current.Mode);
    }

    public static MultiplayerCardAppearanceMode GetAppearanceMode()
    {
        return ParseAppearanceMode(Current.AppearanceMode);
    }

    public static double GetHighProbabilityRewardChance()
    {
        return Math.Clamp(Current.HighProbabilityRewardChance, 0d, 1d);
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

        return GetHighProbabilityRewardChance() > 0d;
    }

    public static bool ShouldForceDebugShopCards(Player player)
    {
        return ShouldAllowModCardsForRun(player.RunState) && GetAppearanceMode() == MultiplayerCardAppearanceMode.DebugShop;
    }

    public static bool IsOurCard(CardModel card)
    {
        return card is ZeroSum or YouSoSelfish;
    }

    public static IReadOnlyList<CardModel> GetModColorlessCards()
    {
        return ModelDb.CardPool<ColorlessCardPool>()
            .AllCards
            .Where(IsOurCard)
            .ToList();
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
            "debug" => MultiplayerCardAppearanceMode.DebugShop,
            _ => MultiplayerCardAppearanceMode.VanillaLike,
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
}

public sealed class MultiplayerCardModConfig : SimpleModConfig
{
    public MultiplayerCardModConfig(MultiplayerCardRuntimeConfig source)
    {
        Mode = MultiplayerCardConfigService.ParseMode(source.Mode);
        AppearanceMode = MultiplayerCardConfigService.ParseAppearanceMode(source.AppearanceMode);
        HighProbabilityRewardChancePercent = Math.Clamp(source.HighProbabilityRewardChance * 100d, 0d, 100d);
    }

    [ConfigSection("Card Mode")]
    public static MultiplayerCardMode Mode { get; set; } = MultiplayerCardMode.MultiplayerMode;

    [ConfigSection("Spawn Frequency")]
    public static MultiplayerCardAppearanceMode AppearanceMode { get; set; } = MultiplayerCardAppearanceMode.VanillaLike;

    [ConfigSection("Spawn Frequency")]
    [SliderRange(0, 100)]
    public static double HighProbabilityRewardChancePercent { get; set; } = 25d;

    public MultiplayerCardRuntimeConfig ToRuntimeConfig()
    {
        return new MultiplayerCardRuntimeConfig
        {
            Mode = NormalizeMode(Mode),
            AppearanceMode = NormalizeAppearanceMode(AppearanceMode),
            HighProbabilityRewardChance = Math.Clamp(HighProbabilityRewardChancePercent / 100d, 0d, 1d),
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
            MultiplayerCardAppearanceMode.DebugShop => "debug",
            _ => "vanilla",
        };
    }
}
