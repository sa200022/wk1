using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using Npgsql;
using WebhookDelivery.Core.Models;
using WebhookDelivery.Core.Repositories;

namespace WebhookDelivery.Orchestrator.Infrastructure;

/// <summary>
/// PostgreSQL Saga Repository for Saga Orchestrator
/// This is the ONLY component allowed to update saga status
/// </summary>
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
        const string sql = @"
            INSERT INTO webhook_delivery_sagas
                (event_id, subscription_id, status, attempt_count, next_attempt_at, final_error_code, created_at, updated_at)
            VALUES
                (@EventId, @SubscriptionId, @Status::saga_status_enum, @AttemptCount, @NextAttemptAt, @FinalErrorCode, @CreatedAt, @UpdatedAt)
            ON CONFLICT (event_id, subscription_id)
            WHERE status <> 'DeadLettered'
            DO NOTHING
            RETURNING id;
        ";

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var id = await connection.ExecuteScalarAsync<long?>(
            new CommandDefinition(
                sql,
                new
                {
                    saga.EventId,
                    saga.SubscriptionId,
                    Status = saga.Status.ToString(),
                    saga.AttemptCount,
                    saga.NextAttemptAt,
                    saga.FinalErrorCode,
                    saga.CreatedAt,
                    saga.UpdatedAt
                },
                cancellationToken: cancellationToken
            )
        );

        if (id.HasValue)
        {
            return saga with { Id = id.Value };
        }

        const string existingSql = @"
            SELECT id, event_id, subscription_id, status, attempt_count,
                   next_attempt_at, final_error_code, created_at, updated_at
            FROM webhook_delivery_sagas
            WHERE event_id = @EventId AND subscription_id = @SubscriptionId
            ORDER BY id DESC
            LIMIT 1
        ";

        var existing = await connection.QuerySingleOrDefaultAsync<dynamic>(
            new CommandDefinition(
                existingSql,
                new { saga.EventId, saga.SubscriptionId },
                cancellationToken: cancellationToken));

        return existing != null ? MapToSaga(existing) : saga;
    }

    public async Task<WebhookDeliverySaga?> GetByIdAsync(long id, CancellationToken cancellationToken = default)
    {
        const string sql = @"
            SELECT id, event_id, subscription_id, status, attempt_count,
                   next_attempt_at, final_error_code, created_at, updated_at
            FROM webhook_delivery_sagas
            WHERE id = @Id
        ";

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var result = await connection.QuerySingleOrDefaultAsync<dynamic>(
            new CommandDefinition(sql, new { Id = id }, cancellationToken: cancellationToken)
        );

        if (result == null)
            return null;

        return MapToSaga(result);
    }

    public async Task<IReadOnlyList<WebhookDeliverySaga>> GetPendingSagasAsync(
        int limit,
        CancellationToken cancellationToken = default)
    {
        const string sql = @"
            SELECT id, event_id, subscription_id, status, attempt_count,
                   next_attempt_at, final_error_code, created_at, updated_at
            FROM webhook_delivery_sagas
            WHERE status = 'Pending'
            ORDER BY created_at ASC
            LIMIT @Limit
        ";

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var results = await connection.QueryAsync<dynamic>(
            new CommandDefinition(sql, new { Limit = limit }, cancellationToken: cancellationToken)
        );

        return results.Select(MapToSaga).ToList();
    }

    public async Task<IReadOnlyList<WebhookDeliverySaga>> GetPendingRetrySagasAsync(
        int limit,
        CancellationToken cancellationToken = default)
    {
        const string sql = @"
            SELECT id, event_id, subscription_id, status, attempt_count,
                   next_attempt_at, final_error_code, created_at, updated_at
            FROM webhook_delivery_sagas
            WHERE status = 'PendingRetry'
              AND next_attempt_at <= NOW()
            ORDER BY next_attempt_at ASC
            LIMIT @Limit
        ";

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var results = await connection.QueryAsync<dynamic>(
            new CommandDefinition(sql, new { Limit = limit }, cancellationToken: cancellationToken)
        );

        return results.Select(MapToSaga).ToList();
    }

    public async Task<IReadOnlyList<WebhookDeliverySaga>> GetInProgressSagasAsync(
        int limit,
        CancellationToken cancellationToken = default)
    {
        const string sql = @"
            SELECT id, event_id, subscription_id, status, attempt_count,
                   next_attempt_at, final_error_code, created_at, updated_at
            FROM webhook_delivery_sagas
            WHERE status = 'InProgress'
            ORDER BY updated_at ASC
            LIMIT @Limit
        ";

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var results = await connection.QueryAsync<dynamic>(
            new CommandDefinition(sql, new { Limit = limit }, cancellationToken: cancellationToken)
        );

        return results.Select(MapToSaga).ToList();
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

    private static WebhookDeliverySaga MapToSaga(dynamic row)
    {
        return new WebhookDeliverySaga
        {
            Id = row.id,
            EventId = row.event_id,
            SubscriptionId = row.subscription_id,
            Status = Enum.Parse<SagaStatus>(row.status),
            AttemptCount = row.attempt_count,
            NextAttemptAt = row.next_attempt_at,
            FinalErrorCode = row.final_error_code,
            CreatedAt = row.created_at,
            UpdatedAt = row.updated_at
        };
    }
}
