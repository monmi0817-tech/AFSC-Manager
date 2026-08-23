using System.IO;

namespace AfterSchoolManager.Utilities;

public static class AppPaths
{
    public static string RootDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AfterSchoolIntegratedManager");

    public static string DataDirectory { get; } = Path.Combine(RootDirectory, "data");
    public static string RecoveryDirectory { get; } = Path.Combine(RootDirectory, "recovery");
    public static string DownloadDirectory { get; } = Path.Combine(RootDirectory, "updates");
    public static string DatabasePath { get; } = Path.Combine(DataDirectory, "afterschool.db");
    public static string SettingsPath { get; } = Path.Combine(RootDirectory, "settings.json");
    public static string LogPath { get; } = Path.Combine(RootDirectory, "app.log");

    public static void EnsureDirectories()
    {
        Directory.CreateDirectory(RootDirectory);
        Directory.CreateDirectory(DataDirectory);
        Directory.CreateDirectory(RecoveryDirectory);
        Directory.CreateDirectory(DownloadDirectory);
    }
}
