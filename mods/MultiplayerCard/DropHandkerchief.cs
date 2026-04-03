using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BaseLib;
using BaseLib.Abstracts;
using BaseLib.Utils;
using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models.CardPools;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MultiplayerCard.Extensions;

namespace MultiplayerCard;

[Pool(typeof(ColorlessCardPool))]
public sealed class DropHandkerchief : CustomCardModel
{
    private const string PortraitFileName = "drop_handkerchief.png";

    public override CardMultiplayerConstraint MultiplayerConstraint => MultiplayerCardConfigService.GetMode() switch
    {
        MultiplayerCardMode.UniversalMode => CardMultiplayerConstraint.None,
        _ => CardMultiplayerConstraint.MultiplayerOnly,
    };

    public override TargetType TargetType =>
        MultiplayerCardConfigService.IsSingleplayerUniversalFallbackEnabled(base.Owner?.RunState) || !HasAliveTeammateTarget()
            ? TargetType.None
            : base.TargetType;

    public override string PortraitPath => PortraitFileName.CardImagePath();

    public override string? CustomPortraitPath => PortraitPath;

    public DropHandkerchief()
        : base(3, CardType.Skill, CardRarity.Rare, TargetType.AnyAlly)
    {
    }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        Player? teammate = cardPlay.Target?.Player;
        if (teammate == null || teammate == base.Owner)
        {
            return;
        }

        List<MegaCrit.Sts2.Core.Models.CardModel> teammateHandCards = PileType.Hand.GetPile(teammate).Cards.ToList();
        if (teammateHandCards.Count == 0)
        {
            return;
        }

        CardSelectorPrefs prefs = new(SelectionScreenPrompt, 1);
        MegaCrit.Sts2.Core.Models.CardModel? selectedCard = (await CardSelectCmd.FromSimpleGrid(choiceContext, teammateHandCards, base.Owner, prefs)).FirstOrDefault();
        if (selectedCard == null)
        {
            return;
        }

        await ExchangeCards(selectedCard, teammate);
    }

    protected override void OnUpgrade()
    {
        base.EnergyCost.UpgradeBy(-1);
    }

    private async Task ExchangeCards(MegaCrit.Sts2.Core.Models.CardModel selectedCard, Player teammate)
    {
        if (base.Owner?.Creature?.CombatState == null)
        {
            return;
        }

        TransferCardToNewOwner(selectedCard, base.Owner);
        TransferCardToNewOwner(this, teammate);

        await CardPileCmd.Add(selectedCard, PileType.Hand, source: this);
        await CardPileCmd.Add(this, PileType.Hand, source: this);
    }

    private static void TransferCardToNewOwner(MegaCrit.Sts2.Core.Models.CardModel card, Player newOwner)
    {
        if (card.CombatState == null)
        {
            return;
        }

        card.RemoveFromCurrentPile();
        RemoveCardVisual(card);

        card.CombatState.RemoveCard(card);
        card.CombatState.AddCard(card, newOwner);
    }

    private static void RemoveCardVisual(MegaCrit.Sts2.Core.Models.CardModel card)
    {
        NCard? cardNode = NCard.FindOnTable(card);
        if (cardNode == null)
        {
            return;
        }

        cardNode.GetParent()?.RemoveChild(cardNode);
        cardNode.QueueFree();
    }

    private bool HasAliveTeammateTarget()
    {
        Player? owner = base.Owner;
        return owner?.RunState?.Players.Any(player => player != owner && player.Creature != null && player.Creature.IsAlive) ?? false;
    }
}
