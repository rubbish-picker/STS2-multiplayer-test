using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Text.Json;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Runs;

namespace AiEvent;

public static class AiEventRuntimeService
{
    private const string DynamicTitlePrefix = "[lb]llm dynamic[rb]";
    private const string CacheTitlePrefix = "[lb]llm cache[rb]";
    private const int DynamicGenerationConcurrency = 5;

    private static readonly object SyncRoot = new();
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
    };

    private static AiEventRunSession? _currentSession;

    private static string SessionStatePath => AiEventStorage.GetCurrentSessionStatePath();

    public static void BeginRun(string seed, IReadOnlyList<ActModel> acts)
    {
        AiEventConfigService.Reload();
        AiEventRepository.Initialize();
        AiEventRepository.ResetActiveToFallbacks();
        AiEventMultiplayerSync.Clear();

        AiEventRunSession session = new(seed, acts.Select(AiEventRegistry.TryGetSlotForAct).Where(slot => slot.HasValue).Select(slot => slot!.Value).ToList());
        lock (SyncRoot)
        {
            _currentSession = session;
        }

        SaveSessionState(session);

        AiEventMode mode = AiEventConfigService.GetMode();
        if (mode is AiEventMode.LlmDynamic or AiEventMode.LlmDebug && ShouldGenerateLocally())
        {
            _ = Task.Run(() => GenerateDynamicPoolAsync(session));
        }
    }

    public static void ResumeRun(RunState runState)
    {
        AiEventConfigService.Reload();
        AiEventRepository.Initialize();
        AiEventMultiplayerSync.InitializeForRun();

        AiEventMode mode = AiEventConfigService.GetMode();
        if (mode is AiEventMode.Vanilla or AiEventMode.VanillaPlusCache)
        {
            lock (SyncRoot)
            {
                _currentSession = null;
            }

            return;
        }

        string seed = runState.Rng.StringSeed;
        AiEventRunSession session = LoadOrCreateSession(seed, runState.Acts);

        lock (SyncRoot)
        {
            _currentSession = session;
        }

        SaveSessionState(session);

        if (session.GeneratedCount < session.GenerationPlan.Count && ShouldGenerateLocally())
        {
            _ = Task.Run(() => GenerateDynamicPoolAsync(session));
        }
    }

    public static EventModel SelectNextEvent(RunState runState, EventModel vanillaEvent)
    {
        AiEventConfigService.Reload();
        AiEventRepository.Initialize();
        AiEventMultiplayerSync.InitializeForRun();

        AiEventMode mode = AiEventConfigService.GetMode();
        AiEventSlot? actSlot = AiEventRegistry.TryGetSlotForAct(runState.Act);
        if (actSlot == null)
        {
            return vanillaEvent;
        }

        if (AiEventMultiplayerSync.IsClientControlled())
        {
            if (AiEventMultiplayerSync.TryConsumeSelection(runState.CurrentLocation, out AiEventSelectionDecision syncedDecision, timeoutMs: 15000))
            {
                return ApplySelectionDecision(syncedDecision, vanillaEvent);
            }

            throw new InvalidOperationException($"[ai-event] client did not receive host event selection for location {runState.CurrentLocation} in act {runState.Act.Id.Entry} within 15 seconds. Refusing to fallback locally because multiplayer must stay host-authoritative.");
        }

        if (mode == AiEventMode.Vanilla)
        {
            BroadcastIfHost(VanillaDecision);
            return vanillaEvent;
        }

        AiEventSelectionDecision? assignedDecision = GetAssignedDecision(runState);
        if (assignedDecision.HasValue)
        {
            BroadcastIfHost(assignedDecision.Value);
            return ApplySelectionDecision(assignedDecision.Value, vanillaEvent);
        }

        List<AiEventSlot> allowedSlots = new() { actSlot.Value, AiEventSlot.Shared };
        IReadOnlyList<AiEventPoolEntry> cachedCandidates = AiEventRepository.GetLatestPoolEntries(allowedSlots, AiEventConfigService.GetEffectiveConfig().CachePoolLimit);

        if (mode == AiEventMode.VanillaPlusCache)
        {
            AiEventSelectionDecision decision = TryChooseCachedOrVanilla(runState, cachedCandidates);
            RememberAssignedDecision(runState, decision);
            BroadcastIfHost(decision);
            return ApplySelectionDecision(decision, vanillaEvent);
        }

        AiEventRunSession? session = GetCurrentSession(runState.Rng.StringSeed);
        if (mode == AiEventMode.LlmDebug)
        {
            if (session != null && TryTakeDynamicEntry(session, allowedSlots, out AiEventPoolEntry? debugEntry))
            {
                AiEventSelectionDecision decision = CreateDecision(debugEntry!, DynamicTitlePrefix);
                RememberAssignedDecision(runState, decision);
                BroadcastIfHost(decision);
                return ApplySelectionDecision(decision, vanillaEvent);
            }

            MainFile.Logger.Warn($"[ai-event] debug mode fell back to vanilla event for act {runState.Act.Id.Entry} because no dynamic event was ready.");
            BroadcastIfHost(VanillaDecision);
            return vanillaEvent;
        }

        AiEventSelectionDecision finalDecision = TryChooseDynamicCacheOrVanilla(runState, session, allowedSlots, cachedCandidates);
        RememberAssignedDecision(runState, finalDecision);
        BroadcastIfHost(finalDecision);
        return ApplySelectionDecision(finalDecision, vanillaEvent);
    }

    public static bool ShouldForceUnknownNodesToEvents()
    {
        AiEventConfigService.Reload();
        return AiEventConfigService.GetMode() == AiEventMode.LlmDebug;
    }

    public static void StopActiveRun(string reason, bool finalizeDynamicEntries)
    {
        AiEventRunSession? endingSession = null;
        lock (SyncRoot)
        {
            if (_currentSession != null)
            {
                endingSession = _currentSession;
                lock (_currentSession.SyncRoot)
                {
                    _currentSession.IsGenerating = false;
                    _currentSession.IsCancelled = true;
                    _currentSession.Cancellation.Cancel();
                }
            }

            _currentSession = null;
        }

        try
        {
            AiEventStorage.EnsureDirectoryForFile(SessionStatePath);
            if (File.Exists(SessionStatePath))
            {
                File.Delete(SessionStatePath);
            }
        }
        catch (Exception ex)
        {
            MainFile.Logger.Error($"[ai-event] failed to clear run session state while stopping run: {ex}");
        }

        if (finalizeDynamicEntries && endingSession != null)
        {
            try
            {
                int promoted = AiEventRepository.PromoteSeedDynamicEntriesToCache(endingSession.Seed);
                MainFile.Logger.Info($"[ai-event] finalized run seed {endingSession.Seed}; promoted {promoted} dynamic event(s) into llm cache.");
            }
            catch (Exception ex)
            {
                MainFile.Logger.Error($"[ai-event] failed to finalize dynamic events for seed {endingSession.Seed}: {ex}");
            }
        }

        MainFile.Logger.Info($"[ai-event] stopped active generation because {reason}.");
    }

    public static AiEventRunStats GetRunStats()
    {
        AiEventConfigService.Reload();

        lock (SyncRoot)
        {
            if (_currentSession != null)
            {
                return BuildStats(_currentSession);
            }
        }

        return LoadPersistedRunStats();
    }

    public static AiEventRunStatsSummary GetRunStatsSummary()
    {
        AiEventConfigService.Reload();

        bool currentIsMultiplayer = RunManager.Instance.NetService?.Type.IsMultiplayer() ?? false;
        AiEventRunStats? inMemoryStats = null;
        lock (SyncRoot)
        {
            if (_currentSession != null)
            {
                inMemoryStats = BuildStats(_currentSession);
            }
        }

        AiEventRunStats singleplayerStats = currentIsMultiplayer
            ? LoadPersistedRunStats(isMultiplayer: false)
            : inMemoryStats ?? LoadPersistedRunStats(isMultiplayer: false);
        AiEventRunStats multiplayerStats = currentIsMultiplayer
            ? inMemoryStats ?? LoadPersistedRunStats(isMultiplayer: true)
            : LoadPersistedRunStats(isMultiplayer: true);

        return new AiEventRunStatsSummary
        {
            Singleplayer = singleplayerStats,
            Multiplayer = multiplayerStats,
            CurrentContextIsMultiplayer = currentIsMultiplayer,
        };
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

    private static AiEventRunStats LoadPersistedRunStats()
    {
        bool isMultiplayer = RunManager.Instance.NetService?.Type.IsMultiplayer() ?? false;
        return LoadPersistedRunStats(isMultiplayer);
    }

    private static AiEventRunStats LoadPersistedRunStats(bool isMultiplayer)
    {
        try
        {
            string sessionStatePath = AiEventStorage.GetSessionStatePath(isMultiplayer);
            if (!File.Exists(sessionStatePath))
            {
                return AiEventRunStats.Empty;
            }

            string json = File.ReadAllText(sessionStatePath);
            AiEventRunSessionState? state = JsonSerializer.Deserialize<AiEventRunSessionState>(json, JsonOptions);
            if (state == null || string.IsNullOrWhiteSpace(state.Seed))
            {
                return AiEventRunStats.Empty;
            }

            int generatedCount = Math.Max(0, state.GeneratedCount);
            int plannedCount = state.GenerationPlan?.Count ?? 0;
            int experiencedCount = state.UsedEntryIds?.Distinct(StringComparer.OrdinalIgnoreCase).Count() ?? 0;

            return new AiEventRunStats
            {
                HasActiveRun = true,
                Seed = state.Seed,
                ExperiencedCount = experiencedCount,
                GeneratedCount = generatedCount,
                PendingCount = Math.Max(0, plannedCount - generatedCount),
                IsGenerating = state.IsGenerating,
                DiscardedCount = Math.Max(0, state.DiscardedCount),
            };
        }
        catch (Exception ex)
        {
            MainFile.Logger.Error($"[ai-event] failed to load run stats: {ex}");
            return AiEventRunStats.Empty;
        }
    }

    private static AiEventRunStats BuildStats(AiEventRunSession session)
    {
        lock (session.SyncRoot)
        {
            return new AiEventRunStats
            {
                HasActiveRun = true,
                Seed = session.Seed,
                ExperiencedCount = session.UsedDynamicEntryIds.Count,
                GeneratedCount = Math.Max(0, session.GeneratedCount),
                PendingCount = Math.Max(0, session.GenerationPlan.Count - session.GeneratedCount),
                IsGenerating = session.IsGenerating,
                DiscardedCount = Math.Max(0, session.DiscardedCount),
            };
        }
    }

    private static readonly AiEventSelectionDecision VanillaDecision = new()
    {
        UseVanilla = true,
        TitlePrefix = string.Empty,
        Payload = null,
    };

    private static AiEventSelectionDecision TryChooseCachedOrVanilla(RunState runState, IReadOnlyList<AiEventPoolEntry> cachedCandidates)
    {
        AiEventRuntimeConfig config = AiEventConfigService.GetEffectiveConfig();
        double vanillaWeight = Math.Max(0d, config.VanillaWeight);
        double cacheWeight = cachedCandidates.Count > 0 ? Math.Max(0d, config.CacheWeight) : 0d;

        if (cacheWeight <= 0d)
        {
            return VanillaDecision;
        }

        if (RollWeight(runState, cacheWeight, vanillaWeight))
        {
            Random random = CreateSelectionRandom(runState);
            AiEventPoolEntry chosen = cachedCandidates[random.Next(cachedCandidates.Count)];
            return CreateDecision(chosen, CacheTitlePrefix);
        }

        return VanillaDecision;
    }

    private static AiEventSelectionDecision TryChooseDynamicCacheOrVanilla(
        RunState runState,
        AiEventRunSession? session,
        IReadOnlyList<AiEventSlot> allowedSlots,
        IReadOnlyList<AiEventPoolEntry> cachedCandidates)
    {
        IReadOnlyList<AiEventPoolEntry> filteredCachedCandidates = FilterCacheCandidatesForDynamicMode(session, cachedCandidates);
        AiEventRuntimeConfig config = AiEventConfigService.GetEffectiveConfig();
        double dynamicWeight = session != null && HasDynamicEntry(session, allowedSlots)
            ? Math.Max(0d, config.DynamicWeight)
            : 0d;
        double cacheWeight = filteredCachedCandidates.Count > 0
            ? Math.Max(0d, config.CacheWeight)
            : 0d;
        double vanillaWeight = Math.Max(0d, config.VanillaWeight);

        string source = RollSource(runState, dynamicWeight, cacheWeight, vanillaWeight);
        switch (source)
        {
            case "dynamic":
                if (session != null && TryTakeDynamicEntry(session, allowedSlots, out AiEventPoolEntry? dynamicEntry))
                {
                    return CreateDecision(dynamicEntry!, DynamicTitlePrefix);
                }

                MainFile.Logger.Warn($"[ai-event] dynamic event fallback to vanilla in act {runState.Act.Id.Entry} because no generated event was ready.");
                return VanillaDecision;

            case "cache":
                if (filteredCachedCandidates.Count > 0)
                {
                    Random random = CreateSelectionRandom(runState);
                    AiEventPoolEntry cachedEntry = filteredCachedCandidates[random.Next(filteredCachedCandidates.Count)];
                    return CreateDecision(cachedEntry, CacheTitlePrefix);
                }

                return VanillaDecision;

            default:
                return VanillaDecision;
        }
    }

    private static void BroadcastIfHost(AiEventSelectionDecision decision)
    {
        if (AiEventMultiplayerSync.IsHostControlled())
        {
            AiEventMultiplayerSync.BroadcastSelection(decision);
        }
    }

    private static AiEventSelectionDecision CreateDecision(AiEventPoolEntry entry, string titlePrefix)
    {
        return new AiEventSelectionDecision
        {
            UseVanilla = false,
            TitlePrefix = titlePrefix,
            Payload = ClonePayloadForRuntime(entry.Payload, titlePrefix),
        };
    }

    private static EventModel ApplySelectionDecision(AiEventSelectionDecision decision, EventModel vanillaEvent)
    {
        if (decision.UseVanilla || decision.Payload == null)
        {
            return vanillaEvent;
        }

        return ActivatePayload(decision.Payload);
    }

    private static AiEventSelectionDecision? GetAssignedDecision(RunState runState)
    {
        AiEventRunSession? session = GetCurrentSession(runState.Rng.StringSeed);
        if (session == null)
        {
            return null;
        }

        string locationKey = GetLocationKey(runState.CurrentLocation);
        lock (session.SyncRoot)
        {
            if (!session.AssignedSelections.TryGetValue(locationKey, out AiEventSelectionDecisionState? state))
            {
                return null;
            }

            return state.ToDecision();
        }
    }

    private static void RememberAssignedDecision(RunState runState, AiEventSelectionDecision decision)
    {
        AiEventRunSession? session = GetCurrentSession(runState.Rng.StringSeed);
        if (session == null)
        {
            return;
        }

        string locationKey = GetLocationKey(runState.CurrentLocation);
        lock (session.SyncRoot)
        {
            session.AssignedSelections[locationKey] = AiEventSelectionDecisionState.FromDecision(decision);
        }

        SaveSessionState(session);
    }

    private static string GetLocationKey(RunLocation location)
    {
        return location.coord.HasValue
            ? $"{location.actIndex}:{location.coord.Value.col},{location.coord.Value.row}"
            : $"{location.actIndex}:null";
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

    private static EventModel ActivatePayload(AiGeneratedEventPayload payload)
    {
        payload.EventKey = AiEventRegistry.GetEventKey(payload.Slot);
        AiEventRepository.SetActive(payload);
        AiEventLocalization.ApplyCurrentLanguage();
        return AiEventRegistry.GetModelForSlot(payload.Slot);
    }

    private static AiGeneratedEventPayload ClonePayloadForRuntime(AiGeneratedEventPayload payload, string titlePrefix)
    {
        AiGeneratedEventPayload clone = new()
        {
            EntryId = payload.EntryId,
            Slot = payload.Slot,
            EventKey = payload.EventKey,
            Options = payload.Options.Select(option => new AiEventOptionPayload
            {
                Key = option.Key,
                Effects = option.Effects.Select(effect => new AiEventEffectPayload
                {
                    Type = effect.Type,
                    Amount = effect.Amount,
                    Count = effect.Count,
                    CardId = effect.CardId,
                    RelicRarity = effect.RelicRarity,
                }).ToList(),
            }).ToList(),
            Eng = new AiLocalizedEventText
            {
                Title = PrefixTitle(payload.Eng.Title, titlePrefix),
                InitialDescription = payload.Eng.InitialDescription,
                Options = payload.Eng.Options.Select(option => new AiLocalizedOptionText
                {
                    Key = option.Key,
                    Title = option.Title,
                    Description = option.Description,
                    ResultDescription = option.ResultDescription,
                }).ToList(),
            },
            Zhs = new AiLocalizedEventText
            {
                Title = PrefixTitle(payload.Zhs.Title, titlePrefix),
                InitialDescription = payload.Zhs.InitialDescription,
                Options = payload.Zhs.Options.Select(option => new AiLocalizedOptionText
                {
                    Key = option.Key,
                    Title = option.Title,
                    Description = option.Description,
                    ResultDescription = option.ResultDescription,
                }).ToList(),
            },
        };

        return clone;
    }

    private static string PrefixTitle(string title, string prefix)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return prefix;
        }

        return title.StartsWith(prefix + " ", StringComparison.OrdinalIgnoreCase)
            ? title
            : $"{prefix} {title}";
    }

    private static IReadOnlyList<AiEventPoolEntry> FilterCacheCandidatesForDynamicMode(
        AiEventRunSession? session,
        IReadOnlyList<AiEventPoolEntry> cachedCandidates)
    {
        if (session == null)
        {
            return cachedCandidates;
        }

        return cachedCandidates
            .Where(entry => !(string.Equals(entry.Source, "llm_dynamic", StringComparison.OrdinalIgnoreCase)
                              && string.Equals(entry.Seed, session.Seed, StringComparison.Ordinal)))
            .ToList();
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
            SaveSessionState(session);
            return true;
        }
    }

    private static List<string> GetRecentGeneratedTitles(AiEventRunSession session)
    {
        lock (session.SyncRoot)
        {
            return session.DynamicReadyEntries
                .OrderByDescending(entry => entry.GeneratedAtUtc)
                .Select(entry => entry.Payload.Zhs?.Title)
                .Concat(session.DynamicReadyEntries
                    .OrderByDescending(entry => entry.GeneratedAtUtc)
                    .Select(entry => entry.Payload.Eng?.Title))
                .Where(static title => !string.IsNullOrWhiteSpace(title))
                .Take(16)
                .Cast<string>()
                .ToList();
        }
    }

    private static async Task GenerateDynamicPoolAsync(AiEventRunSession session)
    {
        try
        {
            lock (session.SyncRoot)
            {
                if (session.IsCancelled)
                {
                    return;
                }

                session.IsGenerating = true;
            }
            SaveSessionState(session);

            if (session.GenerationThemes.Count != session.GenerationPlan.Count)
            {
                List<AiEventThemePlan> generatedThemes = await AiEventGenerationService.GenerateThemesAsync(
                    session.GenerationPlan,
                    session.Seed,
                    session.Cancellation.Token);

                lock (session.SyncRoot)
                {
                    if (!session.IsCancelled)
                    {
                        session.GenerationThemes.Clear();
                        session.GenerationThemes.AddRange(generatedThemes);
                    }
                }

                SaveSessionState(session);
            }

            string? validationError = await AiEventGenerationService.ValidateConnectivityAsync(session.Cancellation.Token);
            if (!string.IsNullOrWhiteSpace(validationError))
            {
                MainFile.Logger.Error($"[ai-event] dynamic generation disabled for seed {session.Seed}: {validationError}");
                return;
            }

            {
                List<int> remainingIndices;
                lock (session.SyncRoot)
                {
                    remainingIndices = Enumerable.Range(0, session.GenerationPlan.Count)
                        .Where(index => !session.CompletedGenerationIndices.Contains(index))
                        .ToList();
                }

                List<AiGeneratedEventPayload> generatedPayloads = new();
                SemaphoreSlim concurrencyGate = new(DynamicGenerationConcurrency);
                List<Task> workers = new();

                foreach (int index in remainingIndices)
                {
                    if (!IsSessionCurrent(session) || session.Cancellation.IsCancellationRequested)
                    {
                        return;
                    }

                    await concurrencyGate.WaitAsync(session.Cancellation.Token);
                    workers.Add(Task.Run(async () =>
                    {
                        try
                        {
                            await GenerateDynamicEntryAsync(session, index, generatedPayloads);
                        }
                        finally
                        {
                            concurrencyGate.Release();
                        }
                    }, session.Cancellation.Token));
                }

                await Task.WhenAll(workers);

                if (generatedPayloads.Count > 0)
                {
                    AiEventRepository.SaveHistorySnapshot(session.Seed, "llm_dynamic_batch", generatedPayloads);
                }

                return;
            }

            List<AiGeneratedEventPayload> legacyGeneratedPayloads = new();
            for (int index = session.GeneratedCount; index < session.GenerationPlan.Count; index++)
            {
                if (!IsSessionCurrent(session) || session.Cancellation.IsCancellationRequested)
                {
                    return;
                }

                AiEventSlot slot = session.GenerationPlan[index];
                string theme = session.GenerationThemes.ElementAtOrDefault(index)?.Theme ?? string.Empty;
                List<string> recentTitles = GetRecentGeneratedTitles(session);
                try
                {
                    AiEventThemePlan plan = session.GenerationThemes.ElementAtOrDefault(index) ?? new AiEventThemePlan
                    {
                        Slot = slot,
                        Theme = theme,
                    };
                    AiGeneratedEventPayload payload = await GenerateUniquePayloadAsync(session, slot, plan, recentTitles);
                    AiEventPoolEntry entry = AiEventRepository.CreatePoolEntry(payload, "llm_dynamic", session.Seed);
                    entry.Theme = $"{plan.Theme} | {plan.OptionCount}选项 | {plan.RewardProfile}";
                    AiEventRepository.AddPoolEntry(entry);

                    lock (session.SyncRoot)
                    {
                        session.DynamicReadyEntries.Add(entry);
                        session.GeneratedCount = Math.Max(session.GeneratedCount, index + 1);
                    }

                    SaveSessionState(session);
                    legacyGeneratedPayloads.Add(entry.Payload);
                    MainFile.Logger.Info($"[ai-event] generated dynamic event {entry.EntryId} for slot {slot} with theme `{theme}`.");
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (Exception ex)
                {
                    lock (session.SyncRoot)
                    {
                        session.DiscardedCount++;
                        session.GeneratedCount = Math.Max(session.GeneratedCount, index + 1);
                    }

                    SaveSessionState(session);
                    MainFile.Logger.Error($"[ai-event] failed to generate dynamic event for slot {slot}: {ex}");
                }
            }

            if (legacyGeneratedPayloads.Count > 0)
            {
                AiEventRepository.SaveHistorySnapshot(session.Seed, "llm_dynamic_batch", legacyGeneratedPayloads);
            }
        }
        catch (Exception ex)
        {
            MainFile.Logger.Error($"[ai-event] dynamic generation loop failed: {ex}");
        }
        finally
        {
            lock (session.SyncRoot)
            {
                session.IsGenerating = false;
            }

            SaveSessionState(session);
        }
    }

    private static async Task GenerateDynamicEntryAsync(
        AiEventRunSession session,
        int index,
        List<AiGeneratedEventPayload> generatedPayloads)
    {
        AiEventSlot slot = session.GenerationPlan[index];
        AiEventThemePlan plan = session.GenerationThemes.ElementAtOrDefault(index) ?? new AiEventThemePlan
        {
            Slot = slot,
            Theme = string.Empty,
        };
        string theme = plan.Theme;
        List<string> recentTitles = GetRecentGeneratedTitles(session);

        try
        {
            AiGeneratedEventPayload payload = await GenerateUniquePayloadAsync(session, slot, plan, recentTitles);
            AiEventPoolEntry entry = AiEventRepository.CreatePoolEntry(payload, "llm_dynamic", session.Seed);
            entry.Theme = $"{plan.Theme} | {plan.OptionCount}选项 | {plan.RewardProfile}";
            AiEventRepository.AddPoolEntry(entry);

            lock (session.SyncRoot)
            {
                session.DynamicReadyEntries.Add(entry);
                session.CompletedGenerationIndices.Add(index);
                session.GeneratedCount = session.CompletedGenerationIndices.Count;
            }

            lock (generatedPayloads)
            {
                generatedPayloads.Add(entry.Payload);
            }

            SaveSessionState(session);
            MainFile.Logger.Info($"[ai-event] generated dynamic event {entry.EntryId} for slot {slot} with theme `{theme}`.");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            lock (session.SyncRoot)
            {
                session.DiscardedCount++;
                session.CompletedGenerationIndices.Add(index);
                session.GeneratedCount = session.CompletedGenerationIndices.Count;
            }

            SaveSessionState(session);
            MainFile.Logger.Error($"[ai-event] failed to generate dynamic event for slot {slot}: {ex}");
        }
    }

    private static bool IsSessionCurrent(AiEventRunSession session)
    {
        lock (SyncRoot)
        {
            return ReferenceEquals(_currentSession, session);
        }
    }

    private static async Task<AiGeneratedEventPayload> GenerateUniquePayloadAsync(
        AiEventRunSession session,
        AiEventSlot slot,
        AiEventThemePlan plan,
        List<string> recentTitles)
    {
        List<string> disallowedTitles = new(recentTitles);
        disallowedTitles.AddRange(GetExistingSeedTitles(session));

        for (int attempt = 0; attempt < 2; attempt++)
        {
            AiGeneratedEventPayload payload = await AiEventGenerationService.GeneratePayloadAsync(
                slot,
                session.Seed,
                plan.Theme,
                plan.OptionCount,
                plan.RewardProfile,
                disallowedTitles,
                session.Cancellation.Token);

            if (!HasDuplicateTitle(session, payload))
            {
                return payload;
            }

            disallowedTitles.Add(payload.Zhs.Title);
            disallowedTitles.Add(payload.Eng.Title);
        }

        throw new InvalidOperationException($"Generated duplicate ai-event title for seed {session.Seed}: {plan.Theme}");
    }

    private static bool HasDuplicateTitle(AiEventRunSession session, AiGeneratedEventPayload payload)
    {
        HashSet<string> existingTitles = GetExistingSeedTitles(session)
            .Where(static title => !string.IsNullOrWhiteSpace(title))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return existingTitles.Contains(payload.Zhs.Title) || existingTitles.Contains(payload.Eng.Title);
    }

    private static IEnumerable<string> GetExistingSeedTitles(AiEventRunSession session)
    {
        foreach (AiEventPoolEntry entry in AiEventRepository.GetPoolEntriesForSeed(session.Seed, "llm_dynamic"))
        {
            if (!string.IsNullOrWhiteSpace(entry.Payload.Zhs?.Title))
            {
                yield return entry.Payload.Zhs.Title;
            }

            if (!string.IsNullOrWhiteSpace(entry.Payload.Eng?.Title))
            {
                yield return entry.Payload.Eng.Title;
            }
        }
    }

    private static bool ShouldGenerateLocally()
    {
        return RunManager.Instance.NetService?.Type != NetGameType.Client;
    }

    private static Random CreateSelectionRandom(RunState runState)
    {
        int seed = HashCode.Combine(runState.Rng.StringSeed, runState.CurrentActIndex, runState.TotalFloor, runState.MapPointHistory.Count);
        return new Random(seed);
    }

    private static AiEventRunSession LoadOrCreateSession(string seed, IReadOnlyList<ActModel> acts)
    {
        AiEventRunSession? restored = LoadSessionState(seed);
        if (restored != null)
        {
            return restored;
        }

        AiEventRunSession session = new(seed, acts.Select(AiEventRegistry.TryGetSlotForAct).Where(slot => slot.HasValue).Select(slot => slot!.Value).ToList());
        IReadOnlyList<AiEventPoolEntry> existingEntries = AiEventRepository.GetPoolEntriesForSeed(seed, "llm_dynamic");
        lock (session.SyncRoot)
        {
            session.DynamicReadyEntries.AddRange(existingEntries.OrderBy(entry => entry.GeneratedAtUtc));
            session.CompletedGenerationIndices.UnionWith(Enumerable.Range(0, session.DynamicReadyEntries.Count));
            session.GeneratedCount = session.CompletedGenerationIndices.Count;
            session.GenerationThemes.AddRange(existingEntries
                .OrderBy(entry => entry.GeneratedAtUtc)
                .Select(entry => new AiEventThemePlan
                {
                    Slot = entry.Payload.Slot,
                    Theme = entry.Theme,
                }));
        }

        return session;
    }

    private static AiEventRunSession? LoadSessionState(string seed)
    {
        try
        {
            if (!File.Exists(SessionStatePath))
            {
                return null;
            }

            string json = File.ReadAllText(SessionStatePath);
            AiEventRunSessionState? state = JsonSerializer.Deserialize<AiEventRunSessionState>(json, JsonOptions);
            if (state == null || !string.Equals(state.Seed, seed, StringComparison.Ordinal))
            {
                return null;
            }

            AiEventRunSession session = new(seed, state.ActSlots ?? new List<AiEventSlot>(), state.GenerationPlan ?? new List<AiEventSlot>());
            IReadOnlyDictionary<string, AiEventPoolEntry> entriesById = AiEventRepository
                .GetPoolEntriesByIds(state.ReadyEntryIds.Concat(state.UsedEntryIds))
                .ToDictionary(entry => entry.EntryId, StringComparer.OrdinalIgnoreCase);

            lock (session.SyncRoot)
            {
                if (state.GenerationThemes != null)
                {
                    session.GenerationThemes.AddRange(state.GenerationThemes);
                }

                foreach (string readyId in state.ReadyEntryIds)
                {
                    if (entriesById.TryGetValue(readyId, out AiEventPoolEntry? entry))
                    {
                        session.DynamicReadyEntries.Add(entry);
                    }
                }

                session.UsedDynamicEntryIds.UnionWith(state.UsedEntryIds);
                if (state.AssignedSelections != null)
                {
                    foreach ((string key, AiEventSelectionDecisionState value) in state.AssignedSelections)
                    {
                        session.AssignedSelections[key] = value;
                    }
                }

                if (state.CompletedGenerationIndices != null)
                {
                    session.CompletedGenerationIndices.UnionWith(state.CompletedGenerationIndices.Where(index => index >= 0));
                }

                if (session.CompletedGenerationIndices.Count == 0 && state.GeneratedCount > 0)
                {
                    session.CompletedGenerationIndices.UnionWith(Enumerable.Range(0, Math.Min(state.GeneratedCount, session.GenerationPlan.Count)));
                }

                session.GeneratedCount = session.CompletedGenerationIndices.Count;
                session.IsGenerating = state.IsGenerating && session.GeneratedCount < session.GenerationPlan.Count;
                session.DiscardedCount = Math.Max(0, state.DiscardedCount);
            }

            return session;
        }
        catch (Exception ex)
        {
            MainFile.Logger.Error($"[ai-event] failed to load run session state: {ex}");
            return null;
        }
    }

    private static void SaveSessionState(AiEventRunSession session)
    {
        try
        {
            AiEventRunSessionState state;
            lock (session.SyncRoot)
            {
                state = new AiEventRunSessionState
                {
                    Seed = session.Seed,
                    ActSlots = session.ActSlots.ToList(),
                    GenerationPlan = session.GenerationPlan.ToList(),
                    ReadyEntryIds = session.DynamicReadyEntries.Select(entry => entry.EntryId).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
                    UsedEntryIds = session.UsedDynamicEntryIds.ToList(),
                    GeneratedCount = session.GeneratedCount,
                    IsGenerating = session.IsGenerating,
                    DiscardedCount = session.DiscardedCount,
                    GenerationThemes = session.GenerationThemes.ToList(),
                    CompletedGenerationIndices = session.CompletedGenerationIndices.OrderBy(index => index).ToList(),
                    AssignedSelections = new Dictionary<string, AiEventSelectionDecisionState>(session.AssignedSelections, StringComparer.OrdinalIgnoreCase),
                };
            }

            AiEventStorage.EnsureDirectoryForFile(SessionStatePath);
            File.WriteAllText(SessionStatePath, JsonSerializer.Serialize(state, JsonOptions));
        }
        catch (Exception ex)
        {
            MainFile.Logger.Error($"[ai-event] failed to save run session state: {ex}");
        }
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

        public AiEventRunSession(string seed, List<AiEventSlot> actSlots, List<AiEventSlot> generationPlan)
        {
            Seed = seed;
            ActSlots = actSlots;
            GenerationPlan = generationPlan.Count > 0 ? generationPlan : BuildGenerationPlan(actSlots);
            Random = new Random(seed.GetHashCode());
        }

        public string Seed { get; }

        public List<AiEventSlot> ActSlots { get; }

        public List<AiEventSlot> GenerationPlan { get; }

        public List<AiEventPoolEntry> DynamicReadyEntries { get; } = new();

        public List<AiEventThemePlan> GenerationThemes { get; } = new();

        public HashSet<string> UsedDynamicEntryIds { get; } = new(StringComparer.OrdinalIgnoreCase);

        public Dictionary<string, AiEventSelectionDecisionState> AssignedSelections { get; } = new(StringComparer.OrdinalIgnoreCase);

        public HashSet<int> CompletedGenerationIndices { get; } = new();

        public object SyncRoot { get; } = new();

        public Random Random { get; }

        public int GeneratedCount { get; set; }

        public bool IsGenerating { get; set; }

        public int DiscardedCount { get; set; }

        public bool IsCancelled { get; set; }

        public CancellationTokenSource Cancellation { get; } = new();

        private static List<AiEventSlot> BuildGenerationPlan(IReadOnlyList<AiEventSlot> actSlots)
        {
            int target = Math.Max(1, AiEventConfigService.GetEffectiveConfig().DynamicEventsPerRun);
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

    private sealed class AiEventRunSessionState
    {
        public string Seed { get; set; } = string.Empty;

        public List<AiEventSlot> ActSlots { get; set; } = new();

        public List<AiEventSlot> GenerationPlan { get; set; } = new();

        public List<string> ReadyEntryIds { get; set; } = new();

        public List<string> UsedEntryIds { get; set; } = new();

        public int GeneratedCount { get; set; }

        public bool IsGenerating { get; set; }

        public int DiscardedCount { get; set; }

        public List<AiEventThemePlan> GenerationThemes { get; set; } = new();

        public List<int> CompletedGenerationIndices { get; set; } = new();

        public Dictionary<string, AiEventSelectionDecisionState> AssignedSelections { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }
}

public sealed class AiEventSelectionDecisionState
{
    public bool UseVanilla { get; set; }

    public string TitlePrefix { get; set; } = string.Empty;

    public AiGeneratedEventPayload? Payload { get; set; }

    public AiEventSelectionDecision ToDecision()
    {
        return new AiEventSelectionDecision
        {
            UseVanilla = UseVanilla,
            TitlePrefix = TitlePrefix,
            Payload = Payload,
        };
    }

    public static AiEventSelectionDecisionState FromDecision(AiEventSelectionDecision decision)
    {
        return new AiEventSelectionDecisionState
        {
            UseVanilla = decision.UseVanilla,
            TitlePrefix = decision.TitlePrefix,
            Payload = decision.Payload,
        };
    }
}

public sealed class AiEventRunStats
{
    public static AiEventRunStats Empty { get; } = new();

    public bool HasActiveRun { get; init; }

    public string Seed { get; init; } = string.Empty;

    public int ExperiencedCount { get; init; }

    public int GeneratedCount { get; init; }

    public int PendingCount { get; init; }

    public bool IsGenerating { get; init; }

    public int DiscardedCount { get; init; }
}

public sealed class AiEventRunStatsSummary
{
    public AiEventRunStats Singleplayer { get; init; } = AiEventRunStats.Empty;

    public AiEventRunStats Multiplayer { get; init; } = AiEventRunStats.Empty;

    public bool CurrentContextIsMultiplayer { get; init; }
}
