using System.IO;
using System.Text.Json;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Extensions;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Multiplayer;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Multiplayer.Game.Lobby;
using MegaCrit.Sts2.Core.Multiplayer.Game.PeerInput;
using MegaCrit.Sts2.Core.Multiplayer.Transport.ENet;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.Multiplayer;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect;
using MegaCrit.Sts2.Core.Nodes.Screens.CustomRun;
using MegaCrit.Sts2.Core.Nodes.Screens.DailyRun;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;
using MegaCrit.Sts2.Core.Platform;
using MegaCrit.Sts2.Core.Platform.Null;
using MegaCrit.Sts2.Core.Platform.Steam;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Saves.Managers;
using MegaCrit.Sts2.Core.Saves.Runs;

namespace BetaDirectConnect;

public static class BetaDirectConnectPatches
{
    private static readonly string BeginRunTracePath = Path.Combine(GetModDirectory(), "BetaDirectConnect.beginrun.trace.log");

    private static readonly AccessTools.FieldRef<NMultiplayerHostSubmenu, Control> HostLoadingOverlayRef =
        AccessTools.FieldRefAccess<NMultiplayerHostSubmenu, Control>("_loadingOverlay");

    private static readonly AccessTools.FieldRef<NMultiplayerHostSubmenu, NSubmenuStack> HostStackRef =
        AccessTools.FieldRefAccess<NMultiplayerHostSubmenu, NSubmenuStack>("_stack");

    private static readonly AccessTools.FieldRef<NMultiplayerSubmenu, Control> MultiplayerLoadingOverlayRef =
        AccessTools.FieldRefAccess<NMultiplayerSubmenu, Control>("_loadingOverlay");

    private static readonly AccessTools.FieldRef<NMultiplayerSubmenu, NSubmenuStack> MultiplayerStackRef =
        AccessTools.FieldRefAccess<NMultiplayerSubmenu, NSubmenuStack>("_stack");

    private static readonly AccessTools.FieldRef<NMultiplayerSubmenu, NSubmenuButton> MultiplayerLoadButtonRef =
        AccessTools.FieldRefAccess<NMultiplayerSubmenu, NSubmenuButton>("_loadButton");

    private static readonly AccessTools.FieldRef<MegaCrit.Sts2.Core.Multiplayer.Quality.NetQualityTracker, INetGameService> NetQualityTrackerNetServiceRef =
        AccessTools.FieldRefAccess<MegaCrit.Sts2.Core.Multiplayer.Quality.NetQualityTracker, INetGameService>("_netService");

    private static readonly MethodInfo RunManagerRemotePlayerDisconnectedMethod =
        AccessTools.Method(typeof(RunManager), "RemotePlayerDisconnected")
        ?? throw new InvalidOperationException("Could not find RunManager.RemotePlayerDisconnected.");

    private static readonly AccessTools.FieldRef<MegaCrit.Sts2.Core.Combat.CombatManager, System.Collections.Generic.HashSet<Player>> CombatPlayersReadyToEndTurnRef =
        AccessTools.FieldRefAccess<MegaCrit.Sts2.Core.Combat.CombatManager, System.Collections.Generic.HashSet<Player>>("_playersReadyToEndTurn");

    private static readonly AccessTools.FieldRef<MegaCrit.Sts2.Core.Combat.CombatManager, System.Collections.Generic.HashSet<Player>> CombatPlayersReadyToBeginEnemyTurnRef =
        AccessTools.FieldRefAccess<MegaCrit.Sts2.Core.Combat.CombatManager, System.Collections.Generic.HashSet<Player>>("_playersReadyToBeginEnemyTurn");

    private static readonly AccessTools.FieldRef<MegaCrit.Sts2.Core.Combat.CombatManager, MegaCrit.Sts2.Core.Combat.CombatState?> CombatStateRef =
        AccessTools.FieldRefAccess<MegaCrit.Sts2.Core.Combat.CombatManager, MegaCrit.Sts2.Core.Combat.CombatState?>("_state");

    private static readonly AccessTools.FieldRef<ActChangeSynchronizer, System.Collections.Generic.List<bool>> ActChangeReadyPlayersRef =
        AccessTools.FieldRefAccess<ActChangeSynchronizer, System.Collections.Generic.List<bool>>("_readyPlayers");

    private static readonly AccessTools.FieldRef<ActChangeSynchronizer, RunState> ActChangeRunStateRef =
        AccessTools.FieldRefAccess<ActChangeSynchronizer, RunState>("_runState");

    private static readonly MethodInfo ActChangeMoveToNextActMethod =
        AccessTools.Method(typeof(ActChangeSynchronizer), "MoveToNextAct")
        ?? throw new InvalidOperationException("Could not find ActChangeSynchronizer.MoveToNextAct.");

    private static readonly AccessTools.FieldRef<MapSelectionSynchronizer, System.Collections.Generic.List<MapVote?>> MapSelectionVotesRef =
        AccessTools.FieldRefAccess<MapSelectionSynchronizer, System.Collections.Generic.List<MapVote?>>("_votes");

    private static readonly AccessTools.FieldRef<MapSelectionSynchronizer, RunState> MapSelectionRunStateRef =
        AccessTools.FieldRefAccess<MapSelectionSynchronizer, RunState>("_runState");

    private static readonly AccessTools.FieldRef<MapSelectionSynchronizer, INetGameService> MapSelectionNetServiceRef =
        AccessTools.FieldRefAccess<MapSelectionSynchronizer, INetGameService>("_netService");

    private static readonly AccessTools.FieldRef<MapSelectionSynchronizer, MegaCrit.Sts2.Core.Random.Rng> MapSelectionRngRef =
        AccessTools.FieldRefAccess<MapSelectionSynchronizer, MegaCrit.Sts2.Core.Random.Rng>("_multiplayerMapPointSelection");

    private static readonly AccessTools.FieldRef<MapSelectionSynchronizer, RunLocation> MapSelectionAcceptingVotesFromSourceRef =
        AccessTools.FieldRefAccess<MapSelectionSynchronizer, RunLocation>("_acceptingVotesFromSource");

    private static readonly AccessTools.FieldRef<MapSelectionSynchronizer, MegaCrit.Sts2.Core.GameActions.Multiplayer.ActionQueueSynchronizer> MapSelectionActionQueueSynchronizerRef =
        AccessTools.FieldRefAccess<MapSelectionSynchronizer, MegaCrit.Sts2.Core.GameActions.Multiplayer.ActionQueueSynchronizer>("_actionQueueSynchronizer");

    private static readonly AccessTools.FieldRef<EventSynchronizer, System.Collections.Generic.List<uint?>> EventPlayerVotesRef =
        AccessTools.FieldRefAccess<EventSynchronizer, System.Collections.Generic.List<uint?>>("_playerVotes");

