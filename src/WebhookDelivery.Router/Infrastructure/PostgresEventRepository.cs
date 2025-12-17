using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using Npgsql;
using WebhookDelivery.Core.Models;
using WebhookDelivery.Core.Repositories;

namespace WebhookDelivery.Router.Infrastructure;

/// <summary>
/// PostgreSQL Event Repository for Router Worker (READ-ONLY)
/// Router only needs to read events to create sagas
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
            SELECT id, external_event_id, event_type, created_at, payload::text AS payload
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
            ExternalEventId = result.external_event_id,
            EventType = result.event_type,
            CreatedAt = result.created_at,
            Payload = JsonDocument.Parse((string)result.payload)
        };
    }

    public async Task<IReadOnlyList<Event>> GetAfterIdAsync(
        long lastSeenEventId,
        int limit,
        CancellationToken cancellationToken = default)
    {
        const string sql = @"
            SELECT id, external_event_id, event_type, created_at, payload::text AS payload
            FROM events
            WHERE id > @LastId
            ORDER BY id ASC
            LIMIT @Limit
        ";

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var results = await connection.QueryAsync<dynamic>(
            new CommandDefinition(sql, new { LastId = lastSeenEventId, Limit = limit }, cancellationToken: cancellationToken)
        );

        return results.Select(r => new Event
        {
            Id = r.id,
            ExternalEventId = r.external_event_id,
            EventType = r.event_type,
            CreatedAt = r.created_at,
            Payload = JsonDocument.Parse((string)r.payload)
        }).ToList();
    }

    public Task<Event> AppendAsync(Event @event, CancellationToken cancellationToken = default)
    {
        // Router should NOT be able to insert events
        throw new InvalidOperationException("Router worker does not have permission to insert events");
    }

    public async Task<long> GetMaxEventIdAsync(CancellationToken cancellationToken = default)
    {
        const string sql = @"SELECT COALESCE(MAX(id), 0) FROM events;";

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var maxId = await connection.ExecuteScalarAsync<long>(
            new CommandDefinition(sql, cancellationToken: cancellationToken));

        return maxId;
    }
}
