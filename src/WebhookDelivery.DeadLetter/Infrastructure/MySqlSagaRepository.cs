using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using MySqlConnector;
using WebhookDelivery.Core.Models;
using WebhookDelivery.Core.Repositories;

namespace WebhookDelivery.DeadLetter.Infrastructure;

/// <summary>
/// MySQL Saga Repository for Dead Letter Service
/// Dead Letter can create NEW sagas for requeue, but cannot update existing ones
/// </summary>
public sealed class MySqlSagaRepository : ISagaRepository
{
    private readonly string _connectionString;

    public MySqlSagaRepository(string connectionString)
    {
        _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
    }

    public async Task<WebhookDeliverySaga?> GetByIdAsync(long id, CancellationToken cancellationToken = default)
    {
        const string sql = @"
            SELECT id, event_id, subscription_id, status, attempt_count,
                   next_attempt_at, final_error_code, created_at, updated_at
            FROM webhook_delivery_sagas
            WHERE id = @Id
        ";

        await using var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var result = await connection.QuerySingleOrDefaultAsync<dynamic>(
            new CommandDefinition(sql, new { Id = id }, cancellationToken: cancellationToken)
        );

        if (result == null)
            return null;

        return new WebhookDeliverySaga
        {
            Id = result.id,
            EventId = result.event_id,
            SubscriptionId = result.subscription_id,
            Status = Enum.Parse<SagaStatus>(result.status),
            AttemptCount = result.attempt_count,
            NextAttemptAt = result.next_attempt_at,
            FinalErrorCode = result.final_error_code,
            CreatedAt = result.created_at,
            UpdatedAt = result.updated_at
        };
    }

    public async Task<WebhookDeliverySaga> CreateIdempotentAsync(
        WebhookDeliverySaga saga,
        CancellationToken cancellationToken = default)
    {
        // For requeue: create a brand-new saga with same event/subscription
        // Use INSERT ... ON DUPLICATE KEY UPDATE for idempotency
        const string sql = @"
            INSERT INTO webhook_delivery_sagas
                (event_id, subscription_id, status, attempt_count, next_attempt_at, created_at, updated_at)
            VALUES
                (@EventId, @SubscriptionId, @Status, @AttemptCount, @NextAttemptAt, UTC_TIMESTAMP(6), UTC_TIMESTAMP(6))
            ON DUPLICATE KEY UPDATE
                id = LAST_INSERT_ID(id);
            SELECT LAST_INSERT_ID();
        ";

        await using var connection = new MySqlConnection(_connectionString);
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

    public Task<IReadOnlyList<WebhookDeliverySaga>> GetPendingSagasAsync(
        int limit,
        CancellationToken cancellationToken = default)
    {
        // Dead Letter service doesn't query pending sagas
        throw new InvalidOperationException("Dead Letter service does not query pending sagas");
    }

    public Task<IReadOnlyList<WebhookDeliverySaga>> GetPendingRetrySagasAsync(
        int limit,
        CancellationToken cancellationToken = default)
    {
        // Dead Letter service doesn't query retry sagas
        throw new InvalidOperationException("Dead Letter service does not query pending retry sagas");
    }

    public Task<IReadOnlyList<WebhookDeliverySaga>> GetInProgressSagasAsync(
        int limit,
        CancellationToken cancellationToken = default)
    {
        // Dead Letter service doesn't query InProgress sagas
        throw new InvalidOperationException("Dead Letter service does not query InProgress sagas");
    }

    public Task UpdateAsync(WebhookDeliverySaga saga, CancellationToken cancellationToken = default)
    {
        // Dead Letter MUST NOT update existing sagas
        // It can only create NEW sagas for requeue
        throw new InvalidOperationException("Dead Letter service cannot update sagas - it can only create new ones for requeue");
    }
}
