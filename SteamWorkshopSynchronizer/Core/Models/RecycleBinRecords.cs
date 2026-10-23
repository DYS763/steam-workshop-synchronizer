namespace SteamWorkshopSynchronizer.Core.Models;

public sealed record RecycleBinItem(
    string Id,
    int AppId,
    string PublishedFileId,
    string? Title,
    string? WorkshopUrl,
    string? SourcePresetId,
    string? SourcePresetVersionId,
    DateTimeOffset RemovedAt,
    string RestoreState,
    string? LastError);
