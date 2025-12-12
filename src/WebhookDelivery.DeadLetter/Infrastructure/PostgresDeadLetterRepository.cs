using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using Npgsql;
using WebhookDelivery.Core.Models;
using WebhookDelivery.Core.Repositories;
using DeadLetterModel = WebhookDelivery.Core.Models.DeadLetter;

namespace WebhookDelivery.DeadLetter.Infrastructure;

public sealed class PostgresDeadLetterRepository : IDeadLetterRepository
{
    private readonly string _connectionString;

    public PostgresDeadLetterRepository(string connectionString)
    {
        _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
    }

    public async Task<DeadLetterModel> CreateAsync(
        DeadLetterModel deadLetter,
        CancellationToken cancellationToken = default)
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

    public async Task<DeadLetterModel?> GetByIdAsync(long id, CancellationToken cancellationToken = default)
    {
        const string sql = @"
            SELECT id, saga_id, event_id, subscription_id, final_error_code, failed_at, payload_snapshot
            FROM dead_letters
            WHERE id = @Id
        ";

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        return await connection.QuerySingleOrDefaultAsync<DeadLetterModel>(
            new CommandDefinition(sql, new { Id = id }, cancellationToken: cancellationToken)
        );
    }

    public async Task<DeadLetterModel?> GetBySagaIdAsync(long sagaId, CancellationToken cancellationToken = default)
    {
        const string sql = @"
            SELECT id, saga_id, event_id, subscription_id, final_error_code, failed_at, payload_snapshot
            FROM dead_letters
            WHERE saga_id = @SagaId
        ";

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        return await connection.QuerySingleOrDefaultAsync<DeadLetterModel>(
            new CommandDefinition(sql, new { SagaId = sagaId }, cancellationToken: cancellationToken)
        );
    }

    public async Task<IReadOnlyList<DeadLetterModel>> GetAllAsync(
        int limit,
        int offset,
        CancellationToken cancellationToken = default)
    {
        const string sql = @"
            SELECT id, saga_id, event_id, subscription_id, final_error_code, failed_at, payload_snapshot
            FROM dead_letters
            ORDER BY failed_at DESC
            LIMIT @Limit OFFSET @Offset
        ";

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var results = await connection.QueryAsync<DeadLetterModel>(
            new CommandDefinition(
                sql,
                new { Limit = limit, Offset = offset },
                cancellationToken: cancellationToken)
        );

        return results.ToList();
    }
}
