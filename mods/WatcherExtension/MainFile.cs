using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Modding;

namespace WatcherExtension;

[ModInitializer(nameof(Initialize))]
public partial class MainFile : Node
{
    public const string ModId = "WatcherExtension";

    public static MegaCrit.Sts2.Core.Logging.Logger Logger { get; } =
        new(ModId, MegaCrit.Sts2.Core.Logging.LogType.Generic);

    public static void Initialize()
    {
        Harmony harmony = new(ModId);
        harmony.PatchAll();

        Logger.Info("WatcherExtension initialized.");
    }
}
