using System;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using BaseLib.Config;
using Godot;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;

namespace CocoRelics;

public enum CocoRelicsPreviewPathMode
{
    Nearest,
    Furthest,
}

public enum CocoRelicsMode
{
    Vanilla,
    HighProbability,
    Debug,
}

public enum CocoRelicsDebugRelicOption
{
    None,
    ZeduCoco,
    BigMeal,
}

public sealed class CocoRelicsRuntimeConfig
{
    [JsonPropertyName("mode")]
    public string Mode { get; set; } = "vanilla";

    [JsonPropertyName("high_probability_bonus_chance")]
    public int HighProbabilityBonusChance { get; set; } = 55;

    [JsonPropertyName("preview_path_mode")]
    public string PreviewPathMode { get; set; } = "nearest";

    [JsonPropertyName("debug_start_relic")]
    public string DebugStartRelic { get; set; } = "none";
}

public static class CocoRelicsConfigService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = null,
    };

    public static CocoRelicsRuntimeConfig Current { get; private set; } = new();

    public static CocoRelicsRuntimeConfig? SyncedFromHost { get; private set; }

    public static CocoRelicsRuntimeConfig? LockedForRun { get; private set; }

    public static CocoRelicsModConfig? UiConfig { get; private set; }

    public static string ConfigPath => Path.Combine(GetModDirectory(), "CocoRelics.runtime.config");

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
                Current = new CocoRelicsRuntimeConfig();
                Save();
                MainFile.Logger.Info($"Created runtime config at {ConfigPath}");
                return;
            }

            string json = File.ReadAllText(ConfigPath);
            Current = JsonSerializer.Deserialize<CocoRelicsRuntimeConfig>(json, JsonOptions) ?? new CocoRelicsRuntimeConfig();
        }
        catch (Exception ex)
        {
            Current = new CocoRelicsRuntimeConfig();
            MainFile.Logger.Error($"Failed to load CocoRelics runtime config: {ex}");
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

    public static CocoRelicsRuntimeConfig GetEffectiveConfig()
    {
        if (LockedForRun != null)
        {
            return LockedForRun;
        }

        return RunManager.Instance.NetService?.Type == NetGameType.Client && SyncedFromHost != null
            ? SyncedFromHost
            : Current;
    }

    public static CocoRelicsPreviewPathMode GetPreviewPathMode()
    {
        return ParsePreviewPathMode(GetEffectiveConfig().PreviewPathMode);
    }

    public static CocoRelicsMode GetMode()
    {
        return ParseMode(GetEffectiveConfig().Mode);
    }

    public static CocoRelicsDebugRelicOption GetDebugStartRelic()
    {
        return ParseDebugRelicOption(GetEffectiveConfig().DebugStartRelic);
    }

    public static float GetHighProbabilityBonusChance()
    {
        int percent = GetEffectiveConfig().HighProbabilityBonusChance;
        percent = Math.Clamp(percent, 0, 100);
        return percent / 100f;
    }

    public static bool IsHighProbabilityMode()
    {
        return GetMode() == CocoRelicsMode.HighProbability;
    }

    public static bool IsDebugMode()
    {
        return GetMode() == CocoRelicsMode.Debug;
    }

    public static CocoRelicsPreviewPathMode ParsePreviewPathMode(string? raw)
    {
        return raw?.Trim().ToLowerInvariant() switch
        {
            "furthest" => CocoRelicsPreviewPathMode.Furthest,
            _ => CocoRelicsPreviewPathMode.Nearest,
        };
    }

    public static CocoRelicsMode ParseMode(string? raw)
    {
        return raw?.Trim().ToLowerInvariant() switch
        {
            "high_probability" => CocoRelicsMode.HighProbability,
            "debug" => CocoRelicsMode.Debug,
            _ => CocoRelicsMode.Vanilla,
        };
    }

    public static CocoRelicsDebugRelicOption ParseDebugRelicOption(string? raw)
    {
        return raw?.Trim().ToLowerInvariant() switch
        {
            "zedu_coco" => CocoRelicsDebugRelicOption.ZeduCoco,
            "big_meal" => CocoRelicsDebugRelicOption.BigMeal,
            _ => CocoRelicsDebugRelicOption.None,
        };
    }

    public static void ApplyHostConfig(CocoRelicsRuntimeConfig config)
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
        CocoRelicsRuntimeConfig? persisted = TryLoadRunConfigFromDisk(isMultiplayer);
        if (persisted != null)
        {
            LockForRun(persisted, persistToDisk: false, isMultiplayer);
            return;
        }

        if (RunManager.Instance.NetService?.Type != NetGameType.Client)
        {
            LockForRun(Current, persistToDisk: true, isMultiplayer);
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
            }
        }
        catch (Exception ex)
        {
            MainFile.Logger.Error($"Failed to clear persisted CocoRelics run config: {ex}");
        }
    }

    public static CocoRelicsDebugRelicOption GetDebugRelicToGrantAtRunStart()
    {
        return GetMode() == CocoRelicsMode.Debug ? GetDebugStartRelic() : CocoRelicsDebugRelicOption.None;
    }

    private static void InitializeUiConfig()
    {
        if (UiConfig != null)
        {
            return;
        }

        UiConfig = new CocoRelicsModConfig(Current);
        UiConfig.Load();
        UiConfig.ConfigChanged += (_, _) => SaveFromUiConfig();
        ModConfigRegistry.Register(MainFile.ModId, UiConfig);
        SaveFromUiConfig();
    }

    private static string GetModDirectory()
    {
        string? location = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
        return string.IsNullOrWhiteSpace(location) ? AppContext.BaseDirectory : location;
    }

    private static void LockForRun(CocoRelicsRuntimeConfig config, bool persistToDisk, bool isMultiplayer)
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
            MainFile.Logger.Error($"Failed to persist CocoRelics run config: {ex}");
        }
    }

    private static CocoRelicsRuntimeConfig? TryLoadRunConfigFromDisk(bool isMultiplayer)
    {
        string path = GetRunSessionConfigPath(isMultiplayer);
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            return JsonSerializer.Deserialize<CocoRelicsRuntimeConfig>(File.ReadAllText(path), JsonOptions);
        }
        catch (Exception ex)
        {
            MainFile.Logger.Error($"Failed to load persisted CocoRelics run config: {ex}");
            return null;
        }
    }

    private static string GetRunSessionConfigPath(bool isMultiplayer)
    {
        string fileName = isMultiplayer
            ? "CocoRelics.current_run_mp.config"
            : "CocoRelics.current_run.config";

        string godotPath = SaveManager.Instance.GetProfileScopedPath(Path.Combine(UserDataPathProvider.SavesDir, fileName));
        return godotPath.StartsWith("user://", StringComparison.OrdinalIgnoreCase)
            ? ProjectSettings.GlobalizePath(godotPath)
            : godotPath;
    }

    private static CocoRelicsRuntimeConfig CloneConfig(CocoRelicsRuntimeConfig config)
    {
        return new CocoRelicsRuntimeConfig
        {
            Mode = config.Mode,
            HighProbabilityBonusChance = config.HighProbabilityBonusChance,
            PreviewPathMode = config.PreviewPathMode,
            DebugStartRelic = config.DebugStartRelic,
        };
    }
}

