using VoxCrm.Domain.Entities;

namespace VoxCrm.Application.ServiceItems;

public sealed record ServiceItemResult(bool Succeeded, ServiceItem? Item = null, string? Error = null, bool NotFound = false);

public interface IServiceItemService
{
    Task<IReadOnlyList<ServiceItem>> ListAsync(bool includeArchived = false, CancellationToken cancellationToken = default);
    Task<ServiceItem?> GetAsync(Guid id, CancellationToken cancellationToken = default);
    Task<ServiceItemResult> CreateAsync(ServiceItem item, CancellationToken cancellationToken = default);
    Task<ServiceItemResult> UpdateAsync(ServiceItem item, CancellationToken cancellationToken = default);
    Task<ServiceItemResult> ArchiveAsync(Guid id, Guid actorUserId, CancellationToken cancellationToken = default);
    Task<ServiceItemResult> RestoreAsync(Guid id, Guid actorUserId, CancellationToken cancellationToken = default);
}
