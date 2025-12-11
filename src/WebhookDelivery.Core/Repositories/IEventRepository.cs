using System.Threading;
using System.Threading.Tasks;
using WebhookDelivery.Core.Models;

namespace WebhookDelivery.Core.Repositories;

/// <summary>
/// Repository interface for event persistence
/// Role: event_ingest_writer - INSERT only, no UPDATE/DELETE
/// </summary>
public interface IEventRepository
{
    /// <summary>
    /// Appends a new event to the immutable event log
    /// </summary>
    Task<Event> AppendAsync(Event @event, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets an event by ID (for loading payload snapshots)
    /// </summary>
    Task<Event?> GetByIdAsync(long id, CancellationToken cancellationToken = default);
}
