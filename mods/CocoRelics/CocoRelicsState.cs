using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Merchant;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Ascension;
using MegaCrit.Sts2.Core.Factories;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Odds;
using MegaCrit.Sts2.Core.Random;
using MegaCrit.Sts2.Core.Rewards;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;

namespace CocoRelics;

public readonly record struct ObservedRoomKey(int ActIndex, MapCoord Coord);

public sealed class ObservedRoomInfo
{
    public required RoomType RoomType { get; init; }

    public ModelId? ModelId { get; init; }

    public MerchantInventory? ShopInventory { get; init; }

    public ObservedTreasurePreview? TreasurePreview { get; init; }

    public CocoObservedRunSnapshot? SnapshotAfterPoint { get; init; }
}

public sealed class ObservedTreasurePreview
{
    public required int GoldAmount { get; init; }

    public required List<ModelId> RelicIds { get; init; }

    public required List<Reward> ExtraRewards { get; init; }
}

public static class CocoRelicsState
{
    private static readonly Dictionary<ObservedRoomKey, ObservedRoomInfo> FrozenObservedRooms = new();
    private static readonly SemaphoreSlim ObserveSemaphore = new(1, 1);
    private static readonly AccessTools.FieldRef<TreasureRoomRelicSynchronizer, RelicGrabBag>? SharedRelicGrabBagRef =
        AccessTools.FieldRefAccess<TreasureRoomRelicSynchronizer, RelicGrabBag>("_sharedGrabBag");
    private static readonly AccessTools.FieldRef<TreasureRoomRelicSynchronizer, Rng>? TreasureRelicRngRef =
        AccessTools.FieldRefAccess<TreasureRoomRelicSynchronizer, Rng>("_rng");
    private static readonly Func<RunManager, RunState?>? StateGetter =
        AccessTools.PropertyGetter(typeof(RunManager), "State") is { } getter
            ? (Func<RunManager, RunState?>)Delegate.CreateDelegate(typeof(Func<RunManager, RunState?>), getter)
            : null;

    public static RunState? GetRunState()
    {
        if (RunManager.Instance == null || StateGetter == null)
        {
            return null;
        }

        return StateGetter(RunManager.Instance);
    }

    public static void Clear()
    {
        FrozenObservedRooms.Clear();
    }

    public static IReadOnlyDictionary<ObservedRoomKey, ObservedRoomInfo> GetObservedRooms()
    {
        return FrozenObservedRooms;
    }

    public static void LoadPersistedSession(RunState runState)
    {
        FrozenObservedRooms.Clear();
        CocoObservedSessionSave? save = CocoRelicsStorage.LoadCurrentSession(runState);
        if (save == null)
        {
            return;
        }

        foreach (CocoObservedRoomInfoSave roomSave in save.ObservedRooms.Where(room => room.ActIndex == runState.CurrentActIndex))
        {
            if (CocoRelicsStorage.TryFromSave(roomSave, runState, out ObservedRoomInfo? restored) && restored != null)
            {
                FreezeObserved(new ObservedRoomKey(roomSave.ActIndex, roomSave.Coord), restored);
            }
        }
    }

    public static bool HasPreviewRelic(IRunState? runState)
    {
        if (runState == null)
        {
            return false;
        }

        return LocalContext.GetMe(runState)?.Relics.Any(relic => relic is ZeduCoco) ?? false;
    }

    public static bool TryGet(MapCoord coord, int actIndex, out ObservedRoomInfo info)
    {
        return FrozenObservedRooms.TryGetValue(new ObservedRoomKey(actIndex, coord), out info!);
    }

    public static async Task<ObservedRoomInfo> GetOrObserveAsync(MapPoint point, RunState runState)
    {
        ObservedRoomKey key = new(runState.CurrentActIndex, point.coord);
        if (FrozenObservedRooms.TryGetValue(key, out ObservedRoomInfo? existing))
        {
            return existing;
        }

        await ObserveSemaphore.WaitAsync();
        try
        {
            if (FrozenObservedRooms.TryGetValue(key, out existing))
            {
                return existing;
            }

            ObservedRoomInfo observed = await ObservePointAsync(point, runState);
            return FreezeObserved(key, observed, runState);
        }
        finally
        {
            ObserveSemaphore.Release();
        }
    }

