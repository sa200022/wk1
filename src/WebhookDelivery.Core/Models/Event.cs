using System;
using System.Text.Json;

namespace WebhookDelivery.Core.Models;

/// <summary>
/// Immutable domain event entity
/// </summary>
public sealed class Event
{
    public long Id { get; init; }

    public string EventType { get; init; } = null!;

    public DateTime CreatedAt { get; init; }

    public JsonDocument Payload { get; init; } = null!;

    /// <summary>
    /// Constructor for creating events (used by repositories)
    /// </summary>
    public Event() { }

    /// <summary>
    /// Factory method for creating new events
    /// </summary>
    public static Event Create(string eventType, JsonDocument payload)
    {
        if (string.IsNullOrWhiteSpace(eventType))
            throw new ArgumentException("Event type cannot be null or empty", nameof(eventType));

        if (payload == null)
            throw new ArgumentNullException(nameof(payload));

        return new Event
        {
            EventType = eventType,
            Payload = payload,
            CreatedAt = DateTime.UtcNow
        };
    }
}
