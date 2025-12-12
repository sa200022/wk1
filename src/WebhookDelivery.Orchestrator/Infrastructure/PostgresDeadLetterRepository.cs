using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using Npgsql;
using WebhookDelivery.Core.Models;
using WebhookDelivery.Core.Repositories;

namespace WebhookDelivery.Orchestrator.Infrastructure;

/// <summary>
/// PostgreSQL Dead Letter Repository for Saga Orchestrator
/// Orchestrator creates dead letter entries when sagas are dead-lettered
/// </summary>
public sealed class PostgresDeadLetterRepository : IDeadLetterRepository
{
    private readonly string _connectionString;

    public PostgresDeadLetterRepository(string connectionString)
    {
        _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
    }

    public async Task<DeadLetter> CreateAsync(DeadLetter deadLetter, CancellationToken cancellationToken = default)
    {
        const string sql = @"
            INSERT INTO dead_letters
                (saga_id, event_id, subscription_id, final_error_code, failed_at, payload_snapshot)
            VALUES
                (@SagaId, @EventId, @SubscriptionId, @FinalErrorCode, @FailedAt, @PayloadSnapshot)
            RETURNING id;
        ";

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        // Serialize payload snapshot to JSON
        var payloadJson = JsonSerializer.Serialize(deadLetter.PayloadSnapshot);

        var id = await connection.ExecuteScalarAsync<long>(
            new CommandDefinition(
                sql,
                new
                {
                    deadLetter.SagaId,
                    deadLetter.EventId,
                    deadLetter.SubscriptionId,
                    deadLetter.FinalErrorCode,
                    deadLetter.FailedAt,
                    PayloadSnapshot = payloadJson
                },
                cancellationToken: cancellationToken
            )
        );

        return deadLetter with { Id = id };
    }

    public async Task<DeadLetter?> GetByIdAsync(long id, CancellationToken cancellationToken = default)
    {
        const string sql = @"
            SELECT id, saga_id, event_id, subscription_id, final_error_code, failed_at, payload_snapshot
            FROM dead_letters
            WHERE id = @Id
        ";

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var result = await connection.QuerySingleOrDefaultAsync<dynamic>(
            new CommandDefinition(sql, new { Id = id }, cancellationToken: cancellationToken)
        );

        if (result == null)
            return null;

        return new DeadLetter
        {
            Id = result.id,
            SagaId = result.saga_id,
            EventId = result.event_id,
            SubscriptionId = result.subscription_id,
            FinalErrorCode = result.final_error_code,
            FailedAt = result.failed_at,
            PayloadSnapshot = JsonSerializer.Deserialize<System.Collections.Generic.Dictionary<string, object>>(result.payload_snapshot)
        };
    }

    public Task<DeadLetter?> GetBySagaIdAsync(long sagaId, CancellationToken cancellationToken = default)
    {
        // Orchestrator doesn't need this method
        throw new NotImplementedException("Orchestrator does not query dead letters by saga ID");
    }

    public Task<IReadOnlyList<DeadLetter>> GetAllAsync(
        int limit,
        int offset,
        CancellationToken cancellationToken = default)
    {
        // Orchestrator doesn't need this method
        throw new NotImplementedException("Orchestrator does not query all dead letters");
    }
}