    public static void SetObserved(MapCoord coord, int actIndex, ObservedRoomInfo info, RunState runState)
    {
        FreezeObserved(new ObservedRoomKey(actIndex, coord), info, runState);
    }

    private static async Task<ObservedRoomInfo> ObservePointAsync(MapPoint point, RunState runState)
    {
        RunState observedRunState = CloneRunState(runState);
        ObservedRoomInfo? lastObserved = null;
        IReadOnlyList<MapPoint> selectedPath = SelectPathFromCurrentPoint(observedRunState, point);
        int globalObservedEventCount = FrozenObservedRooms.Values.Count(static info => info.RoomType == RoomType.Event);
        int encounteredObservedEventsOnPath = 0;
        int reservedOffPathEventsInState = 0;
        foreach (MapPoint simulatedPoint in selectedPath)
        {
            ObservedRoomKey key = new(runState.CurrentActIndex, simulatedPoint.coord);
            if (FrozenObservedRooms.TryGetValue(key, out ObservedRoomInfo? existing))
            {
                if (existing.SnapshotAfterPoint != null)
                {
                    lastObserved = existing;
                    observedRunState = CocoRelicsStorage.RestoreSnapshot(existing.SnapshotAfterPoint, runState);
                    reservedOffPathEventsInState = 0;
                    if (existing.RoomType == RoomType.Event)
                    {
                        encounteredObservedEventsOnPath++;
                    }
                    continue;
                }

                lastObserved = existing;
                observedRunState = ApplyObservedPointToSimulation(simulatedPoint, existing, observedRunState);
                if (existing.RoomType == RoomType.Event)
                {
                    encounteredObservedEventsOnPath++;
                }
                continue;
            }

            int extraObservedEventReservations = Math.Max(0, globalObservedEventCount - encounteredObservedEventsOnPath);
            lastObserved = await ObserveSimulatedPointAsync(simulatedPoint, observedRunState, extraObservedEventReservations - reservedOffPathEventsInState);
            FreezeObserved(key, lastObserved);
            if (lastObserved.RoomType == RoomType.Event)
            {
                reservedOffPathEventsInState = extraObservedEventReservations;
            }
        }

        return lastObserved ?? new ObservedRoomInfo
        {
            RoomType = RoomType.Map,
        };
    }

    private static async Task<ObservedRoomInfo> ObserveSimulatedPointAsync(MapPoint point, RunState observedRunState, int extraObservedEventReservations)
    {
        observedRunState.AddVisitedMapCoord(point.coord);
        RoomType roomType = ResolveRoomType(point, observedRunState);
        if (roomType == RoomType.Event && point.PointType != MapPointType.Ancient)
        {
            ReserveObservedEventSlots(observedRunState, extraObservedEventReservations);
        }

        ModelId? modelId = roomType switch
        {
            RoomType.Monster or RoomType.Elite or RoomType.Boss => observedRunState.Act.PullNextEncounter(roomType).Id,
            RoomType.Event => point.PointType == MapPointType.Ancient
                ? observedRunState.Act.PullAncient().Id
                : observedRunState.Act.PullNextEvent(observedRunState).Id,
            _ => null,
        };

        observedRunState.AppendToMapPointHistory(point.PointType, roomType, modelId);
        observedRunState.Act.MarkRoomVisited(roomType);

        MerchantInventory? shopInventory = roomType == RoomType.Shop ? ObserveShop(observedRunState) : null;
        ObservedTreasurePreview? treasurePreview = roomType == RoomType.Treasure ? await ObserveTreasureAsync(observedRunState) : null;

        return new ObservedRoomInfo
        {
            RoomType = roomType,
            ModelId = modelId,
            ShopInventory = shopInventory,
            TreasurePreview = treasurePreview,
            SnapshotAfterPoint = CocoRelicsStorage.CaptureSnapshot(observedRunState),
        };
    }

