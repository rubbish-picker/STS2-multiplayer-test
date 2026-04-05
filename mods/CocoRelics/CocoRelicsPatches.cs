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
using MegaCrit.Sts2.Core.Multiplayer.Game.Lobby;
using MegaCrit.Sts2.Core.Nodes.Events;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect;
using MegaCrit.Sts2.Core.Nodes.Screens.CustomRun;
using MegaCrit.Sts2.Core.Nodes.Screens.DailyRun;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using MegaCrit.Sts2.Core.Random;
using MegaCrit.Sts2.Core.Rewards;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Factories;
using MegaCrit.Sts2.Core.Models.Relics;

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

    [HarmonyPatch]
    private static class MultiplayerLobbyCtorPatch
    {
        private static System.Collections.Generic.IEnumerable<System.Reflection.MethodBase> TargetMethods()
        {
            foreach (var ctor in AccessTools.GetDeclaredConstructors(typeof(StartRunLobby)))
            {
                yield return ctor;
            }

            foreach (var ctor in AccessTools.GetDeclaredConstructors(typeof(LoadRunLobby)))
            {
                yield return ctor;
            }

            foreach (var ctor in AccessTools.GetDeclaredConstructors(typeof(RunLobby)))
            {
                yield return ctor;
            }
        }

        private static void Postfix()
        {
            CocoRelicsMultiplayerSync.InitializeForRun();
        }
    }

    [HarmonyPatch(typeof(NCharacterSelectScreen), nameof(NCharacterSelectScreen.BeginRun))]
    private static class CharacterSelectBeginRunPatch
    {
        private static void Prefix()
        {
            CocoRelicsConfigService.Reload();
            CocoRelicsMultiplayerSync.InitializeForRun();
            CocoRelicsMultiplayerSync.BroadcastCurrentConfig();
        }
    }

    [HarmonyPatch(typeof(NCustomRunScreen), nameof(NCustomRunScreen.BeginRun))]
    private static class CustomRunBeginRunPatch
    {
        private static void Prefix()
        {
            CocoRelicsConfigService.Reload();
            CocoRelicsMultiplayerSync.InitializeForRun();
            CocoRelicsMultiplayerSync.BroadcastCurrentConfig();
        }
    }

    [HarmonyPatch(typeof(NDailyRunScreen), nameof(NDailyRunScreen.BeginRun))]
    private static class DailyRunBeginRunPatch
    {
        private static void Prefix()
        {
            CocoRelicsConfigService.Reload();
            CocoRelicsMultiplayerSync.InitializeForRun();
            CocoRelicsMultiplayerSync.BroadcastCurrentConfig();
        }
    }

    [HarmonyPatch(typeof(RunManager), nameof(RunManager.SetUpNewSinglePlayer))]
    private static class SetUpNewSinglePlayerPatch
    {
        private static void Prefix()
        {
            CocoRelicsConfigService.PrepareForNewRun(isMultiplayer: false);
        }
    }

    [HarmonyPatch(typeof(RunManager), nameof(RunManager.SetUpNewMultiPlayer))]
    private static class SetUpNewMultiPlayerPatch
    {
        private static void Prefix()
        {
            CocoRelicsConfigService.PrepareForNewRun(isMultiplayer: true);
        }
    }

    [HarmonyPatch(typeof(RunManager), nameof(RunManager.Launch))]
    private static class RunManagerLaunchPatch
    {
        private static void Postfix()
        {
            CocoRelicsConfigService.EnsureRunConfigLoaded();
            CocoRelicsMultiplayerSync.InitializeForRun();
            CocoRelicsMultiplayerSync.BroadcastCurrentConfig();
        }
    }

    [HarmonyPatch(typeof(RunManager), nameof(RunManager.CleanUp))]
    private static class RunManagerCleanUpPatch
    {
        private static void Prefix()
        {
            CocoRelicsConfigService.ClearRunLockInMemory();
            CocoRelicsMultiplayerSync.Clear();
        }
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
        if (pointType != MapPointType.Unknown)
        {
            return;
        }

        RunState? runState = CocoRelicsState.GetRunState();
        if (runState == null)
        {
            return;
        }

        if (TryGetObservedEnteringRoom(runState, out MapCoord coord, out ObservedRoomInfo info))
        {
            __result = info.RoomType;
            MainFile.Logger.Info($"Using frozen room type for {coord}: {info.RoomType}.");
        }
    }

    [HarmonyPatch(typeof(RunManager), "CreateRoom")]
    [HarmonyPrefix]
    private static void UseObservedRoomModel(ref RoomType roomType, ref MapPointType mapPointType, ref AbstractModel? model)
    {
        RunState? runState = CocoRelicsState.GetRunState();
        if (runState == null)
        {
            return;
        }

        if (!TryGetObservedEnteringRoom(runState, out MapCoord coord, out ObservedRoomInfo info))
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
            RoomType.Monster or RoomType.Elite or RoomType.Boss => ModelDb.GetById<EncounterModel>(info.ModelId).ToMutable(),
            RoomType.Event => ModelDb.GetById<EventModel>(info.ModelId),
            _ => model,
        };

        MainFile.Logger.Info($"Using frozen room model for {coord}: type={info.RoomType} model={info.ModelId?.Entry ?? "none"}.");
    }

    [HarmonyPatch(typeof(RunManager), "CreateRoom")]
    [HarmonyPostfix]
    private static void ForceObservedCreatedRoom(ref AbstractRoom __result, RoomType roomType, MapPointType mapPointType, AbstractModel? model)
    {
        RunState? runState = CocoRelicsState.GetRunState();
        if (runState == null)
        {
            return;
        }

        if (!TryGetObservedEnteringRoom(runState, out MapCoord coord, out ObservedRoomInfo info))
        {
            return;
        }

        __result = CreateObservedRoom(info, runState, mapPointType);
        MainFile.Logger.Info($"Rebuilt frozen room for {coord}: type={info.RoomType} model={info.ModelId?.Entry ?? "none"}.");
    }

    [HarmonyPatch(typeof(RunManager), "EnterRoomInternal")]
    [HarmonyPrefix]
    private static void ForceObservedRoomBeforeEnter(ref AbstractRoom room)
    {
        RunState? runState = CocoRelicsState.GetRunState();
        if (runState == null || runState.CurrentRoomCount != 0)
        {
            return;
        }

        if (!TryGetObservedEnteringRoom(runState, out MapCoord coord, out ObservedRoomInfo info))
        {
            return;
        }

        if (room.RoomType == info.RoomType && room.ModelId == info.ModelId)
        {
            return;
        }

        room = CreateObservedRoom(info, runState, runState.CurrentMapPoint?.PointType ?? MapPointType.Unassigned);
        MainFile.Logger.Info($"Forced frozen room before enter for {coord}: type={info.RoomType} model={info.ModelId?.Entry ?? "none"}.");
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

    [HarmonyPatch(typeof(RunManager), nameof(RunManager.GenerateMap))]
    [HarmonyPostfix]
    private static void LoadObservedSessionOnMapGeneration()
    {
        RunState? runState = CocoRelicsState.GetRunState();
        if (runState == null)
        {
            return;
        }

        CocoRelicsState.LoadPersistedSession(runState);
    }

    [HarmonyPatch(typeof(RunManager), nameof(RunManager.OnEnded))]
    [HarmonyPrefix]
    private static void ClearObservedSessionOnRunEnded()
    {
        CocoRelicsState.Clear();
        CocoRelicsConfigService.ClearPersistedRunConfig(RunManager.Instance.NetService?.Type.IsMultiplayer() ?? false);
        CocoRelicsStorage.ClearCurrentSession();
    }

    [HarmonyPatch(typeof(Player), nameof(Player.AddRelicInternal))]
    [HarmonyPrefix]
    private static bool PreventDuplicateCocoRelics(Player __instance, RelicModel relic)
    {
        if (relic is not ZeduCoco && relic is not BigMeal)
        {
            return true;
        }

        if (__instance.Relics.Any(existing => existing.Id == relic.Id))
        {
            MainFile.Logger.Warn($"Skipped duplicate coco relic add for player {__instance.NetId}: {relic.Id}.");
            return false;
        }

        return true;
    }

    [HarmonyPatch(typeof(MegaCrit.Sts2.Core.Commands.RelicCmd), nameof(MegaCrit.Sts2.Core.Commands.RelicCmd.Obtain), typeof(RelicModel), typeof(Player), typeof(int))]
    [HarmonyPostfix]
    private static void TryFuseMealRelic(RelicModel relic, Player player)
    {
        CocoRelicsMealService.TryQueueFusion(player, relic);
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
        CocoRelicsDebugRelicOption debugRelic = CocoRelicsConfigService.GetDebugRelicToGrantAtRunStart();
        if (debugRelic == CocoRelicsDebugRelicOption.None)
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
            RelicModel? relic = CreateConfiguredDebugRelic(debugRelic, player);
            if (relic == null)
            {
                continue;
            }

            relic.FloorAddedToDeck = 1;
            player.AddRelicInternal(relic);
            MainFile.Logger.Info($"Granted debug relic {relic.Id} to player {player.NetId}.");
        }
    }

    [HarmonyPatch(typeof(RelicFactory), nameof(RelicFactory.PullNextRelicFromFront), typeof(Player), typeof(RelicRarity))]
    [HarmonyPrefix]
    private static bool TryBiasRewardRelic(Player player, RelicRarity rarity, ref RelicModel __result)
    {
        if (!CocoRelicsRelicBiasService.TryPullBiasedRewardRelic(player, rarity, out RelicModel relic))
        {
            return true;
        }

        __result = relic;
        return false;
    }

    [HarmonyPatch(typeof(RelicFactory), nameof(RelicFactory.PullNextRelicFromBack), typeof(Player), typeof(RelicRarity), typeof(System.Collections.Generic.IEnumerable<RelicModel>))]
    [HarmonyPrefix]
    private static bool TryBiasShopRelic(Player player, RelicRarity rarity, System.Collections.Generic.IEnumerable<RelicModel> blacklist, ref RelicModel __result)
    {
        if (!CocoRelicsRelicBiasService.TryPullBiasedShopRelic(player, rarity, blacklist, out RelicModel relic))
        {
            return true;
        }

        __result = relic;
        return false;
    }

    [HarmonyPatch(typeof(MerchantRelicEntry), nameof(MerchantRelicEntry.CalcCost))]
    [HarmonyPostfix]
    private static void CapCocoRelicMerchantCost(MerchantRelicEntry __instance)
    {
        if (__instance.Model is not ZeduCoco && __instance.Model is not VeryHotCocoa)
        {
            return;
        }

        AccessTools.Field(typeof(MerchantEntry), "_cost").SetValue(__instance, System.Math.Min(__instance.Cost, 200));
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

    private static bool TryGetObservedEnteringRoom(RunState runState, out MapCoord coord, out ObservedRoomInfo info)
    {
        MapCoord? enteringCoord = runState.CurrentMapCoord ?? _currentEnteringCoord;
        if (!enteringCoord.HasValue)
        {
            coord = default;
            info = null!;
            return false;
        }

        coord = enteringCoord.Value;
        return CocoRelicsState.TryGet(coord, runState.CurrentActIndex, out info);
    }

    private static RelicModel? CreateConfiguredDebugRelic(CocoRelicsDebugRelicOption debugRelic, Player player)
    {
        return debugRelic switch
        {
            CocoRelicsDebugRelicOption.ZeduCoco when player.GetRelic<ZeduCoco>() == null => ModelDb.Relic<ZeduCoco>().ToMutable(),
            CocoRelicsDebugRelicOption.BigMeal when player.GetRelic<BigMeal>() == null => ModelDb.Relic<BigMeal>().ToMutable(),
            _ => null,
        };
    }

    private static AbstractRoom CreateObservedRoom(ObservedRoomInfo info, RunState runState, MapPointType mapPointType)
    {
        return info.RoomType switch
        {
            RoomType.Monster or RoomType.Elite or RoomType.Boss =>
                new CombatRoom(ModelDb.GetById<EncounterModel>(info.ModelId!).ToMutable(), runState),
            RoomType.Event =>
                new EventRoom(ModelDb.GetById<EventModel>(info.ModelId!)),
            RoomType.Treasure =>
                new TreasureRoom(runState.CurrentActIndex),
            RoomType.Shop =>
                new MerchantRoom(),
            RoomType.RestSite =>
                new RestSiteRoom(),
            RoomType.Map =>
                new MapRoom(),
            _ => roomTypeFromFallback(info, runState, mapPointType),
        };
    }

    private static AbstractRoom roomTypeFromFallback(ObservedRoomInfo info, RunState runState, MapPointType mapPointType)
    {
        return info.RoomType switch
        {
            RoomType.Event when mapPointType == MapPointType.Ancient => new EventRoom(ModelDb.GetById<EventModel>(info.ModelId!)),
            _ => new MapRoom(),
        };
    }

    private static async System.Threading.Tasks.Task<int> ApplyObservedTreasureGoldAsync(Player player, int goldAmount)
    {
        await MegaCrit.Sts2.Core.Commands.PlayerCmd.GainGold(goldAmount, player);
        return goldAmount;
    }
}
