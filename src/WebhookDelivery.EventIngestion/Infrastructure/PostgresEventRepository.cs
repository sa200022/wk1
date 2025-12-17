using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using Npgsql;
using WebhookDelivery.Core.Models;
using WebhookDelivery.Core.Repositories;

namespace WebhookDelivery.EventIngestion.Infrastructure;

/// <summary>
/// PostgreSQL implementation of event repository
/// Uses event_ingest_writer role with INSERT-only permissions
/// </summary>
public sealed class PostgresEventRepository : IEventRepository
{
    private readonly string _connectionString;

    public PostgresEventRepository(string connectionString)
    {
        _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
    }

    public async Task<Event> AppendAsync(Event @event, CancellationToken cancellationToken = default)
    {
        const string sql = @"
            INSERT INTO events (external_event_id, event_type, payload, created_at)
            VALUES (@ExternalEventId, @EventType, @Payload::jsonb, @CreatedAt)
            RETURNING id, created_at;
        ";

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        // Get JSON string from JsonDocument
        var payloadJson = @event.Payload.RootElement.GetRawText();

        var row = await connection.QuerySingleAsync<dynamic>(
            new CommandDefinition(
                sql,
                new
                {
                    @event.ExternalEventId,
                    @event.EventType,
                    Payload = payloadJson,
                    @event.CreatedAt
                },
                cancellationToken: cancellationToken
            )
        );

        return new Event
        {
            Id = row.id,
            ExternalEventId = @event.ExternalEventId,
            EventType = @event.EventType,
            Payload = @event.Payload,
            CreatedAt = row.created_at
        };
    }

    public async Task<Event?> GetByIdAsync(long id, CancellationToken cancellationToken = default)
    {
        const string sql = @"
            SELECT id, external_event_id, event_type, payload::text as payload, created_at
            FROM events
            WHERE id = @Id;
        ";

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var row = await connection.QuerySingleOrDefaultAsync<dynamic>(
            new CommandDefinition(sql, new { Id = id }, cancellationToken: cancellationToken)
        );

        if (row == null)
            return null;

        // Parse JSON string to JsonDocument
        var payloadDoc = JsonDocument.Parse((string)row.payload);

        return new Event
        {
            Id = row.id,
            ExternalEventId = row.external_event_id,
            EventType = row.event_type,
            Payload = payloadDoc,
            CreatedAt = row.created_at
        };
    }

    public async Task<IReadOnlyList<Event>> GetAfterIdAsync(
        long lastSeenEventId,
        int limit,
        CancellationToken cancellationToken = default)
    {
        const string sql = @"
            SELECT id, external_event_id, event_type, payload::text AS payload, created_at
            FROM events
            WHERE id > @LastId
            ORDER BY id ASC
            LIMIT @Limit;
        ";

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var rows = await connection.QueryAsync<dynamic>(
            new CommandDefinition(sql, new { LastId = lastSeenEventId, Limit = limit }, cancellationToken: cancellationToken)
        );

        var events = new List<Event>();
        foreach (var row in rows)
        {
            var payloadDoc = JsonDocument.Parse((string)row.payload);
            events.Add(new Event
            {
                Id = row.id,
                ExternalEventId = row.external_event_id,
                EventType = row.event_type,
                Payload = payloadDoc,
                CreatedAt = row.created_at
            });
        }

        return events;
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