    private static void ReserveObservedEventSlots(RunState observedRunState, int count)
    {
        for (int index = 0; index < count; index++)
        {
            observedRunState.Act.PullNextEvent(observedRunState);
            observedRunState.Act.MarkRoomVisited(RoomType.Event);
        }
    }

    private static RunState ApplyObservedPointToSimulation(MapPoint point, ObservedRoomInfo info, RunState observedRunState)
    {
        observedRunState.AddVisitedMapCoord(point.coord);

        switch (info.RoomType)
        {
            case RoomType.Monster:
            case RoomType.Elite:
            case RoomType.Boss:
                observedRunState.Act.PullNextEncounter(info.RoomType);
                break;
            case RoomType.Event:
                if (point.PointType == MapPointType.Ancient)
                {
                    observedRunState.Act.PullAncient();
                }
                else
                {
                    observedRunState.Act.PullNextEvent(observedRunState);
                }
                break;
        }

        observedRunState.AppendToMapPointHistory(point.PointType, info.RoomType, info.ModelId);
        observedRunState.Act.MarkRoomVisited(info.RoomType);
        return observedRunState;
    }

    private static ObservedRoomInfo FreezeObserved(ObservedRoomKey key, ObservedRoomInfo info, RunState? runState = null)
    {
        if (!FrozenObservedRooms.TryGetValue(key, out ObservedRoomInfo? existing))
        {
            FrozenObservedRooms[key] = info;
            existing = info;
            MainFile.Logger.Info($"Frozen observed room at {key.Coord} act={key.ActIndex}: type={info.RoomType} model={info.ModelId?.Entry ?? "none"}.");
            if (runState != null)
            {
                CocoRelicsStorage.SaveCurrentSession(runState, FrozenObservedRooms);
            }
        }
        else if (runState != null)
        {
            CocoRelicsStorage.SaveCurrentSession(runState, FrozenObservedRooms);
        }

        return existing;
    }

    private static MerchantInventory ObserveShop(RunState runState)
    {
        Player player = LocalContext.GetMe(runState) ?? runState.Players.First();
        return MerchantInventory.CreateForNormalMerchant(player);
    }

    private static async Task<ObservedTreasurePreview> ObserveTreasureAsync(RunState runState)
    {
        Player player = LocalContext.GetMe(runState) ?? runState.Players.First();
        TreasureRoom previewRoom = new(runState.CurrentActIndex);
        List<Reward> extraRewards = await new RewardsSet(player).WithRewardsFromRoom(previewRoom).GenerateWithoutOffering();
        return new ObservedTreasurePreview
        {
            GoldAmount = RollTreasureGoldAmount(player),
            RelicIds = RollTreasureRelicIds(runState),
            ExtraRewards = extraRewards,
        };
    }

    private static RoomType ResolveRoomType(MapPoint point, RunState runState)
    {
        return point.PointType switch
        {
            MapPointType.Monster => RoomType.Monster,
            MapPointType.Elite => RoomType.Elite,
            MapPointType.Boss => RoomType.Boss,
            MapPointType.Treasure => RoomType.Treasure,
            MapPointType.Shop => RoomType.Shop,
            MapPointType.RestSite => RoomType.RestSite,
            MapPointType.Ancient => RoomType.Event,
            MapPointType.Unknown => PickUnknownRoomType(runState),
            _ => RoomType.Map,
        };
    }

    private static RoomType PickUnknownRoomType(RunState runState)
    {
        HashSet<RoomType> blacklist = RunManager.BuildRoomTypeBlacklist(runState.CurrentMapPointHistoryEntry, runState.CurrentMapPoint?.Children ?? new HashSet<MapPoint>());
        return runState.Odds.UnknownMapPoint.Roll(blacklist, runState);
    }

    private static int RollTreasureGoldAmount(Player player)
    {
        Rng rewardsRng = new(player.PlayerRng.Rewards.Seed, player.PlayerRng.Rewards.Counter);
        double goldAmount = rewardsRng.NextInt(42, 53);
        if (AscensionHelper.HasAscension(AscensionLevel.Poverty))
        {
            goldAmount *= AscensionHelper.PovertyAscensionGoldMultiplier;
        }

        return (int)goldAmount;
    }

