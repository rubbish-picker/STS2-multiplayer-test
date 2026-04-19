using BaseLib.Extensions;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Powers;
using System;
using System.Collections.Generic;
using System.Reflection;

namespace BalanceTheSpire;

[HarmonyPatch]
internal static class CardBalanceStatPatches
{
    private static readonly FieldInfo CardEnergyCostBaseField = AccessTools.Field(typeof(CardEnergyCost), "_base");

    [HarmonyPatch(typeof(AbstractModel), nameof(AbstractModel.MutableClone))]
    [HarmonyPostfix]
    private static void AbstractMutableClonePostfix(ref AbstractModel __result)
    {
        if (__result is CardModel card)
        {
            ApplyBalancedStats(card, "MutableClone");
        }
    }

    [HarmonyPatch(typeof(CardModel), nameof(CardModel.ToMutable))]
    [HarmonyPostfix]
    private static void CardToMutablePostfix(ref CardModel __result)
    {
        ApplyBalancedStats(__result, "ToMutable");
    }

    [HarmonyPatch(typeof(CardModel), nameof(CardModel.AfterCreated))]
    [HarmonyPostfix]
    private static void CardAfterCreatedPostfix(CardModel __instance)
    {
        ApplyBalancedStats(__instance, "AfterCreated");
    }

    [HarmonyPatch(typeof(CardModel), nameof(CardModel.FinalizeUpgradeInternal))]
    [HarmonyPostfix]
    private static void CardFinalizeUpgradeInternalPostfix(CardModel __instance)
    {
        ApplyBalancedStats(__instance, "FinalizeUpgradeInternal");
    }

    [HarmonyPatch(typeof(CardModel), nameof(CardModel.FromSerializable))]
    [HarmonyPostfix]
    private static void CardFromSerializablePostfix(ref CardModel __result)
    {
        ApplyBalancedStats(__result, "FromSerializable");
    }

    [HarmonyPatch(typeof(ModelDb), nameof(ModelDb.InitIds))]
    [HarmonyPostfix]
    private static void ModelDbInitIdsPostfix()
    {
        RebalanceCanonicalCards();
    }

    [HarmonyPatch(typeof(SpoilsOfBattle), "OnUpgrade")]
    [HarmonyPrefix]
    private static bool SpoilsOfBattleUpgradePrefix(SpoilsOfBattle __instance)
    {
        __instance.DynamicVars.Forge.UpgradeValueBy(3m);
        return false;
    }

    [HarmonyPatch(typeof(Arsenal), "OnUpgrade")]
    [HarmonyPrefix]
    private static bool ArsenalUpgradePrefix(Arsenal __instance)
    {
        if (!__instance.Keywords.Contains(CardKeyword.Innate))
        {
            __instance.AddKeyword(CardKeyword.Innate);
        }

        return false;
    }

    [HarmonyPatch(typeof(CollisionCourse), "OnUpgrade")]
    [HarmonyPrefix]
    private static bool CollisionCourseUpgradePrefix(CollisionCourse __instance)
    {
        __instance.DynamicVars.Damage.UpgradeValueBy(4m);
        return false;
    }

    [HarmonyPatch(typeof(HeirloomHammer), "OnUpgrade")]
    [HarmonyPrefix]
    private static bool HeirloomHammerUpgradePrefix(HeirloomHammer __instance)
    {
        __instance.DynamicVars.Damage.UpgradeValueBy(5m);
        return false;
    }

    [HarmonyPatch(typeof(KinglyKick), "OnUpgrade")]
    [HarmonyPrefix]
    private static bool KinglyKickUpgradePrefix(KinglyKick __instance)
    {
        __instance.DynamicVars.Damage.UpgradeValueBy(8m);
        return false;
    }

    [HarmonyPatch(typeof(KinglyPunch), "OnUpgrade")]
    [HarmonyPrefix]
    private static bool KinglyPunchUpgradePrefix(KinglyPunch __instance)
    {
        __instance.DynamicVars.Damage.UpgradeValueBy(2m);
        __instance.DynamicVars["Increase"].UpgradeValueBy(2m);
        return false;
    }

    [HarmonyPatch(typeof(SolarStrike), "OnUpgrade")]
    [HarmonyPrefix]
    private static bool SolarStrikeUpgradePrefix(SolarStrike __instance)
    {
        __instance.DynamicVars.Damage.UpgradeValueBy(1m);
        return false;
    }

    [HarmonyPatch(typeof(Parry), "OnUpgrade")]
    [HarmonyPrefix]
    private static bool ParryUpgradePrefix(Parry __instance)
    {
        __instance.DynamicVars.Power<ParryPower>().UpgradeValueBy(4m);
        return false;
    }

    [HarmonyPatch(typeof(CelestialMight), "OnUpgrade")]
    [HarmonyPrefix]
    private static bool CelestialMightUpgradePrefix(CelestialMight __instance)
    {
        __instance.DynamicVars.Repeat.UpgradeValueBy(1m);
        return false;
    }

