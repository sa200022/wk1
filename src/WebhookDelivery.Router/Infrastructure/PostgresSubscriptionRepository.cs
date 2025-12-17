using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using Npgsql;
using WebhookDelivery.Core.Models;
using WebhookDelivery.Core.Repositories;

namespace WebhookDelivery.Router.Infrastructure;

/// <summary>
/// PostgreSQL Subscription Repository for Router Worker (READ-ONLY)
/// Router only needs to read subscriptions to match events
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
            SELECT id, event_type, callback_url, active, verified, created_at, updated_at
            FROM subscriptions
            WHERE id = @Id
        ";

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        return await connection.QuerySingleOrDefaultAsync<Subscription>(
            new CommandDefinition(sql, new { Id = id }, cancellationToken: cancellationToken)
        );
    }

    public async Task<IReadOnlyList<Subscription>> GetByEventTypeAsync(
        string eventType,
        CancellationToken cancellationToken = default)
    {
        const string sql = @"
            SELECT id, event_type, callback_url, active, verified, created_at, updated_at
            FROM subscriptions
            WHERE event_type = @EventType
            ORDER BY id ASC
        ";

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var results = await connection.QueryAsync<Subscription>(
            new CommandDefinition(sql, new { EventType = eventType }, cancellationToken: cancellationToken)
        );

        return results.ToList();
    }

    public async Task<IReadOnlyList<Subscription>> GetActiveAndVerifiedAsync(
        string eventType,
        CancellationToken cancellationToken = default)
    {
        const string sql = @"
            SELECT id, event_type, callback_url, active, verified, created_at, updated_at
            FROM subscriptions
            WHERE event_type = @EventType
              AND active = TRUE
              AND verified = TRUE
            ORDER BY id ASC
        ";

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var results = await connection.QueryAsync<Subscription>(
            new CommandDefinition(sql, new { EventType = eventType }, cancellationToken: cancellationToken)
        );

        return results.ToList();
    }

    public Task<Subscription> CreateAsync(Subscription subscription, CancellationToken cancellationToken = default)
    {
        // Router should NOT be able to create subscriptions
        throw new InvalidOperationException("Router worker does not have permission to create subscriptions");
    }

    public Task<Subscription> UpdateAsync(Subscription subscription, CancellationToken cancellationToken = default)
    {
        // Router should NOT be able to update subscriptions
        throw new InvalidOperationException("Router worker does not have permission to update subscriptions");
    }

    public Task<IReadOnlyList<Subscription>> GetAllAsync(int limit, int offset, CancellationToken cancellationToken = default)
    {
        throw new InvalidOperationException("Router worker does not list subscriptions");
    }
}
