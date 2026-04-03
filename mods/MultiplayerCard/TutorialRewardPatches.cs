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

        List<CardCreationResult> results = CardsRef(__instance);
        if (results.Count == 0)
        {
            return;
        }

        if (TryInjectDebugRewardCard(player, options, results))
        {
            MainFile.Logger.Info($"[MultiplayerCard] Injected configured debug reward card for player {player.NetId}.");
            return;
        }

        if (!MultiplayerCardConfigService.ShouldInjectHighProbabilityReward(player, options))
        {
            return;
        }

        if (results.Any(result => MultiplayerCardConfigService.IsOurColorlessCard(result.Card)))
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
        CardModel? replacementCanonical = SelectRewardCandidate(usedIds);
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
