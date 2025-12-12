using System;

namespace WebhookDelivery.Core.Models;

/// <summary>
/// Webhook delivery saga - orchestration layer for delivery state
/// </summary>
public sealed record WebhookDeliverySaga
{
    public long Id { get; init; }

    public long EventId { get; init; }

    public long SubscriptionId { get; init; }

    public SagaStatus Status { get; init; }

    public int AttemptCount { get; init; }

    public DateTime NextAttemptAt { get; init; }

    public string? FinalErrorCode { get; init; }

    public DateTime CreatedAt { get; init; }

    public DateTime UpdatedAt { get; init; }

    /// <summary>
    /// Creates a new pending saga for event routing
    /// </summary>
    public static WebhookDeliverySaga CreatePending(long eventId, long subscriptionId)
    {
        return new WebhookDeliverySaga
        {
            EventId = eventId,
            SubscriptionId = subscriptionId,
            Status = SagaStatus.Pending,
            AttemptCount = 0,
            NextAttemptAt = DateTime.UtcNow
        };
    }

    /// <summary>
    /// Checks if saga is in terminal state
    /// </summary>
    public bool IsTerminal() => Status is SagaStatus.Completed or SagaStatus.DeadLettered;
}

/// <summary>
/// Saga status enumeration
/// </summary>
public enum SagaStatus
{
    Pending,
    InProgress,
    PendingRetry,
    Completed,
    DeadLettered
}
