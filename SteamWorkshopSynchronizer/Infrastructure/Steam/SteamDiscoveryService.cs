using System.Globalization;
using System.IO;
using SteamWorkshopSynchronizer.Core.Models;
using SteamWorkshopSynchronizer.Infrastructure.Diagnostics;

namespace SteamWorkshopSynchronizer.Infrastructure.Steam;

public sealed class SteamDiscoveryService
{
    private readonly SteamLocator locator;
    private readonly SteamVdfParser parser;
    private readonly SteamWorkshopMetadataService? metadata;
    private readonly AppLogger? logger;

    public SteamDiscoveryService(
        SteamLocator? locator = null,
        SteamVdfParser? parser = null,
        AppLogger? logger = null,
        SteamWorkshopMetadataService? metadata = null)
    {
        this.locator = locator ?? new SteamLocator();
        this.parser = parser ?? new SteamVdfParser();
        this.logger = logger;
        this.metadata = metadata;
    }

    public async Task<SteamScanSnapshot> ScanAsync(CancellationToken cancellationToken = default)
    {
        var snapshot = await Task.Run(() => Scan(cancellationToken), cancellationToken).ConfigureAwait(false);
        return metadata is null
            ? snapshot
            : await metadata.EnrichSnapshotAsync(snapshot, cancellationToken).ConfigureAwait(false);
    }

    public SteamScanSnapshot Scan(CancellationToken cancellationToken = default)
    {
        logger?.Info("扫描", "开始扫描 Steam 安装目录、游戏和 Workshop");
        var warnings = new List<string>();
        var roots = locator.FindRoots();
        var steamRoot = roots.FirstOrDefault();
        if (steamRoot is null)
        {
            warnings.Add("未找到 Steam 安装目录");
            logger?.Warning("扫描", "未找到 Steam 安装目录");
            return new(null, null, [], [], [], warnings, DateTimeOffset.UtcNow);
        }

        var currentAccount = ReadCurrentAccount(steamRoot, warnings);
        var libraryPaths = ReadLibraryPaths(steamRoot, warnings);
        var libraries = libraryPaths.Select(path => CreateLibraryInfo(path, warnings)).ToArray();
        var games = new Dictionary<int, GameInfo>();
        var workshopItems = new Dictionary<(int AppId, string PublishedFileId), WorkshopItemSnapshot>();

        foreach (var library in libraries.Where(library => library.IsReadable))
        {
            cancellationToken.ThrowIfCancellationRequested();
            ScanLibrary(library.Path, games, workshopItems, warnings, cancellationToken);
        }

        var snapshot = new SteamScanSnapshot(
            steamRoot,
            currentAccount,
            libraries,
            games.Values.OrderBy(game => game.Name, StringComparer.CurrentCultureIgnoreCase).ToArray(),
            workshopItems.Values.OrderBy(item => item.AppId).ThenBy(item => item.Title ?? item.PublishedFileId, StringComparer.CurrentCultureIgnoreCase).ToArray(),
            warnings,
            DateTimeOffset.UtcNow);
        logger?.Info("扫描", $"Steam 扫描完成：{snapshot.Games.Count} 个游戏，{snapshot.WorkshopItems.Count} 个 Workshop 项，{snapshot.Warnings.Count} 条提示");
        foreach (var warning in snapshot.Warnings)
        {
            logger?.Warning("扫描", warning);
        }

        return snapshot;
    }

    private SteamAccountInfo? ReadCurrentAccount(string steamRoot, ICollection<string> warnings)
    {
        var path = Path.Combine(steamRoot, "config", "loginusers.vdf");
        if (!File.Exists(path))
        {
            warnings.Add("未找到 Steam 当前账号信息");
            return null;
        }

        try
        {
            var root = parser.Parse(File.ReadAllText(path));
            var users = root.GetObject("users");
            var current = users?.Values
                .Select(pair => (SteamId: pair.Key, Value: pair.Value.Object))
                .Where(pair => pair.Value is not null && pair.Value.GetString("MostRecent") == "1")
                .Select(pair => new SteamAccountInfo(
                    pair.SteamId,
                    pair.Value!.GetString("AccountName"),
                    pair.Value.GetString("PersonaName")))
                .FirstOrDefault();
            if (current is null)
            {
                warnings.Add("Steam 当前账号信息中没有 MostRecent 用户");
            }

            return current;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or FormatException)
        {
            warnings.Add($"读取 Steam 账号信息失败：{exception.Message}");
            return null;
        }
    }

