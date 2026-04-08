using System;
using System.Collections.Generic;
using System.Reflection;
using MegaCrit.Sts2.Core.Localization;

namespace WatcherExtension;

public static class WatcherExtensionLocalization
{
    private const string CharactersTableName = "characters";
    private static bool _isRegistered;

    public const string WatcherNeutralAlivePingKey = "WATCHER.banter.alive.endTurnPing.neutral";
    public const string WatcherWrathAlivePingKey = "WATCHER.banter.alive.endTurnPing.wrath";
    public const string WatcherCalmAlivePingKey = "WATCHER.banter.alive.endTurnPing.calm";
    public const string WatcherDivinityAlivePingKey = "WATCHER.banter.alive.endTurnPing.divinity";
    public const string WatcherDeadPingKey = "WATCHER.banter.dead.endTurnPing";

    private static readonly Dictionary<string, string> EnglishTable = new(StringComparer.Ordinal)
    {
        [WatcherNeutralAlivePingKey] = "Tsk.",
        [WatcherWrathAlivePingKey] = "You're killing me!",
        [WatcherCalmAlivePingKey] = "I've got all day.",
        [WatcherDivinityAlivePingKey] = "Begone.",
        [WatcherDeadPingKey] = "..."
    };

    private static readonly Dictionary<string, string> ChineseTable = new(StringComparer.Ordinal)
    {
        [WatcherNeutralAlivePingKey] = "啧。",
        [WatcherWrathAlivePingKey] = "我快死了！",
        [WatcherCalmAlivePingKey] = "我不急。",
        [WatcherDivinityAlivePingKey] = "退下。",
        [WatcherDeadPingKey] = "……"
    };

    public static void ApplyCurrentLanguage()
    {
        try
        {
            if (LocManager.Instance == null)
            {
                return;
            }

            LocManager.Instance.GetTable(CharactersTableName).MergeWith(BuildTableForLanguage(LocManager.Instance.Language));
            MergeEnglishFallbacks();
        }
        catch (Exception ex)
        {
            MainFile.Logger.Error($"Failed to apply WatcherExtension localization: {ex}");
        }
    }

    public static void RegisterAndApply()
    {
        if (LocManager.Instance == null)
        {
            return;
        }

        if (!_isRegistered)
        {
            LocManager.Instance.SubscribeToLocaleChange(ApplyCurrentLanguage);
            _isRegistered = true;
        }

        ApplyCurrentLanguage();
    }

    private static Dictionary<string, string> BuildTableForLanguage(string language)
    {
        if (string.Equals(language, "zhs", StringComparison.OrdinalIgnoreCase))
        {
            return ChineseTable;
        }

        return EnglishTable;
    }

    private static void MergeEnglishFallbacks()
    {
        FieldInfo? field = typeof(LocManager).GetField("_engTables", BindingFlags.Instance | BindingFlags.NonPublic);
        if (field?.GetValue(LocManager.Instance) is not Dictionary<string, LocTable> engTables)
        {
            return;
        }

        if (engTables.TryGetValue(CharactersTableName, out LocTable? table))
        {
            table.MergeWith(EnglishTable);
        }
    }
}
