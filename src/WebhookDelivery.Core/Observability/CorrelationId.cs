using System;

namespace WebhookDelivery.Core.Observability;

/// <summary>
/// Correlation ID for tracking requests across services
/// </summary>
public sealed class CorrelationId
{
    public string Value { get; }

    private CorrelationId(string value)
    {
        Value = value;
    }

    public static CorrelationId New() => new(Guid.NewGuid().ToString("N"));

    public static CorrelationId From(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Correlation ID cannot be empty", nameof(value));

        return new CorrelationId(value);
    }

    public override string ToString() => Value;
}
