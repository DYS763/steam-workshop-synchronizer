using System.IO;

namespace SteamWorkshopSynchronizer.Application;

public static class AppDataPaths
{
    public static string RootDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "SteamWorkshopSynchronizer");

    public static string DatabasePath => Path.Combine(RootDirectory, "data.db");

    public static string LogDirectory => Path.Combine(RootDirectory, "logs");
}
