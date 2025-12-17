using System;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using WebhookDelivery.Core.Models;
using WebhookDelivery.Core.Repositories;

namespace WebhookDelivery.Worker.Services;

/// <summary>
/// Job Worker - executes HTTP delivery attempts
/// Responsibilities:
/// - Acquire jobs via SELECT FOR UPDATE SKIP LOCKED
/// - Set status = Leased, lease_until = now + lease_duration
/// - Execute HTTP request
/// - Report result: Completed or Failed
///
/// MUST NOT:
/// - Modify saga status
/// - Modify attempt_count
/// - Create jobs
/// - Decide retry/dead-letter logic
/// </summary>
public sealed class JobWorkerService : BackgroundService
{
    private readonly IJobRepository _jobRepository;
    private readonly IEventRepository _eventRepository;
    private readonly ISubscriptionRepository _subscriptionRepository;
    private readonly ISagaRepository _sagaRepository;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<JobWorkerService> _logger;
    private readonly TimeSpan _leaseDuration;
    private readonly TimeSpan _pollingInterval;
    private readonly int _batchSize;
    private readonly string? _webhookSigningKey;

    public JobWorkerService(
        IJobRepository jobRepository,
        IEventRepository eventRepository,
        ISubscriptionRepository subscriptionRepository,
        ISagaRepository sagaRepository,
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        ILogger<JobWorkerService> logger)
    {
        _jobRepository = jobRepository ?? throw new ArgumentNullException(nameof(jobRepository));
        _eventRepository = eventRepository ?? throw new ArgumentNullException(nameof(eventRepository));
        _subscriptionRepository = subscriptionRepository ?? throw new ArgumentNullException(nameof(subscriptionRepository));
        _sagaRepository = sagaRepository ?? throw new ArgumentNullException(nameof(sagaRepository));
        _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        _leaseDuration = TimeSpan.FromSeconds(configuration.GetValue<int>("Worker:LeaseDurationSeconds", 60));
        _pollingInterval = TimeSpan.FromSeconds(configuration.GetValue<int>("Worker:PollingIntervalSeconds", 2));
        _batchSize = configuration.GetValue<int>("Worker:BatchSize", 10);
        _webhookSigningKey = configuration.GetValue<string>("Worker:WebhookSigningKey");
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Job Worker started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessPendingJobsAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in Job Worker");
            }