    private static void ApplyBalancedStats(CardModel card, string source)
    {
        bool upgraded = card.CurrentUpgradeLevel > 0;
        bool didAdjust = false;

        switch (card)
        {
            case SpoilsOfBattle spoilsOfBattle:
                spoilsOfBattle.DynamicVars.Forge.BaseValue = upgraded ? 8m : 5m;
                didAdjust = true;
                break;
            case MinionDiveBomb minionDiveBomb:
                SetBaseEnergyCost(minionDiveBomb, 0);
                didAdjust = true;
                break;
            case CollisionCourse collisionCourse:
                collisionCourse.DynamicVars.Damage.BaseValue = upgraded ? 15m : 11m;
                didAdjust = true;
                break;
            case HeirloomHammer heirloomHammer:
                heirloomHammer.DynamicVars.Damage.BaseValue = upgraded ? 25m : 20m;
                didAdjust = true;
                break;
            case GatherLight gatherLight:
                gatherLight.DynamicVars.Block.BaseValue = upgraded ? 11m : 8m;
                didAdjust = true;
                break;
            case BundleOfJoy bundleOfJoy:
                SetBaseEnergyCost(bundleOfJoy, 1);
                didAdjust = true;
                break;
            case IAmInvincible iAmInvincible:
                iAmInvincible.DynamicVars.Block.BaseValue = upgraded ? 13m : 10m;
                didAdjust = true;
                break;
            case KinglyKick kinglyKick:
                kinglyKick.DynamicVars.Damage.BaseValue = upgraded ? 35m : 27m;
                didAdjust = true;
                break;
            case KinglyPunch kinglyPunch:
                kinglyPunch.DynamicVars.Damage.BaseValue = upgraded ? 10m : 8m;
                kinglyPunch.DynamicVars["Increase"].BaseValue = upgraded ? 6m : 4m;
                didAdjust = true;
                break;
            case SolarStrike solarStrike:
                solarStrike.DynamicVars.Damage.BaseValue = upgraded ? 10m : 9m;
                solarStrike.DynamicVars.Stars.BaseValue = 1m;
                didAdjust = true;
                break;
            case Patter patter:
                patter.DynamicVars.Block.BaseValue = upgraded ? 11m : 9m;
                didAdjust = true;
                break;
            case FallingStar fallingStar:
                fallingStar.DynamicVars.Damage.BaseValue = upgraded ? 12m : 8m;
                didAdjust = true;
                break;
            case WroughtInWar wroughtInWar:
                wroughtInWar.DynamicVars.Forge.BaseValue = upgraded ? 9m : 7m;
                didAdjust = true;
                break;
            case Parry parry:
                parry.DynamicVars.Power<ParryPower>().BaseValue = upgraded ? 14m : 10m;
                didAdjust = true;
                break;
            case Glitterstream glitterstream:
                glitterstream.DynamicVars["BlockNextTurn"].BaseValue = upgraded ? 7m : 5m;
                didAdjust = true;
                break;
            case RefineBlade refineBlade:
                refineBlade.DynamicVars.Forge.BaseValue = upgraded ? 13m : 9m;
                didAdjust = true;
                break;
        }

        if (!didAdjust)
        {
            return;
        }

        if (card is SpoilsOfBattle spoils)
        {
            MainFile.Logger.Info($"[BalanceDebug] {source}: {card.Id.Entry} upgraded={upgraded} forge={spoils.DynamicVars.Forge.BaseValue}");
        }
        else if (card is MinionDiveBomb diveBomb)
        {
            MainFile.Logger.Info($"[BalanceDebug] {source}: {card.Id.Entry} upgraded={upgraded} cost={diveBomb.EnergyCost.GetWithModifiers(CostModifiers.Local)} damage={diveBomb.DynamicVars.Damage.BaseValue}");
        }
    }

    private static void RebalanceCanonicalCards()
    {
        try
        {
            HashSet<CardModel> seen = [];
            int rebalancedCount = 0;

            foreach (CardModel card in ModelDb.AllCards)
            {
                if (!seen.Add(card))
                {
                    continue;
                }

                ApplyBalancedStats(card, "ModelDb.InitIds");
                rebalancedCount++;
            }

            MainFile.Logger.Info($"[BalanceDebug] Rebalanced {rebalancedCount} canonical cards after ModelDb.InitIds.");
        }
        catch (Exception ex)
        {
            MainFile.Logger.Error($"[BalanceDebug] Failed to rebalance canonical cards: {ex}");
        }
    }

    private static void SetBaseEnergyCost(CardModel card, int newBaseCost)
    {
        if (!card.IsCanonical)
        {
            card.EnergyCost.SetCustomBaseCost(newBaseCost);
            return;
        }

        CardEnergyCost energyCost = card.EnergyCost;
        CardEnergyCostBaseField.SetValue(energyCost, newBaseCost);
    }
}
