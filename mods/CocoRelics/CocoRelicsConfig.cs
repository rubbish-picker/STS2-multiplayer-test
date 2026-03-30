using System;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using BaseLib.Config;

namespace CocoRelics;

public sealed class CocoRelicsRuntimeConfig
{
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

    public static bool ShouldGrantDebugRelicAtRunStart()
    {
        return Current.DebugGrantZeduCocoAtRunStart;
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
}

public sealed class CocoRelicsModConfig : SimpleModConfig
{
    public CocoRelicsModConfig(CocoRelicsRuntimeConfig source)
    {
        DebugGrantZeduCocoAtRunStart = source.DebugGrantZeduCocoAtRunStart;
    }

    [ConfigSection("Debug")]
    public static bool DebugGrantZeduCocoAtRunStart { get; set; }

    public CocoRelicsRuntimeConfig ToRuntimeConfig()
    {
        return new CocoRelicsRuntimeConfig
        {
            DebugGrantZeduCocoAtRunStart = DebugGrantZeduCocoAtRunStart,
        };
    }
}
