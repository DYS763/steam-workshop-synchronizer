using SteamWorkshopSynchronizer.Core.Models;

namespace SteamWorkshopSynchronizer.Core.Comparison;

public enum WorkshopDiffKind
{
    Missing,
    Extra,
    UpToDate,
    PendingDownload,
    LocalResidual,
    Unknown
}

public sealed record WorkshopDiffEntry(
    string PublishedFileId,
    string? Title,
    string? WorkshopUrl,
    WorkshopDiffKind Kind,
    bool IsIgnored,
    bool DefaultSelected,
    WorkshopItemSnapshot? LocalItem,
    ManifestItem? TargetItem);

public sealed class WorkshopDiffCalculator
{
    public IReadOnlyList<WorkshopDiffEntry> Calculate(
        int appId,
        IEnumerable<WorkshopItemSnapshot> currentItems,
        ManifestDocument target)
    {
        ArgumentNullException.ThrowIfNull(currentItems);
        ArgumentNullException.ThrowIfNull(target);
        if (target.Game.AppId != appId)
        {
            throw new ArgumentException("目标清单的 AppID 与当前游戏不一致", nameof(target));
        }

        var local = currentItems
            .Where(item => item.AppId == appId)
            .GroupBy(item => item.PublishedFileId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        var targetItems = target.Items
            .GroupBy(item => item.PublishedFileId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        var entries = new List<WorkshopDiffEntry>();

        foreach (var targetItem in targetItems.Values)
        {
            local.TryGetValue(targetItem.PublishedFileId, out var localItem);
            var kind = localItem switch
            {
                null => WorkshopDiffKind.Missing,
                { IsSubscribed: true, DownloadState: WorkshopDownloadState.Downloaded } => WorkshopDiffKind.UpToDate,
                { IsSubscribed: true } => WorkshopDiffKind.PendingDownload,
                _ => WorkshopDiffKind.Missing
            };
            var isActionable = kind == WorkshopDiffKind.Missing;
            entries.Add(new(
                targetItem.PublishedFileId,
                targetItem.Title ?? localItem?.Title,
                targetItem.WorkshopUrl ?? localItem?.WorkshopUrl,
                kind,
                targetItem.Ignored,
                isActionable && !targetItem.Ignored,
                localItem,
                targetItem));
        }

        foreach (var localItem in local.Values)
        {
            if (targetItems.ContainsKey(localItem.PublishedFileId))
            {
                continue;
            }

            var kind = localItem.IsLocalResidual
                ? WorkshopDiffKind.LocalResidual
                : localItem.IsSubscribed
                    ? WorkshopDiffKind.Extra
                    : WorkshopDiffKind.Unknown;
            entries.Add(new(
                localItem.PublishedFileId,
                localItem.Title,
                localItem.WorkshopUrl,
                kind,
                false,
                kind == WorkshopDiffKind.Extra,
                localItem,
                null));
        }

        return entries
            .OrderBy(entry => entry.Kind)
            .ThenBy(entry => entry.Title ?? entry.PublishedFileId, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
    }
}
