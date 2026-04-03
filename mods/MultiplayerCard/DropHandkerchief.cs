using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BaseLib;
using BaseLib.Abstracts;
using BaseLib.Utils;
using Godot;
using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models.CardPools;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Screens.CardSelection;
using MegaCrit.Sts2.Core.Nodes.Screens.Overlays;
using MegaCrit.Sts2.Core.Runs;
using MultiplayerCard.Extensions;

namespace MultiplayerCard;

[Pool(typeof(ColorlessCardPool))]
public sealed class DropHandkerchief : CustomCardModel
{
    private enum SelectionOutcome
    {
        Selected,
        NoCard,
        TargetInvalid,
    }

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

        CombatState? combatState = base.CombatState;
        if (combatState == null)
        {
            return;
        }

        if (!IsTeammateStillValid(teammate))
        {
            await CancelCurrentPlayAndReturnToHand(cardPlay.Resources);
            return;
        }

        Player originalOwner = base.Owner;
        while (true)
        {
            (SelectionOutcome outcome, MegaCrit.Sts2.Core.Models.CardModel? selectedCard) = await ChooseTeammateHandCard(choiceContext, teammate);
            if (outcome == SelectionOutcome.TargetInvalid)
            {
                await CancelCurrentPlayAndReturnToHand(cardPlay.Resources);
                return;
            }

            if (outcome == SelectionOutcome.NoCard || selectedCard == null)
            {
                return;
            }

            if (await TransferExistingCardToHand(selectedCard, originalOwner, requireCurrentHandOwner: teammate))
            {
                break;
            }

            MainFile.Logger.Info($"[DropHandkerchief] Selected teammate card changed before transfer completed. Reopening selection for player {teammate.NetId}.");
        }

