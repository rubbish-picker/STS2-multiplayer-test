using System.Collections.Generic;
using HarmonyLib;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect;
using MegaCrit.Sts2.Core.Nodes.Screens.CustomRun;
using MegaCrit.Sts2.Core.Nodes.Screens.DailyRun;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;

namespace AiEvent;

public static class AiEventPatches
{
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
}
