using Microsoft.Data.Sqlite;
using SteamWorkshopSynchronizer.Core.Models;
using SteamWorkshopSynchronizer.Core.Operations;
using SteamWorkshopSynchronizer.Infrastructure.Diagnostics;

namespace SteamWorkshopSynchronizer.Infrastructure.Persistence;

public sealed class OperationStore
{
    private readonly SqliteDatabase database;
    private readonly AppLogger? logger;

    public OperationStore(SqliteDatabase database, AppLogger? logger = null)
    {
        this.database = database ?? throw new ArgumentNullException(nameof(database));
        this.logger = logger;
    }

    public string CreateBatch(
        string kind,
        IReadOnlyList<WorkshopOperationRequest> requests,
        string? presetId = null,
        string? presetVersionId = null,
        string? parentBatchId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);
        ArgumentNullException.ThrowIfNull(requests);
        var batchId = Guid.NewGuid().ToString("N");
        var now = DateTimeOffset.UtcNow.ToString("O");
        var appId = requests.Select(request => (int?)request.AppId).Distinct().SingleOrDefault();

        using var connection = database.OpenConnection();
        using var transaction = connection.BeginTransaction();
        Execute(transaction, """
            INSERT INTO operation_batches (id, kind, app_id, preset_id, preset_version_id, parent_batch_id, status, created_at, started_at)
            VALUES ($id, $kind, $appId, $presetId, $presetVersionId, $parentBatchId, 'running', $createdAt, $startedAt);
            """, ("$id", batchId), ("$kind", kind), ("$appId", (object?)appId ?? DBNull.Value),
            ("$presetId", (object?)presetId ?? DBNull.Value), ("$presetVersionId", (object?)presetVersionId ?? DBNull.Value),
            ("$parentBatchId", (object?)parentBatchId ?? DBNull.Value), ("$createdAt", now), ("$startedAt", now));

        foreach (var request in requests)
        {
            Execute(transaction, """
                INSERT INTO operation_items (id, batch_id, app_id, published_file_id, action, selection_state, result, before_subscribed, created_at)
                VALUES ($id, $batchId, $appId, $publishedFileId, $action, 'selected', 'pending', NULL, $createdAt);
                """, ("$id", Guid.NewGuid().ToString("N")), ("$batchId", batchId), ("$appId", request.AppId),
                ("$publishedFileId", request.PublishedFileId), ("$action", request.Action.ToString().ToLowerInvariant()), ("$createdAt", now));
        }

