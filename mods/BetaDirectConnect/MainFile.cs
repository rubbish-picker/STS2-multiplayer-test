using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Modding;

namespace BetaDirectConnect;

[ModInitializer(nameof(Initialize))]
public partial class MainFile : Node
{
    public const string ModId = "BetaDirectConnect";

    public static MegaCrit.Sts2.Core.Logging.Logger Logger { get; } =
        new(ModId, MegaCrit.Sts2.Core.Logging.LogType.Generic);

    public static void Initialize()
    {
        BetaDirectConnectConfigService.Initialize();

        Harmony harmony = new(ModId);
        harmony.PatchAll();

        Logger.Info("BetaDirectConnect initialized.");
    }
}
