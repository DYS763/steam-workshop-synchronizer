namespace SteamWorkshopSynchronizer.Core.Models;

public sealed record GameInfo(
    int AppId,
    string Name,
    string? InstallPath,
    bool IsInstalled,
    int WorkshopItemCount = 0)
{
    public string DisplayName => $"[{WorkshopItemCount}] {Name}";

    public bool HasMods => WorkshopItemCount > 0;
}
