using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using MySqlConnector;
using WebhookDelivery.Core.Models;
using WebhookDelivery.Core.Repositories;

namespace WebhookDelivery.SubscriptionApi.Infrastructure;

public sealed class MySqlSubscriptionRepository : ISubscriptionRepository
{
    private readonly string _connectionString;

    public MySqlSubscriptionRepository(string connectionString)
    {
        _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
    }

    public async Task<Subscription> CreateAsync(Subscription subscription, CancellationToken cancellationToken = default)
    {
        const string sql = @"
            INSERT INTO subscriptions (event_type, callback_url, active, verified, created_at, updated_at)
            VALUES (@EventType, @CallbackUrl, @Active, @Verified, UTC_TIMESTAMP(6), UTC_TIMESTAMP(6));
            SELECT LAST_INSERT_ID();
        ";

        await using var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var id = await connection.ExecuteScalarAsync<long>(
            new CommandDefinition(sql, subscription, cancellationToken: cancellationToken)
        );

        return subscription with { Id = id };
    }

    public async Task<Subscription?> GetByIdAsync(long id, CancellationToken cancellationToken = default)
    {
        const string sql = @"
            SELECT id, event_type, callback_url, active, verified, created_at, updated_at
            FROM subscriptions
            WHERE id = @Id
        ";

        await using var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        return await connection.QuerySingleOrDefaultAsync<Subscription>(
            new CommandDefinition(sql, new { Id = id }, cancellationToken: cancellationToken)
        );
    }

    public async Task<IReadOnlyList<Subscription>> GetByEventTypeAsync(string eventType, CancellationToken cancellationToken = default)
    {
        const string sql = @"
            SELECT id, event_type, callback_url, active, verified, created_at, updated_at
            FROM subscriptions
            WHERE event_type = @EventType
        ";

        await using var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var results = await connection.QueryAsync<Subscription>(
            new CommandDefinition(sql, new { EventType = eventType }, cancellationToken: cancellationToken)
        );

        return results.ToList();
    }

    public async Task<Subscription> UpdateAsync(Subscription subscription, CancellationToken cancellationToken = default)
    {
        const string sql = @"
            UPDATE subscriptions
            SET event_type = @EventType,
                callback_url = @CallbackUrl,
                active = @Active,
                verified = @Verified,
                updated_at = UTC_TIMESTAMP(6)
            WHERE id = @Id
        ";

        await using var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await connection.ExecuteAsync(
            new CommandDefinition(sql, subscription, cancellationToken: cancellationToken)
        );

        return subscription;
    }

    public async Task<IReadOnlyList<Subscription>> GetActiveAndVerifiedAsync(string eventType, CancellationToken cancellationToken = default)
    {
        const string sql = @"
            SELECT id, event_type, callback_url, active, verified, created_at, updated_at
            FROM subscriptions
            WHERE event_type = @EventType
              AND active = 1
              AND verified = 1
        ";

        await using var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var results = await connection.QueryAsync<Subscription>(
            new CommandDefinition(sql, new { EventType = eventType }, cancellationToken: cancellationToken)
        );

        return results.ToList();
    }
}
