using Microsoft.EntityFrameworkCore;
using VoxCrm.Application.Examinations;
using VoxCrm.Domain.Common;
using VoxCrm.Domain.Entities;
using VoxCrm.Infrastructure.Data;

namespace VoxCrm.Infrastructure.Examinations;

public sealed class ExaminationService : IExaminationService
{
    private readonly VoxCrmDbContext _context;
    private readonly ITenantService _tenant;

    public ExaminationService(VoxCrmDbContext context, ITenantService tenant)
    {
        _context = context;
        _tenant = tenant;
    }

    public async Task<IReadOnlyList<Muayene>> ListAsync(
        bool includeArchived = false,
        CancellationToken cancellationToken = default)
    {
        IQueryable<Muayene> query = TenantExaminations().Include(item => item.Patient);
        if (!includeArchived)
            query = query.Where(item => item.IsActive);
        return await query.OrderByDescending(item => item.CreatedAt).AsNoTracking().ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Patient>> GetPatientOptionsAsync(CancellationToken cancellationToken = default) =>
        await _context.Patients.Where(patient => patient.IsActive).OrderBy(patient => patient.Name).AsNoTracking().ToListAsync(cancellationToken);

    public Task<Muayene?> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
        _context.Muayeneler.Where(item => item.IsActive).Include(item => item.Patient)
            .AsNoTracking().FirstOrDefaultAsync(item => item.ID == id, cancellationToken);

    public async Task<ExaminationResult> CreateAsync(Muayene examination, CancellationToken cancellationToken = default)
    {
        var error = await ValidateAsync(examination, cancellationToken);
        if (error != null) return new(false, Error: error);
        examination.ClinicID = ClinicId;
        _context.Muayeneler.Add(examination);
        await _context.SaveChangesAsync(cancellationToken);
        return new(true, examination);
    }

    public async Task<ExaminationResult> UpdateAsync(Muayene examination, CancellationToken cancellationToken = default)
    {
        var error = await ValidateAsync(examination, cancellationToken);
        if (error != null) return new(false, Error: error);
        var existing = await _context.Muayeneler.FirstOrDefaultAsync(item => item.ID == examination.ID && item.IsActive, cancellationToken);
        if (existing == null) return new(false, NotFound: true);
        existing.PatientId = examination.PatientId;
        existing.AppointmentId = examination.AppointmentId;
        existing.Subjective = examination.Subjective;
        existing.Objective = examination.Objective;
        existing.Assessment = examination.Assessment;
        existing.Plan = examination.Plan;
        existing.WeightAtVisit = examination.WeightAtVisit;
        existing.Temperature = examination.Temperature;
        await _context.SaveChangesAsync(cancellationToken);
        return new(true, existing);
    }

    public async Task<ExaminationResult> RestoreAsync(Guid id, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        var existing = await TenantExaminations()
            .FirstOrDefaultAsync(item => item.ID == id && !item.IsActive, cancellationToken);
        if (existing == null) return new(false, NotFound: true);
        existing.IsActive = true;
        existing.ArchivedAt = null;
        existing.ArchivedByUserId = null;
        await _context.SaveChangesAsync(cancellationToken);
        return new(true, existing);
    }

    public async Task<ExaminationResult> ArchiveAsync(Guid id, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        var existing = await _context.Muayeneler.FirstOrDefaultAsync(item => item.ID == id && item.IsActive, cancellationToken);
        if (existing == null) return new(false, NotFound: true);
        existing.IsActive = false;
        existing.ArchivedAt = DateTime.UtcNow;
        existing.ArchivedByUserId = actorUserId;
        await _context.SaveChangesAsync(cancellationToken);
        return new(true, existing);
    }

    private async Task<string?> ValidateAsync(Muayene examination, CancellationToken cancellationToken)
    {
        if (examination.WeightAtVisit is <= 0 or > 2000) return "Kilo 0 ile 2000 kg arasında olmalıdır.";
        if (examination.Temperature is < 20 or > 45) return "Sıcaklık 20 ile 45 derece arasında olmalıdır.";
        if (!await _context.Patients.AnyAsync(patient => patient.ID == examination.PatientId && patient.IsActive, cancellationToken))
            return "Geçerli ve aktif bir hasta seçin.";
        if (examination.AppointmentId.HasValue
            && !await _context.Appointments.AnyAsync(appointment =>
                appointment.ID == examination.AppointmentId.Value
                && appointment.PatientId == examination.PatientId
                && appointment.IsActive, cancellationToken))
            return "Randevu aynı hastaya ve kliniğe ait olmalıdır.";
        return null;
    }

    private IQueryable<Muayene> TenantExaminations() =>
        _context.Muayeneler.IgnoreQueryFilters().Where(item => item.ClinicID == ClinicId);

    private Guid ClinicId => _tenant.GetClinicId() is var id && id != Guid.Empty
        ? id
        : throw new InvalidOperationException("Clinic context is required.");
}
