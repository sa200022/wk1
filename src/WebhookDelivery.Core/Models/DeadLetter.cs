using System;
using System.Text.Json;

namespace WebhookDelivery.Core.Models;

/// <summary>
/// Dead letter entry for permanently failed delivery sagas
/// </summary>
public sealed record DeadLetter
{
    public long Id { get; init; }

    public long SagaId { get; init; }

    public long EventId { get; init; }

    public long SubscriptionId { get; init; }

    public string? FinalErrorCode { get; init; }

    public DateTime FailedAt { get; init; }

    /// <summary>
    /// Full event payload snapshot (immutable copy)
    /// </summary>
    public JsonDocument PayloadSnapshot { get; init; } = null!;

    /// <summary>
    /// Creates a dead letter from a saga
    /// </summary>
    public static DeadLetter FromSaga(
        WebhookDeliverySaga saga,
        JsonDocument payloadSnapshot)
    {
        if (!saga.IsTerminal() || saga.Status != SagaStatus.DeadLettered)
        {
            throw new InvalidOperationException(
                $"Cannot create dead letter from saga in {saga.Status} status");
        }

        return new DeadLetter
        {
            SagaId = saga.Id,
            EventId = saga.EventId,
            SubscriptionId = saga.SubscriptionId,
            FinalErrorCode = saga.FinalErrorCode,
            FailedAt = DateTime.UtcNow,
            PayloadSnapshot = payloadSnapshot
        };
    }
}
