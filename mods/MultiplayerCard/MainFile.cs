using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Modding;

namespace MultiplayerCard;

[ModInitializer(nameof(Initialize))]
public partial class MainFile : Node
{
    public const string ModId = "MultiplayerCard"; //At the moment, this is used only for the Logger and harmony names.

    public static MegaCrit.Sts2.Core.Logging.Logger Logger { get; } = new(ModId, MegaCrit.Sts2.Core.Logging.LogType.Generic);
    public static bool AutoTestEnabled { get; private set; }
    public static int AutoTestCount { get; private set; } = 5;

    public static void Initialize()
    {
        AutoTestEnabled = CommandLineHelper.HasArg("mpcardautotest");
        if (CommandLineHelper.TryGetValue("mpcardautotestcount", out string? rawCount) && int.TryParse(rawCount, out int parsedCount))
        {
            AutoTestCount = Math.Clamp(parsedCount, 1, 20);
        }

        MultiplayerCardConfigService.Initialize();

        Harmony harmony = new(ModId);

        harmony.PatchAll();

        if (AutoTestEnabled)
        {
            Logger.Info($"[MultiplayerCard] Auto test enabled. Preview count: {AutoTestCount}");
        }
    }
}
