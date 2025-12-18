using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using Npgsql;
using WebhookDelivery.Core.Models;
using WebhookDelivery.Core.Repositories;

namespace WebhookDelivery.Worker.Infrastructure;

public sealed class PostgresJobRepository : IJobRepository
{
    private readonly string _connectionString;

    public PostgresJobRepository(string connectionString)
    {
        _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
    }

    public async Task<WebhookDeliveryJob> CreateAsync(
        WebhookDeliveryJob job,
        CancellationToken cancellationToken = default)
    {
        const string sql = @"
            INSERT INTO webhook_delivery_jobs
                (saga_id, status, attempt_at, lease_until)
            VALUES
                (@SagaId, @Status::job_status_enum, @AttemptAt, @LeaseUntil)
            RETURNING id;
        ";

        await using var connection = new NpgsqlConnection(_connectionString);
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

    public async Task<WebhookDeliveryJob?> GetByIdAsync(long id, CancellationToken cancellationToken = default)
    {
        const string sql = @"
            SELECT
                id AS Id,
                saga_id AS SagaId,
                status AS Status,
                lease_until AS LeaseUntil,
                attempt_at AS AttemptAt,
                response_status AS ResponseStatus,
                error_code AS ErrorCode
            FROM webhook_delivery_jobs
            WHERE id = @Id
        ";

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        return await connection.QuerySingleOrDefaultAsync<WebhookDeliveryJob>(
            new CommandDefinition(sql, new { Id = id }, cancellationToken: cancellationToken)
        );
    }

    public async Task<IReadOnlyList<WebhookDeliveryJob>> GetActiveBySagaIdAsync(
        long sagaId,
        CancellationToken cancellationToken = default)
    {
        const string sql = @"
            SELECT
                id AS Id,
                saga_id AS SagaId,
                status AS Status,
                lease_until AS LeaseUntil,
                attempt_at AS AttemptAt,
                response_status AS ResponseStatus,
                error_code AS ErrorCode
            FROM webhook_delivery_jobs
            WHERE saga_id = @SagaId
              AND status IN ('Pending', 'Leased')
        ";

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var results = await connection.QueryAsync<WebhookDeliveryJob>(
            new CommandDefinition(sql, new { SagaId = sagaId }, cancellationToken: cancellationToken)
        );

        return results.ToList();
    }

    public async Task<IReadOnlyList<WebhookDeliveryJob>> GetTerminalBySagaIdAsync(
        long sagaId,
        CancellationToken cancellationToken = default)
    {
        const string sql = @"
            SELECT
                id AS Id,
                saga_id AS SagaId,
                status AS Status,
                lease_until AS LeaseUntil,
                attempt_at AS AttemptAt,
                response_status AS ResponseStatus,
                error_code AS ErrorCode
            FROM webhook_delivery_jobs
            WHERE saga_id = @SagaId
              AND status IN ('Completed', 'Failed')
            ORDER BY attempt_at DESC
        ";

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var results = await connection.QueryAsync<WebhookDeliveryJob>(
            new CommandDefinition(sql, new { SagaId = sagaId }, cancellationToken: cancellationToken)
        );

        return results.ToList();
    }

    public async Task UpdateAsync(WebhookDeliveryJob job, CancellationToken cancellationToken = default)
    {
        const string sql = @"
            UPDATE webhook_delivery_jobs
            SET status = @Status::job_status_enum,
                lease_until = @LeaseUntil,
                response_status = @ResponseStatus,
                error_code = @ErrorCode
            WHERE id = @Id
        ";

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await connection.ExecuteAsync(
            new CommandDefinition(
                sql,
                new
                {
                    job.Id,
                    Status = job.Status.ToString(),
                    job.LeaseUntil,
                    job.ResponseStatus,
                    job.ErrorCode
                },
                cancellationToken: cancellationToken
            )
        );
    }

    public async Task<IReadOnlyList<WebhookDeliveryJob>> GetPendingJobsAsync(
        int limit,
        CancellationToken cancellationToken = default)
    {
        const string sql = @"
            SELECT
                id AS Id,
                saga_id AS SagaId,
                status AS Status,
                lease_until AS LeaseUntil,
                attempt_at AS AttemptAt,
                response_status AS ResponseStatus,
                error_code AS ErrorCode
            FROM webhook_delivery_jobs
            WHERE status = 'Pending'
            ORDER BY attempt_at ASC
            LIMIT @Limit
            FOR UPDATE SKIP LOCKED
        ";

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var results = await connection.QueryAsync<WebhookDeliveryJob>(
            new CommandDefinition(sql, new { Limit = limit }, cancellationToken: cancellationToken)
        );

        return results.ToList();
    }
}
