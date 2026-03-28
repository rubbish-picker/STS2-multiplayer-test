using System;
using System.Reflection;
using HarmonyLib;

namespace MultiplayerCard;

[HarmonyPatch]
public static class MultiplayerOverlayStabilityPatches
{
    private static MethodBase? TargetMethod()
    {
        return AccessTools.Method("MegaCrit.Sts2.Core.Multiplayer.Game.PeerInput.ScreenStateTracker:OnOverlayStackChanged");
    }

    [HarmonyFinalizer]
    private static Exception? SwallowDuplicateCompletedSignalConnect(Exception? __exception)
    {
        if (__exception == null)
        {
            return null;
        }

        if (__exception.Message.Contains("Signal 'Completed' is already connected", StringComparison.OrdinalIgnoreCase))
        {
            MainFile.Logger.Warn("[MultiplayerCard] swallowed duplicate overlay Completed signal connection while changing overlay stack.");
            return null;
        }

        return __exception;
    }
}
