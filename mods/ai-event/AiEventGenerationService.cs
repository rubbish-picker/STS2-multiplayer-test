using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
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
        if (MegaCrit.Sts2.Core.Localization.LocManager.Instance != null)
        {
            AiEventLocalization.ApplyCurrentLanguage();
        }
    }

    public static bool HasApiConfig()
    {
        AiEventRuntimeConfig config = AiEventConfigService.GetEffectiveConfig();
        return !string.IsNullOrWhiteSpace(config.BaseUrl)
            && !string.IsNullOrWhiteSpace(config.ApiKey)
            && !string.IsNullOrWhiteSpace(config.Model);
    }

    public static async Task<string?> ValidateConnectivityAsync(CancellationToken cancellationToken = default)
    {
        AiEventConfigService.Reload();

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
                Math.Min(Math.Max(15, AiEventConfigService.GetEffectiveConfig().RequestTimeoutSeconds), 20),
                32,
                cancellationToken);

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

    public static async Task<List<AiEventThemePlan>> GenerateThemesAsync(IReadOnlyList<AiEventSlot> slots, string? seed, CancellationToken cancellationToken = default)
    {
        if (slots.Count == 0)
        {
            return new List<AiEventThemePlan>();
        }

        try
        {
            string systemPrompt = "You design concise, distinct Slay the Spire 2 event themes. Return only JSON.";
            string userPrompt =
                "Generate one short, distinct theme plan for each requested Slay the Spire 2 event slot.\n" +
                $"Run seed: `{seed ?? "UNKNOWN"}`.\n" +
                "Return a JSON array. Every item must contain `slot`, `theme`, `option_count`, and `reward_profile`.\n" +
                "Rules:\n" +
                "- Themes must be mechanically and narratively distinct.\n" +
                "- Keep each theme under 18 Chinese characters and under 10 English words worth of meaning.\n" +
                "- Avoid repeating motifs like debt, bargains, tides, mirrors, books, or insects unless specifically required.\n" +
                "- Make them sound like event premises, not card names.\n\n" +
                "- `option_count` must be 2 or 3.\n" +
                "- `reward_profile` must be one of: `favored`, `sharp_tradeoff`, `slightly_bad`.\n" +
                "- `favored` means at least one option is purely positive, and the others can vary.\n" +
                "- `sharp_tradeoff` means at least one option has a large upside with a large downside, while the other options should be smaller tradeoffs.\n" +
                "- `slightly_bad` means at least one option is a purely small negative, while the other options should be small tradeoffs.\n\n" +
                "Requested slots:\n" +
                JsonSerializer.Serialize(slots);

            string rawContent = await SendChatCompletionAsync(systemPrompt, userPrompt, cancellationToken: cancellationToken);
            string json = ExtractJsonArray(rawContent);
            List<AiEventThemePlan>? themes = JsonSerializer.Deserialize<List<AiEventThemePlan>>(json, JsonOptions);
            if (themes == null || themes.Count == 0)
            {
                throw new InvalidOperationException("Theme generation returned no themes.");
            }

            List<AiEventThemePlan> normalized = new();
            for (int i = 0; i < slots.Count; i++)
            {
                AiEventThemePlan? source = themes.ElementAtOrDefault(i);
                string theme = source?.Theme?.Trim() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(theme))
                {
                    theme = GetFallbackTheme(slots[i], i, seed);
                }

                normalized.Add(new AiEventThemePlan
                {
                    Slot = slots[i],
                    Theme = theme,
                    OptionCount = NormalizeOptionCount(source?.OptionCount ?? 0, seed, i),
                    RewardProfile = NormalizeRewardProfile(source?.RewardProfile, seed, i),
                });
            }

            return normalized;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            MainFile.Logger.Warn($"[ai-event] failed to generate themes, falling back to deterministic themes: {ex.Message}");
            return slots.Select((slot, index) => new AiEventThemePlan
            {
                Slot = slot,
                Theme = GetFallbackTheme(slot, index, seed),
                OptionCount = NormalizeOptionCount(0, seed, index),
                RewardProfile = NormalizeRewardProfile(null, seed, index),
            }).ToList();
        }
    }

    public static async Task<AiGeneratedEventPayload> GeneratePayloadAsync(
        AiEventSlot slot,
        string? seed,
        string? theme = null,
        int? optionCount = null,
        string? rewardProfile = null,
        IReadOnlyList<string>? recentTitles = null,
        CancellationToken cancellationToken = default)
    {
        (string systemPrompt, string schema, string samples) = ReadGenerationAssets();
        string requestContext = BuildEventRequestContext(slot, seed, theme, optionCount, rewardProfile, recentTitles, schema, samples);

        string? lastError = null;
        for (int attempt = 0; attempt < 2; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            string userPrompt = requestContext;
            if (attempt > 0 && !string.IsNullOrWhiteSpace(lastError))
            {
                userPrompt +=
                    "\n\nYour previous answer was rejected. Regenerate from scratch and follow these corrections exactly:\n" +
                    lastError +
                    "\nPay extra attention to JSON validity, supported effect types, legal curse card ids, and aligned option text." +
                    "\nFor `add_curse`, prioritize a correct `card_id`; runtime will rewrite localized curse naming from the id.";
            }

            try
            {
                string rawContent = await SendChatCompletionAsync(systemPrompt, userPrompt, cancellationToken: cancellationToken);
                AiGeneratedEventPayload? payload = DeserializePayload(rawContent);
                if (payload == null)
                {
                    throw new InvalidOperationException("LLM returned empty payload.");
                }

                NormalizePayload(payload, slot);
                if (TryValidatePayload(payload, NormalizeRewardProfile(rewardProfile, seed, 0), out string validationError))
                {
                    MainFile.Logger.Info($"Generated ai-event payload for {slot}.");
                    return payload;
                }

                lastError = validationError;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                lastError = ex.Message;
            }
        }

        throw new InvalidOperationException($"Generated event failed validation after one retry. {lastError}");
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
        NormalizeLocalizedCurseMentions(payload);
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
            string rawCardId = (effect.CardId ?? string.Empty).Trim().ToUpperInvariant();
            string cardId = rawCardId;
            if (AiEventEffectCatalog.TryNormalizeCurseCardId(rawCardId, out string normalizedCardId))
            {
                cardId = normalizedCardId;
            }
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

    private static void NormalizeLocalizedCurseMentions(AiGeneratedEventPayload payload)
    {
        for (int i = 0; i < payload.Options.Count; i++)
        {
            if (i < payload.Eng.Options.Count)
            {
                payload.Eng.Options[i] = AiEventEffectCatalog.NormalizeCurseTextFromEffects(payload.Eng.Options[i], payload.Options[i], "eng");
            }

            if (i < payload.Zhs.Options.Count)
            {
                payload.Zhs.Options[i] = AiEventEffectCatalog.NormalizeCurseTextFromEffects(payload.Zhs.Options[i], payload.Options[i], "zhs");
            }
        }
    }

    private static string BuildEventRequestContext(
        AiEventSlot slot,
        string? seed,
        string? theme,
        int? optionCount,
        string? rewardProfile,
        IReadOnlyList<string>? recentTitles,
        string schema,
        string samples)
    {
        string supportedCurses = string.Join(", ", AiEventEffectCatalog.GetSupportedCurseCardIds());
        string avoidedTitles = recentTitles == null || recentTitles.Count == 0
            ? "(none)"
            : string.Join("; ", recentTitles.Where(static title => !string.IsNullOrWhiteSpace(title)).Take(12));
        int plannedOptionCount = Math.Clamp(optionCount ?? 3, 2, 3);
        string plannedRewardProfile = NormalizeRewardProfile(rewardProfile, seed, 0);

        return
            $"Generate one Slay the Spire 2 style event JSON for slot `{slot}`.\n" +
            $"The current run seed is `{seed ?? "UNKNOWN"}`.\n" +
            $"Theme for this event: `{theme ?? GetFallbackTheme(slot, 0, seed)}`.\n" +
            $"Use exactly `{plannedOptionCount}` options.\n" +
            $"Overall reward profile: `{plannedRewardProfile}`.\n" +
            "A `favored` event should have at least one clearly attractive all-upside option. A `sharp_tradeoff` event should feature one harsh but tempting option and smaller tradeoffs elsewhere. A `slightly_bad` event should include one purely small negative option and otherwise only small tradeoffs.\n" +
            "You must generate BOTH the option text and the underlying rewards/costs.\n" +
            "Only use supported effect types from the schema. Do not invent custom mechanics.\n" +
            $"For `add_curse`, only use these card ids: {supportedCurses}.\n" +
            "Do not use `CURSE_` prefixes in `card_id`.\n" +
            "For `add_curse`, getting the `card_id` correct is more important than spelling the curse name perfectly; runtime text, localized curse name, and hover tips will be normalized from the id.\n" +
            "If you are unsure about the exact localized curse name, keep the wording generic and let runtime fill the specific curse from `card_id`.\n" +
            "Output valid JSON only, with no markdown wrapper.\n" +
            "Output both `eng` and `zhs` localized text, and keep the two versions aligned.\n" +
            "Keep the event readable in-game and close to vanilla style.\n\n" +
            "Important writing rules:\n" +
            "- The opening description must be 1 to 3 short paragraphs, never longer than that.\n" +
            "- The event fiction must naturally justify the rewards and costs. Do not bolt mechanics onto unrelated prose.\n" +
            "- Option button text must accurately describe the exact effects.\n" +
            "- Result text should feel like the consequence of that exact choice.\n" +
            "- Never mention a curse, relic, card upgrade, healing, gold, or max HP change in text unless the effects really do that.\n" +
            "- When talking about Gold, the highlight tag must be spelled exactly `[gold]...[/gold]`. Do not write `[goold]`, `[golld]`, or any other variation.\n" +
            "- Only use vanilla STS2 inline markup tags. If you use tags, they must be exactly existing forms like [gold], [red], [blue], [green], [purple], [orange], [aqua], [pink], [jitter], [sine], [shake], [b], [i], [center], [thinky_dots], [font_size=22], or [rainbow freq=0.3 sat=0.8 val=1]. Never invent tags such as [goold] or malformed closing tags.\n" +
            "- Avoid making all events read like the same 3-choice bargain.\n\n" +
            "Avoid repeating or paraphrasing these recent generated event titles:\n" + avoidedTitles + "\n\n" +
            "Schema:\n" + schema + "\n\n" +
            "Vanilla samples:\n" + samples;
    }

    private static bool TryValidatePayload(AiGeneratedEventPayload payload, string rewardProfile, out string error)
    {
        if (string.IsNullOrWhiteSpace(payload.Eng?.Title) || string.IsNullOrWhiteSpace(payload.Zhs?.Title))
        {
            error = "Both eng.title and zhs.title must be present and non-empty.";
            return false;
        }

        if (!TryValidateMarkup(payload, out error))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(payload.Eng?.InitialDescription) || string.IsNullOrWhiteSpace(payload.Zhs?.InitialDescription))
        {
            error = "Both eng.initial_description and zhs.initial_description must be present and non-empty.";
            return false;
        }

        if (!HasValidInitialDescriptionLength(payload.Eng.InitialDescription) || !HasValidInitialDescriptionLength(payload.Zhs.InitialDescription))
        {
            error = "Initial description must be 1 to 3 short paragraphs, close to vanilla event length.";
            return false;
        }

        if (payload.Options == null || payload.Options.Count < 2 || payload.Options.Count > 3)
        {
            error = "The event must contain 2 or 3 runtime options.";
            return false;
        }

        if (payload.Eng?.Options == null || payload.Zhs?.Options == null)
        {
            error = "Both eng.options and zhs.options must be present.";
            return false;
        }

        if (payload.Eng.Options.Count != payload.Options.Count || payload.Zhs.Options.Count != payload.Options.Count)
        {
            error = "Localized option counts must exactly match runtime options count.";
            return false;
        }

        for (int i = 0; i < payload.Options.Count; i++)
        {
            AiEventOptionPayload option = payload.Options[i];
            if (option.Effects == null || option.Effects.Count == 0)
            {
                error = $"Option {i + 1} has no effects.";
                return false;
            }

            foreach (AiEventEffectPayload effect in option.Effects)
            {
                if (!AiEventEffectCatalog.IsSupported(effect.Type))
                {
                    error = $"Unsupported effect type: {effect.Type}.";
                    return false;
                }

                if (AiEventEffectCatalog.RequiresCardId(effect.Type) &&
                    !AiEventEffectCatalog.TryNormalizeCurseCardId(effect.CardId, out _))
                {
                    error = $"Illegal curse card id `{effect.CardId}`. Use only supported ids without CURSE_ prefix.";
                    return false;
                }
            }

            AiLocalizedOptionText engOption = payload.Eng.Options[i];
            AiLocalizedOptionText zhsOption = payload.Zhs.Options[i];
            if (string.IsNullOrWhiteSpace(engOption.Title) || string.IsNullOrWhiteSpace(engOption.Description) || string.IsNullOrWhiteSpace(engOption.ResultDescription))
            {
                error = $"English option text for option {i + 1} is incomplete.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(zhsOption.Title) || string.IsNullOrWhiteSpace(zhsOption.Description) || string.IsNullOrWhiteSpace(zhsOption.ResultDescription))
            {
                error = $"Chinese option text for option {i + 1} is incomplete.";
                return false;
            }

            if (!TryValidateOptionTextMatchesEffects(option, engOption, zhsOption, out error))
            {
                return false;
            }
        }

        if (!TryValidateRewardProfile(payload.Options, rewardProfile, out error))
        {
            return false;
        }

        error = string.Empty;
        return true;
    }

    private static bool TryValidateMarkup(AiGeneratedEventPayload payload, out string error)
    {
        foreach ((string fieldName, string text) in EnumerateMarkupFields(payload))
        {
            if (AiEventMarkup.TryValidateText(text, out string markupError))
            {
                continue;
            }

            error = $"Field `{fieldName}` uses invalid STS2 markup. {markupError}";
            return false;
        }

        error = string.Empty;
        return true;
    }

    private static IEnumerable<(string FieldName, string Text)> EnumerateMarkupFields(AiGeneratedEventPayload payload)
    {
        yield return ("eng.title", payload.Eng.Title);
        yield return ("eng.initial_description", payload.Eng.InitialDescription);
        yield return ("zhs.title", payload.Zhs.Title);
        yield return ("zhs.initial_description", payload.Zhs.InitialDescription);

        for (int i = 0; i < payload.Eng.Options.Count; i++)
        {
            yield return ($"eng.options[{i}].title", payload.Eng.Options[i].Title);
            yield return ($"eng.options[{i}].description", payload.Eng.Options[i].Description);
            yield return ($"eng.options[{i}].result_description", payload.Eng.Options[i].ResultDescription);
        }

        for (int i = 0; i < payload.Zhs.Options.Count; i++)
        {
            yield return ($"zhs.options[{i}].title", payload.Zhs.Options[i].Title);
            yield return ($"zhs.options[{i}].description", payload.Zhs.Options[i].Description);
            yield return ($"zhs.options[{i}].result_description", payload.Zhs.Options[i].ResultDescription);
        }
    }

    private static bool HasValidInitialDescriptionLength(string text)
    {
        string[] paragraphs = text
            .Split(new[] { "\r\n\r\n", "\n\n" }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (paragraphs.Length < 1 || paragraphs.Length > 3)
        {
            return false;
        }

        return paragraphs.All(paragraph => paragraph.Length <= 220);
    }

    private static bool TryValidateRewardProfile(List<AiEventOptionPayload> options, string rewardProfile, out string error)
    {
        string normalized = NormalizeRewardProfile(rewardProfile, null, 0);
        List<OptionImpactProfile> profiles = options.Select(BuildImpactProfile).ToList();

        switch (normalized)
        {
            case "favored":
                if (!profiles.Any(static profile => profile.IsPurePositive))
                {
                    error = "Reward profile `favored` requires at least one purely positive option.";
                    return false;
                }

                error = string.Empty;
                return true;

            case "sharp_tradeoff":
                if (!profiles.Any(static profile => profile.IsBigTradeoff))
                {
                    error = "Reward profile `sharp_tradeoff` requires at least one option with a big upside and a big downside.";
                    return false;
                }

                if (profiles.Any(profile => !profile.IsBigTradeoff && !profile.IsSmallTradeoff))
                {
                    error = "Reward profile `sharp_tradeoff` requires the remaining options to be smaller tradeoffs.";
                    return false;
                }

                error = string.Empty;
                return true;

            case "slightly_bad":
                if (!profiles.Any(static profile => profile.IsPureSmallNegative))
                {
                    error = "Reward profile `slightly_bad` requires at least one purely small negative option.";
                    return false;
                }

                if (profiles.Any(profile => !profile.IsPureSmallNegative && !profile.IsSmallTradeoff))
                {
                    error = "Reward profile `slightly_bad` requires the remaining options to be small tradeoffs.";
                    return false;
                }

                error = string.Empty;
                return true;

            default:
                error = string.Empty;
                return true;
        }
    }

    private static OptionImpactProfile BuildImpactProfile(AiEventOptionPayload option)
    {
        int positiveScore = 0;
        int negativeScore = 0;
        bool hasPositive = false;
        bool hasNegative = false;

        foreach (AiEventEffectPayload effect in option.Effects)
        {
            switch (effect.Type)
            {
                case "gain_gold":
                    positiveScore += effect.Amount >= 90 ? 2 : 1;
                    hasPositive = true;
                    break;
                case "heal":
                    positiveScore += effect.Amount >= 10 ? 2 : 1;
                    hasPositive = true;
                    break;
                case "gain_max_hp":
                    positiveScore += 2;
                    hasPositive = true;
                    break;
                case "upgrade_cards":
                case "upgrade_random":
                    positiveScore += effect.Count >= 2 ? 2 : 1;
                    hasPositive = true;
                    break;
                case "remove_cards":
                    positiveScore += effect.Count >= 2 ? 3 : 2;
                    hasPositive = true;
                    break;
                case "obtain_random_relic":
                    positiveScore += effect.Count >= 2 ? 4 : 3;
                    hasPositive = true;
                    break;
                case "lose_gold":
                    negativeScore += effect.Amount >= 60 ? 2 : 1;
                    hasNegative = true;
                    break;
                case "damage_self":
                    negativeScore += effect.Amount >= 10 ? 2 : 1;
                    hasNegative = true;
                    break;
                case "lose_max_hp":
                    negativeScore += effect.Amount >= 4 ? 3 : 2;
                    hasNegative = true;
                    break;
                case "add_curse":
                    negativeScore += effect.Count >= 2 ? 2 : 1;
                    hasNegative = true;
                    break;
            }
        }

        return new OptionImpactProfile(
            hasPositive && !hasNegative,
            !hasPositive && hasNegative && negativeScore <= 1,
            hasPositive && hasNegative && positiveScore >= 3 && negativeScore >= 2,
            hasPositive && hasNegative && positiveScore >= 1 && positiveScore <= 3 && negativeScore >= 1 && negativeScore <= 2);
    }

    private static bool TryValidateOptionTextMatchesEffects(
        AiEventOptionPayload option,
        AiLocalizedOptionText engOption,
        AiLocalizedOptionText zhsOption,
        out string error)
    {
        string engText = $"{engOption.Title} {engOption.Description} {engOption.ResultDescription}".ToLowerInvariant();
        string zhsText = $"{zhsOption.Title} {zhsOption.Description} {zhsOption.ResultDescription}";

        foreach (AiEventEffectPayload effect in option.Effects)
        {
            if (!EffectLooksMentioned(effect, engText, zhsText))
            {
                error = $"Option `{option.Key}` text does not appear to match effect `{effect.Type}` closely enough.";
                return false;
            }
        }

        error = string.Empty;
        return true;
    }

    private static bool EffectLooksMentioned(AiEventEffectPayload effect, string engText, string zhsText)
    {
        static bool HasAny(string text, params string[] needles) => needles.Any(text.Contains);

        if (string.Equals(effect.Type, "add_curse", StringComparison.OrdinalIgnoreCase))
        {
            return AiEventEffectCatalog.TryNormalizeCurseCardId(effect.CardId, out _);
        }

        return effect.Type switch
        {
            "gain_gold" or "lose_gold" => HasAny(engText, "gold", "coin") || HasAny(zhsText, "金币", "金钱", "钱"),
            "heal" => HasAny(engText, "heal", "recover", "hp") || HasAny(zhsText, "回复", "恢复", "治疗", "生命"),
            "damage_self" => HasAny(engText, "lose", "damage", "hp", "bleed", "blood") || HasAny(zhsText, "失去", "受到", "生命", "流血", "血"),
            "gain_max_hp" or "lose_max_hp" => HasAny(engText, "max hp", "maximum hp") || HasAny(zhsText, "最大生命"),
            "upgrade_cards" or "upgrade_random" => HasAny(engText, "upgrade") || HasAny(zhsText, "升级"),
            "remove_cards" => HasAny(engText, "remove", "purge") || HasAny(zhsText, "移除", "删除"),
            "obtain_random_relic" => HasAny(engText, "relic") || HasAny(zhsText, "遗物"),
            "add_curse" => HasAny(engText, "curse", effect.CardId.ToLowerInvariant()) ||
                           HasAny(zhsText, "诅咒", "懊悔", "羞愧", "笨拙", "疑虑", "腐坏", "受伤", "贪婪", "愚行"),
            _ => true,
        };
    }

    private static AiGeneratedEventPayload? DeserializePayload(string rawContent)
    {
        string json = ExtractJsonObject(rawContent);
        return JsonSerializer.Deserialize<AiGeneratedEventPayload>(json, JsonOptions);
    }

    private static string ExtractJsonArray(string rawContent)
    {
        int start = rawContent.IndexOf('[');
        int end = rawContent.LastIndexOf(']');
        if (start < 0 || end < start)
        {
            return rawContent;
        }

        return rawContent.Substring(start, end - start + 1);
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
        int? maxTokensOverride = null,
        CancellationToken cancellationToken = default)
    {
        AiEventRuntimeConfig config = AiEventConfigService.GetEffectiveConfig();
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
        using HttpResponseMessage response = await client.SendAsync(request, cancellationToken);
        string responseText = await response.Content.ReadAsStringAsync(cancellationToken);
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

    private static string GetFallbackTheme(AiEventSlot slot, int index, string? seed)
    {
        string[] themes = slot switch
        {
            AiEventSlot.Overgrowth => new[]
            {
                "吞噬性的丰收",
                "会呼吸的祭坛",
                "寄生的馈赠",
                "发芽的契约",
                "腐叶下的承诺",
            },
            AiEventSlot.Hive => new[]
            {
                "过度整齐的分工",
                "蜂巢税契",
                "琥珀里的命令",
                "幼虫保管人",
                "错位的群体意志",
            },
            AiEventSlot.Glory => new[]
            {
                "夸耀的代价",
                "镀金的忏悔",
                "仪式化的喝彩",
                "虚荣陈列柜",
                "荣耀借据",
            },
            AiEventSlot.Underdocks => new[]
            {
                "潮水记录员",
                "浸水的赎买",
                "锈蚀船票",
                "雾港对赌",
                "沉底的清单",
            },
            _ => new[]
            {
                "不该出现的旁观者",
                "交换条件外的附注",
                "迟来的见证",
                "误投的贡品",
                "被删去的一页",
            },
        };

        int offset = Math.Abs(HashCode.Combine(slot, seed ?? string.Empty, index));
        return themes[offset % themes.Length];
    }

    private static int NormalizeOptionCount(int optionCount, string? seed, int index)
    {
        if (optionCount is 2 or 3)
        {
            return optionCount;
        }

        return Math.Abs(HashCode.Combine(seed ?? string.Empty, index)) % 2 == 0 ? 2 : 3;
    }

    private static string NormalizeRewardProfile(string? rewardProfile, string? seed, int index)
    {
        string normalized = (rewardProfile ?? string.Empty).Trim().ToLowerInvariant();
        if (normalized is "favored" or "sharp_tradeoff" or "slightly_bad")
        {
            return normalized;
        }

        string[] fallbacks = { "favored", "sharp_tradeoff", "slightly_bad" };
        int offset = Math.Abs(HashCode.Combine(seed ?? string.Empty, index, rewardProfile ?? string.Empty));
        return fallbacks[offset % fallbacks.Length];
    }

    private sealed record OptionImpactProfile(
        bool IsPurePositive,
        bool IsPureSmallNegative,
        bool IsBigTradeoff,
        bool IsSmallTradeoff);

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
