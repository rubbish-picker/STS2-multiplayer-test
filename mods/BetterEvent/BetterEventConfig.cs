using System;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using BaseLib.Config;
using Godot;
using MegaCrit.Sts2.Core.Platform;
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
        return ParseMode(Current.Mode);
    }

    public static bool IsDebugMode()
    {
        return GetMode() == BetterEventMode.Debug;
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
