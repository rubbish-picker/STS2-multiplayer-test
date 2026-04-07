using System;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using BaseLib.Config;
using Godot;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Platform;
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

    public static string ConfigPath => GetScopedRuntimeConfigPath();

    public static void Initialize()
    {
        LogPathInputs();
        Reload();
        InitializeUiConfig();
        LogInitialize();
    }

    public static void Reload()
    {
        try
        {
            if (!File.Exists(ConfigPath))
            {
                CocoRelicsRuntimeConfig? legacy = TryLoadLegacyRuntimeConfig();
                Current = legacy ?? new CocoRelicsRuntimeConfig();
                Save();
                MainFile.Logger.Info($"Created runtime config at {ConfigPath}");
                return;
            }

            string json = File.ReadAllText(ConfigPath);
            Current = JsonSerializer.Deserialize<CocoRelicsRuntimeConfig>(json, JsonOptions) ?? new CocoRelicsRuntimeConfig();
            LogReload();
        }
        catch (Exception ex)
        {
            Current = new CocoRelicsRuntimeConfig();
            MainFile.Logger.Error($"Failed to load CocoRelics runtime config: {ex}");
        }
    }

    public static void Save()
    {
        string? directory = Path.GetDirectoryName(ConfigPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(ConfigPath, JsonSerializer.Serialize(Current, JsonOptions));
        LogSave();
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
        LogSaveFromUi();
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
        LogApplyHostConfigSource(config);
        SyncedFromHost = CloneConfig(config);
        bool isMultiplayer = RunManager.Instance.NetService?.Type.IsMultiplayer() ?? true;
        LockForRun(SyncedFromHost, persistToDisk: false, isMultiplayer);
        LogApplyHostConfig();
    }

    public static void ClearHostConfig()
    {
        SyncedFromHost = null;
        LogClearHostConfig();
    }

    public static void PrepareForNewRun(bool isMultiplayer, bool preferHostConfig)
    {
        LogPrepareForNewRun(isMultiplayer, preferHostConfig);
        if (preferHostConfig)
        {
            LockedForRun = null;
            if (SyncedFromHost != null)
            {
                LockForRun(SyncedFromHost, persistToDisk: false, isMultiplayer);
            }

            LogAfterPrepareForNewRun();
            return;
        }

        LockForRun(Current, persistToDisk: true, isMultiplayer);
        LogAfterPrepareForNewRun();
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
            LogEnsureRunConfigLoaded();
            return;
        }

        if (RunManager.Instance.NetService?.Type != NetGameType.Client)
        {
            LockForRun(Current, persistToDisk: true, isMultiplayer);
        }

        LogEnsureRunConfigLoaded();
    }

    public static void ClearRunLockInMemory()
    {
        LockedForRun = null;
        LogClearRunLock();
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
        ApplyRuntimeConfigToUiAndLog(Current);
        UiConfig.ConfigChanged += (_, _) => SaveFromUiConfig();
        ModConfigRegistry.Register(MainFile.ModId, UiConfig);
        SaveFromUiConfig();
    }

    private static string GetModDirectory()
    {
        string? location = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
        return string.IsNullOrWhiteSpace(location) ? AppContext.BaseDirectory : location;
    }

    private static string GetScopedRuntimeConfigPath()
    {
        int profileId = GetCurrentProfileIdOrDefault();
        ulong playerId = PlatformUtil.GetLocalPlayerId(PlatformType.None);
        string platformDirectory = UserDataPathProvider.GetPlatformDirectoryName(PlatformType.None);
        string godotPath = $"user://{platformDirectory}/{playerId}/modded/profile{profileId}/mod_configs/CocoRelics.runtime.config";
        return godotPath.StartsWith("user://", StringComparison.OrdinalIgnoreCase)
            ? ProjectSettings.GlobalizePath(godotPath)
            : godotPath;
    }

    private static int GetCurrentProfileIdOrDefault()
    {
        try
        {
            return SaveManager.Instance.CurrentProfileId;
        }
        catch (InvalidOperationException)
        {
            return 1;
        }
    }

    private static CocoRelicsRuntimeConfig? TryLoadLegacyRuntimeConfig()
    {
        foreach (string legacyPath in GetLegacyRuntimeConfigCandidates())
        {
            try
            {
                if (!File.Exists(legacyPath))
                {
                    continue;
                }

                string json = File.ReadAllText(legacyPath);
                CocoRelicsRuntimeConfig? config = JsonSerializer.Deserialize<CocoRelicsRuntimeConfig>(json, JsonOptions);
                if (config != null)
                {
                    MainFile.Logger.Info($"Migrating legacy runtime config from {legacyPath} to {ConfigPath}");
                    return config;
                }
            }
            catch (Exception ex)
            {
                MainFile.Logger.Error($"Failed to load legacy CocoRelics runtime config from {legacyPath}: {ex}");
            }
        }

        return null;
    }

    private static string[] GetLegacyRuntimeConfigCandidates()
    {
        int profileId = GetCurrentProfileIdOrDefault();
        ulong playerId = PlatformUtil.GetLocalPlayerId(PlatformType.None);
        string profileDir = $"profile{profileId}";
        string appDataBase = ProjectSettings.GlobalizePath("user://");

        return
        [
            Path.Combine(appDataBase, "default", playerId.ToString(), "modded", profileDir, "mod_configs", "CocoRelics.runtime.config"),
            Path.Combine(GetModDirectory(), "CocoRelics.runtime.config"),
            Path.Combine(appDataBase, "default", playerId.ToString(), profileDir, "mod_configs", "CocoRelics.runtime.config"),
        ];
    }

    private static void ApplyRuntimeConfigToUi(CocoRelicsRuntimeConfig source)
    {
        CocoRelicsModConfig.Mode = ParseMode(source.Mode);
        CocoRelicsModConfig.HighProbabilityBonusChance = Math.Clamp(source.HighProbabilityBonusChance, 0, 100);
        CocoRelicsModConfig.PreviewPathMode = ParsePreviewPathMode(source.PreviewPathMode);
        CocoRelicsModConfig.DebugStartRelic = ParseDebugRelicOption(source.DebugStartRelic);
    }

    private static void LogCurrentConfigSnapshot(string stage)
    {
        CocoRelicsRuntimeConfig effective = GetEffectiveConfig();
        MainFile.Logger.Info(
            $"[CocoRelicsConfig] {stage} current={Current.Mode}/{Current.DebugStartRelic} " +
            $"synced={(SyncedFromHost == null ? "null" : $"{SyncedFromHost.Mode}/{SyncedFromHost.DebugStartRelic}")} " +
            $"locked={(LockedForRun == null ? "null" : $"{LockedForRun.Mode}/{LockedForRun.DebugStartRelic}")} " +
            $"effective={effective.Mode}/{effective.DebugStartRelic} path={ConfigPath}");
    }

    private static void ApplyRuntimeConfigToUiAndLog(CocoRelicsRuntimeConfig source)
    {
        ApplyRuntimeConfigToUi(source);
        LogCurrentConfigSnapshot("ApplyRuntimeConfigToUi");
    }

    private static void LogPrepareForNewRun(bool isMultiplayer, bool preferHostConfig)
    {
        MainFile.Logger.Info($"[CocoRelicsConfig] PrepareForNewRun isMultiplayer={isMultiplayer} preferHostConfig={preferHostConfig}");
        LogCurrentConfigSnapshot("BeforePrepareForNewRun");
    }

    private static void LogAfterPrepareForNewRun()
    {
        LogCurrentConfigSnapshot("AfterPrepareForNewRun");
    }

    private static void LogApplyHostConfig()
    {
        LogCurrentConfigSnapshot("AfterApplyHostConfig");
    }

    private static void LogEnsureRunConfigLoaded()
    {
        LogCurrentConfigSnapshot("AfterEnsureRunConfigLoaded");
    }

    private static void LogReload()
    {
        LogCurrentConfigSnapshot("AfterReload");
    }

    private static void LogSave()
    {
        LogCurrentConfigSnapshot("AfterSave");
    }

    private static void LogClearRunLock()
    {
        LogCurrentConfigSnapshot("AfterClearRunLock");
    }

    private static void LogClearHostConfig()
    {
        LogCurrentConfigSnapshot("AfterClearHostConfig");
    }

    private static void LogInitialize()
    {
        LogCurrentConfigSnapshot("AfterInitialize");
    }

    private static void LogPersistedRunConfigLoad(CocoRelicsRuntimeConfig? persisted, bool isMultiplayer)
    {
        MainFile.Logger.Info(
            $"[CocoRelicsConfig] TryLoadRunConfigFromDisk isMultiplayer={isMultiplayer} " +
            $"{(persisted == null ? "result=null" : $"result={persisted.Mode}/{persisted.DebugStartRelic}")}");
    }

    private static void LogLockForRun(bool persistToDisk, bool isMultiplayer)
    {
        MainFile.Logger.Info($"[CocoRelicsConfig] LockForRun persistToDisk={persistToDisk} isMultiplayer={isMultiplayer}");
        LogCurrentConfigSnapshot("AfterLockForRun");
    }

    private static void LogSaveFromUi()
    {
        LogCurrentConfigSnapshot("AfterSaveFromUi");
    }

    private static void LogLegacyCandidates()
    {
        MainFile.Logger.Info($"[CocoRelicsConfig] Legacy candidates: {string.Join(" | ", GetLegacyRuntimeConfigCandidates())}");
    }

    private static void LogConfigPathResolved()
    {
        MainFile.Logger.Info($"[CocoRelicsConfig] Resolved ConfigPath={ConfigPath}");
    }

    private static void LogGetScopedRuntimeConfigPath()
    {
        MainFile.Logger.Info($"[CocoRelicsConfig] Scoped runtime path={GetScopedRuntimeConfigPath()}");
    }

    private static void LogGetCurrentProfileId(int profileId)
    {
        MainFile.Logger.Info($"[CocoRelicsConfig] CurrentProfileIdOrDefault={profileId}");
    }

    private static void LogGetCurrentPlayerId(ulong playerId)
    {
        MainFile.Logger.Info($"[CocoRelicsConfig] LocalPlayerIdNone={playerId}");
    }

    private static void LogPathInputs()
    {
        int profileId = GetCurrentProfileIdOrDefault();
        ulong playerId = PlatformUtil.GetLocalPlayerId(PlatformType.None);
        LogGetCurrentProfileId(profileId);
        LogGetCurrentPlayerId(playerId);
        LogGetScopedRuntimeConfigPath();
        LogLegacyCandidates();
    }

    private static void LogApplyRuntimeConfigToUi()
    {
        LogCurrentConfigSnapshot("AfterApplyRuntimeConfigToUi");
    }

    private static void LogReloadFailure()
    {
        LogCurrentConfigSnapshot("AfterReloadFailure");
    }

    private static void LogLegacyMiss()
    {
        MainFile.Logger.Info("[CocoRelicsConfig] No legacy runtime config candidates were found.");
    }

    private static void LogLegacyHit(string path)
    {
        MainFile.Logger.Info($"[CocoRelicsConfig] Legacy runtime config hit: {path}");
    }

    private static void LogGetEffectiveConfigCall(string caller)
    {
        MainFile.Logger.Info($"[CocoRelicsConfig] GetEffectiveConfig caller={caller}");
        LogCurrentConfigSnapshot("GetEffectiveConfig");
    }

    private static void LogInitializeUiConfig()
    {
        LogCurrentConfigSnapshot("BeforeInitializeUiConfig");
    }

    private static void LogInitializeUiConfigDone()
    {
        LogCurrentConfigSnapshot("AfterInitializeUiConfig");
    }

    private static void LogSaveDirectory(string? directory)
    {
        MainFile.Logger.Info($"[CocoRelicsConfig] Save directory={directory}");
    }

    private static void LogClearPersistedRunConfig(string path)
    {
        MainFile.Logger.Info($"[CocoRelicsConfig] ClearPersistedRunConfig path={path}");
    }

    private static void LogRunConfigPath(bool isMultiplayer, string path)
    {
        MainFile.Logger.Info($"[CocoRelicsConfig] RunConfigPath isMultiplayer={isMultiplayer} path={path}");
    }

    private static void LogLockForRunSource(CocoRelicsRuntimeConfig config)
    {
        MainFile.Logger.Info($"[CocoRelicsConfig] LockForRun source={config.Mode}/{config.DebugStartRelic}");
    }

    private static void LogApplyHostConfigSource(CocoRelicsRuntimeConfig config)
    {
        MainFile.Logger.Info($"[CocoRelicsConfig] ApplyHostConfig source={config.Mode}/{config.DebugStartRelic}");
    }

    private static void LogReloadLoaded()
    {
        LogCurrentConfigSnapshot("ReloadLoaded");
    }

    private static void LogReloadCreated()
    {
        LogCurrentConfigSnapshot("ReloadCreated");
    }

    private static void LogTryLoadRunConfigPath(string path)
    {
        MainFile.Logger.Info($"[CocoRelicsConfig] TryLoadRunConfig path={path}");
    }

    private static void LogTryLoadRunConfigMissing(string path)
    {
        MainFile.Logger.Info($"[CocoRelicsConfig] TryLoadRunConfig missing path={path}");
    }

    private static void LogTryLoadRunConfigFailure(string path, Exception ex)
    {
        MainFile.Logger.Error($"[CocoRelicsConfig] TryLoadRunConfig failed path={path}: {ex}");
    }

    private static void LogTryLoadLegacyFailure(string path, Exception ex)
    {
        MainFile.Logger.Error($"[CocoRelicsConfig] Legacy load failed path={path}: {ex}");
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
