using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Vfx;
using MegaCrit.Sts2.Core.Runs;

namespace WatcherExtension;

[HarmonyPatch(typeof(FlavorSynchronizer), "CreateEndTurnPingDialogueIfNecessary")]
internal static class WatcherEndTurnPingPatch
{
    private static readonly FieldInfo? EndTurnPingDialoguesField =
        AccessTools.Field(typeof(FlavorSynchronizer), "_endTurnPingDialogues");

    private static bool Prefix(FlavorSynchronizer __instance, Player player)
    {
        if (!IsWatcher(player))
        {
            return true;
        }

        if (NRun.Instance == null)
        {
            return false;
        }

        Dictionary<Player, NSpeechBubbleVfx?>? dialogues =
            EndTurnPingDialoguesField?.GetValue(__instance) as Dictionary<Player, NSpeechBubbleVfx?>;

        if (dialogues != null && dialogues.TryGetValue(player, out NSpeechBubbleVfx? existing) && existing != null && Godot.GodotObject.IsInstanceValid(existing))
        {
            existing.QueueFreeSafely();
        }

        string locKey = GetWatcherPingLocKey(player.Creature);
        LocString locString = new("characters", locKey);
        NSpeechBubbleVfx bubble = NSpeechBubbleVfx.Create(locString.GetFormattedText(), player.Creature, 1.5)!;
        NCombatRoom.Instance?.CombatVfxContainer.AddChildSafely(bubble);

        if (dialogues != null)
        {
            dialogues[player] = bubble;
        }

        return false;
    }

    private static bool IsWatcher(Player? player)
    {
        return string.Equals(player?.Character?.Id.Entry, "WATCHER", System.StringComparison.Ordinal);
    }

    private static string GetWatcherPingLocKey(Creature creature)
    {
        if (creature.IsDead)
        {
            return WatcherExtensionLocalization.WatcherDeadPingKey;
        }

        foreach (var power in creature.Powers)
        {
            string? typeName = power.GetType().FullName;
            if (typeName == "WatcherMod.Wrath")
            {
                return WatcherExtensionLocalization.WatcherWrathAlivePingKey;
            }

            if (typeName == "WatcherMod.Calm")
            {
                return WatcherExtensionLocalization.WatcherCalmAlivePingKey;
            }

            if (typeName == "WatcherMod.Divinity")
            {
                return WatcherExtensionLocalization.WatcherDivinityAlivePingKey;
            }
        }

        return WatcherExtensionLocalization.WatcherNeutralAlivePingKey;
    }
}
