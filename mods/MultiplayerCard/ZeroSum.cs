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
using MultiplayerCard.Extensions;

namespace MultiplayerCard;

[Pool(typeof(ColorlessCardPool))]
public sealed class ZeroSum : CustomCardModel
{
    private const string DrainEnergyKey = "DrainEnergy";
    private const string PortraitFileName = "zero_sum.png";

    public override IEnumerable<CardKeyword> CanonicalKeywords =>
        new[] { CardKeyword.Exhaust };

    protected override IEnumerable<DynamicVar> CanonicalVars =>
        new DynamicVar[]
        {
            new EnergyVar(10),
            new EnergyVar(DrainEnergyKey, 3)
        };

    protected override IEnumerable<IHoverTip> ExtraHoverTips =>
        new[] { HoverTipFactory.ForEnergy(this) }
            .Concat(HoverTipFactory.FromCardWithCardHoverTips<Guilty>());

    public override string? CustomPortraitPath => PortraitFileName.CardImagePath().Replace("\\", "/");

    public ZeroSum()
        : base(0, CardType.Skill, CardRarity.Rare, TargetType.Self)
    {
    }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        if (base.CombatState == null)
        {
            return;
        }

        await PlayerCmd.GainEnergy(base.DynamicVars.Energy.IntValue, base.Owner);

        IEnumerable<Player> teammates = base.CombatState.Players.Where(player =>
            player != base.Owner &&
            player.Creature.IsAlive);

        foreach (Player teammate in teammates)
        {
            await PlayerCmd.LoseEnergy(base.DynamicVars[DrainEnergyKey].IntValue, teammate);
            await CardPileCmd.AddGeneratedCardToCombat(base.CombatState.CreateCard<Guilty>(base.Owner), PileType.Hand, addedByPlayer: true);
        }
    }

    protected override void OnUpgrade()
    {
        RemoveKeyword(CardKeyword.Exhaust);
    }
}
