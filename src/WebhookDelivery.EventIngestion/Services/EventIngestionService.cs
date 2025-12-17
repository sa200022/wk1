using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using WebhookDelivery.Core.Models;
using WebhookDelivery.Core.Repositories;

namespace WebhookDelivery.EventIngestion.Services;

/// <summary>
/// Service responsible for ingesting domain events
/// Responsibility: INSERT into events with immutable JSON payload
/// Must NOT touch sagas/jobs/dead_letters
/// </summary>
public sealed class EventIngestionService
{
    private readonly IEventRepository _eventRepository;
    private readonly ILogger<EventIngestionService> _logger;

    public EventIngestionService(
        IEventRepository eventRepository,
        ILogger<EventIngestionService> logger)
    {
        _eventRepository = eventRepository ?? throw new ArgumentNullException(nameof(eventRepository));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Ingests a new event into the system
    /// </summary>
    /// <param name="eventType">Type of event (e.g., "user.created", "order.completed")</param>
    /// <param name="payload">Immutable JSON payload</param>
    /// <param name="externalEventId">Optional deduplication key from upstream producer</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Persisted event with ID</returns>
    public async Task<Event> IngestAsync(
        string eventType,
        JsonDocument payload,
        string? externalEventId = null,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Ingesting event of type {EventType}",
            eventType);

        try
        {
            // Create immutable event
            var @event = Event.Create(eventType, payload, externalEventId);

            // Persist to database
            var persistedEvent = await _eventRepository.AppendAsync(@event, cancellationToken);

            _logger.LogInformation(
                "Successfully ingested event {EventId} of type {EventType}",
                persistedEvent.Id,
                eventType);

            return persistedEvent;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to ingest event of type {EventType}",
                eventType);
            throw;
        }
    }
}
