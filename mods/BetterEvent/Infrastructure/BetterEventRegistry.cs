using BetterEvent.Templates;
using MegaCrit.Sts2.Core.Models;

namespace BetterEvent.Infrastructure;

public static class BetterEventRegistry
{
    private static readonly List<IBetterEventRegistration> RegistrationsInternal = new();
    private static readonly HashSet<string> RegistrationKeys = new(StringComparer.Ordinal);
    private static bool _initialized;

    public static IReadOnlyList<IBetterEventRegistration> Registrations => RegistrationsInternal;

    public static void Initialize()
    {
        if (_initialized)
        {
            return;
        }

        RegisterProvider(new EmptyBetterEventProvider());
        _initialized = true;

        MainFile.Logger.Info($"BetterEvent registry initialized with {RegistrationsInternal.Count} registrations.");
    }

    public static void RegisterProvider(IBetterEventProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);

        foreach (IBetterEventRegistration registration in provider.GetRegistrations())
        {
            Register(registration);
        }
    }

    public static void Register(IBetterEventRegistration registration)
    {
        ArgumentNullException.ThrowIfNull(registration);

        if (!typeof(EventModel).IsAssignableFrom(registration.EventType))
        {
            throw new ArgumentException($"{registration.EventType.FullName} must inherit from {nameof(EventModel)}.");
        }

        string key = BuildRegistrationKey(registration);
        if (!RegistrationKeys.Add(key))
        {
            MainFile.Logger.Warn($"BetterEvent skipped duplicate registration: {registration.DebugName}");
            return;
        }

        RegistrationsInternal.Add(registration);
    }

    public static IReadOnlyList<IBetterEventRegistration> GetRegistrationsForAct(ActModel actModel)
    {
        return RegistrationsInternal
            .Where(registration => registration.AppliesToAct(actModel))
            .ToList();
    }

    public static EventModel GetCanonicalEventModel(Type eventType)
    {
        return ModelDb.GetById<EventModel>(ModelDb.GetId(eventType));
    }

    private static string BuildRegistrationKey(IBetterEventRegistration registration)
    {
        string acts = registration.IsShared
            ? "*"
            : string.Join("|", registration.Acts.Select(type => type.FullName).OrderBy(name => name, StringComparer.Ordinal));
        return $"{registration.EventType.FullName}:{registration.IsShared}:{acts}";
    }
}
