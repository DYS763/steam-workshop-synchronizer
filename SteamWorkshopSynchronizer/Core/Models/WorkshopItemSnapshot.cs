namespace SteamWorkshopSynchronizer.Core.Models;

public enum WorkshopDownloadState
{
    Downloaded,
    Pending,
    Unknown
}

public sealed record WorkshopItemSnapshot(
    int AppId,
    string PublishedFileId,
    string? Title,
    string? WorkshopUrl,
    bool IsSubscribed,
    WorkshopDownloadState DownloadState,
    bool IsLocalResidual,
    string? LocalPath,
    DateTimeOffset LastSeenAt);
