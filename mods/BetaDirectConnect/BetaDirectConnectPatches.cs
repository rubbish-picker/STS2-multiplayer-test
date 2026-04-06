using System.IO;
using System.Text.Json;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Multiplayer;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Multiplayer.Game.Lobby;
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
        string path = SaveManager.Instance.GetProfileScopedPath(Path.Combine(UserDataPathProvider.SavesDir, RunSaveManager.multiplayerRunSaveFileName));
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

    private static string GetModDirectory()
    {
        string? location = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
        return string.IsNullOrWhiteSpace(location) ? AppContext.BaseDirectory : location;
    }
}
