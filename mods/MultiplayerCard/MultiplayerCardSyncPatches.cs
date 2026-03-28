using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect;
using MegaCrit.Sts2.Core.Nodes.Screens.CustomRun;
using MegaCrit.Sts2.Core.Nodes.Screens.DailyRun;
using MegaCrit.Sts2.Core.Runs;

namespace MultiplayerCard;

public static class MultiplayerCardSyncPatches
{
    [HarmonyPatch(typeof(NCharacterSelectScreen), nameof(NCharacterSelectScreen.BeginRun))]
    private static class CharacterSelectBeginRunPatch
    {
        private static void Prefix()
        {
            MultiplayerCardConfigService.Reload();
            MultiplayerCardMultiplayerSync.InitializeForRun();
            MultiplayerCardMultiplayerSync.BroadcastCurrentConfig();
        }
    }

    [HarmonyPatch(typeof(NCustomRunScreen), nameof(NCustomRunScreen.BeginRun))]
    private static class CustomRunBeginRunPatch
    {
        private static void Prefix()
        {
            MultiplayerCardConfigService.Reload();
            MultiplayerCardMultiplayerSync.InitializeForRun();
            MultiplayerCardMultiplayerSync.BroadcastCurrentConfig();
        }
    }

    [HarmonyPatch(typeof(NDailyRunScreen), nameof(NDailyRunScreen.BeginRun))]
    private static class DailyRunBeginRunPatch
    {
        private static void Prefix()
        {
            MultiplayerCardConfigService.Reload();
            MultiplayerCardMultiplayerSync.InitializeForRun();
            MultiplayerCardMultiplayerSync.BroadcastCurrentConfig();
        }
    }

    [HarmonyPatch(typeof(RunManager), nameof(RunManager.Launch))]
    private static class RunManagerLaunchPatch
    {
        private static void Postfix()
        {
            MultiplayerCardMultiplayerSync.InitializeForRun();
            MultiplayerCardMultiplayerSync.BroadcastCurrentConfig();
        }
    }

    [HarmonyPatch(typeof(RunManager), nameof(RunManager.CleanUp))]
    private static class RunManagerCleanUpPatch
    {
        private static void Prefix()
        {
            MultiplayerCardMultiplayerSync.Clear();
        }
    }
}
