using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes;

namespace AgentTestApi;

[HarmonyPatch(typeof(NGame), nameof(NGame._Ready))]
public static class AgentTestApiBootstrapPatch
{
    [HarmonyPostfix]
    public static void Postfix(NGame __instance)
    {
        AgentTestApiNode.AttachTo(__instance);
    }
}
