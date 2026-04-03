using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Factories;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.CardPools;
using MegaCrit.Sts2.Core.Runs;

namespace MultiplayerCard;

[HarmonyPatch]
public static class CardDistributionPatches
{
    [HarmonyPatch(typeof(CardPoolModel), nameof(CardPoolModel.GetUnlockedCards))]
    [HarmonyPostfix]
    private static void FilterModCardsByMode(
        CardPoolModel __instance,
        CardMultiplayerConstraint multiplayerConstraint,
        ref IEnumerable<CardModel> __result)
    {
        if (!__instance.IsColorless)
        {
            return;
        }

        List<CardModel> cards = __result.ToList();
        if (!cards.Any(MultiplayerCardConfigService.IsOurColorlessCard))
        {
            return;
        }

        bool allowModCards = MultiplayerCardConfigService.ShouldAllowModCardsForConstraint(multiplayerConstraint);
        __result = allowModCards
            ? cards
            : cards.Where(card => !MultiplayerCardConfigService.IsOurColorlessCard(card)).ToList();
    }

    [HarmonyPatch(typeof(CardFactory), nameof(CardFactory.CreateForReward), typeof(Player), typeof(int), typeof(CardCreationOptions))]
    [HarmonyPostfix]
    private static void InjectModCardsIntoEncounterRewards(
        Player player,
        CardCreationOptions options,
        ref IEnumerable<CardCreationResult> __result)
    {
        List<CardCreationResult> results = __result.ToList();
        if (results.Count == 0)
        {
            return;
        }

        if (TryInjectDebugRewardCard(player, options, results))
        {
            __result = results;
            return;
        }

        if (!MultiplayerCardConfigService.ShouldInjectHighProbabilityReward(player, options))
        {
            return;
        }

        double chance = MultiplayerCardConfigService.GetHighProbabilityRewardChance();
        HashSet<ModelId> usedIds = results.Select(result => result.Card.Id).ToHashSet();

        if (results.Any(result => MultiplayerCardConfigService.IsOurColorlessCard(result.Card)))
        {
            __result = results;
            return;
        }

        if (player.PlayerRng.Rewards.NextFloat() > chance)
        {
            __result = results;
            return;
        }

        int index = (int)(player.PlayerRng.Rewards.NextFloat() * results.Count);
        if (index >= results.Count)
        {
            index = results.Count - 1;
        }

        CardModel? canonicalCard = SelectRewardCandidate(usedIds);
        if (canonicalCard == null)
        {
            __result = results;
            return;
        }

        CardModel replacement = player.RunState.CreateCard(canonicalCard, player);
        if (results[index].Card.IsUpgraded && replacement.IsUpgradable)
        {
            CardCmd.Upgrade(replacement);
        }

        results[index] = new CardCreationResult(replacement);

        __result = results;
    }

    private static bool TryInjectDebugRewardCard(
        Player player,
        CardCreationOptions options,
        List<CardCreationResult> results)
    {
        if (!MultiplayerCardConfigService.ShouldForceDebugRewardCard(player, options))
        {
            return false;
        }

        CardModel? canonicalCard = MultiplayerCardConfigService.GetConfiguredDebugRewardCard();
        if (canonicalCard == null)
        {
            return false;
        }

        if (results.Any(result => result.Card.Id == canonicalCard.Id))
        {
            return false;
        }

        int index = results.FindIndex(result => result.Card.Rarity == canonicalCard.Rarity);
        if (index < 0)
        {
            index = results.Count - 1;
        }

        CardModel replacement = player.RunState.CreateCard(canonicalCard, player);
        if (results[index].Card.IsUpgraded && replacement.IsUpgradable)
        {
            CardCmd.Upgrade(replacement);
        }

        results[index] = new CardCreationResult(replacement);
        return true;
    }

    private static CardModel? SelectRewardCandidate(HashSet<ModelId> usedIds)
    {
        CardModel? configuredHighProbabilityCard = MultiplayerCardConfigService.GetConfiguredHighProbabilityRewardCard();
        if (configuredHighProbabilityCard != null && !usedIds.Contains(configuredHighProbabilityCard.Id))
        {
            return configuredHighProbabilityCard;
        }
        
        return null;
    }
}
