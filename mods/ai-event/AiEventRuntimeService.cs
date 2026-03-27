using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Runs;

namespace AiEvent;

public static class AiEventRuntimeService
{
    private static readonly object SyncRoot = new();

    private static AiEventRunSession? _currentSession;

    public static void BeginRun(string seed, IReadOnlyList<ActModel> acts)
    {
        AiEventConfigService.Reload();
        AiEventRepository.Initialize();
        AiEventRepository.ResetActiveToFallbacks();

        AiEventRunSession session = new(seed, acts.Select(AiEventRegistry.TryGetSlotForAct).Where(slot => slot.HasValue).Select(slot => slot!.Value).ToList());
        lock (SyncRoot)
        {
            _currentSession = session;
        }

        AiEventMode mode = AiEventConfigService.GetMode();
        if (mode is AiEventMode.LlmDynamic or AiEventMode.LlmDebug)
        {
            _ = Task.Run(() => GenerateDynamicPoolAsync(session));
        }
    }

    public static EventModel SelectNextEvent(RunState runState, EventModel vanillaEvent)
    {
        AiEventConfigService.Reload();
        AiEventRepository.Initialize();

        AiEventMode mode = AiEventConfigService.GetMode();
        AiEventSlot? actSlot = AiEventRegistry.TryGetSlotForAct(runState.Act);
        if (actSlot == null)
        {
            return vanillaEvent;
        }

        if (mode == AiEventMode.Vanilla)
        {
            return vanillaEvent;
        }

        List<AiEventSlot> allowedSlots = new() { actSlot.Value, AiEventSlot.Shared };
        IReadOnlyList<AiEventPoolEntry> cachedCandidates = AiEventRepository.GetLatestPoolEntries(allowedSlots, AiEventConfigService.Current.CachePoolLimit);

        if (mode == AiEventMode.VanillaPlusCache)
        {
            return TryChooseCachedOrVanilla(runState, vanillaEvent, cachedCandidates);
        }

        AiEventRunSession? session = GetCurrentSession(runState.Rng.StringSeed);
        if (mode == AiEventMode.LlmDebug)
        {
            if (session != null && TryTakeDynamicEntry(session, allowedSlots, out AiEventPoolEntry? debugEntry))
            {
                return ActivateEntry(debugEntry!);
            }

            MainFile.Logger.Warn($"[ai-event] debug mode fell back to vanilla event for act {runState.Act.Id.Entry} because no dynamic event was ready.");
            return vanillaEvent;
        }

        return TryChooseDynamicCacheOrVanilla(runState, vanillaEvent, session, allowedSlots, cachedCandidates);
    }

    public static bool ShouldForceUnknownNodesToEvents()
    {
        AiEventConfigService.Reload();
        return AiEventConfigService.GetMode() == AiEventMode.LlmDebug;
    }

    private static AiEventRunSession? GetCurrentSession(string? seed)
    {
        lock (SyncRoot)
        {
            if (_currentSession == null)
            {
                return null;
            }

            if (!string.Equals(_currentSession.Seed, seed, StringComparison.Ordinal))
            {
                return null;
            }

            return _currentSession;
        }
    }

    private static EventModel TryChooseCachedOrVanilla(RunState runState, EventModel vanillaEvent, IReadOnlyList<AiEventPoolEntry> cachedCandidates)
    {
        double vanillaWeight = Math.Max(0d, AiEventConfigService.Current.VanillaWeight);
        double cacheWeight = cachedCandidates.Count > 0 ? Math.Max(0d, AiEventConfigService.Current.CacheWeight) : 0d;

        if (cacheWeight <= 0d)
        {
            return vanillaEvent;
        }

        if (RollWeight(runState, cacheWeight, vanillaWeight))
        {
            Random random = CreateSelectionRandom(runState);
            AiEventPoolEntry chosen = cachedCandidates[random.Next(cachedCandidates.Count)];
            return ActivateEntry(chosen);
        }

        return vanillaEvent;
    }

