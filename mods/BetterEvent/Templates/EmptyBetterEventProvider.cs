using MegaCrit.Sts2.Core.Models.Acts;
using BetterEvent.Infrastructure;
using BetterEvent.Sample;

namespace BetterEvent.Templates;

internal sealed class EmptyBetterEventProvider : IBetterEventProvider
{
    public IEnumerable<IBetterEventRegistration> GetRegistrations()
    {
        // Replace this file with your real provider when you start adding more content.
        //
        // Example registrations:
        // yield return BetterEventRegistration.Shared<MySharedEvent>();
        // yield return BetterEventRegistration.ForActs<MyOvergrowthEvent>(typeof(Overgrowth));
        // yield return BetterEventRegistration.ForActs<MyMultiActEvent>(new[] { typeof(Overgrowth), typeof(Hive) });
        //
        // The sample event is shared on purpose so debug mode always has at least one event to force.

        _ = typeof(Overgrowth);
        yield return BetterEventRegistration.Shared<BetterEventSampleEvent>("sample-shared-event");
        yield return BetterEventRegistration.Shared<BetterEventHallOfEchoes>("hall-of-echoes");
    }
}
