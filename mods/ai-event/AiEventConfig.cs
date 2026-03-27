using System;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using BaseLib.Config;

namespace AiEvent;

public sealed class AiEventRuntimeConfig
{
    [JsonPropertyName("mode")]
    public string Mode { get; set; } = "llm_dynamic";

    [JsonPropertyName("enable_llm_generation")]
    public bool EnableLlmGeneration { get; set; } = true;

    [JsonPropertyName("base_url")]
    public string BaseUrl { get; set; } = string.Empty;

    [JsonPropertyName("api_key")]
    public string ApiKey { get; set; } = string.Empty;

    [JsonPropertyName("model")]
    public string Model { get; set; } = "gpt-5.4";

    [JsonPropertyName("temperature")]
    public double Temperature { get; set; } = 0.9;

    [JsonPropertyName("max_output_tokens")]
    public int MaxOutputTokens { get; set; } = 2200;

    [JsonPropertyName("request_timeout_seconds")]
    public int RequestTimeoutSeconds { get; set; } = 120;

    [JsonPropertyName("generate_on_run_start")]
    public bool GenerateOnRunStart { get; set; } = true;

    [JsonPropertyName("cache_pool_limit")]
    public int CachePoolLimit { get; set; } = 50;

    [JsonPropertyName("dynamic_events_per_run")]
    public int DynamicEventsPerRun { get; set; } = 20;

    [JsonPropertyName("vanilla_weight")]
    public double VanillaWeight { get; set; } = 0.35;

    [JsonPropertyName("cache_weight")]
    public double CacheWeight { get; set; } = 0.25;

    [JsonPropertyName("dynamic_weight")]
    public double DynamicWeight { get; set; } = 0.4;

    [JsonPropertyName("debug_force_llm_events")]
    public bool DebugForceLlmEvents { get; set; } = false;
}

