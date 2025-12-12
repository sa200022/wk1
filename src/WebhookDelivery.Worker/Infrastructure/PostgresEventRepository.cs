using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using Npgsql;
using WebhookDelivery.Core.Models;
using WebhookDelivery.Core.Repositories;

namespace WebhookDelivery.Worker.Infrastructure;

/// <summary>
/// PostgreSQL Event Repository for Worker (READ-ONLY)
/// Worker only needs to read event payload for webhook delivery
/// </summary>
public sealed class PostgresEventRepository : IEventRepository
{
    private readonly string _connectionString;

    public PostgresEventRepository(string connectionString)
    {
        _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
    }

    public async Task<Event?> GetByIdAsync(long id, CancellationToken cancellationToken = default)
    {
        const string sql = @"
            SELECT id, event_type, created_at, payload
            FROM events
            WHERE id = @Id
        ";

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var result = await connection.QuerySingleOrDefaultAsync<dynamic>(
            new CommandDefinition(sql, new { Id = id }, cancellationToken: cancellationToken)
        );

        if (result == null)
            return null;

        return new Event
        {
            Id = result.id,
            EventType = result.event_type,
            CreatedAt = result.created_at,
            Payload = JsonSerializer.Deserialize<System.Collections.Generic.Dictionary<string, object>>(result.payload)
        };
    }

    public Task<Event> AppendAsync(Event @event, CancellationToken cancellationToken = default)
    {
        // Worker cannot insert events
        throw new InvalidOperationException("Worker does not have permission to insert events");
    }
}
