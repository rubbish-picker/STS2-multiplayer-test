using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using MegaCrit.Sts2.Core.Models;

namespace AiEvent;

[HarmonyPatch(typeof(ModelDb), nameof(ModelDb.AllSharedEvents), MethodType.Getter)]
public static class ModelDbPatches
{
    [HarmonyPostfix]
    private static IEnumerable<MegaCrit.Sts2.Core.Models.EventModel> AddAiEvent(
        IEnumerable<MegaCrit.Sts2.Core.Models.EventModel> __result)
    {
        return __result.Concat(new[] { ModelDb.Event<AiGeneratedEvent>() }).Distinct();
    }
}
