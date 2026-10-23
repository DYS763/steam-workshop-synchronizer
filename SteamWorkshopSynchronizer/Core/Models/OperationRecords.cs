namespace SteamWorkshopSynchronizer.Core.Models;

public sealed record OperationBatchSummary(
    string Id,
    string Kind,
    int? AppId,
    string? PresetId,
    string? PresetVersionId,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? FinishedAt,
    int TotalItems,
    int CompletedItems,
    int FailedItems);
