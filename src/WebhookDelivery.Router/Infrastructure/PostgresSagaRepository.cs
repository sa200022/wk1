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

public sealed class PostgresSagaRepository : ISagaRepository
{
    private readonly string _connectionString;

    public PostgresSagaRepository(string connectionString)
    {
        _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
    }

    public async Task<WebhookDeliverySaga> CreateIdempotentAsync(
        WebhookDeliverySaga saga,
        CancellationToken cancellationToken = default)
    {
        // Using ON CONFLICT for idempotency: if (event_id, subscription_id) exists, return existing id
        const string sql = @"
            WITH upsert AS (
                INSERT INTO webhook_delivery_sagas
                    (event_id, subscription_id, status, attempt_count, next_attempt_at, created_at, updated_at)
                VALUES
                    (@EventId, @SubscriptionId, @Status::saga_status_enum, @AttemptCount, @NextAttemptAt, NOW(), NOW())
                ON CONFLICT (event_id, subscription_id) WHERE status <> 'DeadLettered'
                DO UPDATE SET event_id = EXCLUDED.event_id
                RETURNING id
            )
            SELECT id FROM upsert
            UNION ALL
            SELECT id FROM webhook_delivery_sagas WHERE event_id = @EventId AND subscription_id = @SubscriptionId
            LIMIT 1;
        ";

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var id = await connection.ExecuteScalarAsync<long>(
            new CommandDefinition(
                sql,
                new
                {
                    saga.EventId,
                    saga.SubscriptionId,
                    Status = saga.Status.ToString(),
                    saga.AttemptCount,
                    saga.NextAttemptAt
                },
                cancellationToken: cancellationToken
            )
        );

        return saga with { Id = id };
    }

    public async Task<WebhookDeliverySaga?> GetByIdAsync(long id, CancellationToken cancellationToken = default)
    {
        const string sql = @"
            SELECT
                id AS Id,
                event_id AS EventId,
                subscription_id AS SubscriptionId,
                status AS Status,
                attempt_count AS AttemptCount,
                next_attempt_at AS NextAttemptAt,
                final_error_code AS FinalErrorCode,
                created_at AS CreatedAt,
                updated_at AS UpdatedAt
            FROM webhook_delivery_sagas
            WHERE id = @Id
        ";

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        return await connection.QuerySingleOrDefaultAsync<WebhookDeliverySaga>(
            new CommandDefinition(sql, new { Id = id }, cancellationToken: cancellationToken)
        );
    }

    public async Task<IReadOnlyList<WebhookDeliverySaga>> GetPendingSagasAsync(
        int limit,
        CancellationToken cancellationToken = default)
    {
        const string sql = @"
            SELECT
                id AS Id,
                event_id AS EventId,
                subscription_id AS SubscriptionId,
                status AS Status,
                attempt_count AS AttemptCount,
                next_attempt_at AS NextAttemptAt,
                final_error_code AS FinalErrorCode,
                created_at AS CreatedAt,
                updated_at AS UpdatedAt
            FROM webhook_delivery_sagas
            WHERE status = 'Pending'
            ORDER BY created_at ASC
            LIMIT @Limit
        ";

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var results = await connection.QueryAsync<WebhookDeliverySaga>(
            new CommandDefinition(sql, new { Limit = limit }, cancellationToken: cancellationToken)
        );

        return results.ToList();
    }

    public async Task<IReadOnlyList<WebhookDeliverySaga>> GetPendingRetrySagasAsync(
        int limit,
        CancellationToken cancellationToken = default)
    {
        const string sql = @"
            SELECT
                id AS Id,
                event_id AS EventId,
                subscription_id AS SubscriptionId,
                status AS Status,
                attempt_count AS AttemptCount,
                next_attempt_at AS NextAttemptAt,
                final_error_code AS FinalErrorCode,
                created_at AS CreatedAt,
                updated_at AS UpdatedAt
            FROM webhook_delivery_sagas
            WHERE status = 'PendingRetry'
              AND next_attempt_at <= NOW()
            ORDER BY next_attempt_at ASC
            LIMIT @Limit
        ";

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var results = await connection.QueryAsync<WebhookDeliverySaga>(
            new CommandDefinition(sql, new { Limit = limit }, cancellationToken: cancellationToken)
        );

        return results.ToList();
    }

    public Task<IReadOnlyList<WebhookDeliverySaga>> GetInProgressSagasAsync(
        int limit,
        CancellationToken cancellationToken = default)
    {
        // Router doesn't query InProgress sagas
        throw new InvalidOperationException("Router worker does not query InProgress sagas");
    }

    public async Task UpdateAsync(WebhookDeliverySaga saga, CancellationToken cancellationToken = default)
    {
        const string sql = @"
            UPDATE webhook_delivery_sagas
            SET status = @Status::saga_status_enum,
                attempt_count = @AttemptCount,
                next_attempt_at = @NextAttemptAt,
                final_error_code = @FinalErrorCode,
                updated_at = NOW()
            WHERE id = @Id
        ";

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await connection.ExecuteAsync(
            new CommandDefinition(
                sql,
                new
                {
                    saga.Id,
                    Status = saga.Status.ToString(),
                    saga.AttemptCount,
                    saga.NextAttemptAt,
                    saga.FinalErrorCode
                },
                cancellationToken: cancellationToken
            )
        );
    }
}
