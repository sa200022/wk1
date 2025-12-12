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
            Payload = JsonSerializer.Deserialize<Dictionary<string, object>>(result.payload)
        };
    }

    public async Task<IReadOnlyList<Event>> GetUnprocessedEventsAsync(
        int limit,
        CancellationToken cancellationToken = default)
    {
        // This query would need additional logic to determine "unprocessed"
        // For now, return recent events ordered by created_at
        const string sql = @"
            SELECT id, event_type, created_at, payload
            FROM events
            ORDER BY created_at DESC
            LIMIT @Limit
        ";

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var results = await connection.QueryAsync<dynamic>(
            new CommandDefinition(sql, new { Limit = limit }, cancellationToken: cancellationToken)
        );

        return results.Select(r => new Event
        {
            Id = r.id,
            EventType = r.event_type,
            CreatedAt = r.created_at,
            Payload = JsonSerializer.Deserialize<Dictionary<string, object>>(r.payload)
        }).ToList();
    }

    public Task<Event> AppendAsync(Event @event, CancellationToken cancellationToken = default)
    {
        // Router should NOT be able to insert events
        throw new InvalidOperationException("Router worker does not have permission to insert events");
    }
}
