using System.Collections.Generic;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Events;
using BetterEvent.Templates;

namespace BetterEvent.Sample;

public sealed class BetterEventSampleEvent : BetterEventTemplateBase
{
    private const int GoldReward = 20;

    protected override IReadOnlyList<EventOption> GenerateInitialOptions()
    {
        return new List<EventOption>
        {
            Option("READ", ReadNotebookAsync),
            Option("LEAVE", LeaveAsync),
        };
    }

    private async Task ReadNotebookAsync()
    {
        await PlayerCmd.GainGold(GoldReward, base.Owner!);
        await CreatureCmd.Heal(base.Owner!.Creature, 6);
        Finish("READ");
    }

    private Task LeaveAsync()
    {
        Finish("LEAVE");
        return Task.CompletedTask;
    }
}
