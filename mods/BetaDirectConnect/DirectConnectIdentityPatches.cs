using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.Debug;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Multiplayer;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Multiplayer.Game.Lobby;
using MegaCrit.Sts2.Core.Multiplayer.Messages.Lobby;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;
using MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect;
using MegaCrit.Sts2.Core.Platform;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;

namespace BetaDirectConnect;

public static class DirectConnectIdentityPatches
{
    private static readonly AccessTools.FieldRef<JoinFlow, TaskCompletionSource<ClientLobbyJoinResponseMessage>?> JoinCompletionRef =
        AccessTools.FieldRefAccess<JoinFlow, TaskCompletionSource<ClientLobbyJoinResponseMessage>?>("_joinCompletion");

    private static readonly AccessTools.FieldRef<JoinFlow, TaskCompletionSource<ClientLoadJoinResponseMessage>?> LoadJoinCompletionRef =
        AccessTools.FieldRefAccess<JoinFlow, TaskCompletionSource<ClientLoadJoinResponseMessage>?>("_loadJoinCompletion");

    private static readonly AccessTools.FieldRef<JoinFlow, TaskCompletionSource<ClientRejoinResponseMessage>?> RejoinCompletionRef =
        AccessTools.FieldRefAccess<JoinFlow, TaskCompletionSource<ClientRejoinResponseMessage>?>("_rejoinCompletion");

    [HarmonyPatch(typeof(StartRunLobby), MethodType.Constructor, new[]
    {
        typeof(GameMode),
        typeof(INetGameService),
        typeof(IStartRunLobbyListener),
        typeof(int)
    })]
    private static class StartRunLobbyCtorPatch
    {
        private static void Postfix(StartRunLobby __instance)
        {
            if (__instance.NetService.Platform == PlatformType.None && __instance.NetService.Type == NetGameType.Host)
            {
                DirectConnectIdentityService.AttachStartRunLobby(__instance);
            }
        }
    }

    [HarmonyPatch(typeof(LoadRunLobby), MethodType.Constructor, new[]
    {
        typeof(INetGameService),
        typeof(ILoadRunLobbyListener),
        typeof(SerializableRun)
    })]
    private static class LoadRunLobbyCtorPatch
    {
        private static void Postfix(LoadRunLobby __instance)
        {
            if (__instance.NetService.Platform == PlatformType.None && __instance.NetService.Type == NetGameType.Host)
            {
                DirectConnectIdentityService.AttachLoadRunLobby(__instance);
            }
        }
    }

    [HarmonyPatch(typeof(NetClientGameService), nameof(NetClientGameService.Initialize))]
    private static class NetClientInitializePatch
    {
        private static void Postfix(NetClientGameService __instance, PlatformType platform)
        {
            if (platform == PlatformType.None)
            {
                DirectConnectIdentityService.RegisterClient(__instance);
            }
        }
    }

    [HarmonyPatch(typeof(JoinFlow), "AttemptJoin")]
    private static class JoinFlowAttemptJoinPatch
    {
        private static bool Prefix(JoinFlow __instance, NetClientGameService gameService, ref Task<ClientLobbyJoinResponseMessage> __result)
        {
            if (gameService.Platform != PlatformType.None)
            {
                return true;
            }

            __result = AttemptJoinWithAssignedIdentity(__instance, gameService);
            return false;
        }
    }

    [HarmonyPatch(typeof(JoinFlow), "HandleInitialGameInfoMessage")]
    private static class JoinFlowHandleInitialGameInfoMessagePatch
    {
        private static void Postfix(JoinFlow __instance, InitialGameInfoMessage message)
        {
            if (__instance.NetService?.Platform != PlatformType.None)
            {
                return;
            }

            MainFile.Logger.Info(
                "[JoinDiag][ClientReceivedInitialGameInfo] " +
                $"remoteVersion={message.version} remoteHash={message.idDatabaseHash} remoteMods={FormatMods(message.mods)} " +
                $"remoteFailure={message.connectionFailureReason?.ToString() ?? "<none>"} remoteState={message.sessionState} remoteGameMode={message.gameMode} " +
                $"localVersion={GetLocalVersion()} localHash={ModelIdSerializationCache.Hash} localMods={FormatMods(ModManager.GetGameplayRelevantModNameList())} " +
                $"localClientId={BetaDirectConnectConfigService.EffectiveClientId} localNetId={SafeGetNetId(__instance.NetService)}");
        }
    }

    [HarmonyPatch(typeof(StartRunLobby), "OnConnectedToClientAsHost")]
    private static class StartRunLobbyOnConnectedToClientAsHostPatch
    {
        private static void Prefix(StartRunLobby __instance, ulong playerId)
        {
            if (__instance.NetService.Platform != PlatformType.None || __instance.NetService.Type != NetGameType.Host)
            {
                return;
            }

            LogHostInitialGameInfo("StartRunLobby.OnConnectedToClientAsHost", __instance.NetService, playerId);
        }
    }

    [HarmonyPatch(typeof(JoinFlow), "AttemptLoadJoin")]
    private static class JoinFlowAttemptLoadJoinPatch
    {
        private static bool Prefix(JoinFlow __instance, NetClientGameService gameService, ref Task<ClientLoadJoinResponseMessage> __result)
        {
            if (gameService.Platform != PlatformType.None)
            {
                return true;
            }

            __result = AttemptLoadJoinWithAssignedIdentity(__instance, gameService);
            return false;
        }
    }

