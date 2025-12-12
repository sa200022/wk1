using System;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Dapper;
using MySqlConnector;
using Xunit;

namespace WebhookDelivery.IntegrationTests;

/// <summary>
/// Integration tests for idempotency guarantees
/// Tests scenarios from r03 requirements
/// </summary>
public class IdempotencyTests : TestBase
{
    [Fact]
    public async Task DuplicateEventIngestion_ShouldNotCreateDuplicateEvents()
    {
        // Arrange: Same external event ID ingested twice
        var externalEventId = Guid.NewGuid().ToString();
        var eventType = "order.created";
        var payload = JsonDocument.Parse("{\"orderId\": 123}");

        // Act: Ingest event twice with same external_event_id
        var eventId1 = await InsertTestEventAsync(externalEventId, eventType, payload);

        // Second insert should fail due to unique constraint or return same ID
        await using var conn = new MySqlConnection(ConnectionString);
        var sql = @"
            INSERT INTO events (external_event_id, event_type, payload, created_at)
            VALUES (@ExternalEventId, @EventType, @Payload, @CreatedAt)
            ON DUPLICATE KEY UPDATE id = LAST_INSERT_ID(id)";

        var eventId2 = await conn.ExecuteScalarAsync<long>(sql, new
        {
            ExternalEventId = externalEventId,
            EventType = eventType,
            Payload = payload.RootElement.GetRawText(),
            CreatedAt = DateTime.UtcNow
        });

        // Assert: Only one event record exists
        Assert.Equal(eventId1, eventId2);

        var count = await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM events WHERE external_event_id = @ExternalEventId",
            new { ExternalEventId = externalEventId });
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task DuplicateRouting_ShouldNotCreateDuplicateSagas()
    {
        // Arrange: Same (event_id, subscription_id) routed multiple times
        var eventId = await InsertTestEventAsync(
            Guid.NewGuid().ToString(),
            "order.created",
            JsonDocument.Parse("{\"orderId\": 456}"));

        var subscriptionId = await InsertTestSubscriptionAsync(
            "order.created",
            "https://example.com/webhook");

        // Act: Router processes same (event_id, subscription_id) twice
        var sagaId1 = await InsertTestSagaAsync(eventId, subscriptionId);

        await using var conn = new MySqlConnection(ConnectionString);
        var sql = @"
            INSERT INTO webhook_delivery_sagas
                (event_id, subscription_id, status, attempt_count, next_attempt_at, created_at, updated_at)
            VALUES
                (@EventId, @SubscriptionId, @Status, @AttemptCount, @NextAttemptAt, @CreatedAt, @UpdatedAt)
            ON DUPLICATE KEY UPDATE id = LAST_INSERT_ID(id)";

        var sagaId2 = await conn.ExecuteScalarAsync<long>(sql, new
        {
            EventId = eventId,
            SubscriptionId = subscriptionId,
            Status = "Pending",
            AttemptCount = 0,
            NextAttemptAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });

        // Assert: Only one saga exists for (event_id, subscription_id)
        Assert.Equal(sagaId1, sagaId2);

        var count = await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM webhook_delivery_sagas WHERE event_id = @EventId AND subscription_id = @SubscriptionId",
            new { EventId = eventId, SubscriptionId = subscriptionId });
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task DuplicateJobResult_ShouldOnlyUpdateSagaOnce()
    {
        // Arrange: Create saga and job
        var eventId = await InsertTestEventAsync(
            Guid.NewGuid().ToString(),
            "order.created",
            JsonDocument.Parse("{\"orderId\": 789}"));

        var subscriptionId = await InsertTestSubscriptionAsync(
            "order.created",
            "https://example.com/webhook");

        var sagaId = await InsertTestSagaAsync(eventId, subscriptionId, "InProgress", 1);
        var jobId = await InsertTestJobAsync(sagaId, status: "Completed", responseStatus: 200);

        await using var conn = new MySqlConnection(ConnectionString);

        // Act: Apply job success twice (simulating duplicate processing)
        var updateSql = @"
            UPDATE webhook_delivery_sagas
            SET status = @Status, updated_at = @UpdatedAt
            WHERE id = @Id AND status NOT IN ('Completed', 'DeadLettered')";

        var firstUpdate = await conn.ExecuteAsync(updateSql, new
        {
            Id = sagaId,
            Status = "Completed",
            UpdatedAt = DateTime.UtcNow
        });

        var secondUpdate = await conn.ExecuteAsync(updateSql, new
        {
            Id = sagaId,
            Status = "Completed",
            UpdatedAt = DateTime.UtcNow
        });

        // Assert: First update succeeds, second is no-op (terminal state protection)
        Assert.Equal(1, firstUpdate);
        Assert.Equal(0, secondUpdate); // Terminal state protection blocked second update

        var saga = await conn.QuerySingleAsync<dynamic>(
            "SELECT status FROM webhook_delivery_sagas WHERE id = @Id",
            new { Id = sagaId });
        Assert.Equal("Completed", saga.status);
    }

