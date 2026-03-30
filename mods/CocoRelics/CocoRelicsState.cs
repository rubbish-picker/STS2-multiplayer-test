using System;
using System.Collections.Generic;
using System.Linq;
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
}

public sealed class ObservedTreasurePreview
{
    public required int GoldAmount { get; init; }

    public required List<ModelId> RelicIds { get; init; }

    public required List<Reward> ExtraRewards { get; init; }
}

public static class CocoRelicsState
{
    private static readonly Dictionary<ObservedRoomKey, ObservedRoomInfo> ObservedRooms = new();
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
        ObservedRooms.Clear();
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
        return ObservedRooms.TryGetValue(new ObservedRoomKey(actIndex, coord), out info!);
    }

    public static async Task<ObservedRoomInfo> GetOrObserveAsync(MapPoint point, RunState runState)
    {
        ObservedRoomKey key = new(runState.CurrentActIndex, point.coord);
        if (ObservedRooms.TryGetValue(key, out ObservedRoomInfo? existing))
        {
            return existing;
        }

        ObservedRoomInfo observed = await ObservePointAsync(point, runState);
        ObservedRooms[key] = observed;
        return observed;
    }

    private static async Task<ObservedRoomInfo> ObservePointAsync(MapPoint point, RunState runState)
    {
        RunState observedRunState = CloneRunState(runState);
        ObservedRoomInfo? lastObserved = null;
        foreach (MapPoint simulatedPoint in GetPathFromCurrentPoint(observedRunState, point))
        {
            lastObserved = await ObserveSimulatedPointAsync(simulatedPoint, observedRunState, runState);
        }

        return lastObserved ?? new ObservedRoomInfo
        {
            RoomType = RoomType.Map,
        };
    }

    private static async Task<ObservedRoomInfo> ObserveSimulatedPointAsync(MapPoint point, RunState observedRunState, RunState sourceRunState)
    {
        observedRunState.AddVisitedMapCoord(point.coord);
        RoomType roomType = ResolveRoomType(point, observedRunState);
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

        MerchantInventory? shopInventory = roomType == RoomType.Shop ? ObserveShop(sourceRunState) : null;
        ObservedTreasurePreview? treasurePreview = roomType == RoomType.Treasure ? await ObserveTreasureAsync(sourceRunState) : null;

        return new ObservedRoomInfo
        {
            RoomType = roomType,
            ModelId = modelId,
            ShopInventory = shopInventory,
            TreasurePreview = treasurePreview,
        };
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
        return RunState.FromSerializable(save);
    }

    private static IReadOnlyList<MapPoint> GetPathFromCurrentPoint(RunState runState, MapPoint targetPoint)
    {
        MapPoint? currentPoint = runState.CurrentMapPoint ?? runState.Map.StartingMapPoint;
        if (currentPoint == null || currentPoint.coord.Equals(targetPoint.coord))
        {
            return currentPoint == null ? new[] { targetPoint } : Array.Empty<MapPoint>();
        }

        HashSet<MapPoint> currentDescendants = GetDescendantsIncludingSelf(currentPoint);
        List<MapPoint> reversedPath = new();
        MapPoint? cursor = runState.Map.GetPoint(targetPoint.coord);
        while (cursor != null && !cursor.coord.Equals(currentPoint.coord))
        {
            reversedPath.Add(cursor);
            cursor = cursor.parents
                .OrderByDescending(parent => parent.coord.row)
                .FirstOrDefault(parent => parent.coord.row >= currentPoint.coord.row && currentDescendants.Contains(parent));
        }

        if (cursor == null)
        {
            return new[] { targetPoint };
        }

        reversedPath.Reverse();
        return reversedPath;
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
}
