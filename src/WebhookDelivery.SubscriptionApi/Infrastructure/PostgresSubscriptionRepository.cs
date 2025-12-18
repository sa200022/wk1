using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using Npgsql;
using WebhookDelivery.Core.Models;
using WebhookDelivery.Core.Repositories;

namespace WebhookDelivery.SubscriptionApi.Infrastructure;

public sealed class PostgresSubscriptionRepository : ISubscriptionRepository
{
    private readonly string _connectionString;

    public PostgresSubscriptionRepository(string connectionString)
    {
        _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
    }

    public async Task<Subscription> CreateAsync(Subscription subscription, CancellationToken cancellationToken = default)
    {
        const string sql = @"
            INSERT INTO subscriptions (event_type, callback_url, active, verified, created_at, updated_at)
            VALUES (@EventType, @CallbackUrl, @Active, @Verified, NOW(), NOW())
            RETURNING id;
        ";

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var id = await connection.ExecuteScalarAsync<long>(
            new CommandDefinition(sql, subscription, cancellationToken: cancellationToken)
        );

        return subscription with { Id = id };
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

    public async Task<IReadOnlyList<Subscription>> GetByEventTypeAsync(string eventType, CancellationToken cancellationToken = default)
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
            WHERE event_type = @EventType
        ";

        await using var connection = new NpgsqlConnection(_connectionString);
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
                updated_at = NOW()
            WHERE id = @Id
        ";

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await connection.ExecuteAsync(
            new CommandDefinition(sql, subscription, cancellationToken: cancellationToken)
        );

        return subscription;
    }

    public async Task<IReadOnlyList<Subscription>> GetAllAsync(int limit, int offset, CancellationToken cancellationToken = default)
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
            ORDER BY id ASC
            LIMIT @Limit OFFSET @Offset
        ";

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var results = await connection.QueryAsync<Subscription>(
            new CommandDefinition(sql, new { Limit = limit, Offset = offset }, cancellationToken: cancellationToken)
        );

        return results.ToList();
    }

    public async Task<IReadOnlyList<Subscription>> GetActiveAndVerifiedAsync(string eventType, CancellationToken cancellationToken = default)
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
            WHERE event_type = @EventType
              AND active = TRUE
              AND verified = TRUE
        ";

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var results = await connection.QueryAsync<Subscription>(
            new CommandDefinition(sql, new { EventType = eventType }, cancellationToken: cancellationToken)
        );

        return results.ToList();
    }
}
