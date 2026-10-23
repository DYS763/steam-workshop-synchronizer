using Microsoft.Win32;
using System.IO;

namespace SteamWorkshopSynchronizer.Infrastructure.Steam;

public sealed class SteamLocator
{
    private static readonly string[] RegistryKeys =
    [
        @"HKCU\Software\Valve\Steam",
        @"HKLM\SOFTWARE\WOW6432Node\Valve\Steam",
        @"HKLM\SOFTWARE\Valve\Steam"
    ];

    public IReadOnlyList<string> FindRoots()
    {
        var candidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var keyPath in RegistryKeys)
        {
            var separator = keyPath.IndexOf('\\');
            var hiveName = keyPath[..separator];
            var subKeyPath = keyPath[(separator + 1)..];
            var hive = hiveName switch
            {
                "HKCU" => RegistryHive.CurrentUser,
                "HKLM" => RegistryHive.LocalMachine,
                _ => RegistryHive.CurrentUser
            };

            try
            {
                using var baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
                using var key = baseKey.OpenSubKey(subKeyPath);
                AddIfDirectory(candidates, key?.GetValue("SteamPath") as string);
                AddIfDirectory(candidates, key?.GetValue("InstallPath") as string);
            }
            catch (UnauthorizedAccessException)
            {
                // A registry read failure should not block the other discovery paths.
            }
        }

        AddIfDirectory(candidates, Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86) is { Length: > 0 } x86
            ? Path.Combine(x86, "Steam")
            : null);
        AddIfDirectory(candidates, Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles) is { Length: > 0 } x64
            ? Path.Combine(x64, "Steam")
            : null);

        return candidates.OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static void AddIfDirectory(ISet<string> paths, string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        try
        {
            var fullPath = Path.GetFullPath(path.Trim());
            if (Directory.Exists(fullPath))
            {
                paths.Add(fullPath);
            }
        }
        catch (ArgumentException)
        {
            // Ignore malformed registry values.
        }
    }
}