    private static EventModel TryChooseDynamicCacheOrVanilla(
        RunState runState,
        EventModel vanillaEvent,
        AiEventRunSession? session,
        IReadOnlyList<AiEventSlot> allowedSlots,
        IReadOnlyList<AiEventPoolEntry> cachedCandidates)
    {
        double dynamicWeight = session != null && HasDynamicEntry(session, allowedSlots)
            ? Math.Max(0d, AiEventConfigService.Current.DynamicWeight)
            : 0d;
        double cacheWeight = cachedCandidates.Count > 0
            ? Math.Max(0d, AiEventConfigService.Current.CacheWeight)
            : 0d;
        double vanillaWeight = Math.Max(0d, AiEventConfigService.Current.VanillaWeight);

        string source = RollSource(runState, dynamicWeight, cacheWeight, vanillaWeight);
        switch (source)
        {
            case "dynamic":
                if (session != null && TryTakeDynamicEntry(session, allowedSlots, out AiEventPoolEntry? dynamicEntry))
                {
                    return ActivateEntry(dynamicEntry!);
                }

                MainFile.Logger.Warn($"[ai-event] dynamic event fallback to vanilla in act {runState.Act.Id.Entry} because no generated event was ready.");
                return vanillaEvent;

            case "cache":
                if (cachedCandidates.Count > 0)
                {
                    Random random = CreateSelectionRandom(runState);
                    AiEventPoolEntry cachedEntry = cachedCandidates[random.Next(cachedCandidates.Count)];
                    return ActivateEntry(cachedEntry);
                }

                return vanillaEvent;

            default:
                return vanillaEvent;
        }
    }

    private static string RollSource(RunState runState, double dynamicWeight, double cacheWeight, double vanillaWeight)
    {
        List<(string Name, double Weight)> choices = new();
        if (dynamicWeight > 0d)
        {
            choices.Add(("dynamic", dynamicWeight));
        }

        if (cacheWeight > 0d)
        {
            choices.Add(("cache", cacheWeight));
        }

        if (vanillaWeight > 0d || choices.Count == 0)
        {
            choices.Add(("vanilla", Math.Max(0.0001d, vanillaWeight)));
        }

        double total = choices.Sum(choice => choice.Weight);
        double roll = CreateSelectionRandom(runState).NextDouble() * total;
        double cumulative = 0d;

        foreach ((string name, double weight) in choices)
        {
            cumulative += weight;
            if (roll <= cumulative)
            {
                return name;
            }
        }

        return choices.Last().Name;
    }

    private static bool RollWeight(RunState runState, double preferredWeight, double fallbackWeight)
    {
        double total = preferredWeight + fallbackWeight;
        if (total <= 0d)
        {
            return false;
        }

        return CreateSelectionRandom(runState).NextDouble() * total <= preferredWeight;
    }

    private static EventModel ActivateEntry(AiEventPoolEntry entry)
    {
        AiGeneratedEventPayload payload = entry.Payload;
        payload.EventKey = AiEventRegistry.GetEventKey(payload.Slot);
        AiEventRepository.SetActive(payload);
        AiEventLocalization.ApplyCurrentLanguage();
        return AiEventRegistry.GetModelForSlot(payload.Slot);
    }

    private static bool HasDynamicEntry(AiEventRunSession session, IReadOnlyList<AiEventSlot> allowedSlots)
    {
        lock (session.SyncRoot)
        {
            return session.DynamicReadyEntries.Any(entry =>
                !session.UsedDynamicEntryIds.Contains(entry.EntryId) &&
                allowedSlots.Contains(entry.Payload.Slot));
        }
    }

    private static bool TryTakeDynamicEntry(AiEventRunSession session, IReadOnlyList<AiEventSlot> allowedSlots, out AiEventPoolEntry? entry)
    {
        lock (session.SyncRoot)
        {
            List<AiEventPoolEntry> candidates = session.DynamicReadyEntries
                .Where(item => !session.UsedDynamicEntryIds.Contains(item.EntryId) && allowedSlots.Contains(item.Payload.Slot))
                .ToList();

            if (candidates.Count == 0)
            {
                entry = null;
                return false;
            }

            entry = candidates[session.Random.Next(candidates.Count)];
            session.UsedDynamicEntryIds.Add(entry.EntryId);
            return true;
        }
    }

