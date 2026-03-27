using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using MegaCrit.Sts2.Core.Localization;

namespace AiEvent;

public static class AiEventLocalization
{
    public static void ApplyCurrentLanguage()
    {
        try
        {
            if (LocManager.Instance == null)
            {
                return;
            }

            LocManager.Instance.GetTable(AiEventRegistry.EventsTableName).MergeWith(BuildTableForLanguage(LocManager.Instance.Language));
            MergeEnglishFallbacks(BuildTableForLanguage("eng"));
        }
        catch (Exception ex)
        {
            MainFile.Logger.Error($"Failed to apply ai-event localization: {ex}");
        }
    }

    private static Dictionary<string, string> BuildTableForLanguage(string language)
    {
        Dictionary<string, string> table = new();
        bool useChinese = string.Equals(language, "zhs", StringComparison.OrdinalIgnoreCase);

        foreach ((AiEventSlot _, AiGeneratedEventPayload payload) in AiEventRepository.GetAll())
        {
            AiLocalizedEventText text = useChinese ? payload.Zhs : payload.Eng;
            string key = payload.EventKey;

            table[$"{key}.title"] = text.Title;
            table[$"{key}.pages.INITIAL.description"] = text.InitialDescription;

            foreach (AiLocalizedOptionText option in text.Options.Where(o => !string.IsNullOrWhiteSpace(o.Key)))
            {
                table[$"{key}.pages.INITIAL.options.{option.Key}.title"] = option.Title;
                table[$"{key}.pages.INITIAL.options.{option.Key}.description"] = option.Description;
                table[$"{key}.pages.{option.Key}_RESULT.description"] = option.ResultDescription;
            }
        }

        return table;
    }

    private static void MergeEnglishFallbacks(Dictionary<string, string> englishTable)
    {
        FieldInfo? field = typeof(LocManager).GetField("_engTables", BindingFlags.Instance | BindingFlags.NonPublic);
        if (field?.GetValue(LocManager.Instance) is not Dictionary<string, LocTable> engTables)
        {
            return;
        }

        if (engTables.TryGetValue(AiEventRegistry.EventsTableName, out LocTable? table))
        {
            table.MergeWith(englishTable);
        }
    }
}
