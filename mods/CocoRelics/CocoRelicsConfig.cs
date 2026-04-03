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

public sealed class CocoRelicsRuntimeConfig
{
    [JsonPropertyName("preview_path_mode")]
    public string PreviewPathMode { get; set; } = "nearest";

    [JsonPropertyName("debug_grant_zedu_coco_at_run_start")]
    public bool DebugGrantZeduCocoAtRunStart { get; set; }
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

    public static CocoRelicsPreviewPathMode ParsePreviewPathMode(string? raw)
    {
        return raw?.Trim().ToLowerInvariant() switch
        {
            "furthest" => CocoRelicsPreviewPathMode.Furthest,
            _ => CocoRelicsPreviewPathMode.Nearest,
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

    public static bool ShouldGrantDebugRelicAtRunStart()
    {
        return GetEffectiveConfig().DebugGrantZeduCocoAtRunStart;
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
            PreviewPathMode = config.PreviewPathMode,
            DebugGrantZeduCocoAtRunStart = config.DebugGrantZeduCocoAtRunStart,
        };
    }
}

public sealed class CocoRelicsModConfig : SimpleModConfig
{
    public CocoRelicsModConfig(CocoRelicsRuntimeConfig source)
    {
        PreviewPathMode = CocoRelicsConfigService.ParsePreviewPathMode(source.PreviewPathMode);
        DebugGrantZeduCocoAtRunStart = source.DebugGrantZeduCocoAtRunStart;
    }

    [ConfigSection("Preview")]
    public static CocoRelicsPreviewPathMode PreviewPathMode { get; set; } = CocoRelicsPreviewPathMode.Nearest;

    [ConfigSection("Debug")]
    public static bool DebugGrantZeduCocoAtRunStart { get; set; }

    public CocoRelicsRuntimeConfig ToRuntimeConfig()
    {
        return new CocoRelicsRuntimeConfig
        {
            PreviewPathMode = NormalizePreviewPathMode(PreviewPathMode),
            DebugGrantZeduCocoAtRunStart = DebugGrantZeduCocoAtRunStart,
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
}
