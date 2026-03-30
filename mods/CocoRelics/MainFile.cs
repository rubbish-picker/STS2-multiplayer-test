using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Modding;

namespace CocoRelics;

[ModInitializer(nameof(Initialize))]
public partial class MainFile : Node
{
    public const string ModId = "CocoRelics";

    public static MegaCrit.Sts2.Core.Logging.Logger Logger { get; } = new(ModId, MegaCrit.Sts2.Core.Logging.LogType.Generic);

    public static void Initialize()
    {
        CocoRelicsConfigService.Initialize();
        Harmony harmony = new(ModId);
        harmony.PatchAll();
        PatchWatcherCompatibility(harmony);
    }

    private static void PatchWatcherCompatibility(Harmony harmony)
    {
        var watcherPatchType = AccessTools.TypeByName("WatcherMod.WatcherRestSiteCharacterPatch");
        var watcherPostfix = watcherPatchType == null ? null : AccessTools.Method(watcherPatchType, "Postfix");
        if (watcherPostfix == null)
        {
            return;
        }

        var prefix = new HarmonyMethod(typeof(CocoRelicsPatches), nameof(CocoRelicsPatches.SkipWatcherRestSiteCharacterPatchDuringPreview));
        harmony.Patch(watcherPostfix, prefix: prefix);
    }
}