    private static List<ModelId> RollTreasureRelicIds(RunState runState)
    {
        TreasureRoomRelicSynchronizer synchronizer = RunManager.Instance.TreasureRoomRelicSynchronizer;
        if (SharedRelicGrabBagRef == null || TreasureRelicRngRef == null)
        {
            return new List<ModelId>();
        }

        RelicGrabBag bagClone = RelicGrabBag.FromSerializable(SharedRelicGrabBagRef(synchronizer).ToSerializable());
        Rng rngClone = new(TreasureRelicRngRef(synchronizer).Seed, TreasureRelicRngRef(synchronizer).Counter);
        List<ModelId> relicIds = new();

        foreach (Player player in runState.Players)
        {
            if (!Hook.ShouldGenerateTreasure(runState, player))
            {
                continue;
            }

            RelicModel relic = TryGetTreasureTutorialRelic(runState, player, bagClone)
                ?? bagClone.PullFromFront(RelicFactory.RollRarity(rngClone), runState)
                ?? RelicFactory.FallbackRelic;
            relicIds.Add(relic.Id);
        }

        return relicIds;
    }

    private static RelicModel? TryGetTreasureTutorialRelic(RunState runState, Player player, RelicGrabBag bagClone)
    {
        int priorTreasureCount = runState.MapPointHistory
            .SelectMany(history => history)
            .Count(entry => entry.HasRoomOfType(RoomType.Treasure));

        if (player.UnlockState.NumberOfRuns == 0 && priorTreasureCount == 0)
        {
            bagClone.Remove<Gorget>();
            return ModelDb.Relic<Gorget>();
        }

        return null;
    }

    private static RunState CloneRunState(RunState runState)
    {
        SerializableRun save = RunManager.Instance.ToSave(null);
        RunState cloned = RunState.FromSerializable(save);
        cloned.Map = runState.Map;
        return cloned;
    }

    private static IReadOnlyList<MapPoint> SelectPathFromCurrentPoint(RunState runState, MapPoint targetPoint)
    {
        MapPoint? currentPoint = runState.CurrentMapPoint ?? runState.Map.StartingMapPoint;
        if (currentPoint == null || currentPoint.coord.Equals(targetPoint.coord))
        {
            return currentPoint == null ? new[] { targetPoint } : Array.Empty<MapPoint>();
        }

        HashSet<MapPoint> currentDescendants = GetDescendantsIncludingSelf(currentPoint);
        List<List<MapPoint>> candidatePaths = new();
        CollectPathsToTarget(runState.Map.GetPoint(targetPoint.coord), currentPoint, currentDescendants, new List<MapPoint>(), candidatePaths);
        if (candidatePaths.Count == 0)
        {
            return new[] { targetPoint };
        }

        CocoRelicsPreviewPathMode mode = CocoRelicsConfigService.GetPreviewPathMode();
        ScoredPath? selected = null;
        foreach (List<MapPoint> candidatePath in candidatePaths)
        {
            ScoredPath scored = ScorePath(runState, candidatePath);
            if (selected == null || IsBetterPath(scored, selected, mode))
            {
                selected = scored;
            }
        }

        return selected?.Path ?? candidatePaths[0];
    }

    private static HashSet<MapPoint> GetDescendantsIncludingSelf(MapPoint start)
    {
        HashSet<MapPoint> descendants = new();
        Queue<MapPoint> queue = new();
        queue.Enqueue(start);

        while (queue.Count > 0)
        {
            MapPoint point = queue.Dequeue();
            if (!descendants.Add(point))
            {
                continue;
            }

            foreach (MapPoint child in point.Children)
            {
                queue.Enqueue(child);
            }
        }

        return descendants;
    }

