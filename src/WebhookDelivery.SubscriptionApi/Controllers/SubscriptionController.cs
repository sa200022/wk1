using Microsoft.AspNetCore.Mvc;
using WebhookDelivery.SubscriptionApi.Services;

namespace WebhookDelivery.SubscriptionApi.Controllers;

[ApiController]
[Route("api/subscriptions")]
public sealed class SubscriptionController : ControllerBase
{
    private readonly SubscriptionService _subscriptionService;

    public SubscriptionController(SubscriptionService subscriptionService)
    {
        _subscriptionService = subscriptionService;
    }

    [HttpGet]
    public async Task<IActionResult> ListSubscriptions(
        [FromQuery] string? eventType,
        [FromQuery] int limit = 50,
        [FromQuery] int offset = 0,
        CancellationToken cancellationToken = default)
    {
        var items = await _subscriptionService.GetSubscriptionsAsync(
            eventType,
            limit <= 0 ? 50 : limit,
            Math.Max(offset, 0),
            cancellationToken);
        return Ok(items);
    }

    [HttpPost]
    public async Task<IActionResult> CreateSubscription(
        [FromBody] CreateSubscriptionRequest request,
        CancellationToken cancellationToken)
    {
        var subscription = await _subscriptionService.CreateSubscriptionAsync(
            request.EventType,
            request.CallbackUrl,
            cancellationToken);

        return CreatedAtAction(
            nameof(GetSubscription),
            new { id = subscription.Id },
            subscription);
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetSubscription(long id)
    {
        var subscription = await _subscriptionService.GetByIdAsync(id);
        if (subscription == null)
        {
            return NotFound();
        }

        return Ok(subscription);
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> UpdateSubscription(
        long id,
        [FromBody] UpdateSubscriptionRequest request,
        CancellationToken cancellationToken)
    {
        var updated = await _subscriptionService.UpdateSubscriptionAsync(
            id,
            request.EventType,
            request.CallbackUrl,
            request.Active,
            cancellationToken);
        return Ok(updated);
    }

    [HttpPost("{id}/verify")]
    public async Task<IActionResult> VerifySubscription(
        long id,
        CancellationToken cancellationToken)
    {
        var subscription = await _subscriptionService.VerifySubscriptionAsync(id, cancellationToken);
        return Ok(subscription);
    }

    [HttpPut("{id}/active")]
    public async Task<IActionResult> SetActive(
        long id,
        [FromBody] SetActiveRequest request,
        CancellationToken cancellationToken)
    {
        var subscription = await _subscriptionService.SetActiveAsync(id, request.Active, cancellationToken);
        return Ok(subscription);
    }
}

public record CreateSubscriptionRequest(string EventType, string CallbackUrl);
public record UpdateSubscriptionRequest(string EventType, string CallbackUrl, bool Active);
public record SetActiveRequest(bool Active);
