using System;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using Npgsql;

namespace WebhookDelivery.Router.Infrastructure;

/// <summary>
/// Persists router progress (last processed event id) to avoid replaying history on restart.
/// </summary>
public sealed class RouterStateRepository
{
    private readonly string _connectionString;

    public RouterStateRepository(string connectionString)
    {
        _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
    }

    public async Task<long> GetLastProcessedEventIdAsync(CancellationToken cancellationToken = default)
    {
        const string sql = @"
            INSERT INTO router_offsets (id, last_processed_event_id)
            VALUES (1, 0)
            ON CONFLICT (id) DO NOTHING;
            SELECT last_processed_event_id FROM router_offsets WHERE id = 1;";

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var last = await connection.ExecuteScalarAsync<long>(
            new CommandDefinition(sql, cancellationToken: cancellationToken));

        return last;
    }

    public async Task SaveLastProcessedEventIdAsync(long lastEventId, CancellationToken cancellationToken = default)
    {
        const string sql = @"
            INSERT INTO router_offsets (id, last_processed_event_id)
            VALUES (1, @LastId)
            ON CONFLICT (id) DO UPDATE SET last_processed_event_id = @LastId;";

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await connection.ExecuteAsync(
            new CommandDefinition(sql, new { LastId = lastEventId }, cancellationToken: cancellationToken));
    }
}
