using SteamWorkshopSynchronizer.Core.Models;

namespace SteamWorkshopSynchronizer.Infrastructure.Steam;

public sealed record SteamAccountInfo(string SteamId64, string? AccountName, string? DisplayName);

public sealed record SteamLibraryInfo(string Path, bool IsReadable, string? Error);

public sealed record SteamScanSnapshot(
    string? SteamRoot,
    SteamAccountInfo? CurrentAccount,
    IReadOnlyList<SteamLibraryInfo> Libraries,
    IReadOnlyList<GameInfo> Games,
    IReadOnlyList<WorkshopItemSnapshot> WorkshopItems,
    IReadOnlyList<string> Warnings,
    DateTimeOffset ScannedAtUtc);
