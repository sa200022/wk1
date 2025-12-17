using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using WebhookDelivery.Core.Models;
using WebhookDelivery.Core.Repositories;

namespace WebhookDelivery.Orchestrator.Services;

/// <summary>
/// Saga Orchestrator - the ONLY component allowed to transform delivery state
/// Responsibilities:
/// - Maintain attempt_count
/// - Decide when to retry
/// - Transition between all delivery states
/// - Decide when to dead-letter
/// - Produce new jobs for workers
/// </summary>
public sealed class SagaOrchestratorService : BackgroundService
{
    private readonly ISagaRepository _sagaRepository;
    private readonly IJobRepository _jobRepository;
    private readonly IDeadLetterRepository _deadLetterRepository;
    private readonly IEventRepository _eventRepository;
    private readonly ILogger<SagaOrchestratorService> _logger;
    private readonly TimeSpan _pollingInterval;
    private readonly int _maxRetryLimit;
    private readonly TimeSpan _baseDelay;

    public SagaOrchestratorService(
        ISagaRepository sagaRepository,
        IJobRepository jobRepository,
        IDeadLetterRepository deadLetterRepository,
        IEventRepository eventRepository,
        IConfiguration configuration,
        ILogger<SagaOrchestratorService> logger)
    {
        _sagaRepository = sagaRepository ?? throw new ArgumentNullException(nameof(sagaRepository));
        _jobRepository = jobRepository ?? throw new ArgumentNullException(nameof(jobRepository));
        _deadLetterRepository = deadLetterRepository ?? throw new ArgumentNullException(nameof(deadLetterRepository));
        _eventRepository = eventRepository ?? throw new ArgumentNullException(nameof(eventRepository));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        _maxRetryLimit = configuration.GetValue<int>("Saga:MaxRetryLimit", 5);
        _baseDelay = TimeSpan.FromSeconds(configuration.GetValue<int>("Saga:BaseDelaySeconds", 30));
        _pollingInterval = TimeSpan.FromSeconds(configuration.GetValue<int>("Saga:PollingIntervalSeconds", 5));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Saga Orchestrator started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessPendingSagasAsync(stoppingToken);
                await ProcessPendingRetrySagasAsync(stoppingToken);
                await ProcessJobResultsAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in Saga Orchestrator");
            }

