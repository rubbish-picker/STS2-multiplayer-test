using HarmonyLib;
using MegaCrit.Sts2.Core.Localization;

namespace BalanceTheSpire;

[HarmonyPatch(typeof(LocManager), nameof(LocManager.Initialize))]
internal static class BalanceLocalizationInitializePatch
{
    private static void Postfix()
    {
        BalanceLocalization.RegisterAndApply();
    }
}

[HarmonyPatch(typeof(LocManager), nameof(LocManager.SetLanguage))]
internal static class BalanceLocalizationSetLanguagePatch
{
    private static void Postfix()
    {
        BalanceLocalization.ApplyCurrentLanguage();
    }
}
