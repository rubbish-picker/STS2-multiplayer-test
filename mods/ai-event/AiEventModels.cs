using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Events;
using MegaCrit.Sts2.Core.Factories;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.ValueProps;

namespace AiEvent;

public abstract class AiGeneratedRegionEvent : EventModel
{
    protected abstract AiEventSlot Slot { get; }

    protected virtual bool IsSharedEvent => false;

    public override bool IsShared => IsSharedEvent;

    protected override IReadOnlyList<EventOption> GenerateInitialOptions()
    {
        AiGeneratedEventPayload payload = AiEventRepository.Get(Slot);
        List<EventOption> options = new();

        foreach (AiEventOptionPayload optionPayload in payload.Options)
        {
            string optionKey = optionPayload.Key;
            string textKey = $"{base.Id.Entry}.pages.INITIAL.options.{optionKey}";

            EventOption option = new(this, () => ChooseOptionAsync(optionPayload), textKey);

            int damage = optionPayload.Effects
                .Where(e => e.Type == "damage_self")
                .Sum(e => e.Amount);
            if (damage > 0)
            {
                option = option.ThatDoesDamage(damage);
            }

            int maxHpLoss = optionPayload.Effects
                .Where(e => e.Type == "lose_max_hp")
                .Sum(e => e.Amount);
            if (maxHpLoss > 0)
            {
                option = option.ThatDecreasesMaxHp(maxHpLoss);
            }

            options.Add(option);
        }

        return options;
    }

    private async Task ChooseOptionAsync(AiEventOptionPayload optionPayload)
    {
        foreach (AiEventEffectPayload effect in optionPayload.Effects)
        {
            await ExecuteEffectAsync(effect);
        }

        SetEventFinished(L10NLookup($"{base.Id.Entry}.pages.{optionPayload.Key}_RESULT.description"));
    }

    private async Task ExecuteEffectAsync(AiEventEffectPayload effect)
    {
        switch (effect.Type)
        {
            case "gain_gold":
                await PlayerCmd.GainGold(effect.Amount, base.Owner!);
                return;

            case "lose_gold":
                await PlayerCmd.LoseGold(effect.Amount, base.Owner!);
                return;

            case "heal":
                await CreatureCmd.Heal(base.Owner!.Creature, effect.Amount);
                return;

            case "damage_self":
                await CreatureCmd.Damage(
                    new ThrowingPlayerChoiceContext(),
                    base.Owner!.Creature,
                    effect.Amount,
                    ValueProp.Unblockable | ValueProp.Unpowered,
                    null,
                    null);
                return;

            case "gain_max_hp":
                await CreatureCmd.GainMaxHp(base.Owner!.Creature, effect.Amount);
                return;

            case "lose_max_hp":
                await CreatureCmd.LoseMaxHp(new ThrowingPlayerChoiceContext(), base.Owner!.Creature, effect.Amount, isFromCard: false);
                return;

            case "upgrade_cards":
                await UpgradeChosenCardsAsync(effect.Count);
                return;

            case "upgrade_random":
                UpgradeRandomCards(effect.Count);
                return;

            case "remove_cards":
                await RemoveChosenCardsAsync(effect.Count);
                return;

            case "add_curse":
                await AddCurseAsync(effect.CardId, effect.Count);
                return;

            case "obtain_random_relic":
                await ObtainRandomRelicsAsync(effect.Count);
                return;

            default:
                MainFile.Logger.Warn($"Unsupported ai-event effect type at runtime: {effect.Type}");
                return;
        }
    }

    private async Task UpgradeChosenCardsAsync(int count)
    {
        List<CardModel> cards = (await CardSelectCmd.FromDeckForUpgrade(
                base.Owner!,
                new CardSelectorPrefs(CardSelectorPrefs.UpgradeSelectionPrompt, count)))
            .ToList();

        foreach (CardModel card in cards)
        {
            CardCmd.Upgrade(card, CardPreviewStyle.EventLayout);
        }
    }

    private void UpgradeRandomCards(int count)
    {
        List<CardModel> cards = PileType.Deck.GetPile(base.Owner!).Cards
            .Where(c => c?.IsUpgradable ?? false)
            .OrderBy(_ => base.Rng.NextFloat())
            .Take(count)
            .ToList();

        foreach (CardModel card in cards)
        {
            CardCmd.Upgrade(card, CardPreviewStyle.EventLayout);
        }
    }

    private async Task RemoveChosenCardsAsync(int count)
    {
        List<CardModel> cards = (await CardSelectCmd.FromDeckForRemoval(
                base.Owner!,
                new CardSelectorPrefs(CardSelectorPrefs.RemoveSelectionPrompt, count)))
            .ToList();

        if (cards.Count > 0)
        {
            await CardPileCmd.RemoveFromDeck(cards);
        }
    }

    private async Task AddCurseAsync(string cardId, int count)
    {
        CardModel? curse = AiEventEffectCatalog.TryGetCurseCard(cardId);
        if (curse == null)
        {
            MainFile.Logger.Warn($"ai-event skipped unknown curse id: {cardId}");
            return;
        }

        List<CardModel> curses = Enumerable.Range(0, count)
            .Select(_ => curse)
            .ToList();

        await CardPileCmd.AddCursesToDeck(curses, base.Owner!);
    }

    private async Task ObtainRandomRelicsAsync(int count)
    {
        for (int i = 0; i < count; i++)
        {
            RelicModel relic = RelicFactory.PullNextRelicFromFront(base.Owner!).ToMutable();
            await RelicCmd.Obtain(relic, base.Owner!);
        }
    }
}

public sealed class AiOvergrowthEvent : AiGeneratedRegionEvent
{
    protected override AiEventSlot Slot => AiEventSlot.Overgrowth;
}

public sealed class AiHiveEvent : AiGeneratedRegionEvent
{
    protected override AiEventSlot Slot => AiEventSlot.Hive;
}

public sealed class AiGloryEvent : AiGeneratedRegionEvent
{
    protected override AiEventSlot Slot => AiEventSlot.Glory;
}

public sealed class AiUnderdocksEvent : AiGeneratedRegionEvent
{
    protected override AiEventSlot Slot => AiEventSlot.Underdocks;
}

public sealed class AiSharedEvent : AiGeneratedRegionEvent
{
    protected override AiEventSlot Slot => AiEventSlot.Shared;

    protected override bool IsSharedEvent => true;
}
