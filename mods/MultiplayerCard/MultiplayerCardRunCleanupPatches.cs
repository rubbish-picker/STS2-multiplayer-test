using HarmonyLib;
using MegaCrit.Sts2.Core.Saves;

namespace MultiplayerCard;

[HarmonyPatch]
public static class MultiplayerCardRunCleanupPatches
{
    [HarmonyPatch(typeof(SaveManager), nameof(SaveManager.DeleteCurrentRun))]
    [HarmonyPostfix]
    private static void AfterDeleteCurrentRun()
    {
        MultiplayerCardConfigService.ClearPersistedRunConfig(isMultiplayer: false);
    }

    [HarmonyPatch(typeof(SaveManager), nameof(SaveManager.DeleteCurrentMultiplayerRun))]
    [HarmonyPostfix]
    private static void AfterDeleteCurrentMultiplayerRun()
    {
        MultiplayerCardConfigService.ClearPersistedRunConfig(isMultiplayer: true);
    }
}
