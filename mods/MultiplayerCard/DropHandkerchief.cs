using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BaseLib;
using BaseLib.Abstracts;
using BaseLib.Utils;
using Godot;
using HarmonyLib;
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
        MainFile.Logger.Info($"[DropHandkerchief] OnPlay begin. owner={base.Owner?.NetId}, target={teammate?.NetId}, context={choiceContext.GetType().Name}, action={DescribeCurrentAction()}");
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
            MainFile.Logger.Info($"[DropHandkerchief] Target invalid before teammate card selection. target={teammate.NetId}, action={DescribeCurrentAction()}");
            await CancelCurrentPlayAndReturnToHand(cardPlay.Resources);
            return;
        }

        (SelectionOutcome outcome, MegaCrit.Sts2.Core.Models.CardModel? selectedCard) = await ChooseTeammateHandCard(choiceContext, teammate);
        MainFile.Logger.Info($"[DropHandkerchief] Teammate selection finished. outcome={outcome}, selected={selectedCard?.Id.Entry ?? "null"}, action={DescribeCurrentAction()}");
        if (outcome == SelectionOutcome.TargetInvalid)
        {
            await CancelCurrentPlayAndReturnToHand(cardPlay.Resources);
            return;
        }

        if (outcome == SelectionOutcome.NoCard || selectedCard == null)
        {
            return;
        }

        if (!await TransferExistingCardToHand(selectedCard, base.Owner, requireCurrentHandOwner: teammate))
        {
            return;
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

        CardSelectorPrefs prefs = new(SelectionScreenPrompt, 1);
        MainFile.Logger.Info(
            $"[DropHandkerchief] Opening teammate hand select. owner={base.Owner.NetId}, target={teammate.NetId}, " +
            $"handCount={initialHandCards.Count}, context={choiceContext.GetType().Name}, action={DescribeCurrentAction()}, " +
            $"paused={RunManager.Instance.ActionExecutor.IsPaused}, running={RunManager.Instance.ActionExecutor.IsRunning}");
        uint choiceId = RunManager.Instance.PlayerChoiceSynchronizer.ReserveChoiceId(base.Owner);
        await choiceContext.SignalPlayerChoiceBegun(PlayerChoiceOptions.CancelPlayCardActions);
        List<ulong> pausedOtherQueues = PauseOtherPlayerQueuesDuringSelection(base.Owner.NetId);
        try
        {
            MegaCrit.Sts2.Core.Models.CardModel? selectedCard;
            if (ShouldSelectLocalCard(base.Owner))
            {
                if (CardSelectCmd.Selector != null)
                {
                    selectedCard = (await CardSelectCmd.Selector.GetSelectedCards(initialHandCards, prefs.MinSelect, prefs.MaxSelect)).FirstOrDefault();
                }
                else
                {
                    selectedCard = await SelectTeammateHandCardWithHandUi(teammate, initialHandCards, prefs);
                }

                RunManager.Instance.PlayerChoiceSynchronizer.SyncLocalChoice(
                    base.Owner,
                    choiceId,
                    PlayerChoiceResult.FromMutableCombatCards(selectedCard == null
                        ? Array.Empty<MegaCrit.Sts2.Core.Models.CardModel>()
                        : new[] { selectedCard }));
            }
            else
            {
                selectedCard = (await RunManager.Instance.PlayerChoiceSynchronizer.WaitForRemoteChoice(base.Owner, choiceId))
                    .AsCombatCards()
                    .FirstOrDefault();
            }

            MainFile.Logger.Info($"[DropHandkerchief] Returned from teammate hand select. selected={selectedCard?.Id.Entry ?? "null"}, action={DescribeCurrentAction()}");
            if (selectedCard == null)
            {
                return !IsTeammateStillValid(teammate)
                    ? (SelectionOutcome.TargetInvalid, null)
                    : (SelectionOutcome.NoCard, null);
            }

            return !IsTeammateStillValid(teammate)
                ? (SelectionOutcome.TargetInvalid, null)
                : (SelectionOutcome.Selected, selectedCard);
        }
        finally
        {
            try
            {
                await choiceContext.SignalPlayerChoiceEnded();
            }
            finally
            {
                ResumePlayerQueues(pausedOtherQueues);
            }
        }
    }

    private async Task<MegaCrit.Sts2.Core.Models.CardModel?> SelectTeammateHandCardWithHandUi(
        Player teammate,
        IReadOnlyList<MegaCrit.Sts2.Core.Models.CardModel> teammateHandCards,
        CardSelectorPrefs prefs)
    {
        NPlayerHand? handUi = NCombatRoom.Instance?.Ui?.Hand;
        if (handUi == null)
        {
            return null;
        }

        handUi.CancelAllCardPlay();
        List<MegaCrit.Sts2.Core.Models.CardModel> temporaryCards = new();
        foreach (MegaCrit.Sts2.Core.Models.CardModel teammateCard in teammateHandCards)
        {
            if (handUi.GetCardHolder(teammateCard) != null)
            {
                continue;
            }

            handUi.Add(NCard.Create(teammateCard));
            temporaryCards.Add(teammateCard);
        }

        try
        {
            return (await handUi.SelectCards(
                    prefs,
                    card => temporaryCards.Contains(card) && IsCardStillInTeammateHand(card, teammate),
                    this))
                .FirstOrDefault();
        }
        catch (TaskCanceledException)
        {
            return null;
        }
        finally
        {
            CleanupTemporaryHandCards(handUi, temporaryCards);
        }
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

        CancelQueuedPlayIfNecessary(card);

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

    private static bool IsTeammateStillValid(Player? teammate)
    {
        return teammate?.Creature != null && teammate.Creature.IsAlive;
    }

    private static bool ShouldSelectLocalCard(Player player)
    {
        return LocalContext.IsMe(player) && RunManager.Instance.NetService.Type != NetGameType.Replay;
    }

    private static string DescribeCurrentAction()
    {
        GameAction? action = RunManager.Instance.ActionExecutor.CurrentlyRunningAction;
        if (action == null)
        {
            return "null";
        }

        return $"{action.GetType().Name}(id={action.Id?.ToString() ?? "null"}, state={action.State}, owner={action.OwnerId})";
    }

    private static List<ulong> PauseOtherPlayerQueuesDuringSelection(ulong activePlayerId)
    {
        List<ulong> pausedPlayerIds = new();
        foreach (object queue in GetActionQueues())
        {
            if (!TryGetQueueOwnerId(queue, out ulong ownerId) || ownerId == activePlayerId || IsQueuePaused(queue))
            {
                continue;
            }

            SetQueuePaused(queue, true);
            pausedPlayerIds.Add(ownerId);
        }

        if (pausedPlayerIds.Count > 0)
        {
            MainFile.Logger.Info($"[DropHandkerchief] Paused other player queues during selection: {string.Join(",", pausedPlayerIds)}");
        }

        return pausedPlayerIds;
    }

    private static void ResumePlayerQueues(IEnumerable<ulong> playerIds)
    {
        HashSet<ulong> playerIdSet = playerIds.ToHashSet();
        if (playerIdSet.Count == 0)
        {
            return;
        }

        foreach (object queue in GetActionQueues())
        {
            if (TryGetQueueOwnerId(queue, out ulong ownerId) && playerIdSet.Contains(ownerId))
            {
                SetQueuePaused(queue, false);
            }
        }

        MainFile.Logger.Info($"[DropHandkerchief] Resumed paused player queues after selection: {string.Join(",", playerIdSet)}");
    }

    private static IEnumerable GetActionQueues()
    {
        return AccessTools.Field(typeof(ActionQueueSet), "_actionQueues")?.GetValue(RunManager.Instance.ActionQueueSet) as IEnumerable
            ?? Array.Empty<object>();
    }

    private static bool TryGetQueueOwnerId(object queue, out ulong ownerId)
    {
        if (AccessTools.Field(queue.GetType(), "ownerId")?.GetValue(queue) is ulong value)
        {
            ownerId = value;
            return true;
        }

        ownerId = default;
        return false;
    }

    private static bool IsQueuePaused(object queue)
    {
        return AccessTools.Field(queue.GetType(), "isPaused")?.GetValue(queue) as bool? ?? false;
    }

    private static void SetQueuePaused(object queue, bool paused)
    {
        AccessTools.Field(queue.GetType(), "isPaused")?.SetValue(queue, paused);
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

    private static void CancelQueuedPlayIfNecessary(MegaCrit.Sts2.Core.Models.CardModel card)
    {
        NCardPlayQueue? playQueue = NCombatRoom.Instance?.Ui?.PlayQueue;
        if (playQueue?.GetCardNode(card) == null)
        {
            return;
        }

        PlayCardAction? queuedAction = GetQueuedPlayAction(card);
        if (queuedAction == null)
        {
            MainFile.Logger.Info($"[DropHandkerchief] Selected card {card.Id.Entry} was queued but no PlayCardAction was found.");
            return;
        }

        MainFile.Logger.Info($"[DropHandkerchief] Selected card {card.Id.Entry} was already queued by player {queuedAction.OwnerId}. Cancelling queued play before exchange.");
        queuedAction.Cancel();
    }

    private static PlayCardAction? GetQueuedPlayAction(MegaCrit.Sts2.Core.Models.CardModel card)
    {
        IEnumerable queueItems = AccessTools.Field(typeof(NCardPlayQueue), "_playQueue")?.GetValue(NCombatRoom.Instance?.Ui?.PlayQueue) as IEnumerable
            ?? Array.Empty<object>();

        foreach (object queueItem in queueItems)
        {
            if (AccessTools.Field(queueItem.GetType(), "card")?.GetValue(queueItem) is not NCard queuedCard || queuedCard.Model != card)
            {
                continue;
            }

            return AccessTools.Field(queueItem.GetType(), "action")?.GetValue(queueItem) as PlayCardAction;
        }

        return null;
    }

    private static void CleanupTemporaryHandCards(NPlayerHand handUi, IEnumerable<MegaCrit.Sts2.Core.Models.CardModel> temporaryCards)
    {
        foreach (MegaCrit.Sts2.Core.Models.CardModel temporaryCard in temporaryCards)
        {
            if (handUi.GetCardHolder(temporaryCard) == null)
            {
                continue;
            }

            handUi.Remove(temporaryCard);
        }
    }

    private bool HasAliveTeammateTarget()
    {
        Player? owner = base.Owner;
        return owner?.RunState?.Players.Any(player => player != owner && player.Creature != null && player.Creature.IsAlive) ?? false;
    }
}
