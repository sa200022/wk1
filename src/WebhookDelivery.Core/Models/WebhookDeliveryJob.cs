using System;

namespace WebhookDelivery.Core.Models;

/// <summary>
/// Individual atomic delivery attempt
/// </summary>
public sealed record WebhookDeliveryJob
{
    public long Id { get; init; }

    public long SagaId { get; init; }

    public JobStatus Status { get; init; }

    public DateTime? LeaseUntil { get; init; }

    public DateTime AttemptAt { get; init; }

    public int? ResponseStatus { get; init; }

    public string? ErrorCode { get; init; }

    /// <summary>
    /// Creates a new pending job for a saga
    /// </summary>
    public static WebhookDeliveryJob CreatePending(long sagaId)
    {
        return new WebhookDeliveryJob
        {
            SagaId = sagaId,
            Status = JobStatus.Pending,
            AttemptAt = DateTime.UtcNow
        };
    }

    /// <summary>
    /// Checks if job is active (Pending or Leased)
    /// </summary>
    public bool IsActive() => Status is JobStatus.Pending or JobStatus.Leased;

    /// <summary>
    /// Checks if job is terminal (Completed or Failed)
    /// </summary>
    public bool IsTerminal() => Status is JobStatus.Completed or JobStatus.Failed;
}

/// <summary>
/// Job status enumeration
/// </summary>
public enum JobStatus
{
    Pending,
    Leased,
    Completed,
    Failed
}
