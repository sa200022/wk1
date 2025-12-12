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
/// MySQL Saga Repository for Worker (READ-ONLY)
/// Worker only needs to read saga information, never update
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

        await using var connection = new NpgsqlConnection(_connectionString);
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

    public Task<IReadOnlyList<WebhookDeliverySaga>> GetPendingSagasAsync(
        int limit,
        CancellationToken cancellationToken = default)
    {
        // Worker doesn't query pending sagas
        throw new InvalidOperationException("Worker does not query pending sagas");
    }

    public Task<IReadOnlyList<WebhookDeliverySaga>> GetPendingRetrySagasAsync(
        int limit,
        CancellationToken cancellationToken = default)
    {
        // Worker doesn't query retry sagas
        throw new InvalidOperationException("Worker does not query pending retry sagas");
    }

    public Task<IReadOnlyList<WebhookDeliverySaga>> GetInProgressSagasAsync(
        int limit,
        CancellationToken cancellationToken = default)
    {
        // Worker doesn't query InProgress sagas
        throw new InvalidOperationException("Worker does not query InProgress sagas");
    }

    public Task UpdateAsync(WebhookDeliverySaga saga, CancellationToken cancellationToken = default)
    {
        // Worker MUST NOT update sagas - this is critical to the architecture
        throw new InvalidOperationException("Worker does not have permission to update sagas - only Saga Orchestrator can do this");
    }

    public Task<WebhookDeliverySaga> CreateIdempotentAsync(
        WebhookDeliverySaga saga,
        CancellationToken cancellationToken = default)
    {
        // Worker cannot create sagas
        throw new InvalidOperationException("Worker does not have permission to create sagas");
    }
}
