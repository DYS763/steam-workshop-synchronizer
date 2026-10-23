using Microsoft.Data.Sqlite;
using SteamWorkshopSynchronizer.Core.Models;

namespace SteamWorkshopSynchronizer.Infrastructure.Persistence;

public sealed class PresetStore
{
    private readonly SqliteDatabase database;

    public PresetStore(SqliteDatabase database)
    {
        this.database = database ?? throw new ArgumentNullException(nameof(database));
    }

    public PresetSummary CreateFromManifest(ManifestDocument manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        var presetId = Guid.NewGuid().ToString("N");
        var versionId = Guid.NewGuid().ToString("N");
        var now = DateTimeOffset.UtcNow;
        var timestamp = now.ToString("O");

        using var connection = database.OpenConnection();
        using var transaction = connection.BeginTransaction();
        var presetName = GetAvailableName(transaction, manifest.Game.AppId, manifest.Preset.Name.Trim());
        Execute(transaction, """
            INSERT INTO presets (id, app_id, name, note, source_type, current_version_id, created_at, updated_at)
            VALUES ($id, $appId, $name, $note, $sourceType, $versionId, $createdAt, $updatedAt);
            """, ("$id", presetId), ("$appId", manifest.Game.AppId), ("$name", presetName),
            ("$note", (object?)manifest.Preset.Note ?? DBNull.Value), ("$sourceType", manifest.Source),
            ("$versionId", versionId), ("$createdAt", timestamp), ("$updatedAt", timestamp));
        InsertVersion(transaction, versionId, presetId, 1, now, "import", null, manifest.Items, manifest.Game.AppId);
        transaction.Commit();

        return new(presetId, manifest.Game.AppId, presetName, manifest.Preset.Note, manifest.Source, versionId, now, now);
    }

    public IReadOnlyList<PresetSummary> GetPresets(int? appId = null)
    {
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, app_id, name, note, source_type, current_version_id, created_at, updated_at
            FROM presets
            WHERE archived_at IS NULL
              AND ($appId IS NULL OR app_id = $appId)
            ORDER BY updated_at DESC, name COLLATE NOCASE;
            """;
        command.Parameters.AddWithValue("$appId", (object?)appId ?? DBNull.Value);
        using var reader = command.ExecuteReader();
        var presets = new List<PresetSummary>();
        while (reader.Read())
        {
            presets.Add(ReadSummary(reader));
        }

        return presets;
    }

    public PresetVersionSnapshot? GetCurrentVersion(string presetId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(presetId);
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT pv.id, pv.preset_id, pv.version_number, pv.created_at, pv.created_reason, pv.source_batch_id,
                   pi.app_id, pi.published_file_id, pi.title_snapshot, pi.workshop_url, pi.is_ignored, pi.source
            FROM presets p
            JOIN preset_versions pv ON pv.id = p.current_version_id
            LEFT JOIN preset_items pi ON pi.preset_version_id = pv.id
            WHERE p.id = $presetId
            ORDER BY pi.published_file_id;
            """;
        command.Parameters.AddWithValue("$presetId", presetId);
        return ReadVersion(command);
    }

