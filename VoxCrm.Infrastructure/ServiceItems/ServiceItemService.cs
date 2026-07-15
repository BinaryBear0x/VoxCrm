using Microsoft.EntityFrameworkCore;
using VoxCrm.Application.ServiceItems;
using VoxCrm.Domain.Common;
using VoxCrm.Domain.Entities;
using VoxCrm.Infrastructure.Data;

namespace VoxCrm.Infrastructure.ServiceItems;

public sealed class ServiceItemService : IServiceItemService
{
    private readonly VoxCrmDbContext _context;
    private readonly ITenantService _tenant;

    public ServiceItemService(VoxCrmDbContext context, ITenantService tenant)
    {
        _context = context;
        _tenant = tenant;
    }

    public async Task<IReadOnlyList<ServiceItem>> ListAsync(
        bool includeArchived = false,
        CancellationToken cancellationToken = default)
    {
        var query = TenantItems();
        if (!includeArchived)
            query = query.Where(item => item.IsActive);
        return await query.OrderBy(item => item.Name).AsNoTracking().ToListAsync(cancellationToken);
    }

    public Task<ServiceItem?> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
        _context.ServiceItems.AsNoTracking().FirstOrDefaultAsync(item => item.ID == id && item.IsActive, cancellationToken);

    public async Task<ServiceItemResult> CreateAsync(ServiceItem item, CancellationToken cancellationToken = default)
    {
        var error = Validate(item);
        if (error != null) return new(false, Error: error);
        item.Name = item.Name.Trim();
        if (await HasActiveNameAsync(item.Name, null, cancellationToken))
            return new(false, Error: "Aynı ada sahip aktif bir hizmet zaten var.");
        item.ClinicID = ClinicId;
        _context.ServiceItems.Add(item);
        await _context.SaveChangesAsync(cancellationToken);
        return new(true, item);
    }

    public async Task<ServiceItemResult> UpdateAsync(ServiceItem item, CancellationToken cancellationToken = default)
    {
        var error = Validate(item);
        if (error != null) return new(false, Error: error);
        var normalizedName = item.Name.Trim();
        if (await HasActiveNameAsync(normalizedName, item.ID, cancellationToken))
            return new(false, Error: "Aynı ada sahip aktif bir hizmet zaten var.");
        var existing = await _context.ServiceItems.FirstOrDefaultAsync(candidate => candidate.ID == item.ID && candidate.IsActive, cancellationToken);
        if (existing == null) return new(false, NotFound: true);
        existing.Name = normalizedName;
        existing.Description = item.Description?.Trim();
        existing.Price = item.Price;
        await _context.SaveChangesAsync(cancellationToken);
        return new(true, existing);
    }

    public async Task<ServiceItemResult> RestoreAsync(Guid id, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        var existing = await TenantItems()
            .FirstOrDefaultAsync(item => item.ID == id && !item.IsActive, cancellationToken);
        if (existing == null) return new(false, NotFound: true);
        if (await HasActiveNameAsync(existing.Name, existing.ID, cancellationToken))
            return new(false, Error: "Aynı ada sahip aktif bir hizmet bulunduğu için kayıt geri alınamadı.");
        existing.IsActive = true;
        existing.ArchivedAt = null;
        existing.ArchivedByUserId = null;
        await _context.SaveChangesAsync(cancellationToken);
        return new(true, existing);
    }

    public async Task<ServiceItemResult> ArchiveAsync(Guid id, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        var existing = await _context.ServiceItems.FirstOrDefaultAsync(item => item.ID == id && item.IsActive, cancellationToken);
        if (existing == null) return new(false, NotFound: true);
        existing.IsActive = false;
        existing.ArchivedAt = DateTime.UtcNow;
        existing.ArchivedByUserId = actorUserId;
        await _context.SaveChangesAsync(cancellationToken);
        return new(true, existing);
    }

    private static string? Validate(ServiceItem item)
    {
        if (string.IsNullOrWhiteSpace(item.Name)) return "Hizmet adı zorunludur.";
        if (item.Price < 0) return "Hizmet fiyatı negatif olamaz.";
        return null;
    }

    private Task<bool> HasActiveNameAsync(string name, Guid? excludedId, CancellationToken cancellationToken) =>
        TenantItems().AnyAsync(
            item => item.IsActive
                    && item.Name == name
                    && (!excludedId.HasValue || item.ID != excludedId.Value),
            cancellationToken);

    private IQueryable<ServiceItem> TenantItems() =>
        _context.ServiceItems.IgnoreQueryFilters().Where(item => item.ClinicID == ClinicId);

    private Guid ClinicId => _tenant.GetClinicId() is var id && id != Guid.Empty
        ? id
        : throw new InvalidOperationException("Clinic context is required.");
}
