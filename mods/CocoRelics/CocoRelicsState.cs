using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Odds;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;

namespace CocoRelics;

public readonly record struct ObservedRoomKey(int ActIndex, MapCoord Coord);

public sealed class ObservedRoomInfo
{
    public required RoomType RoomType { get; init; }

    public ModelId? ModelId { get; init; }
}

public static class CocoRelicsState
{
    private static readonly Dictionary<ObservedRoomKey, ObservedRoomInfo> ObservedRooms = new();
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

    public static ObservedRoomInfo GetOrObserve(MapPoint point, RunState runState)
    {
        ObservedRoomKey key = new(runState.CurrentActIndex, point.coord);
        if (ObservedRooms.TryGetValue(key, out ObservedRoomInfo? existing))
        {
            return existing;
        }

        ObservedRoomInfo observed = ObservePoint(point, runState);
        ObservedRooms[key] = observed;
        return observed;
    }

    private static ObservedRoomInfo ObservePoint(MapPoint point, RunState runState)
    {
        RoomType roomType = ResolveRoomType(point, runState);
        ModelId? modelId = roomType switch
        {
            RoomType.Monster or RoomType.Elite or RoomType.Boss => PickEncounterId(runState, point.coord, roomType),
            RoomType.Event => PickEventId(runState, point.coord, point.PointType == MapPointType.Ancient),
            _ => null,
        };

        return new ObservedRoomInfo
        {
            RoomType = roomType,
            ModelId = modelId,
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
            MapPointType.Unknown => PickUnknownRoomType(runState, point.coord),
            _ => RoomType.Map,
        };
    }

    private static RoomType PickUnknownRoomType(RunState runState, MapCoord coord)
    {
        UnknownMapPointOdds odds = runState.Odds.UnknownMapPoint;
        List<(RoomType Type, float Weight)> candidates = new()
        {
            (RoomType.Event, Math.Max(0.01f, odds.EventOdds)),
            (RoomType.Monster, Math.Max(0.01f, odds.MonsterOdds)),
            (RoomType.Treasure, Math.Max(0.01f, odds.TreasureOdds)),
            (RoomType.Shop, Math.Max(0.01f, odds.ShopOdds)),
        };

        if (odds.EliteOdds > 0f)
        {
            candidates.Add((RoomType.Elite, odds.EliteOdds));
        }

        float total = candidates.Sum(candidate => candidate.Weight);
        if (total <= 0f)
        {
            return RoomType.Event;
        }

        float pick = NextUnitFloat(runState, coord, salt: 17) * total;
        float cumulative = 0f;
        foreach ((RoomType type, float weight) in candidates)
        {
            cumulative += weight;
            if (pick <= cumulative)
            {
                return type;
            }
        }

        return candidates[^1].Type;
    }

    private static ModelId? PickEncounterId(RunState runState, MapCoord coord, RoomType roomType)
    {
        List<EncounterModel> candidates = runState.Act.AllEncounters
            .Where(encounter => encounter.RoomType == roomType)
            .DistinctBy(encounter => encounter.Id)
            .ToList();

        if (candidates.Count == 0)
        {
            return null;
        }

        int index = NextIndex(runState, coord, candidates.Count, salt: (int)roomType + 101);
        return candidates[index].Id;
    }

    private static ModelId? PickEventId(RunState runState, MapCoord coord, bool forceAncient)
    {
        IEnumerable<EventModel> pool = forceAncient
            ? runState.Act.AllAncients.Cast<EventModel>()
            : runState.Act.AllEvents.Concat(ModelDb.AllSharedEvents);

        List<EventModel> candidates = pool
            .Where(model => forceAncient || model.IsAllowed(runState))
            .DistinctBy(model => model.Id)
            .ToList();

        if (candidates.Count == 0)
        {
            return null;
        }

        int index = NextIndex(runState, coord, candidates.Count, salt: forceAncient ? 211 : 223);
        return candidates[index].Id;
    }

    private static int NextIndex(RunState runState, MapCoord coord, int count, int salt)
    {
        if (count <= 1)
        {
            return 0;
        }

        return (int)(NextUInt(runState, coord, salt) % (uint)count);
    }

    private static float NextUnitFloat(RunState runState, MapCoord coord, int salt)
    {
        return NextUInt(runState, coord, salt) / (float)uint.MaxValue;
    }

    private static uint NextUInt(RunState runState, MapCoord coord, int salt)
    {
        return unchecked((uint)HashCode.Combine(runState.Rng.StringSeed, runState.CurrentActIndex, coord.col, coord.row, salt));
    }
}
