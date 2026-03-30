using System;
using System.Linq;
using HarmonyLib;
using Godot;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Entities.Merchant;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.Nodes.Events;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using MegaCrit.Sts2.Core.Random;
using MegaCrit.Sts2.Core.Rewards;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Multiplayer.Game;

namespace CocoRelics;

[HarmonyPatch]
public static class CocoRelicsPatches
{
    private static MapCoord? _currentEnteringCoord;
    private static bool _watcherRestSitePreviewCompatibilityActive;
    private static readonly AccessTools.FieldRef<TreasureRoomRelicSynchronizer, System.Collections.Generic.List<RelicModel>?>? CurrentRelicsRef =
        AccessTools.FieldRefAccess<TreasureRoomRelicSynchronizer, System.Collections.Generic.List<RelicModel>?>("_currentRelics");
    private static readonly AccessTools.FieldRef<TreasureRoomRelicSynchronizer, System.Collections.Generic.List<int?>>? VotesRef =
        AccessTools.FieldRefAccess<TreasureRoomRelicSynchronizer, System.Collections.Generic.List<int?>>("_votes");
    private static readonly AccessTools.FieldRef<TreasureRoomRelicSynchronizer, int?>? PredictedVoteRef =
        AccessTools.FieldRefAccess<TreasureRoomRelicSynchronizer, int?>("_predictedVote");
    private static readonly AccessTools.FieldRef<TreasureRoomRelicSynchronizer, Rng>? TreasureRelicRngRef =
        AccessTools.FieldRefAccess<TreasureRoomRelicSynchronizer, Rng>("_rng");
    private static readonly AccessTools.FieldRef<TreasureRoomRelicSynchronizer, IPlayerCollection>? PlayerCollectionRef =
        AccessTools.FieldRefAccess<TreasureRoomRelicSynchronizer, IPlayerCollection>("_playerCollection");

    public static void SetWatcherRestSitePreviewCompatibility(bool active)
    {
        _watcherRestSitePreviewCompatibilityActive = active;
    }

    public static bool SkipWatcherRestSiteCharacterPatchDuringPreview()
    {
        return !_watcherRestSitePreviewCompatibilityActive;
    }