    [HarmonyPatch(typeof(JoinFlow), "AttemptRejoin")]
    private static class JoinFlowAttemptRejoinPatch
    {
        private static bool Prefix(JoinFlow __instance, NetClientGameService gameService, ref Task<ClientRejoinResponseMessage> __result)
        {
            if (gameService.Platform != PlatformType.None)
            {
                return true;
            }

            __result = AttemptRejoinWithAssignedIdentity(__instance, gameService);
            return false;
        }
    }

    [HarmonyPatch(typeof(RunManager), nameof(RunManager.SetUpNewMultiPlayer))]
    private static class RunManagerSetUpNewMultiPlayerIdentityPatch
    {
        private static void Postfix(RunState state, StartRunLobby lobby)
        {
            if (lobby.NetService.Platform == PlatformType.None && lobby.NetService.Type == NetGameType.Host)
            {
                DirectConnectIdentityService.RegisterRunningHost(lobby.NetService, state.Players.Select(player => player.NetId));
            }
        }
    }

    [HarmonyPatch(typeof(RunManager), nameof(RunManager.SetUpSavedMultiPlayer))]
    private static class RunManagerSetUpSavedMultiPlayerIdentityPatch
    {
        private static void Postfix(RunState state, LoadRunLobby lobby)
        {
            if (lobby.NetService.Platform == PlatformType.None && lobby.NetService.Type == NetGameType.Host)
            {
                DirectConnectIdentityService.RegisterRunningHost(lobby.NetService, state.Players.Select(player => player.NetId));
            }
        }
    }

    private static async Task<ClientLobbyJoinResponseMessage> AttemptJoinWithAssignedIdentity(JoinFlow joinFlow, NetClientGameService gameService)
    {
        await DirectConnectIdentityService.EnsureClientIdentityAssigned(gameService);

        LogClientJoinAttempt("AttemptJoin", gameService);

        TaskCompletionSource<ClientLobbyJoinResponseMessage> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        JoinCompletionRef(joinFlow) = completion;

        MegaCrit.Sts2.Core.Unlocks.UnlockState unlockState = MegaCrit.Sts2.Core.Saves.SaveManager.Instance.GenerateUnlockStateFromProgress();
        ClientLobbyJoinRequestMessage message = new()
        {
            maxAscensionUnlocked = MegaCrit.Sts2.Core.Saves.SaveManager.Instance.Progress.MaxMultiplayerAscension,
            unlockState = unlockState.ToSerializable()
        };

        gameService.SendMessage(message);
        return await completion.Task;
    }

    private static async Task<ClientLoadJoinResponseMessage> AttemptLoadJoinWithAssignedIdentity(JoinFlow joinFlow, NetClientGameService gameService)
    {
        await DirectConnectIdentityService.EnsureClientIdentityAssigned(gameService);

        LogClientJoinAttempt("AttemptLoadJoin", gameService);

        TaskCompletionSource<ClientLoadJoinResponseMessage> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        LoadJoinCompletionRef(joinFlow) = completion;
        gameService.SendMessage(default(ClientLoadJoinRequestMessage));
        return await completion.Task;
    }

    private static async Task<ClientRejoinResponseMessage> AttemptRejoinWithAssignedIdentity(JoinFlow joinFlow, NetClientGameService gameService)
    {
        await DirectConnectIdentityService.EnsureClientIdentityAssigned(gameService);

        LogClientJoinAttempt("AttemptRejoin", gameService);

        TaskCompletionSource<ClientRejoinResponseMessage> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        RejoinCompletionRef(joinFlow) = completion;
        gameService.SendMessage(default(ClientRejoinRequestMessage));
        return await completion.Task;
    }

    private static void LogClientJoinAttempt(string phase, NetClientGameService gameService)
    {
        MainFile.Logger.Info(
            "[JoinDiag][ClientAttempt] " +
            $"phase={phase} localVersion={GetLocalVersion()} localHash={ModelIdSerializationCache.Hash} " +
            $"localMods={FormatMods(ModManager.GetGameplayRelevantModNameList())} " +
            $"clientId={BetaDirectConnectConfigService.EffectiveClientId} netId={SafeGetNetId(gameService)} " +
            $"displayId={BetaDirectConnectConfigService.NormalizeDisplayId(BetaDirectConnectConfigService.Current.DisplayId)} " +
            $"requestedNetId={DirectConnectIdentityService.GetEffectiveHostNetId()} configuredOverride={BetaDirectConnectConfigService.Current.NetIdOverride?.ToString() ?? "<none>"}");
    }

    private static void LogHostInitialGameInfo(string phase, INetGameService netService, ulong peerId)
    {
        List<string>? mods = ModManager.GetGameplayRelevantModNameList();
        MainFile.Logger.Info(
            "[JoinDiag][HostInitialGameInfo] " +
            $"phase={phase} peerId={peerId} hostVersion={GetLocalVersion()} hostHash={ModelIdSerializationCache.Hash} " +
            $"hostMods={FormatMods(mods)} hostNetId={SafeGetNetId(netService)}");
    }

    private static string GetLocalVersion()
    {
        return ReleaseInfoManager.Instance.ReleaseInfo?.Version ?? GitHelper.ShortCommitId ?? "UNKNOWN";
    }

    private static string FormatMods(IEnumerable<string>? mods)
    {
        if (mods == null)
        {
            return "<null>";
        }

        List<string> materialized = mods.ToList();
        return materialized.Count == 0 ? "<empty>" : "[" + string.Join(", ", materialized.OrderBy(mod => mod, StringComparer.Ordinal)) + "]";
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
}
