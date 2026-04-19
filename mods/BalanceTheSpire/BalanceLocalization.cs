using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.Json;
using MegaCrit.Sts2.Core.Localization;

namespace BalanceTheSpire;

public static class BalanceLocalization
{
    private static readonly string[] TableNames = ["cards", "powers"];

    public static void ApplyCurrentLanguage()
    {
        try
        {
            if (LocManager.Instance == null)
            {
                return;
            }

            foreach (string tableName in TableNames)
            {
                ApplyTable(tableName, LocManager.Instance.Language);
            }
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

    private static void ApplyTable(string tableName, string? language)
    {
        Dictionary<string, string> activeTable = LoadTableForLanguage(tableName, language);
        if (activeTable.Count > 0)
        {
            LocManager.Instance!.GetTable(tableName).MergeWith(activeTable);
        }

        MergeEnglishFallbacks(tableName, LoadTableForLanguage(tableName, "eng"));
    }

    private static Dictionary<string, string> LoadTableForLanguage(string tableName, string? language)
    {
        string path = Path.Combine(
            GetModDirectory(),
            MainFile.ModId,
            "localization",
            NormalizeLanguage(language),
            $"{tableName}.json");

        if (!File.Exists(path))
        {
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }

        string json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<Dictionary<string, string>>(json)
               ?? new Dictionary<string, string>(StringComparer.Ordinal);
    }

    private static void MergeEnglishFallbacks(string tableName, Dictionary<string, string> englishTable)
    {
        FieldInfo? field = typeof(LocManager).GetField("_engTables", BindingFlags.Instance | BindingFlags.NonPublic);
        if (field?.GetValue(LocManager.Instance) is not Dictionary<string, LocTable> engTables)
        {
            return;
        }

        if (engTables.TryGetValue(tableName, out LocTable? table))
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
