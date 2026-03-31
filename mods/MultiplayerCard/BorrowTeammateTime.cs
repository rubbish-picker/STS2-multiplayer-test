using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BaseLib;
using BaseLib.Abstracts;
using BaseLib.Utils;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models.CardPools;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Powers;
using MultiplayerCard.Extensions;

namespace MultiplayerCard;

[Pool(typeof(NecrobinderCardPool))]
public sealed class BorrowTeammateTime : CustomCardModel
{
    private const string PortraitFileName = "borrow_teammate_time.png";

    public override CardMultiplayerConstraint MultiplayerConstraint => MultiplayerCardConfigService.GetMode() switch
    {
        MultiplayerCardMode.UniversalMode => CardMultiplayerConstraint.None,
        _ => CardMultiplayerConstraint.MultiplayerOnly,
    };

    public override TargetType TargetType =>
        MultiplayerCardConfigService.IsSingleplayerUniversalFallbackEnabled(base.Owner?.RunState) || !HasAliveTeammateTarget()
            ? TargetType.None
            : base.TargetType;

    protected override IEnumerable<DynamicVar> CanonicalVars =>
        new DynamicVar[]
        {
            new PowerVar<DoomPower>(3),
            new EnergyVar(1)
        };

    protected override IEnumerable<IHoverTip> ExtraHoverTips =>
        new IHoverTip[]
        {
            HoverTipFactory.FromPower<DoomPower>(),
            HoverTipFactory.ForEnergy(this)
        };

    public override string PortraitPath => PortraitFileName.CardImagePath();

    public override string? CustomPortraitPath => PortraitPath;

    public BorrowTeammateTime()
        : base(0, CardType.Skill, CardRarity.Uncommon, TargetType.AnyAlly)
    {
    }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        await CreatureCmd.TriggerAnim(base.Owner.Creature, "Cast", base.Owner.Character.CastAnimDelay);
        await PlayerCmd.GainEnergy(base.DynamicVars.Energy.IntValue, base.Owner);

        if (cardPlay.Target?.Player == null || cardPlay.Target.Player == base.Owner)
        {
            return;
        }

        await PowerCmd.Apply<DoomPower>(cardPlay.Target, base.DynamicVars.Doom.IntValue, base.Owner.Creature, this);
    }

    protected override void OnUpgrade()
    {
        base.DynamicVars.Doom.UpgradeValueBy(2m);
        base.DynamicVars.Energy.UpgradeValueBy(2m);
    }

    private bool HasAliveTeammateTarget()
    {
        Player? owner = base.Owner;
        return owner?.RunState?.Players.Any(player => player != owner && player.Creature != null && player.Creature.IsAlive) ?? false;
    }
}
