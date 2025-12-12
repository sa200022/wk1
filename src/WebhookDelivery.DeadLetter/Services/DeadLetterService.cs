using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using WebhookDelivery.Core.Models;
using WebhookDelivery.Core.Repositories;
using DeadLetterModel = WebhookDelivery.Core.Models.DeadLetter;

namespace WebhookDelivery.DeadLetter.Services;

/// <summary>
/// Dead Letter Service - manages permanently failed delivery sagas
/// Responsibilities:
/// - Store dead-lettered sagas with payload snapshot
/// - Provide requeue functionality (creates NEW saga)
///
/// MUST NOT:
/// - Modify old DeadLettered saga
/// - Reuse old jobs
/// - Modify events or subscriptions
/// </summary>
public sealed class DeadLetterService
{
    private readonly IDeadLetterRepository _deadLetterRepository;
    private readonly ISagaRepository _sagaRepository;
    private readonly IEventRepository _eventRepository;
    private readonly ILogger<DeadLetterService> _logger;

    public DeadLetterService(
        IDeadLetterRepository deadLetterRepository,
        ISagaRepository sagaRepository,
        IEventRepository eventRepository,
        ILogger<DeadLetterService> logger)
    {
        _deadLetterRepository = deadLetterRepository ?? throw new ArgumentNullException(nameof(deadLetterRepository));
        _sagaRepository = sagaRepository ?? throw new ArgumentNullException(nameof(sagaRepository));
        _eventRepository = eventRepository ?? throw new ArgumentNullException(nameof(eventRepository));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Records a dead-lettered saga
    /// Called by Saga Orchestrator when saga reaches DeadLettered state
    /// </summary>
    public async Task RecordDeadLetterAsync(
        WebhookDeliverySaga saga,
        CancellationToken cancellationToken = default)
    {
        if (saga.Status != SagaStatus.DeadLettered)
        {
            throw new InvalidOperationException(
                $"Cannot record dead letter for saga in {saga.Status} status");
        }

        _logger.LogInformation(
            "Recording dead letter for saga {SagaId} (Event {EventId}, Subscription {SubscriptionId})",
            saga.Id,
            saga.EventId,
            saga.SubscriptionId);

        // Load event to get payload snapshot
        // In production, this could be optimized by passing payload directly
        var @event = await _eventRepository.GetByIdAsync(saga.EventId, cancellationToken)
            ?? throw new InvalidOperationException($"Event {saga.EventId} not found");

        var deadLetter = DeadLetterModel.FromSaga(saga, @event.Payload);

        await _deadLetterRepository.CreateAsync(deadLetter, cancellationToken);

        _logger.LogInformation(
            "Dead letter recorded for saga {SagaId} with error: {ErrorCode}",
            saga.Id,
            saga.FinalErrorCode);
    }

    /// <summary>
    /// Requeues a dead-lettered saga by creating a BRAND NEW saga
    /// The original dead saga remains unchanged
    /// </summary>
    public async Task<WebhookDeliverySaga> RequeueAsync(
        long deadLetterId,
        CancellationToken cancellationToken = default)
    {
        var deadLetter = await _deadLetterRepository.GetByIdAsync(deadLetterId, cancellationToken);
        if (deadLetter == null)
        {
            throw new InvalidOperationException($"Dead letter {deadLetterId} not found");
        }

        _logger.LogInformation(
            "Requeuing dead letter {DeadLetterId} for event {EventId} and subscription {SubscriptionId}",
            deadLetterId,
            deadLetter.EventId,
            deadLetter.SubscriptionId);

        // Create a BRAND NEW saga with initial state
        // IMPORTANT: Do NOT modify the old saga!
        var newSaga = new WebhookDeliverySaga
        {
            EventId = deadLetter.EventId,
            SubscriptionId = deadLetter.SubscriptionId,
            Status = SagaStatus.Pending,
            AttemptCount = 0,
            NextAttemptAt = DateTime.UtcNow,
            FinalErrorCode = null
        };

        // Create saga (will go through normal routing flow)
        var created = await _sagaRepository.CreateIdempotentAsync(newSaga, cancellationToken);

        _logger.LogInformation(
            "Created new saga {NewSagaId} from dead letter {DeadLetterId}, original saga remains DeadLettered",
            created.Id,
            deadLetterId);

        return created;
    }
}
