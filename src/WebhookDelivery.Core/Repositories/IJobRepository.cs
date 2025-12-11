using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using WebhookDelivery.Core.Models;

namespace WebhookDelivery.Core.Repositories;

/// <summary>
/// Repository interface for job persistence
/// </summary>
public interface IJobRepository
{
    Task<WebhookDeliveryJob> CreateAsync(
        WebhookDeliveryJob job,
        CancellationToken cancellationToken = default);

    Task<WebhookDeliveryJob?> GetByIdAsync(long id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<WebhookDeliveryJob>> GetActiveBySagaIdAsync(
        long sagaId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<WebhookDeliveryJob>> GetTerminalBySagaIdAsync(
        long sagaId,
        CancellationToken cancellationToken = default);

    Task UpdateAsync(WebhookDeliveryJob job, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<WebhookDeliveryJob>> GetPendingJobsAsync(
        int limit,
        CancellationToken cancellationToken = default);
}
