using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
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

    protected override IEnumerable<DynamicVar> CanonicalVars =>
        new DynamicVar[]
        {
            new PowerVar<StrengthPower>(3),
            new DynamicVar(TeammateHpLossKey, 3),
            new MaxHpVar(TeammateMaxHpLossKey, 3)
        };

    protected override IEnumerable<IHoverTip> ExtraHoverTips =>
        new[] { HoverTipFactory.FromPower<StrengthPower>() };

    public override string? CustomPortraitPath => PortraitFileName.CardImagePath().Replace("\\", "/");

    public YouSoSelfish()
        : base(0, CardType.Skill, CardRarity.Uncommon, TargetType.Self)
    {
    }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        if (base.CombatState == null)
        {
            return;
        }

        await PowerCmd.Apply<StrengthPower>(base.Owner.Creature, base.DynamicVars.Strength.IntValue, base.Owner.Creature, this);

        IEnumerable<Player> teammates = base.CombatState.Players.Where(player =>
            player != base.Owner &&
            player.Creature.IsAlive);

        foreach (Player teammate in teammates)
        {
            await CreatureCmd.Damage(
                choiceContext,
                teammate.Creature,
                base.DynamicVars[TeammateHpLossKey].IntValue,
                ValueProp.Unblockable | ValueProp.Unpowered | ValueProp.Move,
                base.Owner.Creature,
                this);

            if (base.IsUpgraded)
            {
                await CreatureCmd.LoseMaxHp(choiceContext, teammate.Creature, base.DynamicVars[TeammateMaxHpLossKey].IntValue, isFromCard: true);
            }
        }
    }

    protected override void OnUpgrade()
    {
        base.DynamicVars.Strength.UpgradeValueBy(3m);
    }
}