    private static readonly AccessTools.FieldRef<EventSynchronizer, IPlayerCollection> EventPlayerCollectionRef =
        AccessTools.FieldRefAccess<EventSynchronizer, IPlayerCollection>("_playerCollection");

    private static readonly AccessTools.FieldRef<EventSynchronizer, INetGameService> EventNetServiceRef =
        AccessTools.FieldRefAccess<EventSynchronizer, INetGameService>("_netService");

    private static readonly AccessTools.FieldRef<EventSynchronizer, MegaCrit.Sts2.Core.Random.Rng> EventRngRef =
        AccessTools.FieldRefAccess<EventSynchronizer, MegaCrit.Sts2.Core.Random.Rng>("_multiplayerOptionSelectionRng");

    private static readonly AccessTools.FieldRef<EventSynchronizer, uint> EventPageIndexRef =
        AccessTools.FieldRefAccess<EventSynchronizer, uint>("_pageIndex");

    private static readonly AccessTools.FieldRef<EventSynchronizer, RunLocationTargetedMessageBuffer> EventMessageBufferRef =
        AccessTools.FieldRefAccess<EventSynchronizer, RunLocationTargetedMessageBuffer>("_messageBuffer");

    private static readonly MethodInfo EventChooseOptionForSharedEventMethod =
        AccessTools.Method(typeof(EventSynchronizer), "ChooseOptionForSharedEvent")
        ?? throw new InvalidOperationException("Could not find EventSynchronizer.ChooseOptionForSharedEvent.");

    private static readonly AccessTools.FieldRef<TreasureRoomRelicSynchronizer, IPlayerCollection> TreasurePlayerCollectionRef =
        AccessTools.FieldRefAccess<TreasureRoomRelicSynchronizer, IPlayerCollection>("_playerCollection");

    private static readonly AccessTools.FieldRef<TreasureRoomRelicSynchronizer, System.Collections.Generic.List<int?>> TreasureVotesRef =
        AccessTools.FieldRefAccess<TreasureRoomRelicSynchronizer, System.Collections.Generic.List<int?>>("_votes");

    private static readonly AccessTools.FieldRef<TreasureRoomRelicSynchronizer, System.Collections.Generic.List<RelicModel>?> TreasureCurrentRelicsRef =
        AccessTools.FieldRefAccess<TreasureRoomRelicSynchronizer, System.Collections.Generic.List<RelicModel>?>("_currentRelics");

    private static readonly AccessTools.FieldRef<TreasureRoomRelicSynchronizer, int?> TreasurePredictedVoteRef =
        AccessTools.FieldRefAccess<TreasureRoomRelicSynchronizer, int?>("_predictedVote");

    private static readonly AccessTools.FieldRef<TreasureRoomRelicSynchronizer, MegaCrit.Sts2.Core.Random.Rng> TreasureRngRef =
        AccessTools.FieldRefAccess<TreasureRoomRelicSynchronizer, MegaCrit.Sts2.Core.Random.Rng>("_rng");

    private static readonly AccessTools.FieldRef<TreasureRoomRelicSynchronizer, Action<List<MegaCrit.Sts2.Core.Entities.TreasureRelicPicking.RelicPickingResult>>?> TreasureRelicsAwardedEventRef =
        AccessTools.FieldRefAccess<TreasureRoomRelicSynchronizer, Action<List<MegaCrit.Sts2.Core.Entities.TreasureRelicPicking.RelicPickingResult>>?>("RelicsAwarded");

    private static readonly AccessTools.FieldRef<TreasureRoomRelicSynchronizer, Action?> TreasureVotesChangedEventRef =
        AccessTools.FieldRefAccess<TreasureRoomRelicSynchronizer, Action?>("VotesChanged");

    private static readonly MethodInfo PeerInputGetStateForPlayerMethod =
        AccessTools.Method(typeof(PeerInputSynchronizer), "GetStateForPlayer")
        ?? throw new InvalidOperationException("Could not find PeerInputSynchronizer.GetStateForPlayer.");

    private static readonly PropertyInfo RunManagerStateProperty =
        AccessTools.Property(typeof(RunManager), "State")
        ?? throw new InvalidOperationException("Could not find RunManager.State.");

    [HarmonyPatch(typeof(NJoinFriendScreen), nameof(NJoinFriendScreen._Ready))]
    private static class JoinFriendReadyPatch
    {
        private static void Postfix(NJoinFriendScreen __instance)
        {
            BetaDirectConnectUi.EnsureJoinPanel(__instance);
        }
    }

    [HarmonyPatch(typeof(NJoinFriendScreen), "FastMpJoin")]
    private static class JoinFriendFastMpJoinPatch
    {
        private static bool Prefix(NJoinFriendScreen __instance, ref Task __result)
        {
            if (!ShouldReplaceJoinPageWithDirectConnect())
            {
                return true;
            }

            BetaDirectConnectUi.ShowJoinPanelOnly(__instance);
            __result = Task.CompletedTask;
            return false;
        }
    }

    [HarmonyPatch(typeof(NMultiplayerHostSubmenu), nameof(NMultiplayerHostSubmenu._Ready))]
    private static class HostReadyPatch
    {
        private static void Postfix(NMultiplayerHostSubmenu __instance)
        {
            BetaDirectConnectUi.EnsureHostPanel(__instance);
        }
    }

    [HarmonyPatch(typeof(NMultiplayerSubmenu), nameof(NMultiplayerSubmenu._Ready))]
    private static class MultiplayerReadyPatch
    {
        private static void Postfix(NMultiplayerSubmenu __instance)
        {
            BetaDirectConnectUi.EnsureLoadPanel(__instance);
        }
    }

    [HarmonyPatch(typeof(NMultiplayerSubmenu), "OnHostPressed")]
    private static class OnHostPressedPatch
    {
        private static bool Prefix(NMultiplayerSubmenu __instance, NButton _)
        {
            if (!ShouldUseHostDirectConnect())
            {
                return true;
            }

            MultiplayerStackRef(__instance).PushSubmenuType<NMultiplayerHostSubmenu>();
            return false;
        }
    }

    [HarmonyPatch(typeof(NMultiplayerHostSubmenu), nameof(NMultiplayerHostSubmenu.StartHostAsync))]
    private static class StartHostAsyncPatch
    {
        private static bool Prefix(GameMode gameMode, Control loadingOverlay, NSubmenuStack stack, ref Task __result)
        {
            if (!ShouldUseHostDirectConnect())
            {
                return true;
            }

            int port = BetaDirectConnectConfigService.Current.HostPort;
            __result = StartHostAsyncDirect(gameMode, loadingOverlay, stack, port);
            return false;
        }
    }

