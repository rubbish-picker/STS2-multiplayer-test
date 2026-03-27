using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System;
using BaseLib.Abstracts;
using BaseLib.Utils;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models.CardPools;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.ValueProps;
using MultiplayerCard.Extensions;

namespace MultiplayerCard;

[Pool(typeof(ColorlessCardPool))]
public sealed class YouSoSelfish : CustomCardModel
{
    private const string TeammateHpLossKey = "TeammateHpLoss";
    private const string TeammateMaxHpLossKey = "TeammateMaxHpLoss";
    private const string PortraitFileName = "hateyou.png";

    public override CardMultiplayerConstraint MultiplayerConstraint => MultiplayerCardConfigService.GetMode() switch
    {
        MultiplayerCardMode.UniversalMode => CardMultiplayerConstraint.None,
        _ => CardMultiplayerConstraint.MultiplayerOnly,
    };

    public override TargetType TargetType =>
        MultiplayerCardConfigService.IsSingleplayerUniversalFallbackEnabled(base.Owner?.RunState)
            ? TargetType.Self
            : base.TargetType;

    protected override IEnumerable<DynamicVar> CanonicalVars =>
        new DynamicVar[]
        {
            new PowerVar<StrengthPower>(3),
            new DynamicVar(TeammateHpLossKey, 3),
            new MaxHpVar(TeammateMaxHpLossKey, 3)
        };

    protected override IEnumerable<IHoverTip> ExtraHoverTips =>
        new[] { HoverTipFactory.FromPower<StrengthPower>() };

    public override string PortraitPath => PortraitFileName.CardImagePath();

    public override string? CustomPortraitPath => PortraitPath;

    public YouSoSelfish()
        : base(0, CardType.Skill, CardRarity.Uncommon, TargetType.AnyAlly)
    {
    }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        await PowerCmd.Apply<StrengthPower>(base.Owner.Creature, base.DynamicVars.Strength.IntValue, base.Owner.Creature, this);

        if (cardPlay.Target == null || cardPlay.Target.Player == base.Owner)
        {
            return;
        }

        await CreatureCmd.Damage(
            choiceContext,
            cardPlay.Target,
            base.DynamicVars[TeammateHpLossKey].IntValue,
            ValueProp.Unblockable | ValueProp.Unpowered | ValueProp.Move,
            base.Owner.Creature,
            this);

        if (base.IsUpgraded)
        {
            await CreatureCmd.LoseMaxHp(choiceContext, cardPlay.Target, base.DynamicVars[TeammateMaxHpLossKey].IntValue, isFromCard: true);
        }
    }

    protected override void OnUpgrade()
    {
        base.DynamicVars.Strength.UpgradeValueBy(3m);
    }
}
