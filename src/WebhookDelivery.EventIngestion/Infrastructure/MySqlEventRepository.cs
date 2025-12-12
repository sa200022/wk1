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
/// MySQL implementation of event repository
/// Uses event_ingest_writer role with INSERT-only permissions
/// </summary>
public sealed class MySqlEventRepository : IEventRepository
{
    private readonly string _connectionString;

    public MySqlEventRepository(string connectionString)
    {
        _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
    }

    public async Task<Event> AppendAsync(Event @event, CancellationToken cancellationToken = default)
    {
        const string sql = @"
            INSERT INTO events (event_type, payload, created_at)
            VALUES (@EventType, @Payload, @CreatedAt)
            RETURNING id;
        ";

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        // Serialize payload to JSON string
        var payloadJson = JsonSerializer.Serialize(@event.Payload);

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

        // Return immutable event with assigned ID
        return @event with { Id = id };
    }
}
