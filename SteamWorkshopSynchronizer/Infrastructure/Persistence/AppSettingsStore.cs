using System.Text.Json;

namespace SteamWorkshopSynchronizer.Infrastructure.Persistence;

public sealed class AppSettingsStore
{
    public const string DeveloperModeKey = "developer_mode";

    private readonly SqliteDatabase database;

    public AppSettingsStore(SqliteDatabase database)
    {
        this.database = database ?? throw new ArgumentNullException(nameof(database));
    }

    public bool GetBool(string key, bool defaultValue = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT value_json FROM app_settings WHERE key = $key;";
        command.Parameters.AddWithValue("$key", key);
        var value = command.ExecuteScalar() as string;
        try
        {
            return value is not null ? JsonSerializer.Deserialize<bool>(value) : defaultValue;
        }
        catch (JsonException)
        {
            return defaultValue;
        }
    }

    public void SetBool(string key, bool value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO app_settings (key, value_json, updated_at)
            VALUES ($key, $value, $updatedAt)
            ON CONFLICT (key) DO UPDATE SET value_json = excluded.value_json, updated_at = excluded.updated_at;
            """;
        command.Parameters.AddWithValue("$key", key);
        command.Parameters.AddWithValue("$value", JsonSerializer.Serialize(value));
        command.Parameters.AddWithValue("$updatedAt", DateTimeOffset.UtcNow.ToString("O"));
        command.ExecuteNonQuery();
    }
}
