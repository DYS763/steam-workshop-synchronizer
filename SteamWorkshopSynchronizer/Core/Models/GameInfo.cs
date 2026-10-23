namespace SteamWorkshopSynchronizer.Core.Models;

public sealed record GameInfo(
    int AppId,
    string Name,
    string? InstallPath,
    bool IsInstalled);
