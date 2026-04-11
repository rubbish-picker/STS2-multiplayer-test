using MegaCrit.Sts2.Core.Models;

namespace BetterEvent.Infrastructure;

public interface IBetterEventRegistration
{
    Type EventType { get; }

    bool IsShared { get; }

    IReadOnlyCollection<Type> Acts { get; }

    string DebugName { get; }

    bool AppliesToAct(ActModel actModel);
}

public interface IBetterEventProvider
{
    IEnumerable<IBetterEventRegistration> GetRegistrations();
}

public sealed class BetterEventRegistration : IBetterEventRegistration
{
    private readonly HashSet<Type> _acts;

    public Type EventType { get; }

    public bool IsShared { get; }

    public IReadOnlyCollection<Type> Acts => _acts;

    public string DebugName { get; }

    private BetterEventRegistration(Type eventType, bool isShared, IEnumerable<Type>? acts, string? debugName)
    {
        if (!typeof(EventModel).IsAssignableFrom(eventType))
        {
            throw new ArgumentException($"{eventType.FullName} must inherit from {nameof(EventModel)}.", nameof(eventType));
        }

        EventType = eventType;
        IsShared = isShared;
        _acts = acts?
            .Where(static actType => actType != null)
            .ToHashSet() ?? new HashSet<Type>();
        DebugName = string.IsNullOrWhiteSpace(debugName) ? eventType.Name : debugName;

        if (!IsShared && _acts.Count == 0)
        {
            throw new ArgumentException("Act-specific registrations must define at least one target act.", nameof(acts));
        }
    }

    public static BetterEventRegistration Shared<TEvent>(string? debugName = null)
        where TEvent : EventModel
    {
        return new BetterEventRegistration(typeof(TEvent), isShared: true, acts: null, debugName);
    }

    public static BetterEventRegistration ForActs<TEvent>(params Type[] acts)
        where TEvent : EventModel
    {
        return new BetterEventRegistration(typeof(TEvent), isShared: false, acts, debugName: null);
    }

    public static BetterEventRegistration ForActs<TEvent>(IEnumerable<Type> acts, string? debugName = null)
        where TEvent : EventModel
    {
        return new BetterEventRegistration(typeof(TEvent), isShared: false, acts, debugName);
    }

    public bool AppliesToAct(ActModel actModel)
    {
        if (IsShared)
        {
            return true;
        }

        Type actType = actModel.GetType();
        return _acts.Contains(actType) || _acts.Any(candidate => candidate.IsAssignableFrom(actType));
    }
}
