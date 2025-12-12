using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using Npgsql;
using WebhookDelivery.Core.Models;
using WebhookDelivery.Core.Repositories;

namespace WebhookDelivery.DeadLetter.Infrastructure;

/// <summary>
/// MySQL Event Repository for Dead Letter Service (READ-ONLY)
/// Dead Letter only needs to read event payload for dead letter records
/// </summary>
public sealed class MySqlEventRepository : IEventRepository
{
    private readonly string _connectionString;

    public MySqlEventRepository(string connectionString)
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
        // Dead Letter service cannot insert events
        throw new InvalidOperationException("Dead Letter service does not have permission to insert events");
    }
}