        MegaCrit.Sts2.Core.Models.CardModel transferredSelf = CardModel.FromSerializable(this.ToSerializable());
        combatState.AddCard(transferredSelf, teammate);
        await CardPileCmd.Add(transferredSelf, PileType.Hand, source: this);
    }

    protected override void OnUpgrade()
    {
        base.EnergyCost.UpgradeBy(-1);
    }

    protected override PileType GetResultPileType()
    {
        return PileType.None;
    }

    private async Task<(SelectionOutcome outcome, MegaCrit.Sts2.Core.Models.CardModel? selectedCard)> ChooseTeammateHandCard(PlayerChoiceContext choiceContext, Player teammate)
    {
        if (!IsTeammateStillValid(teammate))
        {
            return (SelectionOutcome.TargetInvalid, null);
        }

        List<MegaCrit.Sts2.Core.Models.CardModel> initialHandCards = PileType.Hand.GetPile(teammate).Cards.ToList();
        if (initialHandCards.Count == 0)
        {
            return (SelectionOutcome.NoCard, null);
        }

        Player selectingPlayer = base.Owner;
        CardSelectorPrefs prefs = new(SelectionScreenPrompt, 1);
        uint choiceId = RunManager.Instance.PlayerChoiceSynchronizer.ReserveChoiceId(selectingPlayer);
        await choiceContext.SignalPlayerChoiceBegun(PlayerChoiceOptions.None);

        try
        {
            if (ShouldSelectLocalCard(selectingPlayer))
            {
                NPlayerHand.Instance?.CancelAllCardPlay();
                (SelectionOutcome localOutcome, MegaCrit.Sts2.Core.Models.CardModel? selectedCard) = await ChooseTeammateHandCardLocally(teammate, prefs);
                RunManager.Instance.PlayerChoiceSynchronizer.SyncLocalChoice(
                    selectingPlayer,
                    choiceId,
                    PlayerChoiceResult.FromMutableCombatCards(selectedCard == null
                        ? Array.Empty<MegaCrit.Sts2.Core.Models.CardModel>()
                        : new[] { selectedCard }));
                return (localOutcome, selectedCard);
            }

            MegaCrit.Sts2.Core.Models.CardModel? remoteSelectedCard = (await RunManager.Instance.PlayerChoiceSynchronizer.WaitForRemoteChoice(selectingPlayer, choiceId))
                .AsCombatCards()
                .FirstOrDefault();
            return !IsTeammateStillValid(teammate)
                ? (SelectionOutcome.TargetInvalid, null)
                : (remoteSelectedCard == null ? SelectionOutcome.NoCard : SelectionOutcome.Selected, remoteSelectedCard);
        }
        finally
        {
            await choiceContext.SignalPlayerChoiceEnded();
        }
    }

    private async Task<(SelectionOutcome outcome, MegaCrit.Sts2.Core.Models.CardModel? selectedCard)> ChooseTeammateHandCardLocally(Player teammate, CardSelectorPrefs prefs)
    {
        while (true)
        {
            if (!IsTeammateStillValid(teammate))
            {
                return (SelectionOutcome.TargetInvalid, null);
            }

            List<MegaCrit.Sts2.Core.Models.CardModel> teammateHandCards = PileType.Hand.GetPile(teammate).Cards.ToList();
            if (teammateHandCards.Count == 0)
            {
                return (SelectionOutcome.NoCard, null);
            }

            if (CardSelectCmd.Selector != null)
            {
                MegaCrit.Sts2.Core.Models.CardModel? selectedFromSelector = (await CardSelectCmd.Selector.GetSelectedCards(teammateHandCards, prefs.MinSelect, prefs.MaxSelect)).FirstOrDefault();
                if (selectedFromSelector == null)
                {
                    return !IsTeammateStillValid(teammate)
                        ? (SelectionOutcome.TargetInvalid, null)
                        : (SelectionOutcome.NoCard, null);
                }

                if (HasMatchingHandSnapshot(teammate, teammateHandCards) && IsCardStillInTeammateHand(selectedFromSelector, teammate))
                {
                    return (SelectionOutcome.Selected, selectedFromSelector);
                }

                MainFile.Logger.Info($"[DropHandkerchief] Teammate hand changed while selector was active. Refreshing selection for player {teammate.NetId}.");
                continue;
            }

            (SelectionOutcome outcome, bool shouldRefresh, MegaCrit.Sts2.Core.Models.CardModel? selectedCard) = await ShowLiveRefreshingSelectionScreen(teammate, teammateHandCards, prefs);
            if (outcome == SelectionOutcome.TargetInvalid)
            {
                return (SelectionOutcome.TargetInvalid, null);
            }

            if (!shouldRefresh)
            {
                return (selectedCard == null ? SelectionOutcome.NoCard : SelectionOutcome.Selected, selectedCard);
            }

            MainFile.Logger.Info($"[DropHandkerchief] Teammate hand changed while selecting. Refreshing selection for player {teammate.NetId}.");
            await WaitForNextProcessFrame();
        }
    }

    private async Task<(SelectionOutcome outcome, bool shouldRefresh, MegaCrit.Sts2.Core.Models.CardModel? selectedCard)> ShowLiveRefreshingSelectionScreen(
        Player teammate,
        IReadOnlyList<MegaCrit.Sts2.Core.Models.CardModel> snapshot,
        CardSelectorPrefs prefs)
    {
        NOverlayStack? overlayStack = NOverlayStack.Instance;
        if (overlayStack == null)
        {
            return (SelectionOutcome.NoCard, false, snapshot.FirstOrDefault());
        }

        NSimpleCardSelectScreen selectionScreen = NSimpleCardSelectScreen.Create(snapshot, prefs);
        overlayStack.Push(selectionScreen);

        Task<IEnumerable<MegaCrit.Sts2.Core.Models.CardModel>> selectionTask = selectionScreen.CardsSelected();
        Task<SelectionOutcome> refreshTask = WaitForSelectionInvalidationAsync(selectionScreen, teammate, snapshot);
        Task completedTask = await Task.WhenAny(selectionTask, refreshTask);

        if (completedTask == selectionTask)
        {
            return (SelectionOutcome.Selected, false, (await selectionTask).FirstOrDefault());
        }

        SelectionOutcome refreshOutcome = await refreshTask;
        if (selectionScreen.IsInsideTree())
        {
            overlayStack.Remove(selectionScreen);
        }

        try
        {
            await selectionTask;
        }
        catch (TaskCanceledException)
        {
        }

        return (refreshOutcome, refreshOutcome == SelectionOutcome.Selected, null);
    }

    private static async Task<SelectionOutcome> WaitForSelectionInvalidationAsync(
        NSimpleCardSelectScreen selectionScreen,
        Player teammate,
        IReadOnlyList<MegaCrit.Sts2.Core.Models.CardModel> snapshot)
    {
        while (selectionScreen.IsInsideTree())
        {
            await selectionScreen.ToSignal(selectionScreen.GetTree(), SceneTree.SignalName.ProcessFrame);
            if (!IsTeammateStillValid(teammate))
            {
                return SelectionOutcome.TargetInvalid;
            }

            if (!HasMatchingHandSnapshot(teammate, snapshot))
            {
                return SelectionOutcome.Selected;
            }
        }

        return SelectionOutcome.NoCard;
    }

    private static async Task WaitForNextProcessFrame()
    {
        SceneTree? tree = Engine.GetMainLoop() as SceneTree;
        if (tree != null)
        {
            await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
        }
    }

    private static bool ShouldSelectLocalCard(Player player)
    {
        return LocalContext.IsMe(player) && RunManager.Instance.NetService.Type != NetGameType.Replay;
    }

    private async Task CancelCurrentPlayAndReturnToHand(ResourceInfo resources)
    {
        if (base.Pile?.Type == PileType.Play)
        {
            await CardPileCmd.Add(this, PileType.Hand);
        }

        if (resources.EnergySpent > 0)
        {
            await PlayerCmd.GainEnergy(resources.EnergySpent, base.Owner);
        }

        if (resources.StarsSpent > 0)
        {
            await PlayerCmd.GainStars(resources.StarsSpent, base.Owner);
        }
    }

    private async Task<bool> TransferExistingCardToHand(MegaCrit.Sts2.Core.Models.CardModel card, Player newOwner, Player? requireCurrentHandOwner)
    {
        CombatState? combatState = card.CombatState;
        if (combatState == null || card.Pile == null || card.Owner == null)
        {
            return false;
        }

        if (requireCurrentHandOwner != null && !IsCardStillInTeammateHand(card, requireCurrentHandOwner))
        {
            return false;
        }

        if (card.Owner == newOwner && card.Pile.Type == PileType.Hand)
        {
            return true;
        }

        MegaCrit.Sts2.Core.Models.CardModel transferredCard = CardModel.FromSerializable(card.ToSerializable());
        combatState.AddCard(transferredCard, newOwner);
        await CardPileCmd.Add(transferredCard, PileType.Hand, source: this);
        RemoveCardFromCombat(card, combatState);
        return true;
    }

    private static bool IsCardStillInTeammateHand(MegaCrit.Sts2.Core.Models.CardModel? card, Player? teammate)
    {
        return card != null
            && teammate != null
            && card.Owner == teammate
            && card.Pile?.Type == PileType.Hand
            && PileType.Hand.GetPile(teammate).Cards.Contains(card);
    }

    private static bool HasMatchingHandSnapshot(Player teammate, IReadOnlyList<MegaCrit.Sts2.Core.Models.CardModel> snapshot)
    {
        IReadOnlyList<MegaCrit.Sts2.Core.Models.CardModel> currentCards = PileType.Hand.GetPile(teammate).Cards;
        return currentCards.Count == snapshot.Count && currentCards.SequenceEqual(snapshot);
    }

    private static bool IsTeammateStillValid(Player? teammate)
    {
        return teammate?.Creature != null && teammate.Creature.IsAlive;
    }

    private static void RemoveCardVisual(MegaCrit.Sts2.Core.Models.CardModel card, CardPile? oldPile)
    {
        NCombatUi? ui = NCombatRoom.Instance?.Ui;
        if (ui == null)
        {
            return;
        }

        NCard? handCardNode = ui.Hand.GetCard(card);
        if (oldPile?.Type == PileType.Hand && handCardNode != null && !ui.PlayContainer.IsAncestorOf(handCardNode))
        {
            ui.Hand.Remove(card);
            return;
        }

        NCard? playCardNode = ui.GetCardFromPlayContainer(card) ?? NCard.FindOnTable(card, PileType.Play);
        if (playCardNode != null)
        {
            playCardNode.GetParent()?.RemoveChild(playCardNode);
            playCardNode.QueueFree();
            return;
        }

        NCard? queuedCardNode = ui.PlayQueue.GetCardNode(card);
        if (queuedCardNode != null)
        {
            ui.PlayQueue.RemoveCardFromQueueForExecution(card);
            return;
        }
    }

    private static void RemoveCardFromCombat(MegaCrit.Sts2.Core.Models.CardModel card, CombatState combatState)
    {
        CardPile? oldPile = card.Pile;

        card.RemoveFromCurrentPile();
        RemoveCardVisual(card, oldPile);
        combatState.RemoveCard(card);
    }

    private bool HasAliveTeammateTarget()
    {
        Player? owner = base.Owner;
        return owner?.RunState?.Players.Any(player => player != owner && player.Creature != null && player.Creature.IsAlive) ?? false;
    }
}
