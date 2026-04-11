using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Extensions;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Random;
using MegaCrit.Sts2.Core.Rooms;

namespace BetterEvent.Infrastructure;

public static class BetterEventPatches
{
    [HarmonyPatch(typeof(ActModel), nameof(ActModel.GenerateRooms))]
    private static class ActModelGenerateRoomsPatch
    {
        private static readonly FieldInfo? RoomsField = AccessTools.Field(typeof(ActModel), "_rooms");

        private static void Postfix(ActModel __instance, Rng rng)
        {
            if (RoomsField?.GetValue(__instance) is not RoomSet roomSet)
            {
                MainFile.Logger.Warn($"BetterEvent could not access room set for act {__instance.Id.Entry}.");
                return;
            }

            IReadOnlyList<IBetterEventRegistration> registrations = BetterEventRegistry.GetRegistrationsForAct(__instance);
            if (registrations.Count == 0)
            {
                if (BetterEventConfigService.IsDebugMode())
                {
                    MainFile.Logger.Warn($"BetterEvent debug mode is enabled, but act {__instance.Id.Entry} has no registered BetterEvent events.");
                }

                return;
            }

            if (BetterEventConfigService.IsDebugMode())
            {
                roomSet.events.Clear();
            }

            int addedCount = 0;
            foreach (IBetterEventRegistration registration in registrations)
            {
                EventModel model = BetterEventRegistry.GetCanonicalEventModel(registration.EventType);
                if (roomSet.events.Any(existing => existing.Id == model.Id))
                {
                    continue;
                }

                roomSet.events.Add(model);
                addedCount++;
            }

            if (addedCount == 0)
            {
                return;
            }

            roomSet.events.UnstableShuffle(rng);
            string mode = BetterEventConfigService.IsDebugMode() ? "debug" : "vanilla";
            MainFile.Logger.Info($"BetterEvent injected {addedCount} event(s) into act {__instance.Id.Entry} using {mode} mode.");
        }
    }
}
