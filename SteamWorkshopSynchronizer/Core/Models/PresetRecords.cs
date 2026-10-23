namespace SteamWorkshopSynchronizer.Core.Models;

public sealed record PresetSummary(
    string Id,
    int AppId,
    string Name,
    string? Note,
    string SourceType,
    string? CurrentVersionId,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record PresetItemRecord(
    int AppId,
    string PublishedFileId,
    string? Title,
    string? WorkshopUrl,
    bool IsIgnored,
    string? Source);

public sealed record PresetVersionSnapshot(
    string Id,
    string PresetId,
    int VersionNumber,
    DateTimeOffset CreatedAt,
    string CreatedReason,
    string? SourceBatchId,
    IReadOnlyList<PresetItemRecord> Items);
