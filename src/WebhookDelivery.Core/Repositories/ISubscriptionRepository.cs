using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using WebhookDelivery.Core.Models;

namespace WebhookDelivery.Core.Repositories;

/// <summary>
/// Repository interface for subscription management
/// Role: subscription_admin - INSERT/UPDATE subscriptions only
/// </summary>
public interface ISubscriptionRepository
{
    Task<Subscription> CreateAsync(Subscription subscription, CancellationToken cancellationToken = default);

    Task<Subscription?> GetByIdAsync(long id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Subscription>> GetByEventTypeAsync(string eventType, CancellationToken cancellationToken = default);

    Task<Subscription> UpdateAsync(Subscription subscription, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Subscription>> GetActiveAndVerifiedAsync(string eventType, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Subscription>> GetAllAsync(
        int limit,
        int offset,
        CancellationToken cancellationToken = default);
}
