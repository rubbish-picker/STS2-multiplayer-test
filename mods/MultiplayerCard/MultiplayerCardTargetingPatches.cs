using System.Linq;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;

namespace MultiplayerCard;

[HarmonyPatch(typeof(CardModel), nameof(CardModel.IsValidTarget))]
public static class MultiplayerCardTargetingPatches
{
    private static void Postfix(CardModel __instance, Creature? target, ref bool __result)
    {
        if (__instance is not (ZeroSum or YouSoSelfish))
        {
            return;
        }

        Player? owner = __instance.Owner;
        if (owner == null)
        {
            __result = false;
            return;
        }

        bool hasAliveTeammate = owner.RunState?.Players.Any(player => player != owner && player.Creature != null && player.Creature.IsAlive) ?? false;
        if (!hasAliveTeammate)
        {
            return;
        }

        __result = target != null
            && target.IsAlive
            && target.Player != null
            && target.Player != owner;
    }
}
