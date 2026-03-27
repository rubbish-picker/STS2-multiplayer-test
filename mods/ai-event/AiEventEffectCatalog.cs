using System;
using System.Collections.Generic;
using MegaCrit.Sts2.Core.Entities.Cards;
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

    public static CardModel? TryGetCurseCard(string cardId)
    {
        return CurseFactories.TryGetValue(cardId, out Func<CardModel>? factory) ? factory() : null;
    }
}
