using System.IO.Compression;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;
using SteamWorkshopSynchronizer.Core.Models;

namespace SteamWorkshopSynchronizer.Infrastructure.Persistence;

public sealed class BackupStore
{
    public const string Prefix = "SWSB1:";
    public const int CurrentFormatVersion = 1;

    private static readonly string[] BackupTables =
    [
        "steam_accounts",
        "steam_libraries",
        "games",
        "presets",
        "preset_versions",
        "preset_items",
        "recycle_bin_items",
        "operation_batches",
        "operation_items",
        "scan_runs",
        "app_settings"
    ];

    private static readonly string[] DeleteOrder =
    [
        "preset_items",
        "operation_items",
        "preset_versions",
        "recycle_bin_items",
        "operation_batches",
        "scan_runs",
        "presets",
        "games",
        "steam_libraries",
        "steam_accounts",
        "app_settings"
    ];

    private readonly SqliteDatabase database;

    public BackupStore(SqliteDatabase database)
    {
        this.database = database ?? throw new ArgumentNullException(nameof(database));
    }

    public string ExportCompressed(string toolVersion)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(toolVersion);
        using var connection = database.OpenConnection();
        var tables = new JsonObject();
        foreach (var table in BackupTables)
        {
            tables[table] = ReadTable(connection, table);
        }

        var root = new JsonObject
        {
            ["format"] = "sws-backup",
            ["backupFormatVersion"] = CurrentFormatVersion,
            ["createdAtUtc"] = DateTimeOffset.UtcNow.ToString("O"),
            ["toolVersion"] = toolVersion,
            ["tables"] = tables
        };
        return Prefix + Encode(Encoding.UTF8.GetBytes(root.ToJsonString(new JsonSerializerOptions { WriteIndented = false })));
    }

    public BackupRestoreResult RestoreCompressed(
        string encoded,
        BackupRestoreMode mode,
        string? safetySnapshotPath = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(encoded);
        var root = Parse(encoded);
        var version = root["backupFormatVersion"]?.GetValue<int>() ?? 0;
        if (!string.Equals(root["format"]?.GetValue<string>(), "sws-backup", StringComparison.Ordinal) || version <= 0)
        {
            throw new InvalidDataException("备份格式无效");
        }

        if (version > CurrentFormatVersion)
        {
            throw new InvalidDataException($"备份版本 {version} 高于当前支持的版本 {CurrentFormatVersion}");
        }

        var tables = root["tables"]?.AsObject() ?? throw new InvalidDataException("备份缺少数据表");
        using var connection = database.OpenConnection();
        using var transaction = connection.BeginTransaction();
        if (mode == BackupRestoreMode.Overwrite)
        {
            foreach (var table in DeleteOrder)
            {
                Execute(transaction, $"DELETE FROM {table};");
            }
        }

        var importedRows = 0;
        var skippedRows = 0;
        foreach (var table in BackupTables)
        {
            if (tables[table] is not JsonArray rows)
            {
                continue;
            }

            foreach (var row in rows.OfType<JsonObject>())
            {
                var columns = row.Select(property => property.Key).ToArray();
                if (columns.Length == 0)
                {
                    skippedRows++;
                    continue;
                }

                var sql = $"INSERT OR IGNORE INTO {table} ({string.Join(", ", columns)}) VALUES ({string.Join(", ", columns.Select((_, index) => $"$p{index}"))});";
                using var command = transaction.Connection!.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = sql;
                for (var index = 0; index < columns.Length; index++)
                {
                    command.Parameters.AddWithValue($"$p{index}", ToDbValue(row[columns[index]]));
                }

                var changed = command.ExecuteNonQuery();
                if (changed == 0)
                {
                    skippedRows++;
                }
                else
                {
                    importedRows += changed;
                }
            }
        }

        transaction.Commit();
        return new(mode, safetySnapshotPath, importedRows, skippedRows);
    }

    private static JsonArray ReadTable(SqliteConnection connection, string table)
    {
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT * FROM {table};";
        using var reader = command.ExecuteReader();
        var rows = new JsonArray();
        while (reader.Read())
        {
            var row = new JsonObject();
            for (var index = 0; index < reader.FieldCount; index++)
            {
                row[reader.GetName(index)] = reader.IsDBNull(index)
                    ? null
                    : JsonValue.Create(reader.GetValue(index));
            }

            rows.Add(row);
        }

        return rows;
    }

    private static JsonObject Parse(string encoded)
    {
        if (encoded.Length > 8 * 1024 * 1024 || !encoded.StartsWith(Prefix, StringComparison.Ordinal))
        {
            throw new InvalidDataException($"备份必须以 {Prefix} 开头且不超过 8 MB");
        }

        try
        {
            var payload = encoded[Prefix.Length..];
            var compressed = Convert.FromBase64String(payload.Replace('-', '+').Replace('_', '/').PadRight(payload.Length + ((4 - payload.Length % 4) % 4), '='));
            using var input = new MemoryStream(compressed);
            using var gzip = new GZipStream(input, CompressionMode.Decompress);
            using var reader = new StreamReader(gzip, Encoding.UTF8);
            var root = JsonNode.Parse(reader.ReadToEnd())?.AsObject();
            return root ?? throw new InvalidDataException("备份 JSON 无效");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("备份 JSON 无效", exception);
        }
        catch (FormatException exception)
        {
            throw new InvalidDataException("备份编码无效", exception);
        }
    }

    private static object ToDbValue(JsonNode? value)
    {
        if (value is null)
        {
            return DBNull.Value;
        }

        var jsonValue = value.AsValue();
        if (value.GetValueKind() == JsonValueKind.Number && jsonValue.TryGetValue<long>(out var integer))
        {
            return integer;
        }

        if (value.GetValueKind() == JsonValueKind.Number && jsonValue.TryGetValue<double>(out var number))
        {
            return number;
        }

        if (value.GetValueKind() == JsonValueKind.True)
        {
            return 1;
        }

        if (value.GetValueKind() == JsonValueKind.False)
        {
            return 0;
        }

        return (object?)jsonValue.GetValue<string>() ?? DBNull.Value;
    }

    private static string Encode(byte[] bytes) 
    {
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.Optimal, leaveOpen: true))
        {
            gzip.Write(bytes);
        }

        return Convert.ToBase64String(output.ToArray()).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    private static void Execute(SqliteTransaction transaction, string sql)
    {
        using var command = transaction.Connection!.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
}
