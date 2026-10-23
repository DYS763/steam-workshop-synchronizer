using SteamWorkshopSynchronizer.Core.Comparison;
using SteamWorkshopSynchronizer.Core.Serialization;
using SteamWorkshopSynchronizer.Application.Operations;
using SteamWorkshopSynchronizer.Infrastructure.Diagnostics;
using SteamWorkshopSynchronizer.Infrastructure.Persistence;
using SteamWorkshopSynchronizer.Infrastructure.Steam;

namespace SteamWorkshopSynchronizer.Application;

public sealed class AppServices
{
    private AppServices(
        SqliteDatabase database,
        AppSettingsStore settings,
        AppLogger logger,
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
        Settings = settings;
        Logger = logger;
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
    public AppSettingsStore Settings { get; }
    public AppLogger Logger { get; }
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
        var settings = new AppSettingsStore(database);
        var logger = new AppLogger(AppDataPaths.LogDirectory, settings.GetBool(AppSettingsStore.DeveloperModeKey));
        logger.Info("启动", $"应用服务初始化开始，数据库：{database.DatabasePath}");
        var steamOperator = new SteamProtocolOperator(logger);
        return new(
            database,
            settings,
            logger,
            new ScanStore(database),
            new PresetStore(database),
            new OperationStore(database),
            new BackupStore(database),
            new SteamDiscoveryService(logger: logger),
            new ManifestSerializer(),
            new CompressedTextCodec(),
            new WorkshopDiffCalculator(),
            steamOperator,
            new WorkshopOperationQueue(steamOperator, logger));
    }
}
