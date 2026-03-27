using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Modding;

namespace AiEvent;

[ModInitializer(nameof(Initialize))]
public partial class MainFile : Node
{
    public const string ModId = "ai-event";

    public static MegaCrit.Sts2.Core.Logging.Logger Logger { get; } =
        new(ModId, MegaCrit.Sts2.Core.Logging.LogType.Generic);

    public static void Initialize()
    {
        Harmony harmony = new(ModId);
        harmony.PatchAll();

        AiEventConfigService.Initialize();
        AiEventGenerationService.Initialize();
        if (LocManager.Instance != null)
        {
            LocManager.Instance.SubscribeToLocaleChange(AiEventLocalization.ApplyCurrentLanguage);
            AiEventLocalization.ApplyCurrentLanguage();
        }

        Logger.Info("ai-event initialized.");
    }
}
