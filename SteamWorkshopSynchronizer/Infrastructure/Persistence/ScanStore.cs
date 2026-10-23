using Microsoft.Data.Sqlite;
using SteamWorkshopSynchronizer.Core.Models;
using SteamWorkshopSynchronizer.Infrastructure.Diagnostics;
using SteamWorkshopSynchronizer.Infrastructure.Steam;

namespace SteamWorkshopSynchronizer.Infrastructure.Persistence;

public sealed class ScanStore
{
    private readonly SqliteDatabase database;
    private readonly AppLogger? logger;

    public ScanStore(SqliteDatabase database, AppLogger? logger = null)
    {
        this.database = database ?? throw new ArgumentNullException(nameof(database));
        this.logger = logger;
    }

    public void SaveSnapshot(SteamScanSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        logger?.Info("数据库", $"保存扫描快照：{snapshot.Games.Count} 个游戏，{snapshot.WorkshopItems.Count} 个 Workshop 项");
        using var connection = database.OpenConnection();
        using var transaction = connection.BeginTransaction();
        var scanId = Guid.NewGuid().ToString("N");
        var now = snapshot.ScannedAtUtc.ToString("O");

        Execute(transaction, "UPDATE steam_accounts SET is_current = 0;");
        if (snapshot.CurrentAccount is not null)
        {
            Execute(transaction, """
                INSERT INTO steam_accounts (id, steam_id64, display_name, account_name, is_current, last_seen_at)
                VALUES ($id, $steamId, $displayName, $accountName, 1, $lastSeen)
                ON CONFLICT (steam_id64) DO UPDATE SET display_name = excluded.display_name, account_name = excluded.account_name, is_current = 1, last_seen_at = excluded.last_seen_at;
                """, ("$id", Guid.NewGuid().ToString("N")), ("$steamId", snapshot.CurrentAccount.SteamId64),
                ("$displayName", (object?)snapshot.CurrentAccount.DisplayName ?? DBNull.Value),
                ("$accountName", (object?)snapshot.CurrentAccount.AccountName ?? DBNull.Value), ("$lastSeen", now));
        }

        foreach (var library in snapshot.Libraries)
        {
            Execute(transaction, """
                INSERT INTO steam_libraries (id, path, is_readable, last_error, last_scanned_at)
                VALUES ($id, $path, $readable, $error, $scannedAt)
                ON CONFLICT (path) DO UPDATE SET is_readable = excluded.is_readable, last_error = excluded.last_error, last_scanned_at = excluded.last_scanned_at;
                """, ("$id", Guid.NewGuid().ToString("N")), ("$path", library.Path), ("$readable", library.IsReadable ? 1 : 0),
                ("$error", (object?)library.Error ?? DBNull.Value), ("$scannedAt", now));
        }

        Execute(transaction, "UPDATE games SET is_installed = 0, last_scanned_at = $scannedAt;", ("$scannedAt", now));
        foreach (var game in snapshot.Games)
        {
            Execute(transaction, """
                INSERT INTO games (app_id, name, install_path, is_installed, last_scanned_at)
                VALUES ($appId, $name, $installPath, 1, $scannedAt)
                ON CONFLICT (app_id) DO UPDATE SET name = excluded.name, install_path = excluded.install_path, is_installed = 1, last_scanned_at = excluded.last_scanned_at;
                """, ("$appId", game.AppId), ("$name", game.Name), ("$installPath", (object?)game.InstallPath ?? DBNull.Value), ("$scannedAt", now));
        }

        Execute(transaction, "DELETE FROM workshop_items;");
        foreach (var item in snapshot.WorkshopItems)
        {
            Execute(transaction, """
                INSERT INTO workshop_items (app_id, published_file_id, title_snapshot, workshop_url, is_subscribed, download_state, local_path, is_local_residual, last_seen_at)
                VALUES ($appId, $publishedFileId, $title, $url, $subscribed, $downloadState, $localPath, $localResidual, $lastSeen);
                """, ("$appId", item.AppId), ("$publishedFileId", item.PublishedFileId), ("$title", (object?)item.Title ?? DBNull.Value),
                ("$url", (object?)item.WorkshopUrl ?? DBNull.Value), ("$subscribed", item.IsSubscribed ? 1 : 0),
                ("$downloadState", item.DownloadState.ToString()), ("$localPath", (object?)item.LocalPath ?? DBNull.Value),
                ("$localResidual", item.IsLocalResidual ? 1 : 0), ("$lastSeen", item.LastSeenAt.ToString("O")));
        }

        var summary = $"{{\"games\":{snapshot.Games.Count},\"workshopItems\":{snapshot.WorkshopItems.Count},\"warnings\":{snapshot.Warnings.Count}}}";
        Execute(transaction, """
            INSERT INTO scan_runs (id, steam_account_id, status, started_at, finished_at, summary_json)
            VALUES ($id, (SELECT id FROM steam_accounts WHERE is_current = 1 LIMIT 1), $status, $startedAt, $finishedAt, $summary);
            """, ("$id", scanId), ("$status", snapshot.Warnings.Count == 0 ? "completed" : "partial"),
            ("$startedAt", now), ("$finishedAt", now), ("$summary", summary));
        transaction.Commit();
        logger?.Info("数据库", $"扫描快照保存完成，提示 {snapshot.Warnings.Count} 条");
    }