    [HarmonyPatch(typeof(NMultiplayerHostSubmenu), nameof(NMultiplayerHostSubmenu.StartHost))]
    private static class StartHostPatch
    {
        private static bool Prefix(NMultiplayerHostSubmenu __instance, GameMode gameMode)
        {
            if (!ShouldUseHostDirectConnect())
            {
                return true;
            }

            int port = BetaDirectConnectUi.GetConfiguredHostPort(__instance);
            BetaDirectConnectConfigService.UpdateHostPort(port);
            TaskHelper.RunSafely(StartHostAsyncDirect(gameMode, HostLoadingOverlayRef(__instance), HostStackRef(__instance), port));
            return false;
        }
    }

    [HarmonyPatch(typeof(NMultiplayerSubmenu), nameof(NMultiplayerSubmenu.StartHost))]
    private static class StartLoadedRunPatch
    {
        private static bool Prefix(NMultiplayerSubmenu __instance, SerializableRun run)
        {
            if (!ShouldUseHostDirectConnect())
            {
                return true;
            }

            TaskHelper.RunSafely(StartLoadedRunAsyncDirect(__instance, run));
            return false;
        }
    }

    [HarmonyPatch(typeof(NMultiplayerSubmenu), "StartLoad")]
    private static class StartLoadPatch
    {
        private static bool Prefix(NMultiplayerSubmenu __instance, NButton _)
        {
            if (!ShouldUseHostDirectConnect())
            {
                return true;
            }

            ulong localPlayerId = PlatformUtil.GetLocalPlayerId(PlatformType.None);
            int port = BetaDirectConnectUi.GetConfiguredLoadPort(__instance);
            BetaDirectConnectConfigService.UpdateHostPort(port);
            MainFile.Logger.Info($"Attempting direct-connect multiplayer load with localPlayerId={localPlayerId}, port={port}");

            ReadSaveResult<SerializableRun> readSaveResult = SaveManager.Instance.LoadAndCanonicalizeMultiplayerRunSave(localPlayerId);
            if (!readSaveResult.Success || readSaveResult.SaveData == null)
            {
                MainFile.Logger.Error($"Direct-connect load failed. status={readSaveResult.Status}, localPlayerId={localPlayerId}");
                MultiplayerLoadButtonRef(__instance).Disable();
                ShowSimpleError(
                    "Invalid Multiplayer Save / 多人存档无效",
                    "The current multiplayer save does not match the local direct-connect player ID. If this was created under a different host identity, please re-host from a compatible save or start a new room. / 当前多人存档与本地直连玩家 ID 不匹配。如果这是在另一个房主身份下创建的，请使用匹配的存档重新开房，或新建房间。");
                return false;
            }

            __instance.StartHost(readSaveResult.SaveData);
            return false;
        }
    }

    [HarmonyPatch(typeof(NullPlatformUtilStrategy), nameof(NullPlatformUtilStrategy.GetPlayerName))]
    private static class NullPlatformGetPlayerNamePatch
    {
        private static void Postfix(ulong playerId, ref string __result)
        {
            if (playerId == 1UL && string.Equals(__result, "Test Host", StringComparison.Ordinal))
            {
                __result = playerId.ToString();
            }
            else if (playerId == 1000UL && string.Equals(__result, "Test Client 1", StringComparison.Ordinal))
            {
                __result = playerId.ToString();
            }
        }
    }

    [HarmonyPatch(typeof(ENetHost), "get_NetId")]
    private static class ENetHostNetIdPatch
    {
        private static bool Prefix(ref ulong __result)
        {
            __result = PlatformUtil.GetLocalPlayerId(PlatformType.None);
            return false;
        }
    }

    [HarmonyPatch(typeof(StartRunLobby), "HandleLobbyBeginRunMessage")]
    private static class StartRunLobbyBeginRunTracePatch
    {
        private static void Prefix(StartRunLobby __instance, object message, ulong senderId)
        {
            TraceBeginRun(
                $"StartRunLobby.HandleLobbyBeginRunMessage sender={senderId} localNetId={SafeGetNetId(__instance.NetService)} " +
                $"serviceType={__instance.NetService.Type} players={DescribeLobbyPlayers(__instance.Players)} message={message}");
        }
    }

    [HarmonyPatch(typeof(NCharacterSelectScreen), nameof(NCharacterSelectScreen.BeginRun))]
    private static class CharacterSelectBeginRunTracePatch
    {
        private static void Prefix(NCharacterSelectScreen __instance, string seed, object acts, object modifiers)
        {
            TraceBeginRun(
                $"NCharacterSelectScreen.BeginRun localNetId={SafeGetNetId(__instance.Lobby.NetService)} " +
                $"serviceType={__instance.Lobby.NetService.Type} players={DescribeLobbyPlayers(__instance.Lobby.Players)} seed={seed} acts={acts} modifiers={modifiers}");
        }

        private static void Finalizer(Exception? __exception)
        {
            if (__exception != null)
            {
                TraceBeginRun($"NCharacterSelectScreen.BeginRun exception: {__exception}");
            }
        }
    }

    [HarmonyPatch(typeof(NGame), "StartNewMultiplayerRun")]
    private static class NGameStartNewMultiplayerRunTracePatch
    {
        private static void Prefix(StartRunLobby lobby, bool shouldSave, object acts, object modifiers, string seed, int ascensionLevel, DateTimeOffset? dailyTime)
        {
            TraceBeginRun(
                $"NGame.StartNewMultiplayerRun shouldSave={shouldSave} localNetId={SafeGetNetId(lobby.NetService)} " +
                $"serviceType={lobby.NetService.Type} players={DescribeLobbyPlayers(lobby.Players)} seed={seed} ascension={ascensionLevel} dailyTime={dailyTime} acts={acts} modifiers={modifiers}");
        }

        private static void Finalizer(Exception? __exception)
        {
            if (__exception != null)
            {
                TraceBeginRun($"NGame.StartNewMultiplayerRun exception: {__exception}");
            }
        }
    }

    [HarmonyPatch(typeof(RunManager), nameof(RunManager.SetUpNewMultiPlayer))]
    private static class RunManagerSetUpNewMultiPlayerTracePatch
    {
        private static void Prefix(object state, StartRunLobby lobby, bool shouldSave, DateTimeOffset? dailyTime)
        {
            TraceBeginRun(
                $"RunManager.SetUpNewMultiPlayer shouldSave={shouldSave} dailyTime={dailyTime} " +
                $"localNetId={SafeGetNetId(lobby.NetService)} serviceType={lobby.NetService.Type} players={DescribeLobbyPlayers(lobby.Players)} state={state}");
        }

