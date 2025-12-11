using System;
using System.Net.Http;
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
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<JobWorkerService> _logger;
    private readonly TimeSpan _leaseDuration;
    private readonly TimeSpan _pollingInterval = TimeSpan.FromSeconds(2);

    public JobWorkerService(
        IJobRepository jobRepository,
        IEventRepository eventRepository,
        ISubscriptionRepository subscriptionRepository,
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        ILogger<JobWorkerService> logger)
    {
        _jobRepository = jobRepository ?? throw new ArgumentNullException(nameof(jobRepository));
        _eventRepository = eventRepository ?? throw new ArgumentNullException(nameof(eventRepository));
        _subscriptionRepository = subscriptionRepository ?? throw new ArgumentNullException(nameof(subscriptionRepository));
        _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        _leaseDuration = TimeSpan.FromSeconds(configuration.GetValue<int>("Worker:LeaseSeconds", 60));
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
        var jobs = await _jobRepository.GetPendingJobsAsync(10, cancellationToken);

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
            // (In production, this would be optimized with caching or joins)

            // Step 3: Execute HTTP delivery
            var result = await ExecuteWebhookDeliveryAsync(job.SagaId, cancellationToken);

            // Step 4: Report result
            if (result.Success)
            {
                var completed = leased with
                {
                    Status = JobStatus.Completed,
                    ResponseStatus = result.HttpStatusCode
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
                    ErrorCode = result.ErrorCode
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
                ErrorCode = "WORKER_EXCEPTION"
            };
            await _jobRepository.UpdateAsync(failed, cancellationToken);
        }
    }

    private async Task<DeliveryResult> ExecuteWebhookDeliveryAsync(
        long sagaId,
        CancellationToken cancellationToken)
    {
        // Simplified - in production, would load full event payload and subscription details

        try
        {
            var httpClient = _httpClientFactory.CreateClient();

            // Mock webhook delivery
            var callbackUrl = "https://example.com/webhook"; // Would load from subscription
            var payload = new { sagaId, timestamp = DateTime.UtcNow }; // Would use event payload

            var content = new StringContent(
                JsonSerializer.Serialize(payload),
                Encoding.UTF8,
                "application/json");

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
}
