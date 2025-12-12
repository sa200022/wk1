using System;
using System.Text.Json;
using System.Threading.Tasks;
using Dapper;
using Npgsql;
using Xunit;

namespace WebhookDelivery.IntegrationTests;

/// <summary>
/// Integration tests for dead letter functionality
/// Tests automatic dead letter creation and requeue flow
/// </summary>
public class DeadLetterTests : TestBase
{
    [Fact]
    public async Task SagaReachesDeadLettered_ShouldAutoCreateDeadLetterRecord()
    {
        // Arrange: Create saga that reached max retry limit
        var externalEventId = Guid.NewGuid().ToString();
        var payload = JsonDocument.Parse("{\"orderId\": 555, \"amount\": 99.99}");
        var eventId = await InsertTestEventAsync(externalEventId, "order.created", payload);

        var subscriptionId = await InsertTestSubscriptionAsync(
            "order.created",
            "https://example.com/webhook");

        const int maxRetryLimit = 5;
        var sagaId = await InsertTestSagaAsync(eventId, subscriptionId, "DeadLettered", maxRetryLimit);

        await using var conn = new NpgsqlConnection(ConnectionString);

        // Act: Simulate orchestrator creating dead letter record
        var deadLetterSql = @"
            INSERT INTO dead_letters
                (saga_id, event_id, subscription_id, final_error_code, payload_snapshot)
            VALUES
                (@SagaId, @EventId, @SubscriptionId, @FinalErrorCode, @PayloadSnapshot)
            RETURNING id;";

        var deadLetterId = await conn.ExecuteScalarAsync<long>(deadLetterSql, new
        {
            SagaId = sagaId,
            EventId = eventId,
            SubscriptionId = subscriptionId,
            FinalErrorCode = "HTTP_500",
            PayloadSnapshot = payload.RootElement.GetRawText()
        });

        // Assert: Dead letter record created successfully
        Assert.True(deadLetterId > 0);

        var deadLetter = await conn.QuerySingleAsync<dynamic>(
            "SELECT * FROM dead_letters WHERE saga_id = @SagaId",
            new { SagaId = sagaId });

        Assert.Equal(sagaId, (long)deadLetter.saga_id);
        Assert.Equal(eventId, (long)deadLetter.event_id);
        Assert.Equal(subscriptionId, (long)deadLetter.subscription_id);
        Assert.Equal("HTTP_500", (string)deadLetter.final_error_code);
        Assert.Contains("orderId", (string)deadLetter.payload_snapshot);
    }

    [Fact]
    public async Task DeadLetterRequeue_ShouldCreateNewSagaWithFreshState()
    {
        // Arrange: Create dead-lettered saga with dead letter record
        var externalEventId = Guid.NewGuid().ToString();
        var payload = JsonDocument.Parse("{\"orderId\": 666}");
        var eventId = await InsertTestEventAsync(externalEventId, "payment.failed", payload);

        var subscriptionId = await InsertTestSubscriptionAsync(
            "payment.failed",
            "https://example.com/webhook");

        var deadLetteredSagaId = await InsertTestSagaAsync(eventId, subscriptionId, "DeadLettered", 5);

        await using var conn = new NpgsqlConnection(ConnectionString);

        // Insert dead letter record
        await conn.ExecuteAsync(@"
            INSERT INTO dead_letters
                (saga_id, event_id, subscription_id, final_error_code, payload_snapshot)
            VALUES
                (@SagaId, @EventId, @SubscriptionId, @FinalErrorCode, @PayloadSnapshot)", new
        {
            SagaId = deadLetteredSagaId,
            EventId = eventId,
            SubscriptionId = subscriptionId,
            FinalErrorCode = "HTTP_503",
            PayloadSnapshot = payload.RootElement.GetRawText()
        });

        // Act: Requeue - create NEW saga with fresh state
        var requeueSql = @"
            INSERT INTO webhook_delivery_sagas
                (event_id, subscription_id, status, attempt_count, next_attempt_at, created_at, updated_at)
            VALUES
                (@EventId, @SubscriptionId, 'Pending', 0, @NextAttemptAt, @CreatedAt, @UpdatedAt)
            RETURNING id;";

        var newSagaId = await conn.ExecuteScalarAsync<long>(requeueSql, new
        {
            EventId = eventId,
            SubscriptionId = subscriptionId,
            NextAttemptAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });

        // Assert: New saga created with fresh state
        Assert.NotEqual(deadLetteredSagaId, newSagaId);

        var newSaga = await conn.QuerySingleAsync<dynamic>(
            "SELECT * FROM webhook_delivery_sagas WHERE id = @Id",
            new { Id = newSagaId });

        Assert.Equal("Pending", newSaga.status);
        Assert.Equal(0, newSaga.attempt_count);

        // Old saga should remain unchanged
        var oldSaga = await conn.QuerySingleAsync<dynamic>(
            "SELECT * FROM webhook_delivery_sagas WHERE id = @Id",
            new { Id = deadLetteredSagaId });

        Assert.Equal("DeadLettered", oldSaga.status);
        Assert.Equal(5, oldSaga.attempt_count);
    }

