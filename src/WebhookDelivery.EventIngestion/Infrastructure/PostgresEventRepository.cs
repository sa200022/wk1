using System;
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
            INSERT INTO events (event_type, payload, created_at)
            VALUES (@EventType, @Payload::jsonb, @CreatedAt)
            RETURNING id;
        ";

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        // Get JSON string from JsonDocument
        var payloadJson = @event.Payload.RootElement.GetRawText();

        var id = await connection.ExecuteScalarAsync<long>(
            new CommandDefinition(
                sql,
                new
                {
                    @event.EventType,
                    Payload = payloadJson,
                    @event.CreatedAt
                },
                cancellationToken: cancellationToken
            )
        );

        // Create new instance with assigned ID using factory pattern
        var newEvent = Event.Create(@event.EventType, @event.Payload);
        return new Event
        {
            Id = id,
            EventType = newEvent.EventType,
            Payload = newEvent.Payload,
            CreatedAt = newEvent.CreatedAt
        };
    }

    public async Task<Event?> GetByIdAsync(long id, CancellationToken cancellationToken = default)
    {
        const string sql = @"
            SELECT id, event_type, payload::text as payload, created_at
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
            EventType = row.event_type,
            Payload = payloadDoc,
            CreatedAt = row.created_at
        };
    }
}
