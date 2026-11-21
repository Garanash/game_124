using System.Text.Json;
using Npgsql;
using Orleans.Storage;

namespace Realmforge.Server.Grains.Storage;

/// <summary>
/// Grain state as JSONB rows keyed by grain id and state name. The version column is the ETag, so two
/// activations racing on the same grain cannot silently overwrite each other.
/// </summary>
public sealed class PostgresGrainStorage : IGrainStorage
{
    private const string ReadSql = "SELECT payload, version FROM grain_state WHERE grain_id = @id AND state_name = @name";
    private const string InsertSql = """
        INSERT INTO grain_state (grain_id, state_name, payload, version, updated_at)
        VALUES (@id, @name, @payload::jsonb, 1, now())
        ON CONFLICT (grain_id, state_name) DO NOTHING
        """;
    private const string UpdateSql = """
        UPDATE grain_state SET payload = @payload::jsonb, version = version + 1, updated_at = now()
        WHERE grain_id = @id AND state_name = @name AND version = @version
        """;
    private const string DeleteSql = "DELETE FROM grain_state WHERE grain_id = @id AND state_name = @name AND version = @version";

    private readonly NpgsqlDataSource _dataSource;
    private readonly JsonSerializerOptions _json;

    public PostgresGrainStorage(NpgsqlDataSource dataSource, JsonSerializerOptions json)
    {
        _dataSource = dataSource;
        _json = json;
    }

    public async Task ReadStateAsync<T>(string stateName, GrainId grainId, IGrainState<T> grainState)
    {
        await using NpgsqlCommand command = _dataSource.CreateCommand(ReadSql);
        AddKey(command, stateName, grainId);
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
        {
            grainState.RecordExists = false;
            grainState.ETag = null;
            return;
        }
        grainState.State = JsonSerializer.Deserialize<T>(reader.GetString(0), _json)!;
        grainState.ETag = reader.GetInt32(1).ToString();
        grainState.RecordExists = true;
    }

    public async Task WriteStateAsync<T>(string stateName, GrainId grainId, IGrainState<T> grainState)
    {
        bool isNew = grainState.ETag == null;
        await using NpgsqlCommand command = _dataSource.CreateCommand(isNew ? InsertSql : UpdateSql);
        AddKey(command, stateName, grainId);
        command.Parameters.AddWithValue("payload", JsonSerializer.Serialize(grainState.State, _json));
        int expectedVersion = isNew ? 0 : int.Parse(grainState.ETag!);
        if (!isNew)
        {
            command.Parameters.AddWithValue("version", expectedVersion);
        }
        if (await command.ExecuteNonQueryAsync() != 1)
        {
            throw new InconsistentStateException($"Concurrent write to {grainId}/{stateName}", grainState.ETag, "unknown");
        }
        grainState.ETag = (expectedVersion + 1).ToString();
        grainState.RecordExists = true;
    }

    public async Task ClearStateAsync<T>(string stateName, GrainId grainId, IGrainState<T> grainState)
    {
        if (grainState.ETag == null)
        {
            return;
        }
        await using NpgsqlCommand command = _dataSource.CreateCommand(DeleteSql);
        AddKey(command, stateName, grainId);
        command.Parameters.AddWithValue("version", int.Parse(grainState.ETag));
        await command.ExecuteNonQueryAsync();
        grainState.ETag = null;
        grainState.RecordExists = false;
    }

    private static void AddKey(NpgsqlCommand command, string stateName, GrainId grainId)
    {
        command.Parameters.AddWithValue("id", grainId.ToString());
        command.Parameters.AddWithValue("name", stateName);
    }
}