        private static void Finalizer(Exception? __exception)
        {
            if (__exception != null)
            {
                TraceBeginRun($"RunManager.SetUpNewMultiPlayer exception: {__exception}");
            }
        }
    }

    [HarmonyPatch(typeof(RunManager), nameof(RunManager.Launch))]
    private static class RunManagerLaunchTracePatch
    {
        private static void Prefix()
        {
            TraceBeginRun(
                $"RunManager.Launch prefix netServiceType={RunManager.Instance.NetService?.Type} " +
                $"netServiceId={SafeGetNetId(RunManager.Instance.NetService)}");
        }

        private static void Postfix(object __result)
        {
            TraceBeginRun(
                $"RunManager.Launch postfix LocalContext.NetId={MegaCrit.Sts2.Core.Context.LocalContext.NetId} result={__result}");
        }

        private static void Finalizer(Exception? __exception)
        {
            if (__exception != null)
            {
                TraceBeginRun($"RunManager.Launch exception: {__exception}");
            }
        }
    }

    [HarmonyPatch(typeof(MegaCrit.Sts2.Core.Multiplayer.Quality.NetQualityTracker), "HandleHeartbeatRequestMessage")]
    private static class NetQualityTrackerHeartbeatRequestPatch
    {
        private static bool Prefix(MegaCrit.Sts2.Core.Multiplayer.Quality.NetQualityTracker __instance, MegaCrit.Sts2.Core.Multiplayer.Messages.HeartbeatRequestMessage message, ulong senderId)
        {
            INetGameService netService = NetQualityTrackerNetServiceRef(__instance);
            if (netService.Platform != PlatformType.None || netService.Type != NetGameType.Client)
            {
                return true;
            }

            netService.SendMessage(new MegaCrit.Sts2.Core.Multiplayer.Messages.HeartbeatResponseMessage
            {
                counter = message.counter,
                isLoading = netService.IsGameLoading
            });
            return false;
        }
    }

    [HarmonyPatch(typeof(RunSaveManager), nameof(RunSaveManager.SaveRun))]
    private static class SaveRunPatch
    {
        private static bool Prefix(AbstractRoom? preFinishedRoom, ref Task __result)
        {
            if (!ShouldMirrorClientMultiplayerSave())
            {
                return true;
            }

            __result = SaveClientMirrorAsync(preFinishedRoom);
            return false;
        }
    }

    [HarmonyPatch(typeof(RunManager), nameof(RunManager.SetUpSavedMultiPlayer))]
    private static class SetUpSavedMultiPlayerPatch
    {
        private static void Postfix(RunState state, LoadRunLobby lobby)
        {
            if (lobby.NetService.Type != NetGameType.Host)
            {
                return;
            }

            if (lobby.ConnectedPlayerIds.Count >= state.Players.Count)
            {
                return;
            }

            RebuildSavedRunLobbyForConnectedPlayers(state, lobby);
        }
    }

    [HarmonyPatch(typeof(MegaCrit.Sts2.Core.Combat.CombatManager), nameof(MegaCrit.Sts2.Core.Combat.CombatManager.AllPlayersReadyToEndTurn))]
    private static class CombatManagerAllPlayersReadyToEndTurnPatch
    {
        private static bool Prefix(MegaCrit.Sts2.Core.Combat.CombatManager __instance, ref bool __result)
        {
            if (!ShouldUseConnectedPlayerCountsForReadiness())
            {
                return true;
            }

            MegaCrit.Sts2.Core.Combat.CombatState? state = CombatStateRef(__instance);
            if (state == null)
            {
                return true;
            }

            int connectedReadyPlayers = CountConnectedPlayers(
                CombatPlayersReadyToEndTurnRef(__instance).Select(player => player.NetId));
            int expectedConnectedPlayers = GetConnectedPlayerCountOrFallback(state.RunState);
            __result = connectedReadyPlayers >= expectedConnectedPlayers && state.CurrentSide == MegaCrit.Sts2.Core.Combat.CombatSide.Player;
            return false;
        }
    }

    [HarmonyPatch(typeof(MegaCrit.Sts2.Core.Combat.CombatManager), nameof(MegaCrit.Sts2.Core.Combat.CombatManager.SetReadyToBeginEnemyTurn))]
    private static class CombatManagerSetReadyToBeginEnemyTurnPatch
    {
        private static bool Prefix(MegaCrit.Sts2.Core.Combat.CombatManager __instance, Player player, Func<Task>? actionDuringEnemyTurn)
        {
            if (!ShouldUseConnectedPlayerCountsForReadiness())
            {
                return true;
            }

            MegaCrit.Sts2.Core.Combat.CombatState? state = CombatStateRef(__instance);
            if (state == null)
            {
                return true;
            }

            if (!__instance.IsInProgress)
            {
                MainFile.Logger.Error("Trying to set player ready to begin enemy turn, but combat is over!");
            }

            CombatPlayersReadyToBeginEnemyTurnRef(__instance).Add(player);
            bool allConnectedPlayersReady =
                CountConnectedPlayers(CombatPlayersReadyToBeginEnemyTurnRef(__instance).Select(readyPlayer => readyPlayer.NetId))
                >= GetConnectedPlayerCountOrFallback(state.RunState)
                && state.CurrentSide == MegaCrit.Sts2.Core.Combat.CombatSide.Player;

            if (allConnectedPlayersReady || RunManager.Instance.NetService.Type == NetGameType.Singleplayer)
            {
                TaskHelper.RunSafely(InvokeAfterAllPlayersReadyToBeginEnemyTurn(__instance, actionDuringEnemyTurn));
            }

            return false;
        }
    }

    [HarmonyPatch(typeof(ActChangeSynchronizer), nameof(ActChangeSynchronizer.IsWaitingForOtherPlayers))]
    private static class ActChangeSynchronizerIsWaitingPatch
    {
        private static bool Prefix(ActChangeSynchronizer __instance, ref bool __result)
        {
            if (!ShouldUseConnectedPlayerCountsForReadiness())
            {
                return true;
            }

            RunState runState = ActChangeRunStateRef(__instance);
            List<int> connectedSlots = GetConnectedPlayerSlots(runState);
            int localSlot = runState.GetPlayerSlotIndex(LocalContext.NetId!.Value);
            List<bool> readyPlayers = ActChangeReadyPlayersRef(__instance);

            __result = connectedSlots.Any(slot => slot != localSlot && (slot >= readyPlayers.Count || !readyPlayers[slot]));
            return false;
        }
    }

