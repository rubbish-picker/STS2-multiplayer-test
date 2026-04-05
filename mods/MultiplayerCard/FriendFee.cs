using System.Threading.Tasks;
using BaseLib.Abstracts;
using BaseLib.Utils;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models.CardPools;
using MultiplayerCard.Extensions;

namespace MultiplayerCard;

[Pool(typeof(RegentCardPool))]
public sealed class FriendFee : CustomCardModel
{
    private const string PortraitFileName = "friend_fee.png";

    public override CardMultiplayerConstraint MultiplayerConstraint => MultiplayerCardConfigService.GetMode() switch
    {
        MultiplayerCardMode.UniversalMode => CardMultiplayerConstraint.None,
        _ => CardMultiplayerConstraint.MultiplayerOnly,
    };

    public override string PortraitPath => PortraitFileName.CardImagePath();

    public override string? CustomPortraitPath => PortraitPath;

    public override bool CanBeGeneratedInCombat => false;

    public FriendFee()
        : base(1, CardType.Power, CardRarity.Rare, TargetType.Self)
    {
    }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        await CreatureCmd.TriggerAnim(base.Owner.Creature, "Cast", base.Owner.Character.CastAnimDelay);
        await PowerCmd.Apply<FriendFeePower>(base.Owner.Creature, 1, base.Owner.Creature, this);
    }

}
