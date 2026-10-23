using Microsoft.Data.Sqlite;
using System.IO;

namespace SteamWorkshopSynchronizer.Infrastructure.Persistence;

public sealed class SqliteDatabase
{
    public const int CurrentSchemaVersion = 1;

    public SqliteDatabase(string databasePath)
    {
        if (string.IsNullOrWhiteSpace(databasePath))
        {
            throw new ArgumentException("数据库路径不能为空", nameof(databasePath));
        }

        DatabasePath = Path.GetFullPath(databasePath);
    }

    public string DatabasePath { get; }

    public void Initialize()
    {
        var directory = Path.GetDirectoryName(DatabasePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();
        ExecuteNonQuery(transaction, "PRAGMA foreign_keys = ON;");
        ExecuteNonQuery(transaction, "PRAGMA journal_mode = WAL;");
        ExecuteNonQuery(transaction, "CREATE TABLE IF NOT EXISTS schema_info (version INTEGER NOT NULL);");

        var currentVersion = ExecuteScalar<long>(transaction, "SELECT COALESCE(MAX(version), 0) FROM schema_info;");
        if (currentVersion > CurrentSchemaVersion)
        {
            throw new InvalidOperationException($"数据库版本 {currentVersion} 高于当前支持的版本 {CurrentSchemaVersion}");
        }

        if (currentVersion == 0)
        {
            CreateVersionOneSchema(transaction);
            ExecuteNonQuery(transaction, "INSERT INTO schema_info (version) VALUES (1);");
        }

        transaction.Commit();
    }

    public SqliteConnection OpenConnection()
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = DatabasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
            ForeignKeys = true
        }.ToString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA foreign_keys = ON;";
        command.ExecuteNonQuery();
        return connection;
    }

    private static void CreateVersionOneSchema(SqliteTransaction transaction)
    {
        ExecuteNonQuery(transaction, """
            CREATE TABLE IF NOT EXISTS steam_accounts (
                id TEXT PRIMARY KEY NOT NULL,
                steam_id64 TEXT NOT NULL UNIQUE,
                display_name TEXT,
                account_name TEXT,
                is_current INTEGER NOT NULL DEFAULT 0,
                last_seen_at TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS steam_libraries (
                id TEXT PRIMARY KEY NOT NULL,
                path TEXT NOT NULL UNIQUE,
                is_readable INTEGER NOT NULL DEFAULT 0,
                last_error TEXT,
                last_scanned_at TEXT
            );
            CREATE TABLE IF NOT EXISTS games (
                app_id INTEGER PRIMARY KEY NOT NULL,
                name TEXT NOT NULL,
                install_path TEXT,
                is_installed INTEGER NOT NULL DEFAULT 0,
                last_scanned_at TEXT
            );
            CREATE TABLE IF NOT EXISTS workshop_items (
                app_id INTEGER NOT NULL,
                published_file_id TEXT NOT NULL,
                title_snapshot TEXT,
                workshop_url TEXT,
                is_subscribed INTEGER NOT NULL DEFAULT 0,
                download_state TEXT NOT NULL,
                local_path TEXT,
                is_local_residual INTEGER NOT NULL DEFAULT 0,
                last_seen_at TEXT NOT NULL,
                PRIMARY KEY (app_id, published_file_id)
            );
            CREATE TABLE IF NOT EXISTS presets (
                id TEXT PRIMARY KEY NOT NULL,
                app_id INTEGER NOT NULL,
                name TEXT NOT NULL,
                note TEXT,
                source_type TEXT NOT NULL,
                current_version_id TEXT,
                created_at TEXT NOT NULL,
                updated_at TEXT NOT NULL,
                archived_at TEXT
            );
            CREATE TABLE IF NOT EXISTS preset_versions (
                id TEXT PRIMARY KEY NOT NULL,
                preset_id TEXT NOT NULL REFERENCES presets(id) ON DELETE CASCADE,
                version_number INTEGER NOT NULL,
                created_at TEXT NOT NULL,
                created_reason TEXT NOT NULL,
                source_batch_id TEXT,
                UNIQUE (preset_id, version_number)
            );
            CREATE TABLE IF NOT EXISTS preset_items (
                preset_version_id TEXT NOT NULL REFERENCES preset_versions(id) ON DELETE CASCADE,
                app_id INTEGER NOT NULL,
                published_file_id TEXT NOT NULL,
                title_snapshot TEXT,
                workshop_url TEXT,
                is_ignored INTEGER NOT NULL DEFAULT 0,
                source TEXT,
                PRIMARY KEY (preset_version_id, published_file_id)
            );
            CREATE TABLE IF NOT EXISTS recycle_bin_items (
                id TEXT PRIMARY KEY NOT NULL,
                app_id INTEGER NOT NULL,
                published_file_id TEXT NOT NULL,
                title_snapshot TEXT,
                workshop_url TEXT,
                source_preset_id TEXT,
                source_preset_version_id TEXT,
                removed_at TEXT NOT NULL,
                restore_state TEXT NOT NULL,
                last_error TEXT
            );
            CREATE TABLE IF NOT EXISTS operation_batches (
                id TEXT PRIMARY KEY NOT NULL,
                kind TEXT NOT NULL,
                app_id INTEGER,
                preset_id TEXT,
                preset_version_id TEXT,
                parent_batch_id TEXT,
                status TEXT NOT NULL,
                created_at TEXT NOT NULL,
                started_at TEXT,
                finished_at TEXT,
                last_error TEXT
            );
            CREATE TABLE IF NOT EXISTS operation_items (
                id TEXT PRIMARY KEY NOT NULL,
                batch_id TEXT NOT NULL REFERENCES operation_batches(id) ON DELETE CASCADE,
                app_id INTEGER NOT NULL,
                published_file_id TEXT NOT NULL,
                action TEXT NOT NULL,
                selection_state TEXT NOT NULL,
                result TEXT NOT NULL,
                before_subscribed INTEGER,
                after_subscribed INTEGER,
                steam_uri TEXT,
                error_code TEXT,
                error_message TEXT,
                created_at TEXT NOT NULL,
                completed_at TEXT
            );
            CREATE TABLE IF NOT EXISTS scan_runs (
                id TEXT PRIMARY KEY NOT NULL,
                steam_account_id TEXT,
                status TEXT NOT NULL,
                started_at TEXT NOT NULL,
                finished_at TEXT,
                summary_json TEXT
            );
            CREATE TABLE IF NOT EXISTS app_settings (
                key TEXT PRIMARY KEY NOT NULL,
                value_json TEXT NOT NULL,
                updated_at TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS ix_games_installed ON games (is_installed);
            CREATE INDEX IF NOT EXISTS ix_workshop_items_app ON workshop_items (app_id);
            CREATE INDEX IF NOT EXISTS ix_presets_app ON presets (app_id, archived_at);
            CREATE INDEX IF NOT EXISTS ix_operation_batches_created ON operation_batches (created_at DESC);
            CREATE INDEX IF NOT EXISTS ix_recycle_bin_app ON recycle_bin_items (app_id, restore_state);
            """);
    }

    private static void ExecuteNonQuery(SqliteTransaction transaction, string sql)
    {
        using var command = transaction.Connection!.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static T ExecuteScalar<T>(SqliteTransaction transaction, string sql)
    {
        using var command = transaction.Connection!.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        var value = command.ExecuteScalar();
        return value is null or DBNull
            ? default!
            : (T)Convert.ChangeType(value, typeof(T), System.Globalization.CultureInfo.InvariantCulture);
    }
}