    [HarmonyPatch(typeof(ActChangeSynchronizer), nameof(ActChangeSynchronizer.OnPlayerReady))]
    private static class ActChangeSynchronizerOnPlayerReadyPatch
    {
        private static bool Prefix(ActChangeSynchronizer __instance, Player player)
        {
            if (!ShouldUseConnectedPlayerCountsForReadiness())
            {
                return true;
            }

            RunState runState = ActChangeRunStateRef(__instance);
            List<bool> readyPlayers = ActChangeReadyPlayersRef(__instance);
            int playerSlotIndex = runState.GetPlayerSlotIndex(player);
            readyPlayers[playerSlotIndex] = true;

            if (GetConnectedPlayerSlots(runState).All(slot => slot < readyPlayers.Count && readyPlayers[slot]))
            {
                ActChangeMoveToNextActMethod.Invoke(__instance, Array.Empty<object>());
            }

            return false;
        }
    }

    [HarmonyPatch(typeof(MapSelectionSynchronizer), nameof(MapSelectionSynchronizer.PlayerVotedForMapCoord))]
    private static class MapSelectionSynchronizerVotePatch
    {
        private static bool Prefix(MapSelectionSynchronizer __instance, Player player, RunLocation source, MapVote? destination)
        {
            if (!ShouldUseConnectedPlayerCountsForReadiness())
            {
                return true;
            }

            RunLocation acceptingVotesFromSource = MapSelectionAcceptingVotesFromSourceRef(__instance);
            if (acceptingVotesFromSource != source)
            {
                return true;
            }

            RunState runState = MapSelectionRunStateRef(__instance);
            List<MapVote?> votes = MapSelectionVotesRef(__instance);
            INetGameService netService = MapSelectionNetServiceRef(__instance);
            int playerSlotIndex = runState.GetPlayerSlotIndex(player);
            votes[playerSlotIndex] = destination;

            List<int> connectedSlots = GetConnectedPlayerSlots(runState);
            bool allConnectedVotesReady = connectedSlots.All(slot =>
                slot < votes.Count
                && votes[slot].HasValue
                && votes[slot]!.Value.mapGenerationCount == __instance.MapGenerationCount);

            if (allConnectedVotesReady && netService.Type != NetGameType.Client)
            {
                MegaCrit.Sts2.Core.Map.MapCoord coord = MapSelectionRngRef(__instance)
                    .NextItem(connectedSlots.Select(slot => votes[slot]).Where(vote => vote.HasValue).ToList())
                    .Value.coord;
                acceptingVotesFromSource.coord = coord;
                MapSelectionAcceptingVotesFromSourceRef(__instance) = acceptingVotesFromSource;
                MapSelectionActionQueueSynchronizerRef(__instance)
                    .RequestEnqueue(new MegaCrit.Sts2.Core.GameActions.MoveToMapCoordAction(LocalContext.GetMe(runState), coord));
            }

            return false;
        }
    }

    [HarmonyPatch(typeof(EventSynchronizer), "PlayerVotedForSharedOptionIndex")]
    private static class EventSynchronizerVotePatch
    {
        private static bool Prefix(EventSynchronizer __instance, Player player, uint optionIndex, uint pageIndex)
        {
            if (!ShouldUseConnectedPlayerCountsForReadiness())
            {
                return true;
            }

            uint currentPageIndex = EventPageIndexRef(__instance);
            if (pageIndex != currentPageIndex)
            {
                return true;
            }

            IPlayerCollection playerCollection = EventPlayerCollectionRef(__instance);
            List<uint?> votes = EventPlayerVotesRef(__instance);
            votes[playerCollection.GetPlayerSlotIndex(player)] = optionIndex;

            List<int> connectedSlots = GetConnectedPlayerSlots(playerCollection.Players);
            if (!connectedSlots.All(slot => slot < votes.Count && votes[slot].HasValue))
            {
                return false;
            }

            INetGameService netService = EventNetServiceRef(__instance);
            if (netService.Type == NetGameType.Client)
            {
                return false;
            }

            uint chosenOption = EventRngRef(__instance)
                .NextItem(connectedSlots.Select(slot => votes[slot]).Where(vote => vote.HasValue).ToList())
                .Value;
            RunLocationTargetedMessageBuffer messageBuffer = EventMessageBufferRef(__instance);
            netService.SendMessage(new MegaCrit.Sts2.Core.Multiplayer.Messages.Game.Sync.SharedEventOptionChosenMessage
            {
                optionIndex = chosenOption,
                pageIndex = currentPageIndex,
                location = messageBuffer.CurrentLocation
            });
            EventChooseOptionForSharedEventMethod.Invoke(__instance, new object[] { chosenOption });
            return false;
        }
    }

    [HarmonyPatch(typeof(TreasureRoomRelicSynchronizer), nameof(TreasureRoomRelicSynchronizer.OnPicked))]
    private static class TreasureRoomRelicSynchronizerOnPickedPatch
    {
        private static bool Prefix(TreasureRoomRelicSynchronizer __instance, Player player, int index)
        {
            if (!ShouldUseConnectedPlayerCountsForReadiness())
            {
                return true;
            }

            List<int?> votes = TreasureVotesRef(__instance);
            List<RelicModel>? currentRelics = TreasureCurrentRelicsRef(__instance);
            if (currentRelics == null || index >= currentRelics.Count)
            {
                return true;
            }

            IPlayerCollection playerCollection = TreasurePlayerCollectionRef(__instance);
            votes[playerCollection.GetPlayerSlotIndex(player)] = index;
            TreasureVotesChangedEventRef(__instance)?.Invoke();

            List<int> connectedSlots = GetConnectedPlayerSlots(playerCollection.Players);
            if (!connectedSlots.All(slot => slot < votes.Count && votes[slot].HasValue))
            {
                return false;
            }

            int? predictedVote = TreasurePredictedVoteRef(__instance);
            if (predictedVote.HasValue)
            {
                TreasurePredictedVoteRef(__instance) = null;
                int localSlot = playerCollection.GetPlayerSlotIndex(LocalContext.GetMe(playerCollection));
                if (votes[localSlot] != predictedVote.Value)
                {
                    TreasureVotesChangedEventRef(__instance)?.Invoke();
                }
            }

            AwardRelicsForConnectedPlayers(__instance, currentRelics, votes, playerCollection, connectedSlots);
            TreasureCurrentRelicsRef(__instance) = null;
            return false;
        }
    }

    [HarmonyPatch(typeof(PeerInputSynchronizer), nameof(PeerInputSynchronizer.GetControlSpaceFocusPosition))]
    private static class PeerInputSynchronizerGetControlSpaceFocusPositionPatch
    {
        private static bool Prefix(PeerInputSynchronizer __instance, ulong playerId, Control rootControl, ref Vector2 __result)
        {
            if (!ShouldUseMissingPeerInputFallback(__instance, playerId))
            {
                return true;
            }

            __result = rootControl.Size * 0.5f;
            return false;
        }
    }