    private IReadOnlyList<string> ReadLibraryPaths(string steamRoot, ICollection<string> warnings)
    {
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { steamRoot };
        var path = Path.Combine(steamRoot, "steamapps", "libraryfolders.vdf");
        if (!File.Exists(path))
        {
            warnings.Add("未找到 Steam 库配置，仅扫描 Steam 根目录");
            return paths.ToArray();
        }

        try
        {
            var root = parser.Parse(File.ReadAllText(path));
            var folders = root.GetObject("libraryfolders");
            if (folders is null)
            {
                warnings.Add("Steam 库配置缺少 libraryfolders 节点");
                return paths.ToArray();
            }

            foreach (var folder in folders.Values)
            {
                var libraryPath = folder.Value.Object?.GetString("path");
                if (!string.IsNullOrWhiteSpace(libraryPath))
                {
                    paths.Add(Path.GetFullPath(libraryPath));
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or FormatException)
        {
            warnings.Add($"读取 Steam 库配置失败：{exception.Message}");
        }

        return paths.ToArray();
    }

    private static SteamLibraryInfo CreateLibraryInfo(string path, ICollection<string> warnings)
    {
        try
        {
            if (!Directory.Exists(path))
            {
                return new(path, false, "目录不存在");
            }

            _ = Directory.EnumerateFileSystemEntries(path).Take(1).ToArray();
            return new(path, true, null);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            warnings.Add($"无法读取 Steam 库 {path}：{exception.Message}");
            return new(path, false, exception.Message);
        }
    }

    private void ScanLibrary(
        string libraryPath,
        IDictionary<int, GameInfo> games,
        IDictionary<(int AppId, string PublishedFileId), WorkshopItemSnapshot> workshopItems,
        ICollection<string> warnings,
        CancellationToken cancellationToken)
    {
        var steamAppsPath = Path.Combine(libraryPath, "steamapps");
        if (!Directory.Exists(steamAppsPath))
        {
            return;
        }

        foreach (var manifestPath in Directory.EnumerateFiles(steamAppsPath, "appmanifest_*.acf", SearchOption.TopDirectoryOnly))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var root = parser.Parse(File.ReadAllText(manifestPath));
                var appState = root.GetObject("AppState");
                if (appState is null || !int.TryParse(appState.GetString("appid"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var appId))
                {
                    warnings.Add($"跳过无效游戏清单：{manifestPath}");
                    continue;
                }

                var installDir = appState.GetString("installdir");
                var gamePath = string.IsNullOrWhiteSpace(installDir)
                    ? null
                    : Path.Combine(steamAppsPath, "common", installDir);
                var game = new GameInfo(appId, appState.GetString("name") ?? $"App {appId}", gamePath, true);
                games[appId] = game;
                ScanWorkshopItems(steamAppsPath, appId, workshopItems, warnings);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or FormatException)
            {
                warnings.Add($"读取游戏清单失败 {manifestPath}：{exception.Message}");
            }
        }
    }

    private void ScanWorkshopItems(
        string steamAppsPath,
        int appId,
        IDictionary<(int AppId, string PublishedFileId), WorkshopItemSnapshot> workshopItems,
        ICollection<string> warnings)
    {
        var workshopPath = Path.Combine(steamAppsPath, "workshop");
        var appWorkshopPath = Path.Combine(workshopPath, $"appworkshop_{appId}.acf");
        var hasManifest = File.Exists(appWorkshopPath);
        var subscribed = new HashSet<string>(StringComparer.Ordinal);
        var installed = new HashSet<string>(StringComparer.Ordinal);

        if (hasManifest)
        {
            try
            {
                var root = parser.Parse(File.ReadAllText(appWorkshopPath));
                var appWorkshop = root.GetObject("AppWorkshop") ?? root;
                ReadWorkshopIds(appWorkshop.GetObject("WorkshopItemsSubscribed"), subscribed);
                ReadWorkshopIds(appWorkshop.GetObject("WorkshopItemsInstalled"), installed);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or FormatException)
            {
                warnings.Add($"读取 Workshop 状态失败 {appWorkshopPath}：{exception.Message}");
                hasManifest = false;
            }
        }

        var contentPath = Path.Combine(workshopPath, "content", appId.ToString(CultureInfo.InvariantCulture));
        var contentDirectories = Directory.Exists(contentPath)
            ? Directory.EnumerateDirectories(contentPath).Select(Path.GetFileName).Where(id => !string.IsNullOrWhiteSpace(id)).Cast<string>().ToHashSet(StringComparer.Ordinal)
            : new HashSet<string>(StringComparer.Ordinal);
        foreach (var id in contentDirectories)
        {
            installed.Add(id);
        }

        foreach (var id in subscribed.Union(installed))
        {
            var isSubscribed = subscribed.Contains(id);
            var isDownloaded = installed.Contains(id);
            var localPath = Path.Combine(contentPath, id);
            workshopItems[(appId, id)] = new WorkshopItemSnapshot(
                appId,
                id,
                null,
                $"https://steamcommunity.com/sharedfiles/filedetails/?id={id}",
                isSubscribed,
                isDownloaded ? WorkshopDownloadState.Downloaded : hasManifest ? WorkshopDownloadState.Pending : WorkshopDownloadState.Unknown,
                hasManifest && !isSubscribed && isDownloaded,
                Directory.Exists(localPath) ? localPath : null,
                DateTimeOffset.UtcNow);
        }
    }

    private static void ReadWorkshopIds(SteamVdfObject? node, ISet<string> destination)
    {
        if (node is null)
        {
            return;
        }

        foreach (var key in node.Values.Keys)
        {
            if (key.All(char.IsDigit))
            {
                destination.Add(key);
            }
        }
    }
}
