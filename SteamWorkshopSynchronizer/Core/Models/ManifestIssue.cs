namespace SteamWorkshopSynchronizer.Core.Models;

public enum ManifestIssueSeverity
{
    Warning,
    Error
}

public sealed record ManifestIssue(
    ManifestIssueSeverity Severity,
    string Message,
    int? ItemIndex = null);

public sealed record ManifestParseResult(
    ManifestDocument? Document,
    IReadOnlyList<ManifestIssue> Issues,
    bool IsFutureVersion)
{
    public bool HasErrors => Issues.Any(issue => issue.Severity == ManifestIssueSeverity.Error);
}
