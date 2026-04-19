using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.Json;
using MegaCrit.Sts2.Core.Localization;

namespace BalanceTheSpire;

public static class BalanceLocalization
{
    public static void ApplyCurrentLanguage()
    {
        try
        {
            if (LocManager.Instance == null)
            {
                return;
            }

            Dictionary<string, string> activeTable = LoadTableForLanguage(LocManager.Instance.Language);
            if (activeTable.Count > 0)
            {
                LocManager.Instance.GetTable("cards").MergeWith(activeTable);
            }

            MergeEnglishFallbacks(LoadTableForLanguage("eng"));
        }
        catch (Exception ex)
        {
            MainFile.Logger.Error($"Failed to apply BalanceTheSpire localization: {ex}");
        }
    }

    public static void RegisterAndApply()
    {
        if (LocManager.Instance == null)
        {
            return;
        }

        LocManager.Instance.SubscribeToLocaleChange(ApplyCurrentLanguage);
        ApplyCurrentLanguage();
    }

    private static Dictionary<string, string> LoadTableForLanguage(string? language)
    {
        string path = Path.Combine(
            GetModDirectory(),
            MainFile.ModId,
            "localization",
            NormalizeLanguage(language),
            "cards.json");

        if (!File.Exists(path))
        {
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }

        string json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<Dictionary<string, string>>(json)
               ?? new Dictionary<string, string>(StringComparer.Ordinal);
    }

    private static void MergeEnglishFallbacks(Dictionary<string, string> englishTable)
    {
        FieldInfo? field = typeof(LocManager).GetField("_engTables", BindingFlags.Instance | BindingFlags.NonPublic);
        if (field?.GetValue(LocManager.Instance) is not Dictionary<string, LocTable> engTables)
        {
            return;
        }

        if (engTables.TryGetValue("cards", out LocTable? table))
        {
            table.MergeWith(englishTable);
        }
    }

    private static string GetModDirectory()
    {
        string? location = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
        return string.IsNullOrWhiteSpace(location) ? AppContext.BaseDirectory : location;
    }

    private static string NormalizeLanguage(string? language)
    {
        return language?.Trim().ToLowerInvariant() switch
        {
            "zhs" => "zhs",
            _ => "eng",
        };
    }
}
