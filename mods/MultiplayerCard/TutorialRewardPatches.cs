using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Rewards;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;

namespace MultiplayerCard;

[HarmonyPatch]
public static class TutorialRewardPatches
{
    private static readonly AccessTools.FieldRef<CardReward, List<CardCreationResult>> CardsRef =
        AccessTools.FieldRefAccess<CardReward, List<CardCreationResult>>("_cards");
    private static readonly AccessTools.FieldRef<CardReward, bool> CardsWereManuallySetRef =
        AccessTools.FieldRefAccess<CardReward, bool>("_cardsWereManuallySet");

    [HarmonyPatch(typeof(CardReward), nameof(CardReward.Populate))]
    [HarmonyPostfix]
    private static void InjectModCardsIntoManualEncounterRewards(CardReward __instance)
    {
        if (!CardsWereManuallySetRef(__instance))
        {
            return;
        }

        Player player = __instance.Player;
        CardCreationOptions? options = Traverse.Create(__instance).Property("Options").GetValue<CardCreationOptions>();

        if (options == null)
        {
            MainFile.Logger.Warn("[MultiplayerCard] Could not read CardReward.Options while patching manual encounter reward.");
            return;
        }

        if (!MultiplayerCardConfigService.ShouldInjectHighProbabilityReward(player, options))
        {
            return;
        }

        List<CardCreationResult> results = CardsRef(__instance);
        if (results.Count == 0 || results.Any(result => MultiplayerCardConfigService.IsOurCard(result.Card)))
        {
            return;
        }

        double chance = MultiplayerCardConfigService.GetHighProbabilityRewardChance();
        if (player.PlayerRng.Rewards.NextFloat() > chance)
        {
            return;
        }

        int index = (int)(player.PlayerRng.Rewards.NextFloat() * results.Count);
        if (index >= results.Count)
        {
            index = results.Count - 1;
        }

        HashSet<ModelId> usedIds = results.Select(result => result.Card.Id).ToHashSet();
        CardModel? replacementCanonical = SelectRewardCandidate(results[index].Card.Rarity, usedIds);
        if (replacementCanonical == null)
        {
            return;
        }

        CardModel replacement = player.RunState.CreateCard(replacementCanonical, player);
        if (results[index].Card.IsUpgraded && replacement.IsUpgradable)
        {
            CardCmd.Upgrade(replacement);
        }

        results[index] = new CardCreationResult(replacement);
        MainFile.Logger.Info($"[MultiplayerCard] Injected mod card into manual encounter reward for player {player.NetId}: {replacement.Title}");
    }

    private static CardModel? SelectRewardCandidate(CardRarity replacedRarity, HashSet<ModelId> usedIds)
    {
        IReadOnlyList<CardModel> candidates = MultiplayerCardConfigService.GetModColorlessCards();
        IEnumerable<CardModel> filtered = candidates.Where(card => !usedIds.Contains(card.Id));
        List<CardModel> matchingRarity = filtered.Where(card => card.Rarity == replacedRarity).ToList();
        if (matchingRarity.Count > 0)
        {
            return matchingRarity[0];
        }

        return filtered.FirstOrDefault() ?? candidates.FirstOrDefault();
    }
}
