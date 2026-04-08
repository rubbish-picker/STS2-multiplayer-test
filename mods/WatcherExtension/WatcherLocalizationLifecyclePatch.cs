using HarmonyLib;
using MegaCrit.Sts2.Core.Localization;

namespace WatcherExtension;

[HarmonyPatch(typeof(LocManager), nameof(LocManager.Initialize))]
internal static class WatcherLocalizationInitializePatch
{
    private static void Postfix()
    {
        WatcherExtensionLocalization.RegisterAndApply();
    }
}

[HarmonyPatch(typeof(LocManager), nameof(LocManager.SetLanguage))]
internal static class WatcherLocalizationSetLanguagePatch
{
    private static void Postfix()
    {
        WatcherExtensionLocalization.ApplyCurrentLanguage();
    }
}
