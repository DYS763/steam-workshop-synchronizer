using SteamWorkshopSynchronizer.Core.Comparison;
using SteamWorkshopSynchronizer.Core.Serialization;
using SteamWorkshopSynchronizer.Application.Operations;
using SteamWorkshopSynchronizer.Infrastructure.Persistence;
using SteamWorkshopSynchronizer.Infrastructure.Steam;

namespace SteamWorkshopSynchronizer.Application;

public sealed class AppServices
{
    private AppServices(
        SqliteDatabase database,
        ScanStore scans,
        PresetStore presets,
        OperationStore operations,
        BackupStore backups,
        SteamDiscoveryService discovery,
        ManifestSerializer manifests,
        CompressedTextCodec compressedText,
        WorkshopDiffCalculator diffs,
        SteamProtocolOperator steamOperator,
        WorkshopOperationQueue operationQueue)
    {
        Database = database;
        Scans = scans;
        Presets = presets;
        Operations = operations;
        Backups = backups;
        Discovery = discovery;
        Manifests = manifests;
        CompressedText = compressedText;
        Diffs = diffs;
        SteamOperator = steamOperator;
        OperationQueue = operationQueue;
    }

    public SqliteDatabase Database { get; }
    public ScanStore Scans { get; }
    public PresetStore Presets { get; }
    public OperationStore Operations { get; }
    public BackupStore Backups { get; }
    public SteamDiscoveryService Discovery { get; }
    public ManifestSerializer Manifests { get; }
    public CompressedTextCodec CompressedText { get; }
    public WorkshopDiffCalculator Diffs { get; }
    public SteamProtocolOperator SteamOperator { get; }
    public WorkshopOperationQueue OperationQueue { get; }

    public static AppServices CreateDefault()
    {
        var database = new SqliteDatabase(AppDataPaths.DatabasePath);
        database.Initialize();
        var steamOperator = new SteamProtocolOperator();
        return new(
            database,
            new ScanStore(database),
            new PresetStore(database),
            new OperationStore(database),
            new BackupStore(database),
            new SteamDiscoveryService(),
            new ManifestSerializer(),
            new CompressedTextCodec(),
            new WorkshopDiffCalculator(),
            steamOperator,
            new WorkshopOperationQueue(steamOperator));
    }
}