    public void EnsureGame(GameInfo game)
    {
        ArgumentNullException.ThrowIfNull(game);
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO games (app_id, name, install_path, is_installed, last_scanned_at)
            VALUES ($appId, $name, $installPath, $isInstalled, $lastScannedAt)
            ON CONFLICT (app_id) DO UPDATE SET name = CASE WHEN games.name LIKE 'App %' THEN excluded.name ELSE games.name END;
            """;
        command.Parameters.AddWithValue("$appId", game.AppId);
        command.Parameters.AddWithValue("$name", game.Name);
        command.Parameters.AddWithValue("$installPath", (object?)game.InstallPath ?? DBNull.Value);
        command.Parameters.AddWithValue("$isInstalled", game.IsInstalled ? 1 : 0);
        command.Parameters.AddWithValue("$lastScannedAt", DateTimeOffset.UtcNow.ToString("O"));
        command.ExecuteNonQuery();
    }

    public IReadOnlyList<GameInfo> GetGames(bool installedOnly = false)
    {
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT app_id, name, install_path, is_installed
            FROM games
            WHERE $installedOnly = 0 OR is_installed = 1
            ORDER BY is_installed DESC, name COLLATE NOCASE;
            """;
        command.Parameters.AddWithValue("$installedOnly", installedOnly ? 1 : 0);
        using var reader = command.ExecuteReader();
        var games = new List<GameInfo>();
        while (reader.Read())
        {
            games.Add(new(reader.GetInt32(0), reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetString(2), reader.GetInt32(3) == 1));
        }

        return games;
    }

    public IReadOnlyList<WorkshopItemSnapshot> GetWorkshopItems(int appId)
    {
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT app_id, published_file_id, title_snapshot, workshop_url, is_subscribed, download_state, local_path, is_local_residual, last_seen_at
            FROM workshop_items
            WHERE app_id = $appId
            ORDER BY COALESCE(title_snapshot, published_file_id) COLLATE NOCASE;
            """;
        command.Parameters.AddWithValue("$appId", appId);
        using var reader = command.ExecuteReader();
        var items = new List<WorkshopItemSnapshot>();
        while (reader.Read())
        {
            var downloadState = Enum.TryParse<WorkshopDownloadState>(reader.GetString(5), out var parsed)
                ? parsed
                : WorkshopDownloadState.Unknown;
            items.Add(new(reader.GetInt32(0), reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3), reader.GetInt32(4) == 1, downloadState,
                reader.GetInt32(7) == 1, reader.IsDBNull(6) ? null : reader.GetString(6), DateTimeOffset.Parse(reader.GetString(8))));
        }

        return items;
    }

    private static void Execute(SqliteTransaction transaction, string sql, params (string Name, object Value)[] parameters)
    {
        using var command = transaction.Connection!.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        command.ExecuteNonQuery();
    }
}
