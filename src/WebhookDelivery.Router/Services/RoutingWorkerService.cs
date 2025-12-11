using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using WebhookDelivery.Core.Models;
using WebhookDelivery.Core.Repositories;

namespace WebhookDelivery.Router.Services;

/// <summary>
/// Background worker that routes events to sagas
/// Responsibility: For each event, create saga for each active+verified subscription
/// MUST NOT: create jobs, change saga status (only creates Pending sagas)
/// </summary>
public sealed class RoutingWorkerService : BackgroundService
{
    private readonly IEventRepository _eventRepository;
    private readonly ISubscriptionRepository _subscriptionRepository;
    private readonly ISagaRepository _sagaRepository;
    private readonly ILogger<RoutingWorkerService> _logger;
    private readonly TimeSpan _pollingInterval = TimeSpan.FromSeconds(5);

    private long _lastProcessedEventId = 0;

    public RoutingWorkerService(
        IEventRepository eventRepository,
        ISubscriptionRepository subscriptionRepository,
        ISagaRepository sagaRepository,
        ILogger<RoutingWorkerService> logger)
    {
        _eventRepository = eventRepository ?? throw new ArgumentNullException(nameof(eventRepository));
        _subscriptionRepository = subscriptionRepository ?? throw new ArgumentNullException(nameof(subscriptionRepository));
        _sagaRepository = sagaRepository ?? throw new ArgumentNullException(nameof(sagaRepository));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Routing worker started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessNewEventsAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing events in routing worker");
            }

            await Task.Delay(_pollingInterval, stoppingToken);
        }

        _logger.LogInformation("Routing worker stopped");
    }

    private async Task ProcessNewEventsAsync(CancellationToken cancellationToken)
    {
        // Note: This is a simplified implementation
        // In production, use change data capture (CDC) or event streaming
        // to react to new events instead of polling

        // For demonstration, we would:
        // 1. Query events with id > _lastProcessedEventId
        // 2. For each event, load subscriptions matching event_type, active=1, verified=1
        // 3. For each subscription, create saga idempotently
        // 4. Update _lastProcessedEventId

        _logger.LogDebug("Polling for new events after ID {LastEventId}", _lastProcessedEventId);

        // Implementation would go here - omitted for brevity
        // This is where you would call:
        // - Get new events
        // - For each event:
        //   - var subscriptions = await _subscriptionRepository.GetActiveAndVerifiedAsync(event.EventType)
        //   - For each subscription:
        //     - var saga = WebhookDeliverySaga.CreatePending(event.Id, subscription.Id)
        //     - await _sagaRepository.CreateIdempotentAsync(saga)
    }

    /// <summary>
    /// Routes a single event to all eligible subscriptions
    /// </summary>
    public async Task RouteEventAsync(Event @event, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Routing event {EventId} of type {EventType}",
            @event.Id,
            @event.EventType);

        // Load all active and verified subscriptions for this event type
        var subscriptions = await _subscriptionRepository.GetActiveAndVerifiedAsync(
            @event.EventType,
            cancellationToken);

        _logger.LogInformation(
            "Found {SubscriptionCount} eligible subscriptions for event {EventId}",
            subscriptions.Count,
            @event.Id);

        // Create saga for each subscription (idempotent)
        foreach (var subscription in subscriptions)
        {
            try
            {
                var saga = WebhookDeliverySaga.CreatePending(@event.Id, subscription.Id);

                await _sagaRepository.CreateIdempotentAsync(saga, cancellationToken);

                _logger.LogInformation(
                    "Created saga for event {EventId} and subscription {SubscriptionId}",
                    @event.Id,
                    subscription.Id);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Failed to create saga for event {EventId} and subscription {SubscriptionId}",
                    @event.Id,
                    subscription.Id);
                // Continue processing other subscriptions
            }
        }
    }
}
