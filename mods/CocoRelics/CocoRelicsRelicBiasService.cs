using System.Collections.Generic;
using System.Linq;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Random;

namespace CocoRelics;

public static class CocoRelicsRelicBiasService
{
    public static bool TryPullBiasedRewardRelic(Player player, RelicRarity rarity, out RelicModel relic)
    {
        return TryPullBiasedRelic(player, rarity, blacklist: null, player.PlayerRng.Rewards, CocoRelicsConfigService.GetHighProbabilityBonusChance(), out relic);
    }

    public static bool TryPullBiasedShopRelic(Player player, RelicRarity rarity, IEnumerable<RelicModel> blacklist, out RelicModel relic)
    {
        return TryPullBiasedRelic(player, rarity, blacklist, player.PlayerRng.Shops, CocoRelicsConfigService.GetHighProbabilityBonusChance(), out relic);
    }

    private static bool TryPullBiasedRelic(Player player, RelicRarity rarity, IEnumerable<RelicModel>? blacklist, Rng rng, float chance, out RelicModel relic)
    {
        relic = null!;
        if (!CocoRelicsConfigService.IsHighProbabilityMode() || player.GetRelic<BigMeal>() != null)
        {
            return false;
        }

        HashSet<ModelId> blacklistIds = blacklist?
            .Where(static item => item != null)
            .Select(static item => item.Id)
            .ToHashSet() ?? new HashSet<ModelId>();

        List<RelicModel> candidates = GetDesiredRelics(player)
            .Where(candidate => !blacklistIds.Contains(candidate.Id))
            .Where(candidate => player.RelicGrabBag.Contains(candidate))
            .ToList();
        if (candidates.Count == 0)
        {
            return false;
        }

        if (rng.NextFloat() > chance)
        {
            return false;
        }

        RelicModel candidate = candidates.Count == 1 ? candidates[0] : rng.NextItem(candidates)!;
        player.RelicGrabBag.Remove(candidate);
        player.RunState.SharedRelicGrabBag.Remove(candidate);
        relic = candidate;
        MainFile.Logger.Info($"Biased relic pull for player {player.NetId}: {candidate.Id} (requested rarity={rarity}).");
        return true;
    }

    private static IEnumerable<RelicModel> GetDesiredRelics(Player player)
    {
        if (player.GetRelic<ZeduCoco>() == null)
        {
            yield return ModelDb.Relic<ZeduCoco>();
        }

        if (player.GetRelic<VeryHotCocoa>() == null)
        {
            yield return ModelDb.Relic<VeryHotCocoa>();
        }
    }
}
