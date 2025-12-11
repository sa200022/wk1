using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using WebhookDelivery.Core.Models;

namespace WebhookDelivery.Core.Repositories;

/// <summary>
/// Repository interface for saga persistence
/// </summary>
public interface ISagaRepository
{
    /// <summary>
    /// Creates a new saga idempotently using INSERT ... ON DUPLICATE KEY UPDATE
    /// Enforced by unique constraint (event_id, subscription_id)
    /// </summary>
    Task<WebhookDeliverySaga> CreateIdempotentAsync(
        WebhookDeliverySaga saga,
        CancellationToken cancellationToken = default);

    Task<WebhookDeliverySaga?> GetByIdAsync(long id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<WebhookDeliverySaga>> GetPendingSagasAsync(
        int limit,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<WebhookDeliverySaga>> GetPendingRetrySagasAsync(
        int limit,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<WebhookDeliverySaga>> GetInProgressSagasAsync(
        int limit,
        CancellationToken cancellationToken = default);

    Task UpdateAsync(WebhookDeliverySaga saga, CancellationToken cancellationToken = default);
}
