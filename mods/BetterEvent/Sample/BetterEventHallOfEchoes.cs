using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BetterEvent.Templates;
using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Events;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.ValueProps;

namespace BetterEvent.Sample;

public sealed class BetterEventHallOfEchoes : BetterEventTemplateBase
{
    private const string ExplorePage = "EXPLORE";
    private const int GoldReward = 60;
    private const int DamageCost = 10;

    protected override IReadOnlyList<EventOption> GenerateInitialOptions()
    {
        bool canPurge = PileType.Deck.GetPile(base.Owner!).Cards.Any(static card => card?.IsRemovable ?? false);

        return new List<EventOption>
        {
            Option("LISTEN", ListenAsync),
            canPurge
                ? Option("PURGE", PurgeAsync).ThatDoesDamage(DamageCost)
                : new EventOption(this, null, GetOptionKey(InitialPage, "PURGE_LOCKED")),
            Option("LEAVE", LeaveAsync),
        };
    }

    private Task ListenAsync()
    {
        SetPage(
            ExplorePage,
            new List<EventOption>
            {
                Option(ExplorePage, "TREASURE", TakeTreasureAsync, HoverTipFactory.FromCardWithCardHoverTips<Guilty>().ToArray()),
                Option(ExplorePage, "INSIGHT", SeekInsightAsync),
                Option(ExplorePage, "RETREAT", RetreatAsync),
            });
        return Task.CompletedTask;
    }

    private async Task TakeTreasureAsync()
    {
        await PlayerCmd.GainGold(GoldReward, base.Owner!);
        await CardPileCmd.AddCursesToDeck(Enumerable.Repeat(ModelDb.Card<Guilty>(), 1), base.Owner!);
        Finish("TREASURE");
    }

    private async Task SeekInsightAsync()
    {
        List<CardModel> cards = (await CardSelectCmd.FromDeckForUpgrade(
                base.Owner!,
                new CardSelectorPrefs(CardSelectorPrefs.UpgradeSelectionPrompt, 2)))
            .ToList();

        foreach (CardModel card in cards)
        {
            CardCmd.Upgrade(card, CardPreviewStyle.EventLayout);
        }

        Finish("INSIGHT");
    }

    private Task RetreatAsync()
    {
        Finish("RETREAT");
        return Task.CompletedTask;
    }

    private async Task PurgeAsync()
    {
        await CreatureCmd.Damage(
            new ThrowingPlayerChoiceContext(),
            base.Owner!.Creature,
            DamageCost,
            ValueProp.Unblockable | ValueProp.Unpowered,
            null,
            null);

        List<CardModel> cards = (await CardSelectCmd.FromDeckForRemoval(
                base.Owner!,
                new CardSelectorPrefs(CardSelectorPrefs.RemoveSelectionPrompt, 1)))
            .ToList();

        if (cards.Count > 0)
        {
            await CardPileCmd.RemoveFromDeck(cards);
        }

        Finish("PURGE");
    }

    private Task LeaveAsync()
    {
        Finish("LEAVE");
        return Task.CompletedTask;
    }
}
