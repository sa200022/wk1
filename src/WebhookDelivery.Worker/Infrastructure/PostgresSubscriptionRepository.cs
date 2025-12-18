using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using Npgsql;
using WebhookDelivery.Core.Models;
using WebhookDelivery.Core.Repositories;

namespace WebhookDelivery.Worker.Infrastructure;

/// <summary>
/// PostgreSQL Subscription Repository for Worker (READ-ONLY)
/// Worker only needs to read subscription callback_url for delivery
/// </summary>
public sealed class PostgresSubscriptionRepository : ISubscriptionRepository
{
    private readonly string _connectionString;

    public PostgresSubscriptionRepository(string connectionString)
    {
        _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
    }

    public async Task<Subscription?> GetByIdAsync(long id, CancellationToken cancellationToken = default)
    {
        const string sql = @"
            SELECT
                id AS Id,
                event_type AS EventType,
                callback_url AS CallbackUrl,
                active AS Active,
                verified AS Verified,
                created_at AS CreatedAt,
                updated_at AS UpdatedAt
            FROM subscriptions
            WHERE id = @Id
        ";

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        return await connection.QuerySingleOrDefaultAsync<Subscription>(
            new CommandDefinition(sql, new { Id = id }, cancellationToken: cancellationToken)
        );
    }

    public Task<IReadOnlyList<Subscription>> GetByEventTypeAsync(
        string eventType,
        CancellationToken cancellationToken = default)
    {
        // Worker doesn't need this method
        throw new InvalidOperationException("Worker does not query subscriptions by event type");
    }

    public Task<IReadOnlyList<Subscription>> GetActiveAndVerifiedAsync(
        string eventType,
        CancellationToken cancellationToken = default)
    {
        // Worker doesn't need this method
        throw new InvalidOperationException("Worker does not query active subscriptions");
    }

    public Task<Subscription> CreateAsync(Subscription subscription, CancellationToken cancellationToken = default)
    {
        // Worker cannot create subscriptions
        throw new InvalidOperationException("Worker does not have permission to create subscriptions");
    }

    public Task<Subscription> UpdateAsync(Subscription subscription, CancellationToken cancellationToken = default)
    {
        // Worker cannot update subscriptions
        throw new InvalidOperationException("Worker does not have permission to update subscriptions");
    }

    public Task<IReadOnlyList<Subscription>> GetAllAsync(int limit, int offset, CancellationToken cancellationToken = default)
    {
        throw new InvalidOperationException("Worker does not list subscriptions");
    }
}
