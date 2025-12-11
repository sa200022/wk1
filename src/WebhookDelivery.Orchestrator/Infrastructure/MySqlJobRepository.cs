using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using MySqlConnector;
using WebhookDelivery.Core.Models;
using WebhookDelivery.Core.Repositories;

namespace WebhookDelivery.Orchestrator.Infrastructure;

/// <summary>
/// MySQL Job Repository for Saga Orchestrator
/// Orchestrator creates jobs and reads their terminal results
/// </summary>
public sealed class MySqlJobRepository : IJobRepository
{
    private readonly string _connectionString;

    public MySqlJobRepository(string connectionString)
    {
        _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
    }

    public async Task<WebhookDeliveryJob?> GetByIdAsync(long id, CancellationToken cancellationToken = default)
    {
        const string sql = @"
            SELECT id, saga_id, status, lease_until, attempt_at,
                   response_status, error_code
            FROM webhook_delivery_jobs
            WHERE id = @Id
        ";

        await using var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var result = await connection.QuerySingleOrDefaultAsync<dynamic>(
            new CommandDefinition(sql, new { Id = id }, cancellationToken: cancellationToken)
        );

        if (result == null)
            return null;

        return MapToJob(result);
    }

    public async Task<IReadOnlyList<WebhookDeliveryJob>> GetActiveBySagaIdAsync(
        long sagaId,
        CancellationToken cancellationToken = default)
    {
        const string sql = @"
            SELECT id, saga_id, status, lease_until, attempt_at,
                   response_status, error_code
            FROM webhook_delivery_jobs
            WHERE saga_id = @SagaId
              AND status IN ('Pending', 'Leased')
            ORDER BY attempt_at DESC
        ";

        await using var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var results = await connection.QueryAsync<dynamic>(
            new CommandDefinition(sql, new { SagaId = sagaId }, cancellationToken: cancellationToken)
        );

        return results.Select(MapToJob).ToList();
    }

    public async Task<IReadOnlyList<WebhookDeliveryJob>> GetTerminalJobsBySagaIdAsync(
        long sagaId,
        CancellationToken cancellationToken = default)
    {
        const string sql = @"
            SELECT id, saga_id, status, lease_until, attempt_at,
                   response_status, error_code
            FROM webhook_delivery_jobs
            WHERE saga_id = @SagaId
              AND status IN ('Completed', 'Failed')
            ORDER BY attempt_at DESC
        ";

        await using var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var results = await connection.QueryAsync<dynamic>(
            new CommandDefinition(sql, new { SagaId = sagaId }, cancellationToken: cancellationToken)
        );

        return results.Select(MapToJob).ToList();
    }

    public async Task<WebhookDeliveryJob> CreateAsync(
        WebhookDeliveryJob job,
        CancellationToken cancellationToken = default)
    {
        // Use INSERT ... ON DUPLICATE KEY UPDATE for idempotency
        // unique key (saga_id, attempt_at) prevents duplicate job creation
        const string sql = @"
            INSERT INTO webhook_delivery_jobs
                (saga_id, status, attempt_at, lease_until)
            VALUES
                (@SagaId, @Status, @AttemptAt, @LeaseUntil)
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
                    job.SagaId,
                    Status = job.Status.ToString(),
                    job.AttemptAt,
                    job.LeaseUntil
                },
                cancellationToken: cancellationToken
            )
        );

        return job with { Id = id };
    }

    public async Task UpdateAsync(WebhookDeliveryJob job, CancellationToken cancellationToken = default)
    {
        // Orchestrator doesn't update jobs - workers do
        throw new InvalidOperationException("Saga Orchestrator does not update jobs - workers handle this");
    }

    public Task<IReadOnlyList<WebhookDeliveryJob>> AcquireJobsAsync(
        int limit,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken = default)
    {
        // Orchestrator doesn't acquire jobs - workers do
        throw new InvalidOperationException("Saga Orchestrator does not acquire jobs - workers handle this");
    }

    public Task<int> ResetExpiredLeasesAsync(CancellationToken cancellationToken = default)
    {
        // Orchestrator doesn't reset leases - lease cleaner does
        throw new InvalidOperationException("Saga Orchestrator does not reset leases - lease cleaner handles this");
    }

    private static WebhookDeliveryJob MapToJob(dynamic row)
    {
        return new WebhookDeliveryJob
        {
            Id = row.id,
            SagaId = row.saga_id,
            Status = Enum.Parse<JobStatus>(row.status),
            LeaseUntil = row.lease_until,
            AttemptAt = row.attempt_at,
            ResponseStatus = row.response_status,
            ErrorCode = row.error_code
        };
    }
}
