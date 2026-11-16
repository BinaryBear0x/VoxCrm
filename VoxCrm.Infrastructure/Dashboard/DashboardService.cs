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
            (await _context.FinanceAccounts.Where(x => x.IsActive).SumAsync(x => (decimal?)x.Charges.Where(c => c.IsActive).Sum(c => c.Amount), cancellationToken) ?? 0)
                - (await _context.FinanceAccounts.Where(x => x.IsActive).SumAsync(x => (decimal?)x.Receipts.Where(r => r.IsActive).Sum(r => r.Amount), cancellationToken) ?? 0),
            await _context.WhatsAppNotifications.CountAsync(
                item => item.IsActive && item.Status == WhatsAppNotificationStatuses.Pending,
                cancellationToken));
}
