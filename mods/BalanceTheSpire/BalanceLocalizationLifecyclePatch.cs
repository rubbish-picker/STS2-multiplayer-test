using HarmonyLib;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;

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

[HarmonyPatch(typeof(NMainMenu), nameof(NMainMenu._Ready))]
internal static class BalanceMainMenuReadyPatch
{
    private static void Postfix()
    {
        MainFile.RunStartupDiagnosticsOnce();
    }
}
