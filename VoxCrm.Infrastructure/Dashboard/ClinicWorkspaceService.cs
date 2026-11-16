using Microsoft.EntityFrameworkCore;
using VoxCrm.Application.Dashboard;
using VoxCrm.Application.Workday;
using VoxCrm.Domain.Common;
using VoxCrm.Domain.Entities;
using VoxCrm.Infrastructure.Data;

namespace VoxCrm.Infrastructure.Dashboard;

public sealed class ClinicWorkspaceService(VoxCrmDbContext db, ITenantService tenant, IWorkdayService workday, IDashboardService dashboard) : IClinicWorkspaceService
{
    private Guid ClinicId => tenant.GetClinicId() is var id && id != Guid.Empty ? id : throw new WorkdayException("Klinik hesabıyla giriş yapın.", true);
    public async Task<string> GetClinicNameAsync(CancellationToken ct = default) => await db.Clinics.Where(x => x.ID == ClinicId).Select(x => x.Name).SingleOrDefaultAsync(ct) ?? "Kliniğim";
    public async Task<ClinicWorkspace> GetAsync(DateOnly? date, CancellationToken ct = default)
    {
        var day = await workday.GetAsync(date, ct);
        var clinic = await db.Clinics.AsNoTracking().SingleAsync(x => x.ID == ClinicId, ct);
        var zone = TimeZoneInfo.FindSystemTimeZoneById(day.TimeZone);
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, zone));
        var start = TimeZoneInfo.ConvertTimeToUtc(day.Date.ToDateTime(TimeOnly.MinValue), zone);
        var end = TimeZoneInfo.ConvertTimeToUtc(day.Date.AddDays(1).ToDateTime(TimeOnly.MinValue), zone);
        var ids = day.Appointments.Where(x => x.PatientId.HasValue).Select(x => x.PatientId!.Value).Concat(day.Tasks.Where(x => x.PatientId.HasValue).Select(x => x.PatientId!.Value)).Distinct().ToList();
        var patients = await db.Patients.AsNoTracking().Include(x => x.Owners).ThenInclude(x => x.PetOwner).Where(x => x.ClinicID == ClinicId && ids.Contains(x.ID)).ToDictionaryAsync(x => x.ID, ct);
        PetOwner? Owner(Guid? id) => id.HasValue && patients.TryGetValue(id.Value, out var p) ? p.Owners.FirstOrDefault(x => x.IsActive && x.IsPrimaryOwner && x.PetOwner.IsActive)?.PetOwner : null;
        var appointments = day.Appointments.Select(a => { var o = Owner(a.PatientId); return new WorkspaceAppointment(a, o == null ? a.GuestName ?? "Sahip belirtilmedi" : o.FirstName + " " + o.LastName, o?.Phone ?? a.GuestPhone); }).ToList();
        var callbacks = day.Tasks.Where(x => (x.Kind == "CareContact" || x.Kind == "OwnerContact" || x.Kind == "OwnerAppointment") && x.Status == "Open").Select(t => { var o = Owner(t.PatientId); return new WorkspaceCallback(t, t.PatientId.HasValue && patients.TryGetValue(t.PatientId.Value, out var p) ? p.Name ?? "Hasta" : "Hasta", o == null ? "Sahip bilgilerini kontrol edin" : o.FirstName + " " + o.LastName, o?.Phone); }).ToList();
        var totals = await dashboard.GetClinicDashboardAsync(ct);
        return new(clinic.Name, today, day,
            await db.FinanceReceipts.Where(x => x.ClinicID == ClinicId && x.IsActive && x.FinanceAccount.IsActive && x.PaidDate == day.Date).SumAsync(x => (decimal?)x.Amount, ct) ?? 0,
            await db.Muayeneler.CountAsync(x => x.ClinicID == ClinicId && x.IsActive && x.CreatedAt >= start && x.CreatedAt < end, ct),
            clinic.DashboardNote, clinic.DashboardNoteRevision, appointments, callbacks, totals.TotalPatients, totals.TotalPetOwners, totals.TotalOutstandingDebt);
    }
    public async Task SaveNoteAsync(string? note, long revision, Guid actor, CancellationToken ct = default)
    {
        if (note?.Length > 2000) throw new WorkdayException("Klinik notu en fazla 2000 karakter olabilir.");
        if (!await db.Users.AnyAsync(x => x.Id == actor && x.ClinicId == ClinicId && !x.LoginDisabled, ct)) throw new WorkdayException("Yetkili klinik hesabı bulunamadı.", true);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var clinic = await db.Clinics.FromSqlInterpolated($"SELECT * FROM \"Clinics\" WHERE \"ID\" = {ClinicId} FOR UPDATE").SingleAsync(ct);
        if (clinic.DashboardNoteRevision != revision) throw new WorkdayException("Klinik notu başka bir kullanıcı tarafından değiştirildi. Sayfayı yenileyin.");
        clinic.DashboardNote = string.IsNullOrWhiteSpace(note) ? null : note.Trim(); clinic.DashboardNoteRevision++;
        db.SystemAuditLogs.Add(new SystemAuditLog { ClinicId = ClinicId, ActorUserId = actor, Category = "Workday", Action = "Clinic.DashboardNoteUpdated", EntityType = "Clinic", EntityId = ClinicId.ToString(), Message = "Klinik ana sayfa notu güncellendi." });
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
    }
}
