using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Vfx;

namespace BalanceTheSpire;

[HarmonyPatch]
internal static class CardBalanceBehaviorPatches
{
    [HarmonyPatch(typeof(SpoilsOfBattle), "OnPlay")]
    [HarmonyPostfix]
    private static void SpoilsOfBattlePlayPostfix(SpoilsOfBattle __instance, PlayerChoiceContext choiceContext, ref Task __result)
    {
        __result = DrawAfterSpoilsAsync(__result, __instance, choiceContext);
    }

    [HarmonyPatch(typeof(ArsenalPower), nameof(ArsenalPower.AfterCardPlayed))]
    [HarmonyPrefix]
    private static bool ArsenalPowerAfterCardPlayedPrefix(ref Task __result)
    {
        __result = Task.CompletedTask;
        return false;
    }

    [HarmonyPatch(typeof(AbstractModel), nameof(AbstractModel.AfterCardGeneratedForCombat))]
    [HarmonyPostfix]
    private static void ArsenalPowerAfterCardGeneratedPostfix(AbstractModel __instance, CardModel card, bool addedByPlayer, ref Task __result)
    {
        if (__instance is ArsenalPower arsenalPower)
        {
            __result = GainStrengthAfterGeneratedCardAsync(__result, arsenalPower, card, addedByPlayer);
        }
    }

    [HarmonyPatch(typeof(Charge), "get_ExtraHoverTips")]
    [HarmonyPostfix]
    private static void ChargeHoverTipsPostfix(Charge __instance, ref IEnumerable<IHoverTip> __result)
    {
        __result = new[] { HoverTipFactory.FromCard<MinionDiveBomb>(__instance.IsUpgraded) };
    }

    [HarmonyPatch(typeof(Charge), "OnPlay")]
    [HarmonyPrefix]
    private static bool ChargePlayPrefix(Charge __instance, PlayerChoiceContext choiceContext, CardPlay cardPlay, ref Task __result)
    {
        __result = PlayChargeAsync(__instance, choiceContext);
        return false;
    }

    [HarmonyPatch(typeof(Begone), "get_ExtraHoverTips")]
    [HarmonyPostfix]
    private static void BegoneHoverTipsPostfix(Begone __instance, ref IEnumerable<IHoverTip> __result)
    {
        __result = new[] { HoverTipFactory.FromCard<MinionStrike>(__instance.IsUpgraded) };
    }

    [HarmonyPatch(typeof(Begone), "OnPlay")]
    [HarmonyPrefix]
    private static bool BegonePlayPrefix(Begone __instance, PlayerChoiceContext choiceContext, CardPlay cardPlay, ref Task __result)
    {
        __result = PlayBegoneAsync(__instance, choiceContext, cardPlay);
        return false;
    }

    [HarmonyPatch(typeof(SwordSagePower), nameof(SwordSagePower.TryModifyEnergyCostInCombat))]
    [HarmonyPrefix]
    private static bool SwordSagePowerTryModifyEnergyCostInCombatPrefix(decimal originalCost, ref decimal modifiedCost, ref bool __result)
    {
        modifiedCost = originalCost;
        __result = false;
        return false;
    }

    [HarmonyPatch(typeof(GuidingStar), "OnPlay")]
    [HarmonyPrefix]
    private static bool GuidingStarPlayPrefix(GuidingStar __instance, PlayerChoiceContext choiceContext, CardPlay cardPlay, ref Task __result)
    {
        __result = PlayGuidingStarAsync(__instance, choiceContext, cardPlay);
        return false;
    }

    private static async Task DrawAfterSpoilsAsync(Task originalTask, SpoilsOfBattle card, PlayerChoiceContext choiceContext)
    {
        await originalTask;
        await CardPileCmd.Draw(choiceContext, 2, card.Owner);
    }

    private static async Task GainStrengthAfterGeneratedCardAsync(Task originalTask, ArsenalPower power, CardModel card, bool addedByPlayer)
    {
        await originalTask;

        if (!addedByPlayer)
        {
            return;
        }

        if (power.Owner.Player == null || card.Owner != power.Owner.Player)
        {
            return;
        }

        await PowerCmd.Apply<StrengthPower>(power.Owner, power.Amount, power.Owner, null);
    }

    private static async Task PlayChargeAsync(Charge card, PlayerChoiceContext choiceContext)
    {
        await CreatureCmd.TriggerAnim(card.Owner.Creature, "Cast", card.Owner.Character.CastAnimDelay);

        List<CardModel> cardsIn = (from candidate in PileType.Draw.GetPile(card.Owner).Cards
            orderby candidate.Rarity, candidate.Id
            select candidate).ToList();

        List<CardModel> selected = (await CardSelectCmd.FromSimpleGrid(
            choiceContext,
            cardsIn,
            card.Owner,
            new CardSelectorPrefs(CardSelectorPrefs.TransformSelectionPrompt, card.DynamicVars.Cards.IntValue))).ToList();

        foreach (CardModel original in selected)
        {
            CardPileAddResult? transformed = await CardCmd.TransformTo<MinionDiveBomb>(original);
            if (card.IsUpgraded && transformed.HasValue)
            {
                CardCmd.Upgrade(transformed.Value.cardAdded);
            }
        }
    }

    private static async Task PlayBegoneAsync(Begone card, PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        ArgumentNullException.ThrowIfNull(cardPlay.Target, nameof(cardPlay.Target));

        await DamageCmd.Attack(card.DynamicVars.Damage.BaseValue).FromCard(card).Targeting(cardPlay.Target)
            .WithHitFx("vfx/vfx_attack_slash")
            .Execute(choiceContext);

        CardModel? selected = (await CardSelectCmd.FromHand(
            prefs: new CardSelectorPrefs(CardSelectorPrefs.TransformSelectionPrompt, 1),
            context: choiceContext,
            player: card.Owner,
            filter: null,
            source: card)).FirstOrDefault();

        if (selected == null || card.CombatState == null)
        {
            return;
        }

        CardModel replacement = card.CombatState.CreateCard<MinionStrike>(card.Owner);
        if (card.IsUpgraded)
        {
            CardCmd.Upgrade(replacement);
        }

        await CardCmd.Transform(selected, replacement);
    }

    private static async Task PlayGuidingStarAsync(GuidingStar card, PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        ArgumentNullException.ThrowIfNull(cardPlay.Target, nameof(cardPlay.Target));

        await CreatureCmd.TriggerAnim(card.Owner.Creature, "Cast", card.Owner.Character.CastAnimDelay);

        NCombatRoom? combatRoom = NCombatRoom.Instance;
        NCreature? nCreature = combatRoom?.GetCreatureNode(cardPlay.Target);
        if (nCreature != null && combatRoom != null)
        {
            SfxCmd.Play("event:/sfx/characters/regent/regent_guiding_star");
            NSmallMagicMissileVfx? missile = NSmallMagicMissileVfx.Create(nCreature.GetBottomOfHitbox(), new Color("50b598"));
            if (missile != null)
            {
                combatRoom.CombatVfxContainer.AddChildSafely(missile);
                await Cmd.Wait(missile.WaitTime);
            }
        }

        await DamageCmd.Attack(card.DynamicVars.Damage.BaseValue).WithNoAttackerAnim().FromCard(card)
            .Targeting(cardPlay.Target)
            .Execute(choiceContext);

        await CardPileCmd.Draw(choiceContext, card.DynamicVars.Cards.BaseValue, card.Owner);
    }
}
