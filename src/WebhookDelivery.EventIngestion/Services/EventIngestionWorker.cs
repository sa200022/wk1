using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace WebhookDelivery.EventIngestion.Services;

/// <summary>
/// Background worker that keeps the Event Ingestion service running
/// Actual event ingestion happens via EventIngestionService.IngestAsync()
/// </summary>
public sealed class EventIngestionWorker : BackgroundService
{
    private readonly ILogger<EventIngestionWorker> _logger;

    public EventIngestionWorker(ILogger<EventIngestionWorker> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Event Ingestion Worker started");

        // This is a placeholder - in production, this would:
        // 1. Listen to a message queue (RabbitMQ, Kafka, etc.)
        // 2. Receive HTTP requests via an API endpoint
        // 3. Poll an external system for new events

        // For now, just keep the service alive
        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
        }

        _logger.LogInformation("Event Ingestion Worker stopped");
    }
}