    [HarmonyPatch(typeof(PeerInputSynchronizer), nameof(PeerInputSynchronizer.GetMouseDown))]
    private static class PeerInputSynchronizerGetMouseDownPatch
    {
        private static bool Prefix(PeerInputSynchronizer __instance, ulong playerId, ref bool __result)
        {
            if (!ShouldUseMissingPeerInputFallback(__instance, playerId))
            {
                return true;
            }

            __result = false;
            return false;
        }
    }

    [HarmonyPatch(typeof(PeerInputSynchronizer), nameof(PeerInputSynchronizer.GetScreenType))]
    private static class PeerInputSynchronizerGetScreenTypePatch
    {
        private static bool Prefix(PeerInputSynchronizer __instance, ulong playerId, ref NetScreenType __result)
        {
            if (!ShouldUseMissingPeerInputFallback(__instance, playerId))
            {
                return true;
            }

            __result = NetScreenType.None;
            return false;
        }
    }

    [HarmonyPatch(typeof(PeerInputSynchronizer), nameof(PeerInputSynchronizer.GetHoveredModelData))]
    private static class PeerInputSynchronizerGetHoveredModelDataPatch
    {
        private static bool Prefix(PeerInputSynchronizer __instance, ulong playerId, ref HoveredModelData __result)
        {
            if (!ShouldUseMissingPeerInputFallback(__instance, playerId))
            {
                return true;
            }

            __result = default;
            return false;
        }
    }

    [HarmonyPatch(typeof(PeerInputSynchronizer), nameof(PeerInputSynchronizer.GetIsTargeting))]
    private static class PeerInputSynchronizerGetIsTargetingPatch
    {
        private static bool Prefix(PeerInputSynchronizer __instance, ulong playerId, ref bool __result)
        {
            if (!ShouldUseMissingPeerInputFallback(__instance, playerId))
            {
                return true;
            }

            __result = false;
            return false;
        }
    }

    public static void TryJoinFromPanel(NJoinFriendScreen screen, LineEdit ipInput, LineEdit portInput, LineEdit playerIdInput)
    {
        MainFile.Logger.Info("TryJoinFromPanel invoked.");

        string ip = string.IsNullOrWhiteSpace(ipInput.Text) ? "127.0.0.1" : ipInput.Text.Trim();
        if (!int.TryParse(portInput.Text.Trim(), out int port))
        {
            ShowSimpleError("Invalid Port / 端口无效", "Port must be a number between 1 and 65535. / 端口必须是 1 到 65535 之间的数字。");
            return;
        }

        string playerIdText = string.IsNullOrWhiteSpace(playerIdInput.Text)
            ? BetaDirectConnectConfigService.EffectivePlayerIdText
            : playerIdInput.Text.Trim();
        ulong playerId = BetaDirectConnectConfigService.ParsePlayerIdInput(playerIdText);
        if (playerId == 0)
        {
            ShowSimpleError("Invalid Player ID / 玩家 ID 无效", "Player ID cannot be empty. / 玩家 ID 不能为空。");
            return;
        }

        port = BetaDirectConnectConfigService.NormalizePort(port);
        portInput.Text = port.ToString();
        playerIdInput.Text = playerIdText;

        BetaDirectConnectConfigService.UpdateJoinSettings(ip, port, playerIdText, playerId);
        MainFile.Logger.Info($"Direct join requested. ip={ip}, port={port}, playerIdText={playerIdText}, playerId={playerId}");
        TaskHelper.RunSafely(screen.JoinGameAsync(new RetryingDirectConnectInitializer(playerIdText, ip, (ushort)port)));
    }

    private static bool ShouldReplaceJoinPageWithDirectConnect()
    {
        return !SteamInitializer.Initialized || CommandLineHelper.HasArg("fastmp");
    }

    private static bool ShouldUseHostDirectConnect()
    {
        return true;
    }

    private static Task StartHostAsyncDirect(GameMode gameMode, Control loadingOverlay, NSubmenuStack stack, int port)
    {
        loadingOverlay.Visible = true;
        try
        {
            MainFile.Logger.Info($"Starting direct ENet host. mode={gameMode}, port={port}");
            NetHostGameService netService = new();
            NetErrorInfo? netErrorInfo = netService.StartENetHost((ushort)port, 4);
            if (!netErrorInfo.HasValue)
            {
                MainFile.Logger.Info($"Direct ENet host started successfully on port {port}");
                switch (gameMode)
                {
                    case GameMode.Standard:
                    {
                        NCharacterSelectScreen submenuType = stack.GetSubmenuType<NCharacterSelectScreen>();
                        submenuType.InitializeMultiplayerAsHost(netService, 4);
                        stack.Push(submenuType);
                        break;
                    }
                    case GameMode.Daily:
                    {
                        NDailyRunScreen submenuType = stack.GetSubmenuType<NDailyRunScreen>();
                        submenuType.InitializeMultiplayerAsHost(netService);
                        stack.Push(submenuType);
                        break;
                    }
                    default:
                    {
                        NCustomRunScreen submenuType = stack.GetSubmenuType<NCustomRunScreen>();
                        submenuType.InitializeMultiplayerAsHost(netService, 4);
                        stack.Push(submenuType);
                        break;
                    }
                }
            }
            else
            {
                MainFile.Logger.Error($"Direct ENet host failed on port {port}: {netErrorInfo.Value}");
                NErrorPopup? popup = NErrorPopup.Create(netErrorInfo.Value);
                if (popup != null && NModalContainer.Instance != null)
                {
                    NModalContainer.Instance.Add(popup);
                }
            }
        }
        catch
        {
            NErrorPopup? popup = NErrorPopup.Create(new NetErrorInfo(NetError.InternalError, selfInitiated: false));
            if (popup != null && NModalContainer.Instance != null)
            {
                NModalContainer.Instance.Add(popup);
            }

            throw;
        }
        finally
        {
            loadingOverlay.Visible = false;
        }

        return Task.CompletedTask;
    }

