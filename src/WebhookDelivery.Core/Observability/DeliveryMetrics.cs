using System;

namespace WebhookDelivery.Core.Observability;

/// <summary>
/// Metrics for monitoring webhook delivery system
/// </summary>
public static class DeliveryMetrics
{
    public static class EventIngestion
    {
        public const string EventsIngested = "webhook.events.ingested";
        public const string IngestionErrors = "webhook.events.ingestion_errors";
    }

    public static class Routing
    {
        public const string SagasCreated = "webhook.routing.sagas_created";
        public const string RoutingErrors = "webhook.routing.errors";
    }

    public static class SagaOrchestration
    {
        public const string SagasPending = "webhook.sagas.pending";
        public const string SagasInProgress = "webhook.sagas.in_progress";
        public const string SagasPendingRetry = "webhook.sagas.pending_retry";
        public const string SagasCompleted = "webhook.sagas.completed";
        public const string SagasDeadLettered = "webhook.sagas.dead_lettered";
        public const string JobsCreated = "webhook.sagas.jobs_created";
    }

    public static class JobExecution
    {
        public const string JobsAcquired = "webhook.jobs.acquired";
        public const string JobsCompleted = "webhook.jobs.completed";
        public const string JobsFailed = "webhook.jobs.failed";
        public const string JobsLeaseExpired = "webhook.jobs.lease_expired";
        public const string DeliveryLatency = "webhook.jobs.delivery_latency_ms";
    }

    public static class DeadLetter
    {
        public const string DeadLettersCreated = "webhook.dead_letters.created";
        public const string DeadLettersRequeued = "webhook.dead_letters.requeued";
    }
}
