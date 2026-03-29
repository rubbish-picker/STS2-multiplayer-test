using System.Threading.Tasks;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Multiplayer;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.Multiplayer;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect;
using MegaCrit.Sts2.Core.Nodes.Screens.CustomRun;
using MegaCrit.Sts2.Core.Nodes.Screens.DailyRun;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;
using MegaCrit.Sts2.Core.Platform;
using MegaCrit.Sts2.Core.Platform.Steam;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Saves.Runs;

namespace BetaDirectConnect;

public static class BetaDirectConnectPatches
{
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

    public static void TryJoinFromPanel(NJoinFriendScreen screen, LineEdit ipInput, LineEdit portInput, LineEdit playerIdInput)
    {
        MainFile.Logger.Info("TryJoinFromPanel invoked.");

        string ip = string.IsNullOrWhiteSpace(ipInput.Text) ? "127.0.0.1" : ipInput.Text.Trim();
        if (!int.TryParse(portInput.Text.Trim(), out int port))
        {
            ShowSimpleError("Invalid Port / 端口无效", "Port must be a number between 1 and 65535. / 端口必须是 1 到 65535 之间的数字。");
            return;
        }

        string playerIdText = string.IsNullOrWhiteSpace(playerIdInput.Text) ? string.Empty : playerIdInput.Text.Trim();
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

    private static void ShowSimpleError(string title, string body)
    {
        NErrorPopup? popup = NErrorPopup.Create(title, body, showReportBugButton: false);
        if (popup != null && NModalContainer.Instance != null)
        {
            NModalContainer.Instance.Add(popup);
        }
    }
}
