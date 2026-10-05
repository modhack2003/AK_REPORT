using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AkReporting.Domain;
using Npgsql;
using NpgsqlTypes;

namespace AkReporting.Infrastructure;

public sealed class Database(string connectionString) : IAsyncDisposable
{
    public NpgsqlDataSource Source { get; } = NpgsqlDataSource.Create(connectionString);
    public ValueTask DisposeAsync() => Source.DisposeAsync();
    public static string Json<T>(T value) => JsonSerializer.Serialize(value);
    public static T FromJson<T>(string json) => JsonSerializer.Deserialize<T>(json) ?? throw new IntegrityException("Stored JSON is invalid.");
    public static string Hash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    public static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
    public static NpgsqlCommand Command(NpgsqlConnection connection, NpgsqlTransaction? transaction, string sql, params (string Key, object? Value)[] values)
    {
        var cmd = new NpgsqlCommand(sql, connection, transaction);
        foreach (var (key, value) in values)
        {
            var p = new NpgsqlParameter { ParameterName = key, Value = value ?? DBNull.Value };
            // Null UUID/decimal/image parameters must remain typed for PostgreSQL inference.
            if (value is null or DBNull) p.NpgsqlDbType = key.Contains("png", StringComparison.Ordinal) ? NpgsqlDbType.Bytea :
                key == "numeric" ? NpgsqlDbType.Numeric : key is "revision" or "age" ? NpgsqlDbType.Integer :
                key is "time" or "collection_time" ? NpgsqlDbType.TimestampTz : NpgsqlDbType.Uuid;
            cmd.Parameters.Add(p);
        }
        return cmd;
    }
    public static async Task Audit(NpgsqlConnection c, NpgsqlTransaction tx, Actor actor, string action, Guid id, int? revision, CancellationToken ct)
    {
        await using var cmd = Command(c, tx,
            "INSERT INTO audit_event VALUES (@id,@actor,@action,@entity,@revision,now())",
            ("id", Guid.NewGuid()), ("actor", actor.Id), ("action", action), ("entity", id), ("revision", revision ?? (object)DBNull.Value));
        await cmd.ExecuteNonQueryAsync(ct);
    }
    public static async Task Migrate(string ownerConnection, CancellationToken ct = default)
    {
        await using var c = new NpgsqlConnection(ownerConnection);
        await c.OpenAsync(ct);
        await using var tx = await c.BeginTransactionAsync(ct);
        await using (var guard = new NpgsqlCommand("SELECT pg_advisory_xact_lock(78261591); CREATE TABLE IF NOT EXISTS schema_migration (name text PRIMARY KEY, sha256 text NOT NULL, applied_at timestamptz NOT NULL)", c, tx))
            await guard.ExecuteNonQueryAsync(ct);
        var assembly = Assembly.GetExecutingAssembly();
        foreach (var name in assembly.GetManifestResourceNames().Where(x => x.EndsWith(".sql", StringComparison.Ordinal)).Order(StringComparer.Ordinal))
        {
            using var reader = new StreamReader(assembly.GetManifestResourceStream(name)!);
            var sql = await reader.ReadToEndAsync(ct);
            await using var check = Command(c, tx, "SELECT sha256 FROM schema_migration WHERE name=@name", ("name", name));
            var previous = await check.ExecuteScalarAsync(ct) as string;
            if (previous != null)
            {
                if (previous != Hash(sql)) throw new IntegrityException("An applied migration has been modified.");
                continue;
            }
            await using (var apply = new NpgsqlCommand(sql, c, tx)) await apply.ExecuteNonQueryAsync(ct);
            await using var record = Command(c, tx, "INSERT INTO schema_migration VALUES (@name,@hash,now())", ("name", name), ("hash", Hash(sql)));
            await record.ExecuteNonQueryAsync(ct);
        }
        await tx.CommitAsync(ct);
    }
}
