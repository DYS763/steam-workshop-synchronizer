namespace SteamWorkshopSynchronizer.Core.Models;

public enum BackupRestoreMode
{
    Incremental,
    Overwrite
}

public sealed record BackupRestoreResult(
    BackupRestoreMode Mode,
    string? SafetySnapshotPath,
    int ImportedRows,
    int SkippedRows);
