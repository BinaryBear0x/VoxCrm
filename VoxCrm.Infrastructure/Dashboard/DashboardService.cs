using Microsoft.EntityFrameworkCore;
using VoxCrm.Application.Dashboard;
using VoxCrm.Domain.Entities;
using VoxCrm.Infrastructure.Data;

namespace VoxCrm.Infrastructure.Dashboard;

public sealed class DashboardService : IDashboardService
{
    private readonly VoxCrmDbContext _context;

    public DashboardService(VoxCrmDbContext context) => _context = context;

    public async Task<ClinicDashboard> GetClinicDashboardAsync(CancellationToken cancellationToken = default) =>
        new(
            await _context.PetOwners.CountAsync(item => item.IsActive, cancellationToken),
            await _context.Patients.CountAsync(item => item.IsActive, cancellationToken),
            await _context.Appointments.CountAsync(item => item.IsActive, cancellationToken),
            await _context.Borçlar
                .Where(item => item.IsActive && item.CancelledAt == null && !item.IsCollected)
                .SumAsync(item => (decimal?)item.Amount, cancellationToken) ?? 0,
            await _context.WhatsAppNotifications.CountAsync(
                item => item.IsActive && item.Status == WhatsAppNotificationStatuses.Pending,
                cancellationToken));
}