            await Task.Delay(_pollingInterval, stoppingToken);
        }

        _logger.LogInformation("Job Worker stopped");
    }

    private async Task ProcessPendingJobsAsync(CancellationToken cancellationToken)
    {
        var jobs = await _jobRepository.GetPendingJobsAsync(_batchSize, cancellationToken);

        foreach (var job in jobs)
        {
            try
            {
                await ProcessJobAsync(job, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to process job {JobId}", job.Id);
            }
        }
    }

    private async Task ProcessJobAsync(WebhookDeliveryJob job, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Processing job {JobId} for saga {SagaId}", job.Id, job.SagaId);

        // Step 1: Immediately lease the job
        var leased = job with
        {
            Status = JobStatus.Leased,
            LeaseUntil = DateTime.UtcNow.Add(_leaseDuration)
        };
        await _jobRepository.UpdateAsync(leased, cancellationToken);

        try
        {
            // Step 2: Load event and subscription data
            var saga = await _sagaRepository.GetByIdAsync(job.SagaId, cancellationToken);
            if (saga == null)
            {
                _logger.LogError("Saga {SagaId} not found for job {JobId}", job.SagaId, job.Id);
                await MarkJobFailedAsync(leased, "SAGA_NOT_FOUND", cancellationToken);
                return;
            }

            var @event = await _eventRepository.GetByIdAsync(saga.EventId, cancellationToken);
            if (@event == null)
            {
                _logger.LogError("Event {EventId} not found for job {JobId}", saga.EventId, job.Id);
                await MarkJobFailedAsync(leased, "EVENT_NOT_FOUND", cancellationToken);
                return;
            }

            var subscription = await _subscriptionRepository.GetByIdAsync(saga.SubscriptionId, cancellationToken);
            if (subscription == null)
            {
                _logger.LogError("Subscription {SubscriptionId} not found for job {JobId}", saga.SubscriptionId, job.Id);
                await MarkJobFailedAsync(leased, "SUBSCRIPTION_NOT_FOUND", cancellationToken);
                return;
            }

            // Step 3: Execute HTTP delivery
            var result = await ExecuteWebhookDeliveryAsync(
                job.SagaId,
                subscription.CallbackUrl,
                @event.Payload,
                cancellationToken);

            // Step 4: Report result
            if (result.Success)
            {
                var completed = leased with
                {
                    Status = JobStatus.Completed,
                    ResponseStatus = result.HttpStatusCode,
                    LeaseUntil = null
                };
                await _jobRepository.UpdateAsync(completed, cancellationToken);

                _logger.LogInformation(
                    "Job {JobId} completed successfully with status {StatusCode}",
                    job.Id,
                    result.HttpStatusCode);
            }
            else
            {
                var failed = leased with
                {
                    Status = JobStatus.Failed,
                    ErrorCode = result.ErrorCode,
                    LeaseUntil = null
                };
                await _jobRepository.UpdateAsync(failed, cancellationToken);

                _logger.LogWarning(
                    "Job {JobId} failed with error: {ErrorCode}",
                    job.Id,
                    result.ErrorCode);
            }
        }
        catch (Exception ex)
        {
            // If worker crashes here, lease will expire and job will be reset to Pending
            _logger.LogError(ex, "Exception during job {JobId} execution", job.Id);

            var failed = leased with
            {
                Status = JobStatus.Failed,
                ErrorCode = "WORKER_EXCEPTION",
                LeaseUntil = null
            };
            await _jobRepository.UpdateAsync(failed, cancellationToken);
        }
    }

    private async Task MarkJobFailedAsync(
        WebhookDeliveryJob job,
        string errorCode,
        CancellationToken cancellationToken)
    {
        var failed = job with
        {
            Status = JobStatus.Failed,
            ErrorCode = errorCode,
            LeaseUntil = null
        };

        await _jobRepository.UpdateAsync(failed, cancellationToken);
    }

    private async Task<DeliveryResult> ExecuteWebhookDeliveryAsync(
        long sagaId,
        string callbackUrl,
        JsonDocument payload,
        CancellationToken cancellationToken)
    {
        try
        {
            var httpClient = _httpClientFactory.CreateClient("WebhookClient");

            var envelope = new
            {
                sagaId,
                deliveredAt = DateTime.UtcNow,
                payload = payload.RootElement
            };

            var content = new StringContent(
                JsonSerializer.Serialize(envelope),
                Encoding.UTF8,
                "application/json");

            if (!string.IsNullOrWhiteSpace(_webhookSigningKey))
            {
                var signature = ComputeHmacSignature(
                    _webhookSigningKey,
                    await content.ReadAsStringAsync(cancellationToken));
                content.Headers.Add("X-Webhook-Signature", signature);
            }

            var response = await httpClient.PostAsync(callbackUrl, content, cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                return new DeliveryResult
                {
                    Success = true,
                    HttpStatusCode = (int)response.StatusCode
                };
            }

            return new DeliveryResult
            {
                Success = false,
                ErrorCode = $"HTTP_{response.StatusCode}"
            };
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "HTTP request failed for saga {SagaId}", sagaId);
            return new DeliveryResult
            {
                Success = false,
                ErrorCode = "HTTP_REQUEST_FAILED"
            };
        }
        catch (TaskCanceledException)
        {
            return new DeliveryResult
            {
                Success = false,
                ErrorCode = "TIMEOUT"
            };
        }
    }

    private record DeliveryResult
    {
        public bool Success { get; init; }
        public int? HttpStatusCode { get; init; }
        public string? ErrorCode { get; init; }
    }

    private static string ComputeHmacSignature(string key, string payload)
    {
        var keyBytes = Encoding.UTF8.GetBytes(key);
        var payloadBytes = Encoding.UTF8.GetBytes(payload);

        using var hmac = new HMACSHA256(keyBytes);
        var hash = hmac.ComputeHash(payloadBytes);
        var builder = new StringBuilder(hash.Length * 2);
        foreach (var b in hash)
        {
            builder.Append(b.ToString("x2"));
        }
        return builder.ToString();
    }
}
