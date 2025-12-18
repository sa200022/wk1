using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using WebhookDelivery.Core.Models;
using WebhookDelivery.Core.Repositories;
using WebhookDelivery.Router.Infrastructure;

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
    private readonly RouterStateRepository _stateRepository;
    private readonly ILogger<RoutingWorkerService> _logger;
    private readonly TimeSpan _pollingInterval;
    private readonly int _batchSize;

    private long _lastProcessedEventId = 0;

    public RoutingWorkerService(
        IEventRepository eventRepository,
        ISubscriptionRepository subscriptionRepository,
        ISagaRepository sagaRepository,
        RouterStateRepository stateRepository,
        IConfiguration configuration,
        ILogger<RoutingWorkerService> logger)
    {
        _eventRepository = eventRepository ?? throw new ArgumentNullException(nameof(eventRepository));
        _subscriptionRepository = subscriptionRepository ?? throw new ArgumentNullException(nameof(subscriptionRepository));
        _sagaRepository = sagaRepository ?? throw new ArgumentNullException(nameof(sagaRepository));
        _stateRepository = stateRepository ?? throw new ArgumentNullException(nameof(stateRepository));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        _pollingInterval = TimeSpan.FromSeconds(configuration.GetValue<int>("Routing:PollingIntervalSeconds", 5));
        _batchSize = configuration.GetValue<int>("Routing:BatchSize", 100);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Routing worker started");

        // Initialize last processed event ID to the current max to avoid replaying historical events on first start
        _lastProcessedEventId = await _stateRepository.GetLastProcessedEventIdAsync(stoppingToken);
        if (_lastProcessedEventId == 0)
        {
            _lastProcessedEventId = await _eventRepository.GetMaxEventIdAsync(stoppingToken);
            await _stateRepository.SaveLastProcessedEventIdAsync(_lastProcessedEventId, stoppingToken);
        }

        _logger.LogInformation("Router initialized at last event ID {LastEventId}", _lastProcessedEventId);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // Allow local/dev users to reset router offset without restarting the process.
                // If the persisted offset moves backwards, re-sync our in-memory cursor.
                var persisted = await _stateRepository.GetLastProcessedEventIdAsync(stoppingToken);
                if (persisted < _lastProcessedEventId)
                {
                    _logger.LogWarning(
                        "Router offset moved backwards from {Current} to {Persisted}; re-syncing",
                        _lastProcessedEventId,
                        persisted);
                    _lastProcessedEventId = persisted;
                }

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
        _logger.LogDebug("Polling for new events after ID {LastEventId}", _lastProcessedEventId);

        var events = await _eventRepository.GetAfterIdAsync(
            _lastProcessedEventId,
            _batchSize,
            cancellationToken);

        if (events.Count == 0)
        {
            return;
        }

        foreach (var @event in events)
        {
            var subscriptions = await _subscriptionRepository.GetActiveAndVerifiedAsync(
                @event.EventType,
                cancellationToken);

            if (subscriptions.Count == 0)
            {
                _logger.LogDebug(
                    "No active+verified subscriptions for event {EventId} ({EventType})",
                    @event.Id,
                    @event.EventType);
                _lastProcessedEventId = @event.Id;
                await _stateRepository.SaveLastProcessedEventIdAsync(_lastProcessedEventId, cancellationToken);
                continue;
            }

            var anyFailed = false;
            foreach (var subscription in subscriptions)
            {
                try
                {
                    var saga = WebhookDeliverySaga.CreatePending(@event.Id, subscription.Id);
                    await _sagaRepository.CreateIdempotentAsync(saga, cancellationToken);

                    _logger.LogInformation(
                        "Created saga for event {EventId} -> subscription {SubscriptionId}",
                        @event.Id,
                        subscription.Id);
                }
                catch (Exception ex)
                {
                    anyFailed = true;
                    _logger.LogError(
                        ex,
                        "Failed to create saga for event {EventId} -> subscription {SubscriptionId}",
                        @event.Id,
                        subscription.Id);
                }
            }

            if (anyFailed)
            {
                _logger.LogWarning(
                    "One or more sagas failed to create for event {EventId}; will retry and will not advance offset",
                    @event.Id);
                break;
            }

            _lastProcessedEventId = @event.Id;
            await _stateRepository.SaveLastProcessedEventIdAsync(_lastProcessedEventId, cancellationToken);
        }
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
