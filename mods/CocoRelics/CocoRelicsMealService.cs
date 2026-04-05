using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Relics;

namespace CocoRelics;

public static class CocoRelicsMealService
{
    private static readonly HashSet<ulong> BusyPlayers = new();
    private static readonly HashSet<ulong> PendingPlayers = new();

    public static void TryQueueFusion(Player player, RelicModel? obtainedRelic = null)
    {
        // Only real obtains of the fusion inputs should enqueue fusion work.
        if (obtainedRelic is not null && obtainedRelic is not ZeduCoco && obtainedRelic is not VeryHotCocoa)
        {
            return;
        }

        if (BusyPlayers.Contains(player.NetId) || !PendingPlayers.Add(player.NetId))
        {
            return;
        }

        TaskHelper.RunSafely(TryFuseDeferredAsync(player));
    }

    public static async Task TransferBigMealAsync(BigMeal meal, Player inheritor)
    {
        Player previousOwner = meal.Owner;
        if (!BusyPlayers.Add(previousOwner.NetId))
        {
            return;
        }

        BusyPlayers.Add(inheritor.NetId);
        try
        {
            int targetIndex = inheritor.Relics.Count;
            await RelicCmd.Remove(meal);
            BigMeal newMeal = (BigMeal)ModelDb.Relic<BigMeal>().ToMutable();
            await RelicCmd.Obtain(newMeal, inheritor, targetIndex);
            MainFile.Logger.Info($"Transferred BigMeal from {previousOwner.NetId} to {inheritor.NetId}.");
        }
        finally
        {
            BusyPlayers.Remove(previousOwner.NetId);
            BusyPlayers.Remove(inheritor.NetId);
        }
    }

    private static async Task FuseIntoBigMealAsync(Player player, ZeduCoco zeduCoco, VeryHotCocoa veryHotCocoa)
    {
        if (!BusyPlayers.Add(player.NetId))
        {
            return;
        }

        try
        {
            List<RelicModel> relics = player.Relics.ToList();
            int targetIndex = System.Math.Min(relics.IndexOf(zeduCoco), relics.IndexOf(veryHotCocoa));
            await RelicCmd.Remove(zeduCoco);
            await RelicCmd.Remove(veryHotCocoa);
            BigMeal meal = (BigMeal)ModelDb.Relic<BigMeal>().ToMutable();
            await RelicCmd.Obtain(meal, player, targetIndex);
            MainFile.Logger.Info($"Fused ZeduCoco + VeryHotCocoa into BigMeal for player {player.NetId}.");
        }
        finally
        {
            BusyPlayers.Remove(player.NetId);
        }
    }

    private static async Task TryFuseDeferredAsync(Player player)
    {
        try
        {
            await Task.Yield();

            if (player.GetRelic<BigMeal>() != null)
            {
                return;
            }

            ZeduCoco? zeduCoco = player.GetRelic<ZeduCoco>();
            VeryHotCocoa? veryHotCocoa = player.GetRelic<VeryHotCocoa>();
            if (zeduCoco == null || veryHotCocoa == null)
            {
                return;
            }

            await FuseIntoBigMealAsync(player, zeduCoco, veryHotCocoa);
        }
        finally
        {
            PendingPlayers.Remove(player.NetId);
        }
    }
}