    private static void CollectPathsToTarget(
        MapPoint? cursor,
        MapPoint currentPoint,
        HashSet<MapPoint> currentDescendants,
        List<MapPoint> reversedPath,
        List<List<MapPoint>> results)
    {
        if (cursor == null)
        {
            return;
        }

        if (cursor.coord.Equals(currentPoint.coord))
        {
            List<MapPoint> path = reversedPath.ToList();
            path.Reverse();
            results.Add(path);
            return;
        }

        reversedPath.Add(cursor);
        foreach (MapPoint parent in cursor.parents
                     .Where(parent => parent.coord.row >= currentPoint.coord.row && currentDescendants.Contains(parent))
                     .OrderBy(parent => parent.coord.col)
                     .ThenBy(parent => parent.coord.row))
        {
            CollectPathsToTarget(parent, currentPoint, currentDescendants, reversedPath, results);
        }

        reversedPath.RemoveAt(reversedPath.Count - 1);
    }

    private static ScoredPath ScorePath(RunState runState, IReadOnlyList<MapPoint> path)
    {
        RunState simulated = CloneRunState(runState);
        int samePoolCountBeforeTarget = 0;
        int totalCountBeforeTarget = 0;
        RoomType targetRoomType = RoomType.Map;

        for (int i = 0; i < path.Count; i++)
        {
            MapPoint point = path[i];
            simulated.AddVisitedMapCoord(point.coord);
            RoomType roomType = ResolveRoomType(point, simulated);
            ModelId? modelId = roomType switch
            {
                RoomType.Monster or RoomType.Elite or RoomType.Boss => simulated.Act.PullNextEncounter(roomType).Id,
                RoomType.Event => point.PointType == MapPointType.Ancient
                    ? simulated.Act.PullAncient().Id
                    : simulated.Act.PullNextEvent(simulated).Id,
                _ => null,
            };

            if (i == path.Count - 1)
            {
                targetRoomType = roomType;
                samePoolCountBeforeTarget = CountPoolProgress(simulated, roomType);
            }

            simulated.AppendToMapPointHistory(point.PointType, roomType, modelId);
            simulated.Act.MarkRoomVisited(roomType);

            if (i < path.Count - 1)
            {
                totalCountBeforeTarget++;
            }
        }

        return new ScoredPath(path.ToList(), targetRoomType, samePoolCountBeforeTarget, totalCountBeforeTarget);
    }

    private static int CountPoolProgress(RunState runState, RoomType roomType)
    {
        return roomType switch
        {
            RoomType.Event => runState.MapPointHistory.SelectMany(static history => history).Count(static entry => entry.HasRoomOfType(RoomType.Event)),
            RoomType.Monster => runState.MapPointHistory.SelectMany(static history => history).Count(static entry => entry.HasRoomOfType(RoomType.Monster)),
            RoomType.Elite => runState.MapPointHistory.SelectMany(static history => history).Count(static entry => entry.HasRoomOfType(RoomType.Elite)),
            RoomType.Boss => runState.MapPointHistory.SelectMany(static history => history).Count(static entry => entry.HasRoomOfType(RoomType.Boss)),
            _ => runState.MapPointHistory.SelectMany(static history => history).Count(),
        };
    }

    private static bool IsBetterPath(ScoredPath candidate, ScoredPath current, CocoRelicsPreviewPathMode mode)
    {
        int primary = candidate.SamePoolCountBeforeTarget.CompareTo(current.SamePoolCountBeforeTarget);
        if (primary != 0)
        {
            return mode == CocoRelicsPreviewPathMode.Nearest ? primary < 0 : primary > 0;
        }

        int secondary = candidate.TotalCountBeforeTarget.CompareTo(current.TotalCountBeforeTarget);
        if (secondary != 0)
        {
            return mode == CocoRelicsPreviewPathMode.Nearest ? secondary < 0 : secondary > 0;
        }

        string candidateKey = string.Join(";", candidate.Path.Select(static point => $"{point.coord.row},{point.coord.col}"));
        string currentKey = string.Join(";", current.Path.Select(static point => $"{point.coord.row},{point.coord.col}"));
        return string.CompareOrdinal(candidateKey, currentKey) < 0;
    }

    private sealed record ScoredPath(
        List<MapPoint> Path,
        RoomType TargetRoomType,
        int SamePoolCountBeforeTarget,
        int TotalCountBeforeTarget);
}
