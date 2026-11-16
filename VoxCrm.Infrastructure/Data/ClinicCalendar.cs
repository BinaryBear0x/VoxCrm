using Microsoft.EntityFrameworkCore;
using VoxCrm.Domain.Common;
namespace VoxCrm.Infrastructure.Data;
public static class ClinicCalendar
{
    public static async Task<DateOnly> TodayAsync(VoxCrmDbContext db, Guid clinicId, CancellationToken ct = default)
    {
        var zone=await db.Clinics.AsNoTracking().Where(x=>x.ID==clinicId).Select(x=>x.TimeZoneId).SingleAsync(ct);
        return ClinicTime.Today(zone);
    }
}