public static class AiEventConfigService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = null,
    };

    public static AiEventRuntimeConfig Current { get; private set; } = new();

    public static AiEventModConfig? UiConfig { get; private set; }

    public static string ConfigPath => Path.Combine(GetModDirectory(), "ai-event.runtime.config");

    public static void Initialize()
    {
        Reload();
        InitializeUiConfig();
    }

    public static void Reload()
    {
        try
        {
            string legacyPath = Path.Combine(GetModDirectory(), "ai-event.runtime.json");
            if (!File.Exists(ConfigPath) && File.Exists(legacyPath))
            {
                File.Copy(legacyPath, ConfigPath, overwrite: true);
            }

            if (!File.Exists(ConfigPath))
            {
                Current = new AiEventRuntimeConfig();
                Save();
                MainFile.Logger.Info($"Created runtime config at {ConfigPath}");
                return;
            }

            string json = File.ReadAllText(ConfigPath);
            Current = JsonSerializer.Deserialize<AiEventRuntimeConfig>(json, JsonOptions) ?? new AiEventRuntimeConfig();
        }
        catch (Exception ex)
        {
            Current = new AiEventRuntimeConfig();
            MainFile.Logger.Error($"Failed to load ai-event runtime config: {ex}");
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

    public static AiEventMode GetMode()
    {
        return ParseMode(Current.Mode);
    }

    public static AiEventMode ParseMode(string? raw)
    {
        return raw?.Trim().ToLowerInvariant() switch
        {
            "vanilla" => AiEventMode.Vanilla,
            "vanilla_plus_cache" => AiEventMode.VanillaPlusCache,
            "cache" => AiEventMode.VanillaPlusCache,
            "llm_dynamic" => AiEventMode.LlmDynamic,
            "dynamic" => AiEventMode.LlmDynamic,
            "llm_debug" => AiEventMode.LlmDebug,
            "debug" => AiEventMode.LlmDebug,
            _ => AiEventMode.LlmDynamic,
        };
    }

    private static void InitializeUiConfig()
    {
        if (UiConfig != null)
        {
            return;
        }

        UiConfig = new AiEventModConfig(Current);
        UiConfig.Load();
        UiConfig.ConfigChanged += (_, _) =>
        {
            SaveFromUiConfig();
        };
        ModConfigRegistry.Register(MainFile.ModId, UiConfig);
        SaveFromUiConfig();
    }
}

public sealed class AiEventModConfig : SimpleModConfig
{
    public AiEventModConfig(AiEventRuntimeConfig source)
    {
        Mode = source.Mode;
        EnableLlmGeneration = source.EnableLlmGeneration;
        BaseUrl = source.BaseUrl;
        ApiKey = source.ApiKey;
        Model = source.Model;
        TemperatureInput = source.Temperature.ToString("0.00");
        MaxOutputTokens = source.MaxOutputTokens;
        RequestTimeoutSeconds = source.RequestTimeoutSeconds;
        GenerateOnRunStart = source.GenerateOnRunStart;
        CachePoolLimit = source.CachePoolLimit;
        DynamicEventsPerRun = source.DynamicEventsPerRun;
        VanillaWeightInput = source.VanillaWeight.ToString("0.00");
        CacheWeightInput = source.CacheWeight.ToString("0.00");
        DynamicWeightInput = source.DynamicWeight.ToString("0.00");
        DebugForceLlmEvents = source.DebugForceLlmEvents;
    }

    [ConfigSection("General")]
    [ConfigTextInput(TextInputPreset.Anything, MaxLength = 40)]
    public static string Mode { get; set; } = "llm_dynamic";

    [ConfigSection("General")]
    public static bool EnableLlmGeneration { get; set; } = true;

    [ConfigSection("LLM")]
    [ConfigTextInput(TextInputPreset.Anything, MaxLength = 300)]
    public static string BaseUrl { get; set; } = string.Empty;

    [ConfigSection("LLM")]
    [ConfigTextInput(TextInputPreset.Anything, MaxLength = 300)]
    public static string ApiKey { get; set; } = string.Empty;

    [ConfigSection("LLM")]
    [ConfigTextInput(TextInputPreset.Anything, MaxLength = 120)]
    public static string Model { get; set; } = "gpt-5.4";

    [ConfigSection("LLM")]
    [ConfigTextInput(TextInputPreset.Anything, MaxLength = 16)]
    public static string TemperatureInput { get; set; } = "0.90";

    [ConfigSection("LLM")]
    [SliderRange(256, 8192)]
    public static double MaxOutputTokens { get; set; } = 2200;

    [ConfigSection("LLM")]
    [SliderRange(15, 300)]
    public static double RequestTimeoutSeconds { get; set; } = 120;

    [ConfigSection("Generation")]
    public static bool GenerateOnRunStart { get; set; } = true;

    [ConfigSection("Generation")]
    [SliderRange(1, 200)]
    public static double CachePoolLimit { get; set; } = 50;

    [ConfigSection("Generation")]
    [SliderRange(1, 80)]
    public static double DynamicEventsPerRun { get; set; } = 20;

    [ConfigSection("Generation")]
    [ConfigTextInput(TextInputPreset.Anything, MaxLength = 16)]
    public static string VanillaWeightInput { get; set; } = "0.35";

    [ConfigSection("Generation")]
    [ConfigTextInput(TextInputPreset.Anything, MaxLength = 16)]
    public static string CacheWeightInput { get; set; } = "0.25";

    [ConfigSection("Generation")]
    [ConfigTextInput(TextInputPreset.Anything, MaxLength = 16)]
    public static string DynamicWeightInput { get; set; } = "0.40";

    [ConfigSection("Debug")]
    public static bool DebugForceLlmEvents { get; set; } = false;

    [ConfigIgnore]
    public int NotAConfigProperty { get; set; }

    public AiEventRuntimeConfig ToRuntimeConfig()
    {
        return new AiEventRuntimeConfig
        {
            Mode = NormalizeMode(Mode),
            EnableLlmGeneration = EnableLlmGeneration,
            BaseUrl = BaseUrl,
            ApiKey = ApiKey,
            Model = Model,
            Temperature = ParseTemperature(TemperatureInput),
            MaxOutputTokens = (int)Math.Round(MaxOutputTokens),
            RequestTimeoutSeconds = (int)Math.Round(RequestTimeoutSeconds),
            GenerateOnRunStart = GenerateOnRunStart,
            CachePoolLimit = Math.Clamp((int)Math.Round(CachePoolLimit), 1, 200),
            DynamicEventsPerRun = Math.Clamp((int)Math.Round(DynamicEventsPerRun), 1, 80),
            VanillaWeight = ParseWeight(VanillaWeightInput, 0.35),
            CacheWeight = ParseWeight(CacheWeightInput, 0.25),
            DynamicWeight = ParseWeight(DynamicWeightInput, 0.40),
            DebugForceLlmEvents = DebugForceLlmEvents,
        };
    }

    private static double ParseTemperature(string? rawValue)
    {
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            return 0.9;
        }

        string normalized = rawValue.Trim().Replace(',', '.');
        if (!double.TryParse(normalized, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double value))
        {
            return 0.9;
        }

        return Math.Clamp(value, 0d, 2d);
    }

    private static double ParseWeight(string? rawValue, double fallback)
    {
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            return fallback;
        }

        string normalized = rawValue.Trim().Replace(',', '.');
        if (!double.TryParse(normalized, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double value))
        {
            return fallback;
        }

        return Math.Clamp(value, 0d, 100d);
    }

    private static string NormalizeMode(string? rawValue)
    {
        return AiEventConfigService.ParseMode(rawValue) switch
        {
            AiEventMode.Vanilla => "vanilla",
            AiEventMode.VanillaPlusCache => "vanilla_plus_cache",
            AiEventMode.LlmDynamic => "llm_dynamic",
            AiEventMode.LlmDebug => "llm_debug",
            _ => "llm_dynamic",
        };
    }
}