            await Task.Delay(_pollingInterval, stoppingToken);
        }

        _logger.LogInformation("Saga Orchestrator stopped");
    }

    /// <summary>
    /// Process Pending sagas: create first job and move to InProgress
    /// </summary>
    private async Task ProcessPendingSagasAsync(CancellationToken cancellationToken)
    {
        var sagas = await _sagaRepository.GetPendingSagasAsync(100, cancellationToken);

        foreach (var saga in sagas)
        {
            try
            {
                // Check if saga already has an active job
                var activeJobs = await _jobRepository.GetActiveBySagaIdAsync(saga.Id, cancellationToken);
                if (activeJobs.Any())
                {
                    _logger.LogWarning(
                        "Saga {SagaId} is Pending but already has active job, skipping",
                        saga.Id);
                    continue;
                }

                // Create first job
                var job = WebhookDeliveryJob.CreatePending(saga.Id);
                await _jobRepository.CreateAsync(job, cancellationToken);

                // Move saga to InProgress
                var updated = saga with { Status = SagaStatus.InProgress };
                await _sagaRepository.UpdateAsync(updated, cancellationToken);

                _logger.LogInformation(
                    "Created first job for saga {SagaId}, moved to InProgress",
                    saga.Id);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to process pending saga {SagaId}", saga.Id);
            }
        }
    }

    /// <summary>
    /// Process PendingRetry sagas: create retry job if conditions met
    /// </summary>
    private async Task ProcessPendingRetrySagasAsync(CancellationToken cancellationToken)
    {
        var sagas = await _sagaRepository.GetPendingRetrySagasAsync(100, cancellationToken);

        foreach (var saga in sagas)
        {
            try
            {
                // Verify retry is allowed
                if (saga.AttemptCount >= _maxRetryLimit)
                {
                    _logger.LogWarning(
                        "Saga {SagaId} reached max retry limit, this should have been dead-lettered",
                        saga.Id);
                    continue;
                }

                // Check if saga already has an active job
                var activeJobs = await _jobRepository.GetActiveBySagaIdAsync(saga.Id, cancellationToken);
                if (activeJobs.Any())
                {
                    _logger.LogWarning(
                        "Saga {SagaId} is PendingRetry but already has active job, skipping",
                        saga.Id);
                    continue;
                }

                // Create retry job
                var job = WebhookDeliveryJob.CreatePending(saga.Id);
                await _jobRepository.CreateAsync(job, cancellationToken);

                // Move saga to InProgress
                var updated = saga with { Status = SagaStatus.InProgress };
                await _sagaRepository.UpdateAsync(updated, cancellationToken);

                _logger.LogInformation(
                    "Created retry job for saga {SagaId}, attempt {AttemptCount}",
                    saga.Id,
                    saga.AttemptCount + 1);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to process pending retry saga {SagaId}", saga.Id);
            }
        }
    }

    /// <summary>
    /// Process terminal job results and update saga state
    /// </summary>
    private async Task ProcessJobResultsAsync(CancellationToken cancellationToken)
    {
        // Get all InProgress sagas
        var inProgressSagas = await _sagaRepository.GetInProgressSagasAsync(100, cancellationToken);

        foreach (var saga in inProgressSagas)
        {
            try
            {
                // Get terminal jobs for this saga
                var terminalJobs = await _jobRepository.GetTerminalBySagaIdAsync(saga.Id, cancellationToken);

                if (!terminalJobs.Any())
                {
                    // No terminal job yet, saga is still waiting for job worker
                    continue;
                }

                // Get the latest terminal job (most recent attempt)
                var latestJob = terminalJobs.OrderByDescending(j => j.AttemptAt).First();

                // Apply job result to saga based on job status
                if (latestJob.Status == JobStatus.Completed)
                {
                    await ApplyJobSuccessAsync(saga.Id, latestJob.Id, cancellationToken);
                }
                else if (latestJob.Status == JobStatus.Failed)
                {
                    var errorCode = latestJob.ErrorCode ?? "UNKNOWN_ERROR";
                    await ApplyJobFailureAsync(saga.Id, latestJob.Id, errorCode, cancellationToken);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Failed to process job results for saga {SagaId}",
                    saga.Id);
            }
        }
    }

    /// <summary>
    /// Applies a job success result to the saga
    /// </summary>
    public async Task ApplyJobSuccessAsync(long sagaId, long jobId, CancellationToken cancellationToken = default)
    {
        var saga = await _sagaRepository.GetByIdAsync(sagaId, cancellationToken);
        if (saga == null)
        {
            throw new InvalidOperationException($"Saga {sagaId} not found");
        }

        // Terminal state protection
        if (saga.IsTerminal())
        {
            _logger.LogWarning(
                "Saga {SagaId} is already terminal ({Status}), ignoring job success",
                sagaId,
                saga.Status);
            return;
        }

        // Idempotency check: verify this is the latest job for this saga
        var job = await _jobRepository.GetByIdAsync(jobId, cancellationToken);
        if (job == null)
        {
            _logger.LogWarning("Job {JobId} not found, ignoring", jobId);
            return;
        }

        // Get all terminal jobs for this saga to check if this job was already processed
        var terminalJobs = await _jobRepository.GetTerminalBySagaIdAsync(sagaId, cancellationToken);
        var latestTerminalJob = terminalJobs.OrderByDescending(j => j.AttemptAt).FirstOrDefault();

        if (latestTerminalJob != null && latestTerminalJob.Id != jobId)
        {
            // A more recent job has already been processed
            _logger.LogWarning(
                "Job {JobId} is not the latest terminal job for saga {SagaId}, ignoring (latest: {LatestJobId})",
                jobId,
                sagaId,
                latestTerminalJob.Id);
            return;
        }

        // Mark saga as Completed
        var updated = saga with
        {
            Status = SagaStatus.Completed,
            FinalErrorCode = null
        };

        await _sagaRepository.UpdateAsync(updated, cancellationToken);

        _logger.LogInformation("Saga {SagaId} completed successfully", sagaId);
    }

    /// <summary>
    /// Applies a job failure result to the saga
    /// </summary>
    public async Task ApplyJobFailureAsync(
        long sagaId,
        long jobId,
        string errorCode,
        CancellationToken cancellationToken = default)
    {
        var saga = await _sagaRepository.GetByIdAsync(sagaId, cancellationToken);
        if (saga == null)
        {
            throw new InvalidOperationException($"Saga {sagaId} not found");
        }

        // Terminal state protection
        if (saga.IsTerminal())
        {
            _logger.LogWarning(
                "Saga {SagaId} is already terminal ({Status}), ignoring job failure",
                sagaId,
                saga.Status);
            return;
        }

        // Idempotency check: verify this is the latest job for this saga
        var job = await _jobRepository.GetByIdAsync(jobId, cancellationToken);
        if (job == null)
        {
            _logger.LogWarning("Job {JobId} not found, ignoring", jobId);
            return;
        }

        // Get all terminal jobs for this saga to check if this job was already processed
        var terminalJobs = await _jobRepository.GetTerminalBySagaIdAsync(sagaId, cancellationToken);
        var latestTerminalJob = terminalJobs.OrderByDescending(j => j.AttemptAt).FirstOrDefault();

        if (latestTerminalJob != null && latestTerminalJob.Id != jobId)
        {
            // A more recent job has already been processed
            _logger.LogWarning(
                "Job {JobId} is not the latest terminal job for saga {SagaId}, ignoring (latest: {LatestJobId})",
                jobId,
                sagaId,
                latestTerminalJob.Id);
            return;
        }

        // Increment attempt count
        var newAttemptCount = saga.AttemptCount + 1;

        // Check if retry limit exceeded
        if (newAttemptCount >= _maxRetryLimit)
        {
            // Dead letter the saga
            var deadLettered = saga with
            {
                Status = SagaStatus.DeadLettered,
                AttemptCount = newAttemptCount,
                FinalErrorCode = errorCode
            };

            await _sagaRepository.UpdateAsync(deadLettered, cancellationToken);

            _logger.LogWarning(
                "Saga {SagaId} dead-lettered after {AttemptCount} attempts",
                sagaId,
                newAttemptCount);

            // Create dead letter record with event payload snapshot
            await CreateDeadLetterRecordAsync(deadLettered, cancellationToken);

            return;
        }

        // Calculate next retry time using exponential backoff
        var backoffTime = CalculateBackoff(newAttemptCount);
        var nextAttemptAt = DateTime.UtcNow.Add(backoffTime);

        var pendingRetry = saga with
        {
            Status = SagaStatus.PendingRetry,
            AttemptCount = newAttemptCount,
            NextAttemptAt = nextAttemptAt,
            FinalErrorCode = errorCode
        };

        await _sagaRepository.UpdateAsync(pendingRetry, cancellationToken);

        _logger.LogInformation(
            "Saga {SagaId} moved to PendingRetry, attempt {AttemptCount}, next retry at {NextAttemptAt}",
            sagaId,
            newAttemptCount,
            nextAttemptAt);
    }

    /// <summary>
    /// Calculates exponential backoff: base_delay * (2^(attempt_count - 1))
    /// </summary>
    private TimeSpan CalculateBackoff(int attemptCount)
    {
        var multiplier = Math.Pow(2, attemptCount - 1);
        var delaySeconds = _baseDelay.TotalSeconds * multiplier;
        return TimeSpan.FromSeconds(Math.Min(delaySeconds, 3600)); // Cap at 1 hour
    }

    /// <summary>
    /// Creates a dead letter record when a saga is permanently failed
    /// Includes event payload snapshot for future requeue
    /// </summary>
    private async Task CreateDeadLetterRecordAsync(
        WebhookDeliverySaga saga,
        CancellationToken cancellationToken)
    {
        try
        {
            // Get event payload for snapshot
            var @event = await _eventRepository.GetByIdAsync(saga.EventId, cancellationToken);
            if (@event == null)
            {
                _logger.LogError(
                    "Cannot create dead letter for saga {SagaId}: event {EventId} not found",
                    saga.Id,
                    saga.EventId);
                return;
            }

            // Create dead letter entry using factory method
            var deadLetter = DeadLetter.FromSaga(saga, @event.Payload);

            await _deadLetterRepository.CreateAsync(deadLetter, cancellationToken);

            _logger.LogInformation(
                "Dead letter record created for saga {SagaId} (event {EventId}, subscription {SubscriptionId})",
                saga.Id,
                saga.EventId,
                saga.SubscriptionId);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to create dead letter record for saga {SagaId}",
                saga.Id);
            // Don't throw - saga is already dead-lettered, this is just for observability
        }
    }
}
