using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using WebhookDelivery.Core.Models;
using WebhookDelivery.Core.Repositories;

namespace WebhookDelivery.SubscriptionApi.Services;

/// <summary>
/// Service for managing webhook subscriptions
/// Responsibility: Pure configuration management - no orchestration, no side effects
/// </summary>
public sealed class SubscriptionService
{
    private readonly ISubscriptionRepository _subscriptionRepository;
    private readonly ILogger<SubscriptionService> _logger;

    public SubscriptionService(
        ISubscriptionRepository subscriptionRepository,
        ILogger<SubscriptionService> logger)
    {
        _subscriptionRepository = subscriptionRepository ?? throw new ArgumentNullException(nameof(subscriptionRepository));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Creates a new subscription
    /// Enforces HTTPS callback_url requirement
    /// </summary>
    public async Task<Subscription> CreateSubscriptionAsync(
        string eventType,
        string callbackUrl,
        CancellationToken cancellationToken = default)
    {
        var subscription = new Subscription
        {
            EventType = eventType,
            CallbackUrl = callbackUrl,
            Active = true,
            Verified = false // Must be verified before routing
        };

        // Enforce HTTPS requirement
        if (!subscription.IsCallbackUrlValid())
        {
            throw new ArgumentException("Callback URL must be a valid HTTPS endpoint", nameof(callbackUrl));
        }

        _logger.LogInformation(
            "Creating subscription for event type {EventType} with callback {CallbackUrl}",
            eventType,
            callbackUrl);

        var created = await _subscriptionRepository.CreateAsync(subscription, cancellationToken);

        _logger.LogInformation(
            "Created subscription {SubscriptionId}, verification required",
            created.Id);

        return created;
    }

    /// <summary>
    /// Marks a subscription as verified after verification flow completes
    /// </summary>
    public async Task<Subscription> VerifySubscriptionAsync(
        long subscriptionId,
        CancellationToken cancellationToken = default)
    {
        var subscription = await _subscriptionRepository.GetByIdAsync(subscriptionId, cancellationToken);

        if (subscription == null)
        {
            throw new InvalidOperationException($"Subscription {subscriptionId} not found");
        }

        if (subscription.Verified)
        {
            _logger.LogInformation("Subscription {SubscriptionId} already verified", subscriptionId);
            return subscription;
        }

        var updated = subscription with { Verified = true };
        await _subscriptionRepository.UpdateAsync(updated, cancellationToken);

        _logger.LogInformation(
            "Verified subscription {SubscriptionId} for event type {EventType}",
            subscriptionId,
            subscription.EventType);

        return updated;
    }

    /// <summary>
    /// Updates subscription active status (soft enable/disable)
    /// </summary>
    public async Task<Subscription> SetActiveAsync(
        long subscriptionId,
        bool active,
        CancellationToken cancellationToken = default)
    {
        var subscription = await _subscriptionRepository.GetByIdAsync(subscriptionId, cancellationToken);

        if (subscription == null)
        {
            throw new InvalidOperationException($"Subscription {subscriptionId} not found");
        }

        var updated = subscription with { Active = active };
        await _subscriptionRepository.UpdateAsync(updated, cancellationToken);

        _logger.LogInformation(
            "Set subscription {SubscriptionId} active status to {Active}",
            subscriptionId,
            active);

        return updated;
    }

    public Task<Subscription?> GetByIdAsync(
        long subscriptionId,
        CancellationToken cancellationToken = default)
    {
        return _subscriptionRepository.GetByIdAsync(subscriptionId, cancellationToken);
    }

    public async Task<IReadOnlyList<Subscription>> GetSubscriptionsAsync(
        string? eventType,
        int limit,
        int offset,
        CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrWhiteSpace(eventType))
        {
            return await _subscriptionRepository.GetByEventTypeAsync(eventType, cancellationToken);
        }

        return await _subscriptionRepository.GetAllAsync(limit, offset, cancellationToken);
    }

    public async Task<Subscription> UpdateSubscriptionAsync(
        long subscriptionId,
        string eventType,
        string callbackUrl,
        bool active,
        CancellationToken cancellationToken = default)
    {
        var subscription = await _subscriptionRepository.GetByIdAsync(subscriptionId, cancellationToken);
        if (subscription == null)
        {
            throw new InvalidOperationException($"Subscription {subscriptionId} not found");
        }

        var updated = subscription with
        {
            EventType = eventType,
            CallbackUrl = callbackUrl,
            Active = active
        };

        if (!updated.IsCallbackUrlValid())
        {
            throw new ArgumentException("Callback URL must be a valid HTTPS endpoint", nameof(callbackUrl));
        }

        _logger.LogInformation(
            "Updating subscription {SubscriptionId} -> eventType={EventType}, callback={CallbackUrl}, active={Active}",
            subscriptionId,
            eventType,
            callbackUrl,
            active);

        return await _subscriptionRepository.UpdateAsync(updated, cancellationToken);
    }
}
