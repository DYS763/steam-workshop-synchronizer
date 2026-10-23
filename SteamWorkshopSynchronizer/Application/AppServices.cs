using SteamWorkshopSynchronizer.Core.Comparison;
using SteamWorkshopSynchronizer.Core.Serialization;
using SteamWorkshopSynchronizer.Infrastructure.Persistence;
using SteamWorkshopSynchronizer.Infrastructure.Steam;

namespace SteamWorkshopSynchronizer.Application;

public sealed class AppServices
{
    private AppServices(
        SqliteDatabase database,
        ScanStore scans,
        PresetStore presets,
        SteamDiscoveryService discovery,
        ManifestSerializer manifests,
        CompressedTextCodec compressedText,
        WorkshopDiffCalculator diffs)
    {
        Database = database;
        Scans = scans;
        Presets = presets;
        Discovery = discovery;
        Manifests = manifests;
        CompressedText = compressedText;
        Diffs = diffs;
    }

    public SqliteDatabase Database { get; }
    public ScanStore Scans { get; }
    public PresetStore Presets { get; }
    public SteamDiscoveryService Discovery { get; }
    public ManifestSerializer Manifests { get; }
    public CompressedTextCodec CompressedText { get; }
    public WorkshopDiffCalculator Diffs { get; }

    public static AppServices CreateDefault()
    {
        var database = new SqliteDatabase(AppDataPaths.DatabasePath);
        database.Initialize();
        return new(
            database,
            new ScanStore(database),
            new PresetStore(database),
            new SteamDiscoveryService(),
            new ManifestSerializer(),
            new CompressedTextCodec(),
            new WorkshopDiffCalculator());
    }
}
