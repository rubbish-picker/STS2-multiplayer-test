using System;
using HarmonyLib;
using Godot;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;

namespace CocoRelics;

[HarmonyPatch]
public static class CocoRelicsPatches
{
    private static MapCoord? _currentEnteringCoord;

    [HarmonyPatch(typeof(NMapScreen), "_Ready")]
    [HarmonyPostfix]
    private static void EnsurePreviewOverlayExists(NMapScreen __instance)
    {
        if (__instance.GetNodeOrNull<CocoPreviewOverlay>(nameof(CocoPreviewOverlay)) == null)
        {
            __instance.AddChild(CocoPreviewOverlay.Create());
        }
    }

    [HarmonyPatch(typeof(NMapScreen), "Close")]
    [HarmonyPrefix]
    private static void ClosePreviewOverlay(NMapScreen __instance)
    {
        __instance.GetNodeOrNull<CocoPreviewOverlay>(nameof(CocoPreviewOverlay))?.ClosePreview();
    }

    [HarmonyPatch(typeof(NMapScreen), "ProcessMouseDrawingEvent")]
    [HarmonyPrefix]
    private static bool InterceptRightClickPreview(NMapScreen __instance, InputEvent inputEvent)
    {
        if (inputEvent is not InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Right })
        {
            return true;
        }

        RunState? runState = CocoRelicsState.GetRunState();
        if (!CocoRelicsState.HasPreviewRelic(runState))
        {
            return true;
        }

        Control? hovered = __instance.GetViewport().GuiGetHoveredControl();
        NMapPoint? mapPoint = hovered?.GetParentOrNull<NMapPoint>() ?? hovered?.GetNodeOrNull<NMapPoint>("..");
        if (mapPoint == null)
        {
            Node? current = hovered;
            while (current != null && mapPoint == null)
            {
                mapPoint = current as NMapPoint;
                current = current.GetParent();
            }
        }

        if (mapPoint == null || mapPoint.Point == null)
        {
            return true;
        }

        CocoPreviewOverlay? overlay = __instance.GetNodeOrNull<CocoPreviewOverlay>(nameof(CocoPreviewOverlay));
        if (overlay == null)
        {
            return true;
        }

        _ = overlay.OpenPreviewAsync(mapPoint.Point);
        return false;
    }

    [HarmonyPatch(typeof(RunManager), "EnterMapPointInternal")]
    [HarmonyPrefix]
    private static void TrackEnteringCoord(MapCoord? coord)
    {
        _currentEnteringCoord = coord;
    }

    [HarmonyPatch(typeof(RunManager), "EnterMapPointInternal")]
    [HarmonyPostfix]
    private static void ClearEnteringCoord()
    {
        _currentEnteringCoord = null;
    }

    [HarmonyPatch(typeof(RunManager), "RollRoomTypeFor")]
    [HarmonyPostfix]
    private static void UseObservedRoomType(MapPointType pointType, ref RoomType __result)
    {
        if (pointType != MapPointType.Unknown || !_currentEnteringCoord.HasValue)
        {
            return;
        }

        RunState? runState = CocoRelicsState.GetRunState();
        if (runState == null)
        {
            return;
        }

        if (CocoRelicsState.TryGet(_currentEnteringCoord.Value, runState.CurrentActIndex, out ObservedRoomInfo info))
        {
            __result = info.RoomType;
        }
    }

    [HarmonyPatch(typeof(RunManager), "CreateRoom")]
    [HarmonyPrefix]
    private static void UseObservedRoomModel(ref RoomType roomType, ref MapPointType mapPointType, ref AbstractModel? model)
    {
        if (!_currentEnteringCoord.HasValue)
        {
            return;
        }

        RunState? runState = CocoRelicsState.GetRunState();
        if (runState == null)
        {
            return;
        }

        if (!CocoRelicsState.TryGet(_currentEnteringCoord.Value, runState.CurrentActIndex, out ObservedRoomInfo info))
        {
            return;
        }

        roomType = info.RoomType;
        if (info.RoomType == RoomType.Event)
        {
            mapPointType = mapPointType == MapPointType.Ancient ? MapPointType.Ancient : MapPointType.Unknown;
        }

        if (info.ModelId == null)
        {
            model = null;
            return;
        }

        model = info.RoomType switch
        {
            RoomType.Monster or RoomType.Elite or RoomType.Boss => ModelDb.GetById<EncounterModel>(info.ModelId),
            RoomType.Event => ModelDb.GetById<EventModel>(info.ModelId),
            _ => model,
        };
    }

    [HarmonyPatch(typeof(RunManager), nameof(RunManager.GenerateMap))]
    [HarmonyPrefix]
    private static void ClearOnMapGeneration()
    {
        CocoRelicsState.Clear();
    }

    [HarmonyPatch(typeof(RunManager), "InitializeNewRun")]
    [HarmonyPostfix]
    private static void GrantDebugRelicOnNewRun(RunManager __instance)
    {
        if (!CocoRelicsConfigService.ShouldGrantDebugRelicAtRunStart())
        {
            return;
        }

        RunState? runState = CocoRelicsState.GetRunState();
        if (runState == null)
        {
            return;
        }

        foreach (Player player in runState.Players)
        {
            if (player.GetRelic<ZeduCoco>() != null)
            {
                continue;
            }

            RelicModel relic = ModelDb.Relic<ZeduCoco>().ToMutable();
            relic.FloorAddedToDeck = 1;
            player.AddRelicInternal(relic);
            MainFile.Logger.Info($"Granted debug relic {relic.Id} to player {player.NetId}.");
        }
    }
}
