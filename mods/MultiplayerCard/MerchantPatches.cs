using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Factories;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.CardPools;

namespace MultiplayerCard;

[HarmonyPatch(typeof(CardFactory))]
public static class MerchantPatches
{
    [HarmonyPatch(nameof(CardFactory.CreateForMerchant), typeof(Player), typeof(IEnumerable<CardModel>), typeof(CardRarity))]
    [HarmonyPrefix]
    private static bool ForceTestCardsInColorlessShop(
        Player player,
        IEnumerable<CardModel> options,
        CardRarity rarity,
        ref CardCreationResult __result)
    {
        List<CardModel> optionList = options.ToList();

        if (!optionList.Any(card => card.Pool is ColorlessCardPool))
        {
            return true;
        }

        CardModel? forcedCard = rarity switch
        {
            CardRarity.Uncommon => optionList.FirstOrDefault(card => card is YouSoSelfish),
            CardRarity.Rare => optionList.FirstOrDefault(card => card is ZeroSum),
            _ => null
        };

        if (forcedCard == null)
        {
            return true;
        }

        __result = new CardCreationResult(player.RunState.CreateCard(forcedCard, player));
        return false;
    }
}