        transaction.Commit();
        logger?.Info("数据库", $"创建操作批次 {batchId}：{kind}，共 {requests.Count} 项");
        return batchId;
    }

    public void RecordResult(string batchId, WorkshopOperationResult result)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(batchId);
        ArgumentNullException.ThrowIfNull(result);
        var now = DateTimeOffset.UtcNow.ToString("O");
        using var connection = database.OpenConnection();
        using var transaction = connection.BeginTransaction();
        Execute(transaction, """
            UPDATE operation_items
            SET result = $result, steam_uri = $steamUri, error_message = $errorMessage, completed_at = $completedAt
            WHERE id = (
                SELECT id FROM operation_items
                WHERE batch_id = $batchId AND app_id = $appId AND published_file_id = $publishedFileId AND result = 'pending'
                ORDER BY created_at
                LIMIT 1
            );
            """, ("$result", ToStoredResult(result.State)), ("$steamUri", (object?)result.ProtocolUri ?? DBNull.Value),
            ("$errorMessage", (object?)result.ErrorMessage ?? DBNull.Value), ("$completedAt", now), ("$batchId", batchId),
            ("$appId", result.Request.AppId), ("$publishedFileId", result.Request.PublishedFileId));

        if (result.Request.Action == WorkshopOperationAction.Unsubscribe && result.State == WorkshopOperationResultState.Submitted)
        {
            Execute(transaction, """
                INSERT INTO recycle_bin_items (id, app_id, published_file_id, title_snapshot, workshop_url, removed_at, restore_state)
                VALUES ($id, $appId, $publishedFileId, $title, $url, $removedAt, 'available');
                """, ("$id", Guid.NewGuid().ToString("N")), ("$appId", result.Request.AppId),
                ("$publishedFileId", result.Request.PublishedFileId), ("$title", (object?)result.Request.Title ?? DBNull.Value),
                ("$url", (object?)result.Request.WorkshopUrl ?? DBNull.Value), ("$removedAt", now));
        }

        transaction.Commit();
        if (result.State == WorkshopOperationResultState.Failed)
        {
            logger?.Error("数据库", $"记录操作结果失败：批次 {batchId}，Workshop ID {result.Request.PublishedFileId}，{result.ErrorMessage}");
        }
        else
        {
            logger?.Info("数据库", $"记录操作结果：批次 {batchId}，Workshop ID {result.Request.PublishedFileId}，{result.State}");
        }
    }

    public void FinishBatch(string batchId, string status, string? error = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(batchId);
        ArgumentException.ThrowIfNullOrWhiteSpace(status);
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE operation_batches
            SET status = $status, finished_at = $finishedAt, last_error = $error
            WHERE id = $id;
            """;
        command.Parameters.AddWithValue("$status", status);
        command.Parameters.AddWithValue("$finishedAt", DateTimeOffset.UtcNow.ToString("O"));
        command.Parameters.AddWithValue("$error", (object?)error ?? DBNull.Value);
        command.Parameters.AddWithValue("$id", batchId);
        command.ExecuteNonQuery();
        logger?.Info("数据库", $"操作批次 {batchId} 已结束，状态：{status}");
    }

    public IReadOnlyList<OperationBatchSummary> GetHistory(int limit = 100)
    {
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT b.id, b.kind, b.app_id, b.preset_id, b.preset_version_id, b.status, b.created_at, b.finished_at,
                   COUNT(i.id),
                   SUM(CASE WHEN i.result IN ('submitted', 'succeeded', 'failed', 'stopped') THEN 1 ELSE 0 END),
                   SUM(CASE WHEN i.result = 'failed' THEN 1 ELSE 0 END)
            FROM operation_batches b
            LEFT JOIN operation_items i ON i.batch_id = b.id
            GROUP BY b.id
            ORDER BY b.created_at DESC
            LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$limit", Math.Clamp(limit, 1, 500));
        using var reader = command.ExecuteReader();
        var history = new List<OperationBatchSummary>();
        while (reader.Read())
        {
            history.Add(new(
                reader.GetString(0),
                reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetInt32(2),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetString(4),
                reader.GetString(5),
                DateTimeOffset.Parse(reader.GetString(6)),
                reader.IsDBNull(7) ? null : DateTimeOffset.Parse(reader.GetString(7)),
                checked((int)reader.GetInt64(8)),
                reader.IsDBNull(9) ? 0 : checked((int)reader.GetInt64(9)),
                reader.IsDBNull(10) ? 0 : checked((int)reader.GetInt64(10))));
        }

        return history;
    }

    public IReadOnlyList<WorkshopOperationRequest> GetUndoRequests(string batchId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(batchId);
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT oi.app_id, oi.published_file_id, oi.action,
                   wi.title_snapshot, wi.workshop_url
            FROM operation_items oi
            LEFT JOIN workshop_items wi
              ON wi.app_id = oi.app_id AND wi.published_file_id = oi.published_file_id
            WHERE oi.batch_id = $batchId AND oi.result = 'submitted'
            ORDER BY oi.created_at;
            """;
        command.Parameters.AddWithValue("$batchId", batchId);
        using var reader = command.ExecuteReader();
        var requests = new List<WorkshopOperationRequest>();
        while (reader.Read())
        {
            var originalAction = reader.GetString(2);
            var inverse = string.Equals(originalAction, "subscribe", StringComparison.OrdinalIgnoreCase)
                ? WorkshopOperationAction.Unsubscribe
                : WorkshopOperationAction.Subscribe;
            requests.Add(new(
                reader.GetInt32(0),
                reader.GetString(1),
                inverse,
                reader.IsDBNull(3) ? null : reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetString(4)));
        }

        return requests;
    }

    public IReadOnlyList<RecycleBinItem> GetRecycleBin(int? appId = null)
    {
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, app_id, published_file_id, title_snapshot, workshop_url, source_preset_id, source_preset_version_id,
                   removed_at, restore_state, last_error
            FROM recycle_bin_items
            WHERE restore_state <> 'restored' AND ($appId IS NULL OR app_id = $appId)
            ORDER BY removed_at DESC;
            """;
        command.Parameters.AddWithValue("$appId", (object?)appId ?? DBNull.Value);
        using var reader = command.ExecuteReader();
        var items = new List<RecycleBinItem>();
        while (reader.Read())
        {
            items.Add(new(
                reader.GetString(0),
                reader.GetInt32(1),
                reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetString(4),
                reader.IsDBNull(5) ? null : reader.GetString(5),
                reader.IsDBNull(6) ? null : reader.GetString(6),
                DateTimeOffset.Parse(reader.GetString(7)),
                reader.GetString(8),
                reader.IsDBNull(9) ? null : reader.GetString(9)));
        }

        return items;
    }

    public void MarkRecycleBinRestored(string id)
    {
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE recycle_bin_items SET restore_state = 'restored', last_error = NULL WHERE id = $id;";
        command.Parameters.AddWithValue("$id", id);
        command.ExecuteNonQuery();
    }

    public void MarkRecycleBinFailed(string id, string error)
    {
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE recycle_bin_items SET restore_state = 'failed', last_error = $error WHERE id = $id;";
        command.Parameters.AddWithValue("$id", id);
        command.Parameters.AddWithValue("$error", error);
        command.ExecuteNonQuery();
    }

    public void ClearRecycleBin()
    {
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM recycle_bin_items;";
        command.ExecuteNonQuery();
    }

    private static string ToStoredResult(WorkshopOperationResultState state) => state switch
    {
        WorkshopOperationResultState.Submitted => "submitted",
        WorkshopOperationResultState.Failed => "failed",
        WorkshopOperationResultState.Skipped => "skipped",
        _ => "stopped"
    };

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
