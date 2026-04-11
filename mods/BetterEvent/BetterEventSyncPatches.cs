using HarmonyLib;
using MegaCrit.Sts2.Core.Multiplayer.Game.Lobby;
using MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect;
using MegaCrit.Sts2.Core.Nodes.Screens.CustomRun;
using MegaCrit.Sts2.Core.Nodes.Screens.DailyRun;
using MegaCrit.Sts2.Core.Runs;
using System.Collections.Generic;
using System.Reflection;

namespace BetterEvent;

public static class BetterEventSyncPatches
{
    [HarmonyPatch]
    private static class MultiplayerLobbyCtorPatch
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            foreach (ConstructorInfo ctor in AccessTools.GetDeclaredConstructors(typeof(StartRunLobby)))
            {
                yield return ctor;
            }

            foreach (ConstructorInfo ctor in AccessTools.GetDeclaredConstructors(typeof(LoadRunLobby)))
            {
                yield return ctor;
            }

            foreach (ConstructorInfo ctor in AccessTools.GetDeclaredConstructors(typeof(RunLobby)))
            {
                yield return ctor;
            }
        }

        private static void Postfix()
        {
            BetterEventMultiplayerSync.InitializeForRun();
        }
    }

    [HarmonyPatch(typeof(NCharacterSelectScreen), nameof(NCharacterSelectScreen.BeginRun))]
    private static class CharacterSelectBeginRunPatch
    {
        private static void Prefix()
        {
            BetterEventConfigService.Reload();
            BetterEventMultiplayerSync.InitializeForRun();
            BetterEventMultiplayerSync.BroadcastCurrentConfig();
        }
    }

    [HarmonyPatch(typeof(NCustomRunScreen), nameof(NCustomRunScreen.BeginRun))]
    private static class CustomRunBeginRunPatch
    {
        private static void Prefix()
        {
            BetterEventConfigService.Reload();
            BetterEventMultiplayerSync.InitializeForRun();
            BetterEventMultiplayerSync.BroadcastCurrentConfig();
        }
    }

    [HarmonyPatch(typeof(NDailyRunScreen), nameof(NDailyRunScreen.BeginRun))]
    private static class DailyRunBeginRunPatch
    {
        private static void Prefix()
        {
            BetterEventConfigService.Reload();
            BetterEventMultiplayerSync.InitializeForRun();
            BetterEventMultiplayerSync.BroadcastCurrentConfig();
        }
    }

    [HarmonyPatch(typeof(RunManager), nameof(RunManager.SetUpNewSinglePlayer))]
    private static class SetUpNewSinglePlayerPatch
    {
        private static void Prefix()
        {
            BetterEventConfigService.PrepareForNewRun(isMultiplayer: false);
        }
    }

    [HarmonyPatch(typeof(RunManager), nameof(RunManager.SetUpNewMultiPlayer))]
    private static class SetUpNewMultiPlayerPatch
    {
        private static void Prefix()
        {
            BetterEventConfigService.PrepareForNewRun(isMultiplayer: true);
        }
    }

    [HarmonyPatch(typeof(RunManager), nameof(RunManager.Launch))]
    private static class RunManagerLaunchPatch
    {
        private static void Postfix()
        {
            BetterEventConfigService.EnsureRunConfigLoaded();
            BetterEventMultiplayerSync.InitializeForRun();
            BetterEventMultiplayerSync.BroadcastCurrentConfig();
        }
    }

    [HarmonyPatch(typeof(RunManager), nameof(RunManager.CleanUp))]
    private static class RunManagerCleanUpPatch
    {
        private static void Prefix()
        {
            BetterEventConfigService.ClearRunLockInMemory();
            BetterEventConfigService.ClearPersistedRunConfig(isMultiplayer: false);
            BetterEventConfigService.ClearPersistedRunConfig(isMultiplayer: true);
            BetterEventMultiplayerSync.Clear();
        }
    }
}
