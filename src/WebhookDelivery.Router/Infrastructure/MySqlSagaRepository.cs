using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using MySqlConnector;
using WebhookDelivery.Core.Models;
using WebhookDelivery.Core.Repositories;

namespace WebhookDelivery.Router.Infrastructure;

public sealed class MySqlSagaRepository : ISagaRepository
{
    private readonly string _connectionString;

    public MySqlSagaRepository(string connectionString)
    {
        _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
    }

    public async Task<WebhookDeliverySaga> CreateIdempotentAsync(
        WebhookDeliverySaga saga,
        CancellationToken cancellationToken = default)
    {
        // Using INSERT ... ON DUPLICATE KEY UPDATE for idempotency
        // If (event_id, subscription_id) already exists, no update is performed
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

        return await connection.QuerySingleOrDefaultAsync<WebhookDeliverySaga>(
            new CommandDefinition(sql, new { Id = id }, cancellationToken: cancellationToken)
        );
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

        await using var connection = new MySqlConnection(_connectionString);
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
            SELECT id, event_id, subscription_id, status, attempt_count,
                   next_attempt_at, final_error_code, created_at, updated_at
            FROM webhook_delivery_sagas
            WHERE status = 'PendingRetry'
              AND next_attempt_at <= UTC_TIMESTAMP(6)
            ORDER BY next_attempt_at ASC
            LIMIT @Limit
        ";

        await using var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var results = await connection.QueryAsync<WebhookDeliverySaga>(
            new CommandDefinition(sql, new { Limit = limit }, cancellationToken: cancellationToken)
        );

        return results.ToList();
    }

    public async Task UpdateAsync(WebhookDeliverySaga saga, CancellationToken cancellationToken = default)
    {
        const string sql = @"
            UPDATE webhook_delivery_sagas
            SET status = @Status,
                attempt_count = @AttemptCount,
                next_attempt_at = @NextAttemptAt,
                final_error_code = @FinalErrorCode,
                updated_at = UTC_TIMESTAMP(6)
            WHERE id = @Id
        ";

        await using var connection = new MySqlConnection(_connectionString);
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