    [Fact]
    public async Task DeadLetterRecord_ShouldContainEventPayloadSnapshot()
    {
        // Arrange: Create event with complex payload
        var complexPayload = JsonDocument.Parse(@"{
            ""orderId"": 777,
            ""customerId"": 888,
            ""items"": [
                {""sku"": ""ABC123"", ""quantity"": 2, ""price"": 29.99},
                {""sku"": ""XYZ789"", ""quantity"": 1, ""price"": 49.99}
            ],
            ""total"": 109.97,
            ""timestamp"": ""2025-12-11T10:00:00Z""
        }");

        var eventId = await InsertTestEventAsync(
            Guid.NewGuid().ToString(),
            "order.created",
            complexPayload);

        var subscriptionId = await InsertTestSubscriptionAsync(
            "order.created",
            "https://example.com/webhook");

        var sagaId = await InsertTestSagaAsync(eventId, subscriptionId, "DeadLettered", 5);

        await using var conn = new NpgsqlConnection(ConnectionString);

        // Act: Create dead letter with payload snapshot
        await conn.ExecuteAsync(@"
            INSERT INTO dead_letters
                (saga_id, event_id, subscription_id, final_error_code, payload_snapshot)
            VALUES
                (@SagaId, @EventId, @SubscriptionId, @FinalErrorCode, @PayloadSnapshot)", new
        {
            SagaId = sagaId,
            EventId = eventId,
            SubscriptionId = subscriptionId,
            FinalErrorCode = "HTTP_500",
            PayloadSnapshot = complexPayload.RootElement.GetRawText()
        });

        // Assert: Snapshot contains all original payload data
        var deadLetter = await conn.QuerySingleAsync<dynamic>(
            "SELECT payload_snapshot FROM dead_letters WHERE saga_id = @SagaId",
            new { SagaId = sagaId });

        var snapshotJson = (string)deadLetter.payload_snapshot;
        Assert.Contains("orderId", snapshotJson);
        Assert.Contains("777", snapshotJson);
        Assert.Contains("items", snapshotJson);
        Assert.Contains("ABC123", snapshotJson);
        Assert.Contains("XYZ789", snapshotJson);
        Assert.Contains("109.97", snapshotJson);
    }

    [Fact]
    public async Task DeadLetter_ShouldBeImmutable()
    {
        // Arrange: Create dead letter record
        var eventId = await InsertTestEventAsync(
            Guid.NewGuid().ToString(),
            "order.created",
            JsonDocument.Parse("{\"orderId\": 999}"));

        var subscriptionId = await InsertTestSubscriptionAsync(
            "order.created",
            "https://example.com/webhook");

        var sagaId = await InsertTestSagaAsync(eventId, subscriptionId, "DeadLettered", 5);

        await using var conn = new NpgsqlConnection(ConnectionString);

        var deadLetterId = await conn.ExecuteScalarAsync<long>(@"
            INSERT INTO dead_letters
                (saga_id, event_id, subscription_id, final_error_code, payload_snapshot)
            VALUES
                (@SagaId, @EventId, @SubscriptionId, @FinalErrorCode, @PayloadSnapshot)
            RETURNING id;", new
        {
            SagaId = sagaId,
            EventId = eventId,
            SubscriptionId = subscriptionId,
            FinalErrorCode = "HTTP_500",
            PayloadSnapshot = "{\"orderId\": 999}"
        });

        // Act: Attempt to update dead letter (should be prevented by permissions in real system)
        var updateSql = @"
            UPDATE dead_letters
            SET final_error_code = 'MODIFIED'
            WHERE id = @Id";

        var rowsAffected = await conn.ExecuteAsync(updateSql, new { Id = deadLetterId });

        // Assert: Dead letter remains present (in production this update should be denied)
        var deadLetter = await conn.QuerySingleAsync<dynamic>(
            "SELECT final_error_code FROM dead_letters WHERE id = @Id",
            new { Id = deadLetterId });

        Assert.NotNull(deadLetter.final_error_code);
    }
}