    private static async Task GenerateDynamicPoolAsync(AiEventRunSession session)
    {
        try
        {
            string? validationError = await AiEventGenerationService.ValidateConnectivityAsync();
            if (!string.IsNullOrWhiteSpace(validationError))
            {
                MainFile.Logger.Error($"[ai-event] dynamic generation disabled for seed {session.Seed}: {validationError}");
                return;
            }

            List<AiGeneratedEventPayload> generatedPayloads = new();
            foreach (AiEventSlot slot in session.GenerationPlan)
            {
                if (!IsSessionCurrent(session))
                {
                    return;
                }

                try
                {
                    AiGeneratedEventPayload payload = await AiEventGenerationService.GeneratePayloadAsync(slot, session.Seed);
                    AiEventPoolEntry entry = AiEventRepository.CreatePoolEntry(payload, "llm_dynamic", session.Seed);
                    AiEventRepository.AddPoolEntry(entry);

                    lock (session.SyncRoot)
                    {
                        session.DynamicReadyEntries.Add(entry);
                    }

                    generatedPayloads.Add(entry.Payload);
                    MainFile.Logger.Info($"[ai-event] generated dynamic event {entry.EntryId} for slot {slot}.");
                }
                catch (Exception ex)
                {
                    MainFile.Logger.Error($"[ai-event] failed to generate dynamic event for slot {slot}: {ex}");
                }
            }

            if (generatedPayloads.Count > 0)
            {
                AiEventRepository.SaveHistorySnapshot(session.Seed, "llm_dynamic_batch", generatedPayloads);
            }
        }
        catch (Exception ex)
        {
            MainFile.Logger.Error($"[ai-event] dynamic generation loop failed: {ex}");
        }
    }

    private static bool IsSessionCurrent(AiEventRunSession session)
    {
        lock (SyncRoot)
        {
            return ReferenceEquals(_currentSession, session);
        }
    }

    private static Random CreateSelectionRandom(RunState runState)
    {
        int seed = HashCode.Combine(runState.Rng.StringSeed, runState.CurrentActIndex, runState.TotalFloor, runState.MapPointHistory.Count);
        return new Random(seed);
    }

    private sealed class AiEventRunSession
    {
        public AiEventRunSession(string seed, List<AiEventSlot> actSlots)
        {
            Seed = seed;
            ActSlots = actSlots;
            GenerationPlan = BuildGenerationPlan(actSlots);
            Random = new Random(seed.GetHashCode());
        }

        public string Seed { get; }

        public List<AiEventSlot> ActSlots { get; }

        public List<AiEventSlot> GenerationPlan { get; }

        public List<AiEventPoolEntry> DynamicReadyEntries { get; } = new();

        public HashSet<string> UsedDynamicEntryIds { get; } = new(StringComparer.OrdinalIgnoreCase);

        public object SyncRoot { get; } = new();

        public Random Random { get; }

        private static List<AiEventSlot> BuildGenerationPlan(IReadOnlyList<AiEventSlot> actSlots)
        {
            int target = Math.Max(1, AiEventConfigService.Current.DynamicEventsPerRun);
            if (actSlots.Count == 0)
            {
                return Enumerable.Repeat(AiEventSlot.Shared, target).ToList();
            }

            List<AiEventSlot> plan = new();
            int index = 0;
            while (plan.Count < target)
            {
                AiEventSlot actSlot = actSlots[index % actSlots.Count];
                if (plan.Count < target)
                {
                    plan.Add(actSlot);
                }

                if (plan.Count < target)
                {
                    plan.Add(actSlot);
                }

                if (plan.Count < target)
                {
                    plan.Add(actSlot);
                }

                if (plan.Count < target)
                {
                    plan.Add(AiEventSlot.Shared);
                }

                if (plan.Count < target)
                {
                    plan.Add(actSlot);
                }

                index++;
            }

            return plan.Take(target).ToList();
        }
    }
}