    public IReadOnlyList<PresetVersionSnapshot> GetVersions(string presetId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(presetId);
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT pv.id, pv.preset_id, pv.version_number, pv.created_at, pv.created_reason, pv.source_batch_id,
                   pi.app_id, pi.published_file_id, pi.title_snapshot, pi.workshop_url, pi.is_ignored, pi.source
            FROM preset_versions pv
            LEFT JOIN preset_items pi ON pi.preset_version_id = pv.id
            WHERE pv.preset_id = $presetId
            ORDER BY pv.version_number DESC, pi.published_file_id;
            """;
        command.Parameters.AddWithValue("$presetId", presetId);
        using var reader = command.ExecuteReader();
        return ReadVersionGroups(reader);
    }

    public PresetVersionSnapshot CreateVersion(
        string presetId,
        int appId,
        IEnumerable<PresetItemRecord> items,
        string reason,
        string? sourceBatchId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(presetId);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        ArgumentNullException.ThrowIfNull(items);
        var itemList = items
            .Where(item => item.AppId == appId)
            .GroupBy(item => item.PublishedFileId, StringComparer.Ordinal)
            .Select(group => group.First())
            .ToArray();
        var now = DateTimeOffset.UtcNow;
        var timestamp = now.ToString("O");
        var versionId = Guid.NewGuid().ToString("N");
        var nextVersion = 0;

        using var connection = database.OpenConnection();
        using var transaction = connection.BeginTransaction();
        using (var check = connection.CreateCommand())
        {
            check.Transaction = transaction;
            check.CommandText = "SELECT app_id, COALESCE(MAX(version_number), 0) FROM presets p LEFT JOIN preset_versions pv ON pv.preset_id = p.id WHERE p.id = $presetId GROUP BY p.app_id;";
            check.Parameters.AddWithValue("$presetId", presetId);
            using var reader = check.ExecuteReader();
            if (!reader.Read())
            {
                throw new KeyNotFoundException($"找不到预设 {presetId}");
            }

            if (reader.GetInt32(0) != appId)
            {
                throw new InvalidOperationException("预设 AppID 与新版本不一致");
            }

            nextVersion = reader.GetInt32(1) + 1;
        }

        InsertVersion(transaction, versionId, presetId, nextVersion, now, reason, sourceBatchId, itemList, appId);
        Execute(transaction, "UPDATE presets SET current_version_id = $versionId, updated_at = $updatedAt WHERE id = $presetId;",
            ("$versionId", versionId), ("$updatedAt", timestamp), ("$presetId", presetId));

        transaction.Commit();
        return new(versionId, presetId, nextVersion, now, reason, sourceBatchId, itemList);
    }

    public void SetCurrentVersion(string presetId, string versionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(presetId);
        ArgumentException.ThrowIfNullOrWhiteSpace(versionId);
        using var connection = database.OpenConnection();
        using var transaction = connection.BeginTransaction();
        Execute(transaction, """
            UPDATE presets
            SET current_version_id = (
                SELECT id FROM preset_versions WHERE id = $versionId AND preset_id = $presetId
            ), updated_at = $updatedAt
            WHERE id = $presetId
              AND EXISTS (SELECT 1 FROM preset_versions WHERE id = $versionId AND preset_id = $presetId);
            """, ("$versionId", versionId), ("$presetId", presetId), ("$updatedAt", DateTimeOffset.UtcNow.ToString("O")));
        transaction.Commit();
    }

    public void Archive(string presetId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(presetId);
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE presets SET archived_at = $archivedAt, updated_at = $archivedAt WHERE id = $id;";
        command.Parameters.AddWithValue("$archivedAt", DateTimeOffset.UtcNow.ToString("O"));
        command.Parameters.AddWithValue("$id", presetId);
        command.ExecuteNonQuery();
    }

    public void UpdateMetadata(string presetId, string name, string? note)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(presetId);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE presets SET name = $name, note = $note, updated_at = $updatedAt WHERE id = $id;";
        command.Parameters.AddWithValue("$name", name.Trim());
        command.Parameters.AddWithValue("$note", (object?)note?.Trim() ?? DBNull.Value);
        command.Parameters.AddWithValue("$updatedAt", DateTimeOffset.UtcNow.ToString("O"));
        command.Parameters.AddWithValue("$id", presetId);
        command.ExecuteNonQuery();
    }

    private static void InsertVersion(
        SqliteTransaction transaction,
        string versionId,
        string presetId,
        int versionNumber,
        DateTimeOffset createdAt,
        string reason,
        string? sourceBatchId,
        IEnumerable<ManifestItem> items,
        int appId)
    {
        Execute(transaction, """
            INSERT INTO preset_versions (id, preset_id, version_number, created_at, created_reason, source_batch_id)
            VALUES ($id, $presetId, $versionNumber, $createdAt, $reason, $sourceBatchId);
            """, ("$id", versionId), ("$presetId", presetId), ("$versionNumber", versionNumber),
            ("$createdAt", createdAt.ToString("O")), ("$reason", reason), ("$sourceBatchId", (object?)sourceBatchId ?? DBNull.Value));

        foreach (var item in items)
        {
            Execute(transaction, """
                INSERT INTO preset_items (preset_version_id, app_id, published_file_id, title_snapshot, workshop_url, is_ignored, source)
                VALUES ($versionId, $appId, $publishedFileId, $title, $url, $ignored, $source);
                """, ("$versionId", versionId), ("$appId", appId), ("$publishedFileId", item.PublishedFileId),
                ("$title", (object?)item.Title ?? DBNull.Value), ("$url", (object?)item.WorkshopUrl ?? DBNull.Value),
                ("$ignored", item.Ignored ? 1 : 0), ("$source", (object?)item.Source ?? DBNull.Value));
        }
    }

    private static void InsertVersion(
        SqliteTransaction transaction,
        string versionId,
        string presetId,
        int versionNumber,
        DateTimeOffset createdAt,
        string reason,
        string? sourceBatchId,
        IEnumerable<PresetItemRecord> items,
        int appId)
    {
        Execute(transaction, """
            INSERT INTO preset_versions (id, preset_id, version_number, created_at, created_reason, source_batch_id)
            VALUES ($id, $presetId, $versionNumber, $createdAt, $reason, $sourceBatchId);
            """, ("$id", versionId), ("$presetId", presetId), ("$versionNumber", versionNumber),
            ("$createdAt", createdAt.ToString("O")), ("$reason", reason), ("$sourceBatchId", (object?)sourceBatchId ?? DBNull.Value));

        foreach (var item in items)
        {
            Execute(transaction, """
                INSERT INTO preset_items (preset_version_id, app_id, published_file_id, title_snapshot, workshop_url, is_ignored, source)
                VALUES ($versionId, $appId, $publishedFileId, $title, $url, $ignored, $source);
                """, ("$versionId", versionId), ("$appId", appId), ("$publishedFileId", item.PublishedFileId),
                ("$title", (object?)item.Title ?? DBNull.Value), ("$url", (object?)item.WorkshopUrl ?? DBNull.Value),
                ("$ignored", item.IsIgnored ? 1 : 0), ("$source", (object?)item.Source ?? DBNull.Value));
        }
    }

    private static PresetSummary ReadSummary(SqliteDataReader reader) => new(
        reader.GetString(0),
        reader.GetInt32(1),
        reader.GetString(2),
        reader.IsDBNull(3) ? null : reader.GetString(3),
        reader.GetString(4),
        reader.IsDBNull(5) ? null : reader.GetString(5),
        DateTimeOffset.Parse(reader.GetString(6)),
        DateTimeOffset.Parse(reader.GetString(7)));

    private static PresetVersionSnapshot? ReadVersion(SqliteCommand command)
    {
        using var reader = command.ExecuteReader();
        var groups = ReadVersionGroups(reader);
        return groups.FirstOrDefault();
    }

    private static IReadOnlyList<PresetVersionSnapshot> ReadVersionGroups(SqliteDataReader reader)
    {
        var versions = new List<PresetVersionSnapshot>();
        var items = new List<PresetItemRecord>();
        string? currentId = null;
        string? presetId = null;
        var versionNumber = 0;
        var createdAt = DateTimeOffset.MinValue;
        var reason = string.Empty;
        string? sourceBatchId = null;

        void Flush()
        {
            if (currentId is not null)
            {
                versions.Add(new(currentId, presetId!, versionNumber, createdAt, reason, sourceBatchId, items.ToArray()));
                items = [];
            }
        }

        while (reader.Read())
        {
            var id = reader.GetString(0);
            if (!string.Equals(currentId, id, StringComparison.Ordinal))
            {
                Flush();
                currentId = id;
                presetId = reader.GetString(1);
                versionNumber = reader.GetInt32(2);
                createdAt = DateTimeOffset.Parse(reader.GetString(3));
                reason = reader.GetString(4);
                sourceBatchId = reader.IsDBNull(5) ? null : reader.GetString(5);
            }

            if (!reader.IsDBNull(6))
            {
                items.Add(new(
                    reader.GetInt32(6),
                    reader.GetString(7),
                    reader.IsDBNull(8) ? null : reader.GetString(8),
                    reader.IsDBNull(9) ? null : reader.GetString(9),
                    reader.GetInt32(10) == 1,
                    reader.IsDBNull(11) ? null : reader.GetString(11)));
            }
        }

        Flush();
        return versions;
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

    private static string GetAvailableName(SqliteTransaction transaction, int appId, string requestedName)
    {
        using var command = transaction.Connection!.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT name FROM presets WHERE app_id = $appId AND archived_at IS NULL;";
        command.Parameters.AddWithValue("$appId", appId);
        using var reader = command.ExecuteReader();
        var names = new HashSet<string>(StringComparer.CurrentCultureIgnoreCase);
        while (reader.Read())
        {
            names.Add(reader.GetString(0));
        }

        if (names.Add(requestedName))
        {
            return requestedName;
        }

        var copyName = $"{requestedName} 副本";
        var copyNumber = 2;
        while (!names.Add(copyName))
        {
            copyName = $"{requestedName} 副本 {copyNumber++}";
        }

        return copyName;
    }
}
