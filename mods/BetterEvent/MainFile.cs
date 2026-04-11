using BetterEvent.Infrastructure;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Modding;

namespace BetterEvent;

[ModInitializer(nameof(Initialize))]
public partial class MainFile : Node
{
    public const string ModId = "BetterEvent";

    public static MegaCrit.Sts2.Core.Logging.Logger Logger { get; } =
        new(ModId, MegaCrit.Sts2.Core.Logging.LogType.Generic);

    public static void Initialize()
    {
        BetterEventConfigService.Initialize();
        BetterEventRegistry.Initialize();

        Harmony harmony = new(ModId);
        harmony.PatchAll();

        if (LocManager.Instance != null)
        {
            LocManager.Instance.SubscribeToLocaleChange(BetterEventLocalization.ApplyCurrentLanguage);
            BetterEventLocalization.ApplyCurrentLanguage();
        }

        Logger.Info("BetterEvent initialized.");
    }
}
