using System;
using System.IO;
using Godot;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;

namespace AiEvent;

public static class AiEventStorage
{
    private const string DataDirectoryName = "ai-event";

    public static string GetProfileDataDirectory()
    {
        return ToFileSystemPath(SaveManager.Instance.GetProfileScopedPath(DataDirectoryName));
    }

    public static string GetActiveCachePath()
    {
        return Path.Combine(GetProfileDataDirectory(), "ai-event.generated.cache.json");
    }

    public static string GetPoolPath()
    {
        return Path.Combine(GetProfileDataDirectory(), "ai-event.event_pool.json");
    }

    public static string GetPoolDatabasePath()
    {
        return Path.Combine(GetProfileDataDirectory(), "ai-event.event_pool_db");
    }

    public static string GetHistoryDirectoryPath()
    {
        return Path.Combine(GetProfileDataDirectory(), "generated_history");
    }

    public static string GetSessionStatePath(bool isMultiplayer)
    {
        string fileName = isMultiplayer
            ? "ai-event.current_run_mp.session.json"
            : "ai-event.current_run.session.json";

        return ToFileSystemPath(SaveManager.Instance.GetProfileScopedPath(Path.Combine(UserDataPathProvider.SavesDir, fileName)));
    }

    public static string GetCurrentSessionStatePath()
    {
        bool isMultiplayer = RunManager.Instance.NetService?.Type.IsMultiplayer() ?? false;
        return GetSessionStatePath(isMultiplayer);
    }

    public static string[] GetAllSessionStatePaths()
    {
        return new[]
        {
            GetSessionStatePath(false),
            GetSessionStatePath(true),
        };
    }

    public static void EnsureDirectoryForFile(string path)
    {
        string? directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }

    public static void EnsureDataDirectories()
    {
        Directory.CreateDirectory(GetProfileDataDirectory());
        Directory.CreateDirectory(GetHistoryDirectoryPath());
    }

    private static string ToFileSystemPath(string path)
    {
        return path.StartsWith("user://", StringComparison.OrdinalIgnoreCase)
            ? ProjectSettings.GlobalizePath(path)
            : path;
    }

    public static void MigrateLegacyFiles(string modDirectory)
    {
        TryMoveFile(Path.Combine(modDirectory, "ai-event.generated.cache.json"), GetActiveCachePath());
        TryMoveFile(Path.Combine(modDirectory, "ai-event.event_pool.json"), GetPoolPath());
        TryMoveFile(Path.Combine(modDirectory, "ai-event.run_session.json"), GetSessionStatePath(false));
        TryMoveFile(Path.Combine(modDirectory, "ai-event.run_session_mp.json"), GetSessionStatePath(true));
        TryMoveFile(Path.Combine(GetProfileDataDirectory(), "ai-event.run_session.json"), GetSessionStatePath(false));
        TryMoveFile(Path.Combine(GetProfileDataDirectory(), "ai-event.run_session_mp.json"), GetSessionStatePath(true));
        TryMoveDirectory(Path.Combine(modDirectory, "generated_history"), GetHistoryDirectoryPath());
    }

    private static void TryMoveFile(string sourcePath, string destinationPath)
    {
        try
        {
            if (!File.Exists(sourcePath))
            {
                return;
            }

            EnsureDirectoryForFile(destinationPath);
            if (File.Exists(destinationPath))
            {
                File.Delete(sourcePath);
                return;
            }

            File.Move(sourcePath, destinationPath);
        }
        catch (Exception ex)
        {
            MainFile.Logger.Error($"[ai-event] failed to migrate legacy file {sourcePath} -> {destinationPath}: {ex}");
        }
    }

    private static void TryMoveDirectory(string sourcePath, string destinationPath)
    {
        try
        {
            if (!Directory.Exists(sourcePath))
            {
                return;
            }

            Directory.CreateDirectory(destinationPath);
            foreach (string file in Directory.GetFiles(sourcePath, "*", SearchOption.AllDirectories))
            {
                string relativePath = Path.GetRelativePath(sourcePath, file);
                string destinationFile = Path.Combine(destinationPath, relativePath);
                EnsureDirectoryForFile(destinationFile);
                if (File.Exists(destinationFile))
                {
                    File.Delete(file);
                    continue;
                }

                File.Move(file, destinationFile);
            }

            Directory.Delete(sourcePath, recursive: true);
        }
        catch (Exception ex)
        {
            MainFile.Logger.Error($"[ai-event] failed to migrate legacy directory {sourcePath} -> {destinationPath}: {ex}");
        }
    }
}
