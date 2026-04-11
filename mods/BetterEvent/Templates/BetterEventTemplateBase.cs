using System.Collections.Generic;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Events;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Models;

namespace BetterEvent.Templates;

public abstract class BetterEventTemplateBase : EventModel
{
    protected const string InitialPage = "INITIAL";

    protected EventOption Option(string optionKey, Func<Task>? onChosen, params IHoverTip[] hoverTips)
    {
        return Option(InitialPage, optionKey, onChosen, hoverTips);
    }

    protected EventOption Option(string pageKey, string optionKey, Func<Task>? onChosen, params IHoverTip[] hoverTips)
    {
        return new EventOption(this, onChosen, GetOptionKey(pageKey, optionKey), hoverTips);
    }

    protected void SetPage(string pageKey, IReadOnlyList<EventOption> options)
    {
        SetEventState(L10NLookup(GetPageDescriptionKey(pageKey)), options);
    }

    protected void Finish(string pageKey)
    {
        SetEventFinished(L10NLookup(GetPageDescriptionKey(pageKey)));
    }

    protected string GetPageDescriptionKey(string pageKey)
    {
        return $"{base.Id.Entry}.pages.{pageKey}.description";
    }

    protected string GetOptionKey(string pageKey, string optionKey)
    {
        return $"{base.Id.Entry}.pages.{pageKey}.options.{optionKey}";
    }
}