    private static Task StartLoadedRunAsyncDirect(NMultiplayerSubmenu submenu, SerializableRun run)
    {
        Control loadingOverlay = MultiplayerLoadingOverlayRef(submenu);
        NSubmenuStack stack = MultiplayerStackRef(submenu);
        int port = BetaDirectConnectConfigService.Current.HostPort;
        loadingOverlay.Visible = true;
        try
        {
            MainFile.Logger.Info($"Starting direct ENet loaded-run host on port {port}");
            NetHostGameService netService = new();
            NetErrorInfo? netErrorInfo = netService.StartENetHost((ushort)port, 4);
            if (!netErrorInfo.HasValue)
            {
                MainFile.Logger.Info($"Direct ENet loaded-run host started successfully on port {port}");
                if (run.Modifiers.Count > 0)
                {
                    if (run.DailyTime.HasValue)
                    {
                        NDailyRunLoadScreen submenuType = stack.GetSubmenuType<NDailyRunLoadScreen>();
                        submenuType.InitializeAsHost(netService, run);
                        stack.Push(submenuType);
                    }
                    else
                    {
                        NCustomRunLoadScreen submenuType = stack.GetSubmenuType<NCustomRunLoadScreen>();
                        submenuType.InitializeAsHost(netService, run);
                        stack.Push(submenuType);
                    }
                }
                else
                {
                    NMultiplayerLoadGameScreen submenuType = stack.GetSubmenuType<NMultiplayerLoadGameScreen>();
                    submenuType.InitializeAsHost(netService, run);
                    stack.Push(submenuType);
                }
            }
            else
            {
                MainFile.Logger.Error($"Direct ENet loaded-run host failed on port {port}: {netErrorInfo.Value}");
                NErrorPopup? popup = NErrorPopup.Create(netErrorInfo.Value);
                if (popup != null && NModalContainer.Instance != null)
                {
                    NModalContainer.Instance.Add(popup);
                }
            }
        }
        finally
        {
            loadingOverlay.Visible = false;
        }

        return Task.CompletedTask;
    }

    private static bool ShouldMirrorClientMultiplayerSave()
    {
        if (!RunManager.Instance.ShouldSave)
        {
            return false;
        }

        INetGameService? netService = RunManager.Instance.NetService;
        return netService != null
            && netService.Platform == PlatformType.None
            && netService.Type == NetGameType.Client;
    }

