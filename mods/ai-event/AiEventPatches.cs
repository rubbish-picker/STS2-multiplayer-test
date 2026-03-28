using System.Collections.Generic;
using HarmonyLib;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Multiplayer.Game.Lobby;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Screens.GameOverScreen;
using MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect;
using MegaCrit.Sts2.Core.Nodes.Screens.CustomRun;
using MegaCrit.Sts2.Core.Nodes.Screens.DailyRun;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using System.Reflection;

namespace AiEvent;

public static class AiEventPatches
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
            AiEventMultiplayerSync.InitializeForRun();
        }
    }

    [HarmonyPatch(typeof(NCharacterSelectScreen), nameof(NCharacterSelectScreen.BeginRun))]
    private static class CharacterSelectBeginRunPatch
    {
        private static void Prefix(string seed, List<ActModel> acts)
        {
            AiEventRuntimeService.BeginRun(seed, acts);
        }
    }

    [HarmonyPatch(typeof(NCustomRunScreen), nameof(NCustomRunScreen.BeginRun))]
    private static class CustomRunBeginRunPatch
    {
        private static void Prefix(string seed, List<ActModel> acts)
        {
            AiEventRuntimeService.BeginRun(seed, acts);
        }
    }

    [HarmonyPatch(typeof(NDailyRunScreen), nameof(NDailyRunScreen.BeginRun))]
    private static class DailyRunBeginRunPatch
    {
        private static void Prefix(string seed, List<ActModel> acts)
        {
            AiEventRuntimeService.BeginRun(seed, acts);
        }
    }

    [HarmonyPatch(typeof(Hook), nameof(Hook.ModifyNextEvent))]
    private static class ModifyNextEventPatch
    {
        private static void Postfix(IRunState runState, ref EventModel __result)
        {
            if (runState is RunState concreteRunState)
            {
                __result = AiEventRuntimeService.SelectNextEvent(concreteRunState, __result);
            }
        }
    }

    [HarmonyPatch(typeof(MegaCrit.Sts2.Core.Odds.UnknownMapPointOdds), nameof(MegaCrit.Sts2.Core.Odds.UnknownMapPointOdds.Roll))]
    private static class UnknownMapPointRollPatch
    {
        private static void Postfix(ref RoomType __result)
        {
            if (AiEventRuntimeService.ShouldForceUnknownNodesToEvents())
            {
                __result = RoomType.Event;
            }
        }
    }

    [HarmonyPatch(typeof(NGame), nameof(NGame.LoadRun))]
    private static class LoadRunPatch
    {
        private static void Prefix(RunState runState)
        {
            AiEventRuntimeService.ResumeRun(runState);
        }
    }

    [HarmonyPatch(typeof(RunManager), nameof(RunManager.Launch))]
    private static class RunManagerLaunchPatch
    {
        private static void Postfix()
        {
            AiEventMultiplayerSync.InitializeForRun();
            AiEventMultiplayerSync.BroadcastConfig();
        }
    }

    [HarmonyPatch(typeof(NMainMenu), nameof(NMainMenu._Ready))]
    private static class MainMenuReadyPatch
    {
        private static void Prefix(NMainMenu __instance)
        {
            AiEventMainMenuIntegration.InstallButton(__instance);
        }

        private static void Postfix(NMainMenu __instance)
        {
            AiEventMainMenuIntegration.FinalizeMenu(__instance);
        }
    }

    [HarmonyPatch(typeof(RunManager), nameof(RunManager.Abandon))]
    private static class RunManagerAbandonPatch
    {
        private static void Prefix()
        {
            AiEventRuntimeService.StopActiveRun("the player abandoned the in-progress run", finalizeDynamicEntries: true);
            AiEventMultiplayerSync.Clear();
        }
    }

    [HarmonyPatch(typeof(NMainMenu), nameof(NMainMenu.AbandonRun))]
    private static class MainMenuAbandonRunPatch
    {
        private static void Prefix()
        {
            AiEventRuntimeService.StopActiveRun("the player abandoned the run from main menu", finalizeDynamicEntries: true);
            AiEventMultiplayerSync.Clear();
        }
    }

    [HarmonyPatch(typeof(MegaCrit.Sts2.Core.Nodes.NRun), nameof(MegaCrit.Sts2.Core.Nodes.NRun.ShowGameOverScreen))]
    private static class ShowGameOverScreenPatch
    {
        private static void Prefix()
        {
            AiEventRuntimeService.StopActiveRun("the current run reached game over", finalizeDynamicEntries: true);
            AiEventMultiplayerSync.Clear();
        }
    }

    [HarmonyPatch(typeof(RunManager), nameof(RunManager.CleanUp))]
    private static class RunManagerCleanUpPatch
    {
        private static void Prefix()
        {
            AiEventMultiplayerSync.Clear();
        }
    }
}
