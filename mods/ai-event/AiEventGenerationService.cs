using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Godot;

namespace AiEvent;

public static class AiEventGenerationService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
    };

    public static void Initialize()
    {
        AiEventRepository.Initialize();
        if (MegaCrit.Sts2.Core.Localization.LocManager.Instance != null)
        {
            AiEventLocalization.ApplyCurrentLanguage();
        }
    }

    public static bool HasApiConfig()
    {
        AiEventRuntimeConfig config = AiEventConfigService.Current;
        return !string.IsNullOrWhiteSpace(config.BaseUrl)
            && !string.IsNullOrWhiteSpace(config.ApiKey)
            && !string.IsNullOrWhiteSpace(config.Model);
    }

    public static async Task<string?> ValidateConnectivityAsync()
    {
        AiEventConfigService.Reload();

        if (!AiEventConfigService.Current.EnableLlmGeneration)
        {
            return null;
        }

        if (!HasApiConfig())
        {
            return "ai-event 的 LLM 配置不完整，已取消本次开局。\n请检查 base_url、api_key 和 model 是否都已填写。";
        }

        try
        {
            ReadGenerationAssets();
        }
        catch (Exception ex)
        {
            MainFile.Logger.Error($"ai-event preflight asset check failed: {ex}");
            return "ai-event 缺少运行时提示词资源，已取消本次开局。\n请重新发布模组后再试。\n\n错误: " + GetInnermostMessage(ex);
        }

        try
        {
            string response = await SendChatCompletionAsync(
                "You are a connectivity probe for a Slay the Spire 2 mod. Reply with only OK.",
                "Reply with only OK.",
                Math.Min(Math.Max(15, AiEventConfigService.Current.RequestTimeoutSeconds), 20),
                32);

            if (string.IsNullOrWhiteSpace(response))
            {
                throw new InvalidOperationException("LLM test request returned an empty response.");
            }
        }
        catch (Exception ex)
        {
            MainFile.Logger.Error($"ai-event preflight request failed: {ex}");
            return "ai-event 无法连接到 LLM，已取消本次开局。\n请检查 base_url、api_key、model 和代理设置。\n\n错误: " + GetInnermostMessage(ex);
        }

        return null;
    }

    public static async Task<AiGeneratedEventPayload> GeneratePayloadAsync(AiEventSlot slot, string? seed)
    {
        (string systemPrompt, string schema, string samples) = ReadGenerationAssets();

        string userPrompt =
            $"Generate one Slay the Spire 2 style event JSON for slot `{slot}`.\n" +
            $"The current run seed is `{seed ?? "UNKNOWN"}`.\n" +
            "Generate a fully playable event with 2 or 3 options.\n" +
            "You must generate BOTH the option text and the underlying rewards/costs.\n" +
            "Only use supported effect types from the schema. Do not invent custom mechanics.\n" +
            "Output both `eng` and `zhs` localized text, and keep the two versions aligned.\n" +
            "Keep the event readable in-game and close to vanilla style.\n\n" +
            "Schema:\n" + schema + "\n\n" +
            "Vanilla samples:\n" + samples;

        string rawContent = await SendChatCompletionAsync(systemPrompt, userPrompt);
        AiGeneratedEventPayload? payload = DeserializePayload(rawContent);
        if (payload == null)
        {
            throw new InvalidOperationException("LLM returned empty payload.");
        }

        NormalizePayload(payload, slot);
        MainFile.Logger.Info($"Generated ai-event payload for {slot}.");
        return payload;
    }

    private static void NormalizePayload(AiGeneratedEventPayload payload, AiEventSlot slot)
    {
        AiGeneratedEventPayload fallback = AiEventFallbacks.Create(slot);

        payload.Slot = slot;
        payload.EventKey = AiEventRegistry.GetEventKey(slot);
        payload.Eng ??= fallback.Eng;
        payload.Zhs ??= fallback.Zhs;
        payload.Options = NormalizeRuntimeOptions(payload.Options, fallback.Options);

        payload.Eng.Title = Coalesce(payload.Eng.Title, fallback.Eng.Title);
        payload.Eng.InitialDescription = Coalesce(payload.Eng.InitialDescription, fallback.Eng.InitialDescription);
        payload.Zhs.Title = Coalesce(payload.Zhs.Title, fallback.Zhs.Title);
        payload.Zhs.InitialDescription = Coalesce(payload.Zhs.InitialDescription, fallback.Zhs.InitialDescription);

        payload.Eng.Options = NormalizeLocalizedOptions(payload.Eng.Options, payload.Options, fallback.Eng.Options);
        payload.Zhs.Options = NormalizeLocalizedOptions(payload.Zhs.Options, payload.Options, fallback.Zhs.Options);
    }

    private static List<AiEventOptionPayload> NormalizeRuntimeOptions(
        List<AiEventOptionPayload>? options,
        List<AiEventOptionPayload> fallbackOptions)
    {
        List<AiEventOptionPayload> source = options ?? new List<AiEventOptionPayload>();
        List<AiEventOptionPayload> normalized = new();

        for (int i = 0; i < source.Count && normalized.Count < 3; i++)
        {
            AiEventOptionPayload option = source[i] ?? new AiEventOptionPayload();
            List<AiEventEffectPayload> effects = NormalizeEffects(option.Effects);
            if (effects.Count == 0)
            {
                continue;
            }

            normalized.Add(new AiEventOptionPayload
            {
                Key = GetOptionKey(normalized.Count),
                Effects = effects,
            });
        }

        foreach (AiEventOptionPayload fallback in fallbackOptions)
        {
            if (normalized.Count >= 2)
            {
                break;
            }

            normalized.Add(new AiEventOptionPayload
            {
                Key = GetOptionKey(normalized.Count),
                Effects = NormalizeEffects(fallback.Effects),
            });
        }

        if (normalized.Count > 3)
        {
            normalized = normalized.Take(3).ToList();
        }

        return normalized;
    }

    private static List<AiEventEffectPayload> NormalizeEffects(List<AiEventEffectPayload>? effects)
    {
        if (effects == null)
        {
            return new List<AiEventEffectPayload>();
        }

        List<AiEventEffectPayload> normalized = new();
        foreach (AiEventEffectPayload effect in effects)
        {
            if (effect == null)
            {
                continue;
            }

            string type = (effect.Type ?? string.Empty).Trim().ToLowerInvariant();
            if (!AiEventEffectCatalog.IsSupported(type))
            {
                continue;
            }

            int amount = Math.Max(0, effect.Amount);
            int count = Math.Clamp(effect.Count <= 0 ? 1 : effect.Count, 1, 10);
            string cardId = (effect.CardId ?? string.Empty).Trim().ToUpperInvariant();
            string relicRarity = (effect.RelicRarity ?? string.Empty).Trim().ToLowerInvariant();

            if (AiEventEffectCatalog.RequiresAmount(type) && amount <= 0)
            {
                continue;
            }

            if (AiEventEffectCatalog.RequiresCardId(type) && string.IsNullOrWhiteSpace(cardId))
            {
                continue;
            }

            normalized.Add(new AiEventEffectPayload
            {
                Type = type,
                Amount = amount,
                Count = count,
                CardId = cardId,
                RelicRarity = relicRarity,
            });
        }

        return normalized;
    }

    private static List<AiLocalizedOptionText> NormalizeLocalizedOptions(
        List<AiLocalizedOptionText>? options,
        List<AiEventOptionPayload> runtimeOptions,
        List<AiLocalizedOptionText> fallbackOptions)
    {
        Dictionary<string, AiLocalizedOptionText> fallbackByKey = fallbackOptions.ToDictionary(o => o.Key, StringComparer.OrdinalIgnoreCase);
        List<AiLocalizedOptionText> normalized = new();

        for (int i = 0; i < runtimeOptions.Count; i++)
        {
            string key = runtimeOptions[i].Key;
            AiLocalizedOptionText? source = options?
                .FirstOrDefault(o => string.Equals(o.Key, key, StringComparison.OrdinalIgnoreCase))
                ?? (i < options?.Count ? options[i] : null);

            fallbackByKey.TryGetValue(key, out AiLocalizedOptionText? fallback);
            fallback ??= fallbackOptions.ElementAtOrDefault(Math.Min(i, fallbackOptions.Count - 1)) ?? new AiLocalizedOptionText();

            normalized.Add(new AiLocalizedOptionText
            {
                Key = key,
                Title = Coalesce(source?.Title, fallback.Title),
                Description = Coalesce(source?.Description, fallback.Description),
                ResultDescription = Coalesce(source?.ResultDescription, fallback.ResultDescription),
            });
        }

        return normalized;
    }

    private static string GetOptionKey(int index)
    {
        return index switch
        {
            0 => "OPTION_A",
            1 => "OPTION_B",
            2 => "OPTION_C",
            _ => "OPTION_A",
        };
    }

    private static string Coalesce(string? value, string fallback)
    {
        return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
    }

    private static AiGeneratedEventPayload? DeserializePayload(string rawContent)
    {
        string json = ExtractJsonObject(rawContent);
        return JsonSerializer.Deserialize<AiGeneratedEventPayload>(json, JsonOptions);
    }

    private static string ExtractJsonObject(string rawContent)
    {
        int start = rawContent.IndexOf('{');
        int end = rawContent.LastIndexOf('}');
        if (start < 0 || end < start)
        {
            return rawContent;
        }

        return rawContent.Substring(start, end - start + 1);
    }

    private static (string SystemPrompt, string Schema, string Samples) ReadGenerationAssets()
    {
        string systemPrompt = ReadAssetText(
            new[] { "runtime_assets", "prompts", "system_prompt.txt" },
            new[] { "prompts", "system_prompt.md" });
        string schema = ReadAssetText(
            new[] { "runtime_assets", "prompts", "event_output_schema.txt" },
            new[] { "prompts", "event_output_schema.json" });
        string samples = ReadAssetText(
            new[] { "runtime_assets", "data", "vanilla_event_samples.txt" },
            new[] { "data", "vanilla_event_samples.json" });

        return (systemPrompt, schema, samples);
    }

    private static async Task<string> SendChatCompletionAsync(
        string systemPrompt,
        string userPrompt,
        int? timeoutSecondsOverride = null,
        int? maxTokensOverride = null)
    {
        AiEventRuntimeConfig config = AiEventConfigService.Current;
        using System.Net.Http.HttpClient client = new()
        {
            Timeout = TimeSpan.FromSeconds(timeoutSecondsOverride ?? Math.Max(15, config.RequestTimeoutSeconds)),
        };

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", config.ApiKey);

        string endpoint = config.BaseUrl.Trim();
        JsonObject requestBody = new()
        {
            ["model"] = config.Model,
            ["temperature"] = config.Temperature,
            ["max_tokens"] = maxTokensOverride ?? GetSafeMaxOutputTokens(config.MaxOutputTokens),
            ["messages"] = new JsonArray
            {
                new JsonObject
                {
                    ["role"] = "system",
                    ["content"] = systemPrompt,
                },
                new JsonObject
                {
                    ["role"] = "user",
                    ["content"] = userPrompt,
                },
            },
        };

        using HttpRequestMessage request = new(HttpMethod.Post, endpoint);
        byte[] jsonBytes = Encoding.UTF8.GetBytes(requestBody.ToJsonString());
        request.Content = new ByteArrayContent(jsonBytes);
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        using HttpResponseMessage response = await client.SendAsync(request);
        string responseText = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"Response status code does not indicate success: {(int)response.StatusCode} ({response.ReasonPhrase}). Body: {TrimForError(responseText, 800)}");
        }

        JsonNode? root = JsonNode.Parse(responseText);
        JsonNode? messageContentNode = root?["choices"]?[0]?["message"]?["content"];
        if (messageContentNode == null)
        {
            throw new InvalidOperationException("Chat completion response did not contain message content.");
        }

        if (messageContentNode is JsonValue)
        {
            return messageContentNode.ToString();
        }

        if (messageContentNode is JsonArray array)
        {
            StringBuilder builder = new();
            foreach (JsonNode? item in array)
            {
                string? text = item?["text"]?.ToString();
                if (!string.IsNullOrWhiteSpace(text))
                {
                    builder.AppendLine(text);
                }
            }

            if (builder.Length > 0)
            {
                return builder.ToString();
            }
        }

        return messageContentNode.ToJsonString();
    }

    private static int GetSafeMaxOutputTokens(int configuredValue)
    {
        return Math.Clamp(configuredValue, 64, 2048);
    }

    private static string TrimForError(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "(empty)";
        }

        string trimmed = value.Trim();
        if (trimmed.Length <= maxLength)
        {
            return trimmed;
        }

        return trimmed.Substring(0, maxLength) + "...";
    }

    private static string GetInnermostMessage(Exception ex)
    {
        Exception current = ex;
        while (current.InnerException != null)
        {
            current = current.InnerException;
        }

        return current.Message;
    }

    private static string ReadAssetText(params string[][] candidatePaths)
    {
        string modDirectory = AiEventConfigService.GetModDirectory();
        foreach (string[] segments in candidatePaths)
        {
            string nestedPath = Path.Combine(modDirectory, "ai-event");
            foreach (string segment in segments)
            {
                nestedPath = Path.Combine(nestedPath, segment);
            }

            if (File.Exists(nestedPath))
            {
                return File.ReadAllText(nestedPath);
            }

            string flatPath = modDirectory;
            foreach (string segment in segments)
            {
                flatPath = Path.Combine(flatPath, segment);
            }

            if (File.Exists(flatPath))
            {
                return File.ReadAllText(flatPath);
            }

            string resourcePath = "res://ai-event";
            foreach (string segment in segments)
            {
                resourcePath += "/" + segment.Replace("\\", "/");
            }

            if (Godot.FileAccess.FileExists(resourcePath))
            {
                return Godot.FileAccess.GetFileAsString(resourcePath);
            }
        }

        throw new FileNotFoundException("ai-event asset not found in any runtime asset location.");
    }
}
