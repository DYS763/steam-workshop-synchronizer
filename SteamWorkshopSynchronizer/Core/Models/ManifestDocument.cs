namespace SteamWorkshopSynchronizer.Core.Models;

public sealed record ManifestDocument(
    string Format,
    int FormatVersion,
    ManifestGame Game,
    ManifestPreset Preset,
    IReadOnlyList<ManifestItem> Items,
    DateTimeOffset ExportedAtUtc,
    string Source);

public sealed record ManifestGame(int AppId, string Name);

public sealed record ManifestPreset(string Name, string? Note);

public sealed record ManifestItem(
    string PublishedFileId,
    string? Title,
    string? WorkshopUrl,
    bool Ignored,
    string? Source);
