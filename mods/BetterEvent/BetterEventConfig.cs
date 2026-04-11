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

namespace BetterEvent;

public enum BetterEventMode
{
    Vanilla,
    Debug,
}

public sealed class BetterEventRuntimeConfig
{
    [JsonPropertyName("mode")]
    public string Mode { get; set; } = "vanilla";
}

public static class BetterEventConfigService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = null,
    };

    public static BetterEventRuntimeConfig Current { get; private set; } = new();
    public static BetterEventRuntimeConfig? SyncedFromHost { get; private set; }
    public static BetterEventRuntimeConfig? LockedForRun { get; private set; }
    public static BetterEventModConfig? UiConfig { get; private set; }

    public static string ConfigPath => GetScopedRuntimeConfigPath();

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
                Current = new BetterEventRuntimeConfig();
                Save();
                MainFile.Logger.Info($"Created BetterEvent runtime config at {ConfigPath}");
                return;
            }

            string json = File.ReadAllText(ConfigPath);
            Current = JsonSerializer.Deserialize<BetterEventRuntimeConfig>(json, JsonOptions) ?? new BetterEventRuntimeConfig();
        }
        catch (Exception ex)
        {
            Current = new BetterEventRuntimeConfig();
            MainFile.Logger.Error($"Failed to load BetterEvent runtime config: {ex}");
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

    public static BetterEventMode GetMode()
    {
        return ParseMode(GetEffectiveConfig().Mode);
    }

    public static bool IsDebugMode()
    {
        return GetMode() == BetterEventMode.Debug;
    }

    public static BetterEventRuntimeConfig GetEffectiveConfig()
    {
        if (LockedForRun != null)
        {
            return LockedForRun;
        }

        return RunManager.Instance.NetService?.Type == NetGameType.Client && SyncedFromHost != null
            ? SyncedFromHost
            : Current;
    }

    public static void ApplyHostConfig(BetterEventRuntimeConfig config)
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
        if (RunManager.Instance.NetService?.Type == NetGameType.Client && SyncedFromHost != null)
        {
            LockForRun(SyncedFromHost, persistToDisk: false, isMultiplayer);
            return;
        }

        LockForRun(Current, persistToDisk: true, isMultiplayer);
    }

    public static void EnsureRunConfigLoaded()
    {
        if (LockedForRun != null)
        {
            return;
        }

        bool isMultiplayer = RunManager.Instance.NetService?.Type.IsMultiplayer() ?? false;
        BetterEventRuntimeConfig? persisted = TryLoadRunConfigFromDisk(isMultiplayer);
        if (persisted != null)
        {
            LockForRun(persisted, persistToDisk: false, isMultiplayer);
            return;
        }

        if (RunManager.Instance.NetService?.Type == NetGameType.Client && SyncedFromHost != null)
        {
            LockForRun(SyncedFromHost, persistToDisk: false, isMultiplayer);
            return;
        }

        LockForRun(Current, persistToDisk: true, isMultiplayer);
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
            MainFile.Logger.Error($"Failed to clear persisted BetterEvent run config: {ex}");
        }
    }

    public static BetterEventMode ParseMode(string? raw)
    {
        return raw?.Trim().ToLowerInvariant() switch
        {
            "debug" => BetterEventMode.Debug,
            _ => BetterEventMode.Vanilla,
        };
    }

    public static string GetModDirectory()
    {
        string? location = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
        return string.IsNullOrWhiteSpace(location) ? AppContext.BaseDirectory : location;
    }

    private static void InitializeUiConfig()
    {
        if (UiConfig != null)
        {
            return;
        }

        UiConfig = new BetterEventModConfig(Current);
        UiConfig.Load();
        ApplyRuntimeConfigToUi(Current);
        UiConfig.ConfigChanged += (_, _) => SaveFromUiConfig();
        ModConfigRegistry.Register(MainFile.ModId, UiConfig);
        SaveFromUiConfig();
    }

    private static void ApplyRuntimeConfigToUi(BetterEventRuntimeConfig source)
    {
        BetterEventModConfig.Mode = ParseMode(source.Mode);
    }

    private static string GetScopedRuntimeConfigPath()
    {
        int profileId = GetCurrentProfileIdOrDefault();
        ulong playerId = PlatformUtil.GetLocalPlayerId(PlatformType.None);
        string platformDirectory = UserDataPathProvider.GetPlatformDirectoryName(PlatformType.None);
        string godotPath = $"user://{platformDirectory}/{playerId}/modded/profile{profileId}/mod_configs/BetterEvent.runtime.config";
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
        catch
        {
            return 0;
        }
    }

    private static void LockForRun(BetterEventRuntimeConfig config, bool persistToDisk, bool isMultiplayer)
    {
        LockedForRun = CloneConfig(config);
        if (!persistToDisk)
        {
            return;
        }

        string path = GetRunSessionConfigPath(isMultiplayer);
        string? directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(path, JsonSerializer.Serialize(LockedForRun, JsonOptions));
    }

    private static BetterEventRuntimeConfig? TryLoadRunConfigFromDisk(bool isMultiplayer)
    {
        string path = GetRunSessionConfigPath(isMultiplayer);
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            string json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<BetterEventRuntimeConfig>(json, JsonOptions);
        }
        catch (Exception ex)
        {
            MainFile.Logger.Error($"Failed to load persisted BetterEvent run config: {ex}");
            return null;
        }
    }

    private static string GetRunSessionConfigPath(bool isMultiplayer)
    {
        string suffix = isMultiplayer ? "multiplayer" : "singleplayer";
        string? directory = Path.GetDirectoryName(ConfigPath);
        return string.IsNullOrWhiteSpace(directory)
            ? Path.Combine(GetModDirectory(), $"BetterEvent.run.{suffix}.config")
            : Path.Combine(directory, $"BetterEvent.run.{suffix}.config");
    }

    private static BetterEventRuntimeConfig CloneConfig(BetterEventRuntimeConfig source)
    {
        return new BetterEventRuntimeConfig
        {
            Mode = source.Mode,
        };
    }
}

public sealed class BetterEventModConfig : SimpleModConfig
{
    public BetterEventModConfig(BetterEventRuntimeConfig source)
    {
        Mode = BetterEventConfigService.ParseMode(source.Mode);
    }

    [ConfigSection("General")]
    public static BetterEventMode Mode { get; set; } = BetterEventMode.Vanilla;

    public BetterEventRuntimeConfig ToRuntimeConfig()
    {
        return new BetterEventRuntimeConfig
        {
            Mode = NormalizeMode(Mode),
        };
    }

    private static string NormalizeMode(BetterEventMode mode)
    {
        return mode switch
        {
            BetterEventMode.Debug => "debug",
            _ => "vanilla",
        };
    }
}
