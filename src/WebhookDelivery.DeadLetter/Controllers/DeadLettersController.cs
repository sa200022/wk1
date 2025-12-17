using Microsoft.AspNetCore.Mvc;
using WebhookDelivery.Core.Repositories;
using WebhookDelivery.DeadLetter.Services;

namespace WebhookDelivery.DeadLetter.Controllers;

[ApiController]
[Route("api/deadletters")]
public sealed class DeadLettersController : ControllerBase
{
    private readonly IDeadLetterRepository _deadLetterRepository;
    private readonly DeadLetterService _deadLetterService;

    public DeadLettersController(
        IDeadLetterRepository deadLetterRepository,
        DeadLetterService deadLetterService)
    {
        _deadLetterRepository = deadLetterRepository;
        _deadLetterService = deadLetterService;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll(
        [FromQuery] int limit = 50,
        [FromQuery] int offset = 0,
        CancellationToken cancellationToken = default)
    {
        var items = await _deadLetterRepository.GetAllAsync(
            limit <= 0 ? 50 : limit,
            Math.Max(0, offset),
            cancellationToken);
        return Ok(items);
    }

    [HttpGet("{id:long}")]
    public async Task<IActionResult> GetById(long id, CancellationToken cancellationToken = default)
    {
        var item = await _deadLetterRepository.GetByIdAsync(id, cancellationToken);
        if (item == null)
        {
            return NotFound();
        }

        return Ok(item);
    }

    [HttpPost("{id:long}/requeue")]
    public async Task<IActionResult> Requeue(long id, CancellationToken cancellationToken = default)
    {
        var newSaga = await _deadLetterService.RequeueAsync(id, cancellationToken);
        return Created($"/api/deadletters/{id}", new { sagaId = newSaga.Id });
    }
}
