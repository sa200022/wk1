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
        // Implementation would call repository directly
        return Ok();
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
public record SetActiveRequest(bool Active);
