using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;

namespace AiEvent;

public static class AiEventEffectCatalog
{
    private static readonly HashSet<string> SupportedEffectTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "gain_gold",
        "lose_gold",
        "heal",
        "damage_self",
        "gain_max_hp",
        "lose_max_hp",
        "upgrade_cards",
        "upgrade_random",
        "remove_cards",
        "add_curse",
        "obtain_random_relic",
    };

    private static readonly IReadOnlyDictionary<string, Func<CardModel>> CurseFactories =
        new Dictionary<string, Func<CardModel>>(StringComparer.OrdinalIgnoreCase)
        {
            ["BAD_LUCK"] = () => ModelDb.Card<BadLuck>(),
            ["CLUMSY"] = () => ModelDb.Card<Clumsy>(),
            ["CURSE_OF_THE_BELL"] = () => ModelDb.Card<CurseOfTheBell>(),
            ["DEBT"] = () => ModelDb.Card<Debt>(),
            ["DECAY"] = () => ModelDb.Card<Decay>(),
            ["DOUBT"] = () => ModelDb.Card<Doubt>(),
            ["ENTHRALLED"] = () => ModelDb.Card<Enthralled>(),
            ["FOLLY"] = () => ModelDb.Card<Folly>(),
            ["GREED"] = () => ModelDb.Card<Greed>(),
            ["INJURY"] = () => ModelDb.Card<Injury>(),
            ["REGRET"] = () => ModelDb.Card<Regret>(),
            ["SHAME"] = () => ModelDb.Card<Shame>(),
            ["SPORE_MIND"] = () => ModelDb.Card<SporeMind>(),
        };

    private static readonly IReadOnlyDictionary<string, string> CurseAliases =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["CURSE_BAD_LUCK"] = "BAD_LUCK",
            ["CURSE_CLUMSY"] = "CLUMSY",
            ["CURSE_OF_THE_BELL"] = "CURSE_OF_THE_BELL",
            ["CURSE_DEBT"] = "DEBT",
            ["CURSE_DECAY"] = "DECAY",
            ["CURSE_DOUBT"] = "DOUBT",
            ["CURSE_ENTHRALLED"] = "ENTHRALLED",
            ["CURSE_FOLLY"] = "FOLLY",
            ["CURSE_GREED"] = "GREED",
            ["CURSE_INJURY"] = "INJURY",
            ["CURSE_REGRET"] = "REGRET",
            ["CURSE_SHAME"] = "SHAME",
            ["CURSE_SPORE_MIND"] = "SPORE_MIND",
        };

    public static bool IsSupported(string effectType)
    {
        return SupportedEffectTypes.Contains(effectType);
    }

    public static bool RequiresAmount(string effectType)
    {
        return effectType is "gain_gold" or "lose_gold" or "heal" or "damage_self" or "gain_max_hp" or "lose_max_hp";
    }

    public static bool RequiresCardId(string effectType)
    {
        return effectType == "add_curse";
    }

    public static string NormalizeCurseCardId(string? cardId)
    {
        string normalized = (cardId ?? string.Empty).Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return "REGRET";
        }

        if (CurseAliases.TryGetValue(normalized, out string? alias))
        {
            normalized = alias;
        }

        if (!CurseFactories.ContainsKey(normalized))
        {
            return "REGRET";
        }

        return normalized;
    }

    public static bool TryNormalizeCurseCardId(string? cardId, out string normalized)
    {
        normalized = (cardId ?? string.Empty).Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return false;
        }

        if (CurseAliases.TryGetValue(normalized, out string? alias))
        {
            normalized = alias;
        }

        return CurseFactories.ContainsKey(normalized);
    }

    public static IReadOnlyList<string> GetSupportedCurseCardIds()
    {
        return CurseFactories.Keys.OrderBy(static value => value).ToList();
    }

    public static CardModel? TryGetCurseCard(string cardId)
    {
        string normalized = NormalizeCurseCardId(cardId);
        return CurseFactories.TryGetValue(normalized, out Func<CardModel>? factory) ? factory() : null;
    }

    public static string GetCurseDisplayName(string cardId, string language)
    {
        CardModel? curse = TryGetCurseCard(cardId);
        if (curse == null)
        {
            return NormalizeCurseCardId(cardId);
        }

        string key = curse.Id.Entry + ".title";
        if (string.Equals(language, "eng", StringComparison.OrdinalIgnoreCase) &&
            TryGetEnglishRawText("cards", key, out string englishTitle))
        {
            return englishTitle;
        }

        return curse.TitleLocString.GetRawText();
    }

    public static string NormalizeCurseMentionInText(string text, string cardId, string language)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return text;
        }

        string canonical = GetCurseDisplayName(cardId, language);
        foreach (string candidate in GetAllLocalizedCurseNames(language))
        {
            if (!string.Equals(candidate, canonical, StringComparison.OrdinalIgnoreCase))
            {
                text = text.Replace(candidate, canonical, StringComparison.OrdinalIgnoreCase);
            }
        }

        if (string.Equals(language, "eng", StringComparison.OrdinalIgnoreCase))
        {
            return text.Replace("Doubtful", canonical, StringComparison.OrdinalIgnoreCase);
        }

        return text
            .Replace("迟疑", canonical, StringComparison.Ordinal)
            .Replace("后悔", canonical, StringComparison.Ordinal)
            .Replace("懊悔", canonical, StringComparison.Ordinal)
            .Replace("羞愧", canonical, StringComparison.Ordinal);
    }

    public static AiLocalizedOptionText NormalizeCurseTextFromEffects(
        AiLocalizedOptionText optionText,
        AiEventOptionPayload optionPayload,
        string language)
    {
        if (optionPayload.Effects == null || optionPayload.Effects.Count == 0)
        {
            return optionText;
        }

        AiLocalizedOptionText normalized = new()
        {
            Key = optionText.Key,
            Title = optionText.Title,
            Description = optionText.Description,
            ResultDescription = optionText.ResultDescription,
        };

        foreach (AiEventEffectPayload effect in optionPayload.Effects.Where(effect =>
                     string.Equals(effect.Type, "add_curse", StringComparison.OrdinalIgnoreCase)))
        {
            normalized.Title = NormalizeCurseMentionInText(normalized.Title, effect.CardId, language);
            normalized.Description = RewriteAddCurseDescription(normalized.Description, effect.CardId, effect.Count, language);
            normalized.ResultDescription = NormalizeCurseMentionInText(normalized.ResultDescription, effect.CardId, language);
        }

        return normalized;
    }

    private static IEnumerable<string> GetAllLocalizedCurseNames(string language)
    {
        foreach (string cardId in CurseFactories.Keys)
        {
            yield return GetCurseDisplayName(cardId, language);
        }
    }

    private static bool TryGetEnglishRawText(string table, string key, out string value)
    {
        value = string.Empty;
        if (LocManager.Instance == null)
        {
            return false;
        }

        System.Reflection.FieldInfo? field = typeof(LocManager).GetField("_engTables", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        if (field?.GetValue(LocManager.Instance) is not Dictionary<string, LocTable> engTables)
        {
            return false;
        }

        if (!engTables.TryGetValue(table, out LocTable? locTable))
        {
            return false;
        }

        value = locTable.GetRawText(key);
        return !string.IsNullOrWhiteSpace(value);
    }

    private static string RewriteAddCurseDescription(string text, string cardId, int count, string language)
    {
        string canonicalSentence = BuildAddCurseSentence(cardId, count, language);
        if (string.IsNullOrWhiteSpace(text))
        {
            return canonicalSentence;
        }

        string normalized = NormalizeCurseMentionInText(text, cardId, language);
        List<string> sentences = SplitSentences(normalized, language);
        for (int i = 0; i < sentences.Count; i++)
        {
            if (!LooksLikeAddCurseSentence(sentences[i], language))
            {
                continue;
            }

            sentences[i] = canonicalSentence;
            return JoinSentences(sentences, language);
        }

        if (ContainsAnyCurseToken(normalized, language))
        {
            return ReplaceFirstCurseHighlight(normalized, cardId, language);
        }

        return AppendSentence(normalized, canonicalSentence, language);
    }

    private static string BuildAddCurseSentence(string cardId, int count, string language)
    {
        string name = GetCurseDisplayName(cardId, language);
        string highlighted = $"[red]{name}[/red]";
        int normalizedCount = Math.Max(1, count);

        if (string.Equals(language, "eng", StringComparison.OrdinalIgnoreCase))
        {
            return normalizedCount == 1
                ? $"Add {highlighted} to your [gold]Deck[/gold]."
                : $"Add [blue]{normalizedCount}[/blue] {highlighted} to your [gold]Deck[/gold].";
        }

        return normalizedCount == 1
            ? $"将一张{highlighted}加入你的[gold]牌组[/gold]。"
            : $"将[blue]{normalizedCount}[/blue]张{highlighted}加入你的[gold]牌组[/gold]。";
    }

    private static bool LooksLikeAddCurseSentence(string sentence, string language)
    {
        if (string.IsNullOrWhiteSpace(sentence))
        {
            return false;
        }

        if (ContainsAnyCurseToken(sentence, language))
        {
            return true;
        }

        if (string.Equals(language, "eng", StringComparison.OrdinalIgnoreCase))
        {
            return sentence.Contains("deck", StringComparison.OrdinalIgnoreCase)
                && (sentence.Contains("add", StringComparison.OrdinalIgnoreCase)
                    || sentence.Contains("receive", StringComparison.OrdinalIgnoreCase)
                    || sentence.Contains("curse", StringComparison.OrdinalIgnoreCase));
        }

        return (sentence.Contains("牌组", StringComparison.Ordinal)
                || sentence.Contains("牌库", StringComparison.Ordinal))
            && (sentence.Contains("加入", StringComparison.Ordinal)
                || sentence.Contains("得到", StringComparison.Ordinal)
                || sentence.Contains("获得", StringComparison.Ordinal)
                || sentence.Contains("诅咒", StringComparison.Ordinal));
    }

    private static bool ContainsAnyCurseToken(string text, string language)
    {
        return GetAllCurseTokens(language).Any(token =>
            !string.IsNullOrWhiteSpace(token) &&
            text.Contains(token, StringComparison.OrdinalIgnoreCase));
    }

    private static IEnumerable<string> GetAllCurseTokens(string language)
    {
        foreach (string cardId in CurseFactories.Keys)
        {
            yield return cardId;
            yield return GetCurseDisplayName(cardId, language);
        }

        foreach ((string alias, string canonical) in CurseAliases)
        {
            yield return alias;
            yield return GetCurseDisplayName(canonical, language);
        }
    }

    private static string ReplaceFirstCurseHighlight(string text, string cardId, string language)
    {
        string replacement = $"[red]{GetCurseDisplayName(cardId, language)}[/red]";
        MatchCollection matches = Regex.Matches(text, @"\[(?<color>red|purple)\](?<name>.*?)\[/\k<color>\]", RegexOptions.IgnoreCase);
        foreach (Match match in matches)
        {
            string name = match.Groups["name"].Value;
            if (!ContainsAnyCurseToken(name, language))
            {
                continue;
            }

            return text.Remove(match.Index, match.Length).Insert(match.Index, replacement);
        }

        return text;
    }

    private static List<string> SplitSentences(string text, string language)
    {
        string separator = string.Equals(language, "eng", StringComparison.OrdinalIgnoreCase) ? "." : "。";
        return text
            .Split(new[] { separator }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(sentence => sentence + separator)
            .ToList();
    }

    private static string JoinSentences(List<string> sentences, string language)
    {
        string separator = string.Equals(language, "eng", StringComparison.OrdinalIgnoreCase) ? " " : string.Empty;
        return string.Join(separator, sentences.Where(sentence => !string.IsNullOrWhiteSpace(sentence)).Select(sentence => sentence.Trim()));
    }

    private static string AppendSentence(string text, string sentence, string language)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return sentence;
        }

        if (string.Equals(language, "eng", StringComparison.OrdinalIgnoreCase))
        {
            return text.TrimEnd() + " " + sentence;
        }

        return text.TrimEnd() + sentence;
    }
}
