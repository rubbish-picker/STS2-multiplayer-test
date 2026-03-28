using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System;
using BaseLib;
using BaseLib.Abstracts;
using BaseLib.Utils;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models.CardPools;
using MegaCrit.Sts2.Core.Models.Cards;
using MultiplayerCard.Extensions;

namespace MultiplayerCard;

[Pool(typeof(ColorlessCardPool))]
public sealed class ZeroSum : CustomCardModel
{
    private const string DrainEnergyKey = "DrainEnergy";
    private const string PortraitFileName = "zero_sum.png";

    public override CardMultiplayerConstraint MultiplayerConstraint => MultiplayerCardConfigService.GetMode() switch
    {
        MultiplayerCardMode.UniversalMode => CardMultiplayerConstraint.None,
        _ => CardMultiplayerConstraint.MultiplayerOnly,
    };

    public override TargetType TargetType =>
        MultiplayerCardConfigService.IsSingleplayerUniversalFallbackEnabled(base.Owner?.RunState) || !HasAliveTeammateTarget()
            ? TargetType.None
            : base.TargetType;

    public override IEnumerable<CardKeyword> CanonicalKeywords =>
        new[] { CardKeyword.Exhaust };

    protected override IEnumerable<DynamicVar> CanonicalVars =>
        new DynamicVar[]
        {
            new EnergyVar(9),
            new EnergyVar(DrainEnergyKey, 3)
        };

    protected override IEnumerable<IHoverTip> ExtraHoverTips =>
        new[] { HoverTipFactory.ForEnergy(this) }
            .Concat(HoverTipFactory.FromCardWithCardHoverTips<Guilty>());

    public override string PortraitPath => PortraitFileName.CardImagePath();

    public override string? CustomPortraitPath => PortraitPath;

    public ZeroSum()
        : base(0, CardType.Skill, CardRarity.Rare, TargetType.AnyAlly)
    {
    }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        await PlayerCmd.GainEnergy(base.DynamicVars.Energy.IntValue, base.Owner);

        Player? teammate = cardPlay.Target?.Player;
        if (teammate == null || teammate == base.Owner)
        {
            return;
        }

        await PlayerCmd.LoseEnergy(base.DynamicVars[DrainEnergyKey].IntValue, teammate);
        await CardPileCmd.AddGeneratedCardToCombat(base.Owner.Creature.CombatState!.CreateCard<Guilty>(base.Owner), PileType.Hand, addedByPlayer: true);
    }

    public override bool ShouldAllowTargeting(Creature target)
    {
        return IsAllowedTarget(target);
    }

    protected override void OnUpgrade()
    {
        RemoveKeyword(CardKeyword.Exhaust);
    }

    private bool HasAliveTeammateTarget()
    {
        Player? owner = base.Owner;
        return owner?.RunState?.Players.Any(player => player != owner && player.Creature != null && player.Creature.IsAlive) ?? false;
    }

    private bool IsAllowedTarget(Creature? target)
    {
        if (target == null || base.Owner == null)
        {
            return false;
        }

        if (!HasAliveTeammateTarget())
        {
            return false;
        }

        return target.Player != null
            && target.Player != base.Owner
            && target.IsAlive;
    }
}
