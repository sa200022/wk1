using System;

namespace WebhookDelivery.Core.Models;

/// <summary>
/// Subscription configuration entity
/// Pure configuration layer - no orchestration, no side effects
/// </summary>
public sealed record Subscription
{
    public long Id { get; init; }

    public string EventType { get; init; } = null!;

    public string CallbackUrl { get; init; } = null!;

    public bool Active { get; init; }

    public bool Verified { get; init; }

    public DateTime CreatedAt { get; init; }

    public DateTime UpdatedAt { get; init; }

    /// <summary>
    /// Validates callback URL is HTTPS
    /// </summary>
    public bool IsCallbackUrlValid()
    {
        if (string.IsNullOrWhiteSpace(CallbackUrl))
            return false;

        return Uri.TryCreate(CallbackUrl, UriKind.Absolute, out var uri)
               && uri.Scheme == Uri.UriSchemeHttps;
    }

    /// <summary>
    /// Checks if subscription is eligible for routing
    /// </summary>
    public bool IsEligibleForRouting() => Active && Verified;
}
