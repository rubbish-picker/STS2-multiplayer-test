using Godot;
using HarmonyLib;
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Modding;

namespace AiEvent;

[ModInitializer(nameof(Initialize))]
public partial class MainFile : Node
{
    public const string ModId = "ai-event";
    private static bool _dependencyResolverInstalled;
    private static bool _sqliteDependenciesPreloaded;

    public static MegaCrit.Sts2.Core.Logging.Logger Logger { get; } =
        new(ModId, MegaCrit.Sts2.Core.Logging.LogType.Generic);

    public static void Initialize()
    {
        InstallDependencyResolver();
        PreloadSqliteDependencies();

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

    private static void InstallDependencyResolver()
    {
        if (_dependencyResolverInstalled)
        {
            return;
        }

        AssemblyLoadContext.Default.Resolving += ResolveFromModDirectory;
        _dependencyResolverInstalled = true;
    }

    private static Assembly? ResolveFromModDirectory(AssemblyLoadContext context, AssemblyName assemblyName)
    {
        try
        {
            string modDirectory = GetModDirectory();
            string candidatePath = Path.Combine(modDirectory, $"{assemblyName.Name}.dll");
            if (File.Exists(candidatePath))
            {
                return context.LoadFromAssemblyPath(candidatePath);
            }
        }
        catch (Exception ex)
        {
            Logger.Error($"[ai-event] failed to resolve dependency {assemblyName.FullName} from mod directory: {ex}");
        }

        return null;
    }

    private static void PreloadSqliteDependencies()
    {
        if (_sqliteDependenciesPreloaded)
        {
            return;
        }

        string[] preloadOrder =
        {
            "SQLitePCLRaw.core",
            "SQLitePCLRaw.provider.e_sqlite3",
            "SQLitePCLRaw.batteries_v2",
            "Microsoft.Data.Sqlite",
        };

        foreach (string assemblyName in preloadOrder)
        {
            TryLoadDependencyAssembly(assemblyName);
        }

        TryInitializeSqliteBatteries();
        _sqliteDependenciesPreloaded = true;
    }

    private static void TryLoadDependencyAssembly(string assemblyName)
    {
        try
        {
            if (AppDomain.CurrentDomain.GetAssemblies().Any(assembly =>
                    string.Equals(assembly.GetName().Name, assemblyName, StringComparison.OrdinalIgnoreCase)))
            {
                return;
            }

            string path = Path.Combine(GetModDirectory(), $"{assemblyName}.dll");
            if (File.Exists(path))
            {
                AssemblyLoadContext.Default.LoadFromAssemblyPath(path);
            }
        }
        catch (Exception ex)
        {
            Logger.Error($"[ai-event] failed to preload dependency {assemblyName}: {ex}");
        }
    }

    private static void TryInitializeSqliteBatteries()
    {
        try
        {
            Type? batteriesType = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(assembly => string.Equals(assembly.GetName().Name, "SQLitePCLRaw.batteries_v2", StringComparison.OrdinalIgnoreCase))
                ?.GetType("SQLitePCL.Batteries_V2");
            MethodInfo? initMethod = batteriesType?.GetMethod("Init", BindingFlags.Public | BindingFlags.Static);
            initMethod?.Invoke(null, null);
        }
        catch (Exception ex)
        {
            Logger.Error($"[ai-event] failed to initialize SQLite batteries: {ex}");
        }
    }

    private static string GetModDirectory()
    {
        string? location = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
        return string.IsNullOrWhiteSpace(location) ? AppContext.BaseDirectory : location;
    }
}