public sealed class CocoRelicsModConfig : SimpleModConfig
{
    public CocoRelicsModConfig(CocoRelicsRuntimeConfig source)
    {
        Mode = CocoRelicsConfigService.ParseMode(source.Mode);
        HighProbabilityBonusChance = Math.Clamp(source.HighProbabilityBonusChance, 0, 100);
        PreviewPathMode = CocoRelicsConfigService.ParsePreviewPathMode(source.PreviewPathMode);
        DebugStartRelic = CocoRelicsConfigService.ParseDebugRelicOption(source.DebugStartRelic);
    }

    [ConfigSection("General")]
    public static CocoRelicsMode Mode { get; set; } = CocoRelicsMode.Vanilla;

    [ConfigSection("General")]
    [SliderRange(0, 100)]
    public static double HighProbabilityBonusChance { get; set; } = 55d;

    [ConfigSection("Preview")]
    public static CocoRelicsPreviewPathMode PreviewPathMode { get; set; } = CocoRelicsPreviewPathMode.Nearest;

    [ConfigSection("Debug")]
    public static CocoRelicsDebugRelicOption DebugStartRelic { get; set; } = CocoRelicsDebugRelicOption.None;

    public CocoRelicsRuntimeConfig ToRuntimeConfig()
    {
        return new CocoRelicsRuntimeConfig
        {
            Mode = NormalizeMode(Mode),
            HighProbabilityBonusChance = Math.Clamp((int)Math.Round(HighProbabilityBonusChance), 0, 100),
            PreviewPathMode = NormalizePreviewPathMode(PreviewPathMode),
            DebugStartRelic = NormalizeDebugRelic(DebugStartRelic),
        };
    }

    private static string NormalizeMode(CocoRelicsMode mode)
    {
        return mode switch
        {
            CocoRelicsMode.HighProbability => "high_probability",
            CocoRelicsMode.Debug => "debug",
            _ => "vanilla",
        };
    }

    private static string NormalizePreviewPathMode(CocoRelicsPreviewPathMode mode)
    {
        return mode switch
        {
            CocoRelicsPreviewPathMode.Furthest => "furthest",
            _ => "nearest",
        };
    }

    private static string NormalizeDebugRelic(CocoRelicsDebugRelicOption option)
    {
        return option switch
        {
            CocoRelicsDebugRelicOption.ZeduCoco => "zedu_coco",
            CocoRelicsDebugRelicOption.BigMeal => "big_meal",
            _ => "none",
        };
    }
}
