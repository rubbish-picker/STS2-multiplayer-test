using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Events;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;

namespace AiEvent;

public sealed class AiGeneratedEvent : MegaCrit.Sts2.Core.Models.EventModel
{
    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new GoldVar(28),
        new HealVar(6),
    };

    protected override IReadOnlyList<EventOption> GenerateInitialOptions()
    {
        return new EventOption[]
        {
            new(this, AcceptTheDraft, "AI_GENERATED_EVENT.pages.INITIAL.options.ACCEPT_THE_DRAFT"),
            new(this, ReviseThePrompt, "AI_GENERATED_EVENT.pages.INITIAL.options.REVISE_THE_PROMPT"),
        };
    }

    private async Task AcceptTheDraft()
    {
        await PlayerCmd.GainGold(DynamicVars.Gold.IntValue, Owner!);
        await CreatureCmd.Heal(Owner!.Creature, DynamicVars.Heal.IntValue);
        SetEventFinished(L10NLookup("AI_GENERATED_EVENT.pages.ACCEPT_THE_DRAFT.description"));
    }

    private Task ReviseThePrompt()
    {
        CardModel? card = PileType.Deck
            .GetPile(Owner!)
            .Cards
            .Where(card => card?.IsUpgradable ?? false)
            .OrderBy(_ => Rng.NextInt())
            .FirstOrDefault();

        if (card is not null)
        {
            CardCmd.Upgrade(card);
        }

        SetEventFinished(L10NLookup("AI_GENERATED_EVENT.pages.REVISE_THE_PROMPT.description"));
        return Task.CompletedTask;
    }
}
