using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using WebhookDelivery.Core.Models;

namespace WebhookDelivery.Core.Repositories;

/// <summary>
/// Repository interface for dead letter management
/// </summary>
public interface IDeadLetterRepository
{
    Task<DeadLetter> CreateAsync(DeadLetter deadLetter, CancellationToken cancellationToken = default);

    Task<DeadLetter?> GetByIdAsync(long id, CancellationToken cancellationToken = default);

    Task<DeadLetter?> GetBySagaIdAsync(long sagaId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<DeadLetter>> GetAllAsync(
        int limit,
        int offset,
        CancellationToken cancellationToken = default);
}