    private static async Task SaveClientMirrorAsync(AbstractRoom? preFinishedRoom)
    {
        SerializableRun save = RunManager.Instance.ToSave(preFinishedRoom);
        string path = ToFileSystemPath(SaveManager.Instance.GetProfileScopedPath(Path.Combine(UserDataPathProvider.SavesDir, RunSaveManager.multiplayerRunSaveFileName)));
        string backupPath = path + ".backup";

        string? directory = Path.GetDirectoryName(path);
        if (string.IsNullOrWhiteSpace(directory))
        {
            throw new InvalidOperationException("Multiplayer save path has no parent directory.");
        }

        Directory.CreateDirectory(directory);
        if (File.Exists(path))
        {
            File.Copy(path, backupPath, overwrite: true);
        }

        await using FileStream stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, save, JsonSerializationUtility.GetTypeInfo<SerializableRun>());
        MainFile.Logger.Info($"Mirrored direct-connect client multiplayer save to {path}");
    }

    private static void RebuildSavedRunLobbyForConnectedPlayers(RunState state, LoadRunLobby loadLobby)
    {
        RunManager runManager = RunManager.Instance;
        RunLobby? originalRunLobby = runManager.RunLobby;
        MegaCrit.Sts2.Core.Multiplayer.CombatStateSynchronizer? originalCombatSync = runManager.CombatStateSynchronizer;

        originalCombatSync?.Dispose();
        originalRunLobby?.Dispose();

        RunLobby runLobby = new(
            loadLobby.GameMode,
            loadLobby.NetService,
            runManager,
            state,
            loadLobby.ConnectedPlayerIds);
        runLobby.RemotePlayerDisconnected += CreateRemotePlayerDisconnectedHandler(runManager);

        MegaCrit.Sts2.Core.Multiplayer.CombatStateSynchronizer combatSync = new(loadLobby.NetService, runLobby, state);

        AccessTools.Property(typeof(RunManager), nameof(RunManager.RunLobby))?.SetValue(runManager, runLobby);
        AccessTools.Property(typeof(RunManager), nameof(RunManager.CombatStateSynchronizer))?.SetValue(runManager, combatSync);

        MainFile.Logger.Info(
            $"Rebuilt saved multiplayer RunLobby using connected players [{string.Join(", ", loadLobby.ConnectedPlayerIds.OrderBy(id => id))}] " +
            $"instead of saved players [{string.Join(", ", state.Players.Select(player => player.NetId).OrderBy(id => id))}].");
    }

    private static bool ShouldUseConnectedPlayerCountsForReadiness()
    {
        INetGameService? netService = RunManager.Instance.NetService;
        return netService != null
            && netService.Platform == PlatformType.None
            && netService.Type == NetGameType.Host
            && RunManager.Instance.RunLobby != null;
    }

    private static bool ShouldUseMissingPeerInputFallback(PeerInputSynchronizer synchronizer, ulong playerId)
    {
        if (HasPeerInputState(synchronizer, playerId))
        {
            return false;
        }

        INetGameService netService = synchronizer.NetService;
        if (!netService.IsConnected || netService.Platform != PlatformType.None || RunManager.Instance.RunLobby == null)
        {
            return false;
        }

        HashSet<ulong> savedPlayerIds = GetSavedPlayerIds();
        if (!savedPlayerIds.Contains(playerId))
        {
            return false;
        }

        return !RunManager.Instance.RunLobby.ConnectedPlayerIds.Contains(playerId);
    }

    private static bool HasPeerInputState(PeerInputSynchronizer synchronizer, ulong playerId)
    {
        return PeerInputGetStateForPlayerMethod.Invoke(synchronizer, new object?[] { playerId }) != null;
    }

    private static HashSet<ulong> GetSavedPlayerIds()
    {
        RunState? state = RunManagerStateProperty.GetValue(RunManager.Instance) as RunState;
        return state?.Players.Select(player => player.NetId).ToHashSet() ?? new HashSet<ulong>();
    }

    private static int GetConnectedPlayerCountOrFallback(IRunState state)
    {
        RunLobby? runLobby = RunManager.Instance.RunLobby;
        if (runLobby == null)
        {
            return state.Players.Count;
        }

        int connected = runLobby.ConnectedPlayerIds.Count;
        return connected > 0 ? connected : state.Players.Count;
    }

    private static List<int> GetConnectedPlayerSlots(IEnumerable<Player> players)
    {
        HashSet<ulong> connectedPlayerIds = GetConnectedPlayerIdsOrAll(players.Select(player => player.NetId));
        return players
            .Select((player, index) => new { player.NetId, index })
            .Where(entry => connectedPlayerIds.Contains(entry.NetId))
            .Select(entry => entry.index)
            .ToList();
    }

    private static List<int> GetConnectedPlayerSlots(IRunState state)
    {
        return GetConnectedPlayerSlots(state.Players);
    }

    private static int CountConnectedPlayers(IEnumerable<ulong> playerIds)
    {
        RunLobby? runLobby = RunManager.Instance.RunLobby;
        if (runLobby == null)
        {
            return playerIds.Distinct().Count();
        }

        HashSet<ulong> connected = runLobby.ConnectedPlayerIds.ToHashSet();
        return playerIds.Where(connected.Contains).Distinct().Count();
    }

    private static HashSet<ulong> GetConnectedPlayerIdsOrAll(IEnumerable<ulong> fallbackPlayerIds)
    {
        HashSet<ulong> fallback = fallbackPlayerIds.ToHashSet();
        RunLobby? runLobby = RunManager.Instance.RunLobby;
        if (runLobby == null || runLobby.ConnectedPlayerIds.Count == 0)
        {
            return fallback;
        }

        return runLobby.ConnectedPlayerIds.ToHashSet();
    }

    private static void AwardRelicsForConnectedPlayers(
        TreasureRoomRelicSynchronizer synchronizer,
        List<RelicModel> currentRelics,
        List<int?> votes,
        IPlayerCollection playerCollection,
        List<int> connectedSlots)
    {
        Dictionary<int, List<Player>> voteBuckets = new();
        for (int i = 0; i < currentRelics.Count; i++)
        {
            voteBuckets[i] = new List<Player>();
        }

        foreach (int slot in connectedSlots)
        {
            Player player = playerCollection.Players[slot];
            int vote = votes[slot]!.Value;
            voteBuckets[vote].Add(player);
        }

        List<MegaCrit.Sts2.Core.Entities.TreasureRelicPicking.RelicPickingResult> results = new();
        List<RelicModel> unclaimedRelics = new();
        MegaCrit.Sts2.Core.Random.Rng rng = TreasureRngRef(synchronizer);

        foreach ((int relicIndex, List<Player> voters) in voteBuckets)
        {
            RelicModel relic = currentRelics[relicIndex];
            if (voters.Count == 0)
            {
                unclaimedRelics.Add(relic);
            }
            else if (voters.Count == 1)
            {
                results.Add(new MegaCrit.Sts2.Core.Entities.TreasureRelicPicking.RelicPickingResult
                {
                    type = MegaCrit.Sts2.Core.Entities.TreasureRelicPicking.RelicPickingResultType.OnlyOnePlayerVoted,
                    relic = relic,
                    player = voters[0]
                });
            }
            else
            {
                MegaCrit.Sts2.Core.Entities.TreasureRelicPicking.RelicPickingFightMove[] possibleMoves =
                    Enum.GetValues<MegaCrit.Sts2.Core.Entities.TreasureRelicPicking.RelicPickingFightMove>();
                results.Add(MegaCrit.Sts2.Core.Entities.TreasureRelicPicking.RelicPickingResult.GenerateRelicFight(
                    voters,
                    relic,
                    () => rng.NextItem(possibleMoves)));
            }
        }

        List<Player> connectedPlayers = connectedSlots.Select(slot => playerCollection.Players[slot]).ToList();
        List<Player> playersWithoutRelic = connectedPlayers
            .Where(player => results.All(result => result.player != player))
            .ToList();
        unclaimedRelics.StableShuffle(rng);
        for (int i = 0; i < Mathf.Min(unclaimedRelics.Count, playersWithoutRelic.Count); i++)
        {
            results.Add(new MegaCrit.Sts2.Core.Entities.TreasureRelicPicking.RelicPickingResult
            {
                type = MegaCrit.Sts2.Core.Entities.TreasureRelicPicking.RelicPickingResultType.ConsolationPrize,
                player = playersWithoutRelic[i],
                relic = unclaimedRelics[i]
            });
        }

        TreasureRelicsAwardedEventRef(synchronizer)?.Invoke(results);
    }

    private static Task InvokeAfterAllPlayersReadyToBeginEnemyTurn(MegaCrit.Sts2.Core.Combat.CombatManager combatManager, Func<Task>? actionDuringEnemyTurn)
    {
        MethodInfo method = AccessTools.Method(
            typeof(MegaCrit.Sts2.Core.Combat.CombatManager),
            "AfterAllPlayersReadyToBeginEnemyTurn",
            new[] { typeof(Func<Task>) })
            ?? throw new InvalidOperationException("Could not find CombatManager.AfterAllPlayersReadyToBeginEnemyTurn.");

        return (Task)(method.Invoke(combatManager, new object?[] { actionDuringEnemyTurn })
            ?? throw new InvalidOperationException("AfterAllPlayersReadyToBeginEnemyTurn returned null."));
    }

    private static Action<ulong> CreateRemotePlayerDisconnectedHandler(RunManager runManager)
    {
        return (Action<ulong>)Delegate.CreateDelegate(typeof(Action<ulong>), runManager, RunManagerRemotePlayerDisconnectedMethod);
    }

    private static void ShowSimpleError(string title, string body)
    {
        NErrorPopup? popup = NErrorPopup.Create(title, body, showReportBugButton: false);
        if (popup != null && NModalContainer.Instance != null)
        {
            NModalContainer.Instance.Add(popup);
        }
    }

    private static void TraceBeginRun(string message)
    {
        try
        {
            Directory.CreateDirectory(GetModDirectory());
            File.AppendAllText(BeginRunTracePath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {message}{System.Environment.NewLine}");
        }
        catch
        {
        }

        MainFile.Logger.Info($"[BeginRunTrace] {message}");
    }

    private static string DescribeLobbyPlayers(System.Collections.Generic.IEnumerable<LobbyPlayer> players)
    {
        return string.Join(", ", players.Select(player => $"{player.id}:{player.character.Id.Entry}:slot{player.slotId}:ready={player.isReady}"));
    }

    private static ulong SafeGetNetId(INetGameService? netService)
    {
        if (netService == null || !netService.IsConnected)
        {
            return 0UL;
        }

        try
        {
            return netService.NetId;
        }
        catch
        {
            return 0UL;
        }
    }

    private static ulong SafeGetHostNetId(INetGameService? netService)
    {
        if (netService is not NetClientGameService clientService || !clientService.IsConnected)
        {
            return 0UL;
        }

        try
        {
            return clientService.HostNetId;
        }
        catch
        {
            return 0UL;
        }
    }

    private static string ToFileSystemPath(string path)
    {
        return path.StartsWith("user://", StringComparison.OrdinalIgnoreCase)
            ? ProjectSettings.GlobalizePath(path)
            : path;
    }

    private static string GetModDirectory()
    {
        string? location = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
        return string.IsNullOrWhiteSpace(location) ? AppContext.BaseDirectory : location;
    }
}
