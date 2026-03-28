using System;
using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;

namespace MultiplayerCard;

[HarmonyPatch]
public static class MultiplayerRewardAutoTestPatches
{
    private static bool _scheduledAutoEmbark;
    private static bool _scheduledAutoPreview;

    [HarmonyPatch(typeof(NCharacterSelectScreen), nameof(NCharacterSelectScreen.OnSubmenuOpened))]
    [HarmonyPostfix]
    private static void AutoEmbarkOnCharacterSelectOpened(NCharacterSelectScreen __instance)
    {
        if (!MainFile.AutoTestEnabled || _scheduledAutoEmbark)
        {
            return;
        }

        NetGameType netType = __instance.Lobby.NetService.Type;
        if (netType is not (NetGameType.Host or NetGameType.Client))
        {
            return;
        }

        _scheduledAutoEmbark = true;
        TaskHelper.RunSafely(AutoEmbarkAsync(__instance, netType));
    }

    [HarmonyPatch(typeof(NCharacterSelectScreen), nameof(NCharacterSelectScreen.BeginRun))]
    [HarmonyPostfix]
    private static void AutoPreviewAfterBeginRun(NCharacterSelectScreen __instance)
    {
        if (!MainFile.AutoTestEnabled || _scheduledAutoPreview)
        {
            return;
        }

        if (__instance.Lobby.NetService.Type != NetGameType.Host)
        {
            return;
        }

        _scheduledAutoPreview = true;
        TaskHelper.RunSafely(AutoPreviewAsync());
    }

    private static async Task AutoEmbarkAsync(NCharacterSelectScreen screen, NetGameType netType)
    {
        await Task.Delay(netType == NetGameType.Host ? 1500 : 3000);

        if (!Godot.GodotObject.IsInstanceValid(screen))
        {
            return;
        }

        MainFile.Logger.Info($"[MultiplayerCard] Auto test embarking for {netType}.");
        AccessTools.Method(typeof(NCharacterSelectScreen), "OnEmbarkPressed")?.Invoke(screen, new object?[] { null });
    }

    private static async Task AutoPreviewAsync()
    {
        for (int attempt = 0; attempt < 40; attempt++)
        {
            await Task.Delay(500);

            if (!RunManager.Instance.IsInProgress)
            {
                continue;
            }

            var runState = RunManager.Instance.DebugOnlyGetState();
            if (runState == null || runState.Players.Count == 0)
            {
                continue;
            }

            MainFile.Logger.Info("[MultiplayerCard] Auto test run started. Previewing multiplayer rewards.");
            MultiplayerRewardTestService.RunPreviewForAllPlayers(runState, RoomType.Monster, MainFile.AutoTestCount);
            return;
        }

        MainFile.Logger.Error("[MultiplayerCard] Auto test timed out before run start.");
    }
}