    [HarmonyPatch(typeof(NMapScreen), "_Ready")]
    [HarmonyPostfix]
    private static void EnsurePreviewOverlayExists(NMapScreen __instance)
    {
        if (__instance.GetNodeOrNull<CocoPreviewOverlay>(nameof(CocoPreviewOverlay)) == null)
        {
            __instance.AddChild(CocoPreviewOverlay.Create(__instance.GetNodeOrNull<NBackButton>("Back")));
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

    [HarmonyPatch(typeof(MerchantInventory), nameof(MerchantInventory.CreateForNormalMerchant))]
    [HarmonyPrefix]
    private static bool UseObservedShopInventory(Player player, ref MerchantInventory __result)
    {
        RunState? runState = CocoRelicsState.GetRunState();
        if (runState == null || !_currentEnteringCoord.HasValue)
        {
            return true;
        }

        if (!CocoRelicsState.TryGet(_currentEnteringCoord.Value, runState.CurrentActIndex, out ObservedRoomInfo info) || info.ShopInventory == null)
        {
            return true;
        }

        __result = info.ShopInventory;
        return false;
    }

    [HarmonyPatch(typeof(OneOffSynchronizer), "DoTreasureRoomRewards")]
    [HarmonyPrefix]
    private static bool UseObservedTreasureGold(Player player, ref System.Threading.Tasks.Task<int> __result)
    {
        RunState? runState = CocoRelicsState.GetRunState();
        if (runState == null || runState.CurrentRoom?.RoomType != RoomType.Treasure || !runState.CurrentMapCoord.HasValue)
        {
            return true;
        }

        if (!CocoRelicsState.TryGet(runState.CurrentMapCoord.Value, runState.CurrentActIndex, out ObservedRoomInfo info) || info.TreasurePreview == null)
        {
            return true;
        }

        __result = ApplyObservedTreasureGoldAsync(player, info.TreasurePreview.GoldAmount);
        return false;
    }

    [HarmonyPatch(typeof(TreasureRoomRelicSynchronizer), nameof(TreasureRoomRelicSynchronizer.BeginRelicPicking))]
    [HarmonyPrefix]
    private static bool UseObservedTreasureRelics(TreasureRoomRelicSynchronizer __instance)
    {
        RunState? runState = CocoRelicsState.GetRunState();
        if (runState == null || !runState.CurrentMapCoord.HasValue || CurrentRelicsRef == null || VotesRef == null || PredictedVoteRef == null || PlayerCollectionRef == null || TreasureRelicRngRef == null)
        {
            return true;
        }

        if (!CocoRelicsState.TryGet(runState.CurrentMapCoord.Value, runState.CurrentActIndex, out ObservedRoomInfo info) || info.TreasurePreview == null)
        {
            return true;
        }

        var currentRelics = CurrentRelicsRef(__instance);
        if (currentRelics != null)
        {
            return true;
        }

        var votes = VotesRef(__instance);
        currentRelics = info.TreasurePreview.RelicIds.Select(ModelDb.GetById<RelicModel>).ToList();
        votes.Clear();
        PredictedVoteRef(__instance) = null;
        foreach (Player _ in PlayerCollectionRef(__instance).Players)
        {
            votes.Add(null);
        }

        CurrentRelicsRef(__instance) = currentRelics;
        if (currentRelics.Count == 0)
        {
            __instance.CompleteWithNoRelics();
            return false;
        }

        if (RunManager.Instance.IsSinglePlayerOrFakeMultiplayer && PlayerCollectionRef(__instance).Players.Count > 1)
        {
            Player? localPlayer = LocalContext.GetMe(PlayerCollectionRef(__instance));
            foreach (Player player in PlayerCollectionRef(__instance).Players)
            {
                if (!ReferenceEquals(player, localPlayer))
                {
                    votes[PlayerCollectionRef(__instance).GetPlayerSlotIndex(player)] = TreasureRelicRngRef(__instance).NextInt(currentRelics.Count);
                }
            }
        }

        return false;
    }

    [HarmonyPatch(typeof(MegaCrit.Sts2.Core.Commands.RewardsCmd), nameof(MegaCrit.Sts2.Core.Commands.RewardsCmd.OfferForRoomEnd))]
    [HarmonyPrefix]
    private static bool UseObservedTreasureExtraRewards(Player player, AbstractRoom room, ref System.Threading.Tasks.Task __result)
    {
        RunState? runState = CocoRelicsState.GetRunState();
        if (runState == null || room.RoomType != RoomType.Treasure || !runState.CurrentMapCoord.HasValue)
        {
            return true;
        }

        if (!CocoRelicsState.TryGet(runState.CurrentMapCoord.Value, runState.CurrentActIndex, out ObservedRoomInfo info) || info.TreasurePreview == null)
        {
            return true;
        }

        __result = MegaCrit.Sts2.Core.Commands.RewardsCmd.OfferCustom(player, info.TreasurePreview.ExtraRewards);
        return false;
    }

    [HarmonyPatch(typeof(RunManager), nameof(RunManager.GenerateMap))]
    [HarmonyPrefix]
    private static void ClearOnMapGeneration()
    {
        CocoRelicsState.Clear();
    }

    [HarmonyPatch(typeof(NCombatRoom), "UpdateCreatureNavigation")]
    [HarmonyPrefix]
    private static bool FixDetachedPreviewCombatNavigation(NCombatRoom __instance)
    {
        if (NCombatRoom.Instance == __instance)
        {
            return true;
        }

        var navigable = __instance.CreatureNodes
            .Where(node => node.IsInteractable)
            .OrderBy(node => node.GlobalPosition.X)
            .ToList();

        for (int index = 0; index < navigable.Count; index++)
        {
            NCreature current = navigable[index];
            current.Hitbox.FocusNeighborLeft = (index > 0 ? navigable[index - 1].Hitbox : navigable[^1].Hitbox).GetPath();
            current.Hitbox.FocusNeighborRight = (index < navigable.Count - 1 ? navigable[index + 1].Hitbox : navigable[0].Hitbox).GetPath();
            current.Hitbox.FocusNeighborBottom = __instance.Ui.Hand.CardHolderContainer.GetPath();
            current.Hitbox.FocusNeighborTop = current.Hitbox.GetPath();
            current.UpdateNavigation();
        }

        __instance.Ui.Hand.CardHolderContainer.FocusNeighborTop = navigable.FirstOrDefault()?.Hitbox.GetPath();
        return false;
    }

    [HarmonyPatch(typeof(NEventLayout), "InitializeVisuals")]
    [HarmonyPrefix]
    private static bool FixDetachedPreviewEventVisuals(NEventLayout __instance)
    {
        if (NEventRoom.Instance?.Layout == __instance)
        {
            return true;
        }

        EventModel? eventModel = AccessTools.Field(typeof(NEventLayout), "_event").GetValue(__instance) as EventModel;
        if (eventModel == null)
        {
            return false;
        }

        __instance.SetPortrait(eventModel.CreateInitialPortrait());
        if (eventModel.HasVfx)
        {
            Node2D vfx = eventModel.CreateVfx();
            __instance.AddVfxAnchoredToPortrait(vfx);
            vfx.Position = EventModel.VfxOffset;
        }

        return false;
    }

    [HarmonyPatch(typeof(NEventOptionButton), "EnableButton")]
    [HarmonyPrefix]
    private static bool PreventDetachedPreviewEventButtonsFromEnabling(NEventOptionButton __instance)
    {
        NEventRoom? ownerRoom = FindAncestor<NEventRoom>(__instance);
        if (ownerRoom == null || ownerRoom == NEventRoom.Instance)
        {
            return true;
        }

        __instance.MouseFilter = Control.MouseFilterEnum.Ignore;
        __instance.FocusMode = Control.FocusModeEnum.None;
        return false;
    }

    [HarmonyPatch(typeof(NEventRoom), "OptionButtonClicked")]
    [HarmonyPrefix]
    private static bool BlockDetachedPreviewEventOptionClicks(NEventRoom __instance)
    {
        return __instance == NEventRoom.Instance;
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

    private static TNode? FindAncestor<TNode>(Node? node) where TNode : Node
    {
        Node? current = node;
        while (current != null)
        {
            if (current is TNode match)
            {
                return match;
            }

            current = current.GetParent();
        }

        return null;
    }

    private static async System.Threading.Tasks.Task<int> ApplyObservedTreasureGoldAsync(Player player, int goldAmount)
    {
        await MegaCrit.Sts2.Core.Commands.PlayerCmd.GainGold(goldAmount, player);
        return goldAmount;
    }
}