    [Fact]
    public async Task WorkerCrashBeforeJobUpdate_ShouldResetViaLeaseCleaner()
    {
        // Arrange: Create job with expired lease (simulating worker crash)
        var eventId = await InsertTestEventAsync(
            Guid.NewGuid().ToString(),
            "order.created",
            JsonDocument.Parse("{\"orderId\": 999}"));

        var subscriptionId = await InsertTestSubscriptionAsync(
            "order.created",
            "https://example.com/webhook");

        var sagaId = await InsertTestSagaAsync(eventId, subscriptionId, "InProgress", 1);

        await using var conn = new MySqlConnection(ConnectionString);

        // Insert job with expired lease (5 minutes ago)
        var expiredLeaseTime = DateTime.UtcNow.AddMinutes(-5);
        var jobId = await InsertTestJobAsync(
            sagaId,
            status: "Leased",
            leaseUntil: expiredLeaseTime,
            attemptAt: DateTime.UtcNow.AddMinutes(-5));

        // Act: Simulate lease cleaner reset
        var resetSql = @"
            UPDATE webhook_delivery_jobs
            SET status = 'Pending', lease_until = NULL
            WHERE status = 'Leased' AND lease_until < @Now";

        var resetCount = await conn.ExecuteAsync(resetSql, new { Now = DateTime.UtcNow });

        // Assert: Job reset to Pending
        Assert.Equal(1, resetCount);

        var job = await conn.QuerySingleAsync<dynamic>(
            "SELECT status, lease_until FROM webhook_delivery_jobs WHERE id = @Id",
            new { Id = jobId });

        Assert.Equal("Pending", job.status);
        Assert.Null(job.lease_until);
    }

    [Fact]
    public async Task RetryUntilDeadLetter_ShouldRespectMaxRetryLimit()
    {
        // Arrange: Create saga with attempt_count at max retry limit
        var eventId = await InsertTestEventAsync(
            Guid.NewGuid().ToString(),
            "order.created",
            JsonDocument.Parse("{\"orderId\": 111}"));

        var subscriptionId = await InsertTestSubscriptionAsync(
            "order.created",
            "https://example.com/webhook");

        const int maxRetryLimit = 5;
        var sagaId = await InsertTestSagaAsync(eventId, subscriptionId, "InProgress", maxRetryLimit);

        // Insert final failed job
        var jobId = await InsertTestJobAsync(
            sagaId,
            status: "Failed",
            responseStatus: 500,
            errorCode: "Internal Server Error");

        await using var conn = new MySqlConnection(ConnectionString);

        // Act: Simulate orchestrator processing failure at max retry
        var updateSql = @"
            UPDATE webhook_delivery_sagas
            SET status = 'DeadLettered', updated_at = @UpdatedAt
            WHERE id = @Id AND attempt_count >= @MaxRetryLimit";

        var updateCount = await conn.ExecuteAsync(updateSql, new
        {
            Id = sagaId,
            MaxRetryLimit = maxRetryLimit,
            UpdatedAt = DateTime.UtcNow
        });

        // Assert: Saga transitioned to DeadLettered
        Assert.Equal(1, updateCount);

        var saga = await conn.QuerySingleAsync<dynamic>(
            "SELECT status, attempt_count FROM webhook_delivery_sagas WHERE id = @Id",
            new { Id = sagaId });

        Assert.Equal("DeadLettered", saga.status);
        Assert.Equal(maxRetryLimit, saga.attempt_count);
    }

    [Fact]
    public async Task Requeue_ShouldCreateNewSaga_NotModifyOldSaga()
    {
        // Arrange: Create dead-lettered saga
        var eventId = await InsertTestEventAsync(
            Guid.NewGuid().ToString(),
            "order.created",
            JsonDocument.Parse("{\"orderId\": 222}"));

        var subscriptionId = await InsertTestSubscriptionAsync(
            "order.created",
            "https://example.com/webhook");

        var oldSagaId = await InsertTestSagaAsync(eventId, subscriptionId, "DeadLettered", 5);

        await using var conn = new MySqlConnection(ConnectionString);

        // Read old saga state before requeue
        var oldSaga = await conn.QuerySingleAsync<dynamic>(
            "SELECT status, attempt_count FROM webhook_delivery_sagas WHERE id = @Id",
            new { Id = oldSagaId });

        // Act: Requeue - create NEW saga
        var newSagaSql = @"
            INSERT INTO webhook_delivery_sagas
                (event_id, subscription_id, status, attempt_count, next_attempt_at, created_at, updated_at)
            VALUES
                (@EventId, @SubscriptionId, 'Pending', 0, @NextAttemptAt, @CreatedAt, @UpdatedAt);
            SELECT LAST_INSERT_ID();";

        var newSagaId = await conn.ExecuteScalarAsync<long>(newSagaSql, new
        {
            EventId = eventId,
            SubscriptionId = subscriptionId,
            NextAttemptAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });

        // Assert: New saga created with fresh state
        Assert.NotEqual(oldSagaId, newSagaId);

        var newSaga = await conn.QuerySingleAsync<dynamic>(
            "SELECT status, attempt_count FROM webhook_delivery_sagas WHERE id = @Id",
            new { Id = newSagaId });

        Assert.Equal("Pending", newSaga.status);
        Assert.Equal(0, newSaga.attempt_count);

        // Old saga unchanged
        var oldSagaAfter = await conn.QuerySingleAsync<dynamic>(
            "SELECT status, attempt_count FROM webhook_delivery_sagas WHERE id = @Id",
            new { Id = oldSagaId });

        Assert.Equal("DeadLettered", oldSagaAfter.status);
        Assert.Equal(5, oldSagaAfter.attempt_count);
    }
}
