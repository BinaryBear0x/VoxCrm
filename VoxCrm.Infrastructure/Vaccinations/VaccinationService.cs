using Microsoft.EntityFrameworkCore;
using VoxCrm.Application.Audit;
using VoxCrm.Application.Vaccinations;
using VoxCrm.Domain.Common;
using VoxCrm.Domain.Entities;
using VoxCrm.Infrastructure.Data;

namespace VoxCrm.Infrastructure.Vaccinations;

public sealed class VaccinationService : IVaccinationService
{
    private readonly VoxCrmDbContext _context;
    private readonly ITenantService _tenant;
    private readonly IAuditLogger _audit;

    public VaccinationService(VoxCrmDbContext context, ITenantService tenant, IAuditLogger audit)
    {
        _context = context;
        _tenant = tenant;
        _audit = audit;
    }

    public async Task<IReadOnlyList<VaccinationRecord>> ListAsync(bool includeArchived = false, CancellationToken cancellationToken = default)
    {
        var query = TenantRecords(includeArchived)
            .Include(v => v.Patient)
            .Include(v => v.VaccineType)
            .AsNoTracking();
        return await query.OrderBy(v => v.NextDueDate).ToListAsync(cancellationToken);
    }

    public Task<VaccinationRecord?> GetAsync(Guid id, bool includeArchived = false, CancellationToken cancellationToken = default) =>
        TenantRecords(includeArchived).AsNoTracking().FirstOrDefaultAsync(v => v.ID == id, cancellationToken);

    public async Task<VaccinationChoices> GetChoicesAsync(CancellationToken cancellationToken = default)
    {
        var patients = await _context.Patients.Where(p => p.IsActive).OrderBy(p => p.Name).AsNoTracking().ToListAsync(cancellationToken);
        var vaccineTypes = await _context.VaccineTypes.Where(v => v.IsActive).OrderBy(v => v.Name).AsNoTracking().ToListAsync(cancellationToken);
        return new VaccinationChoices(patients, vaccineTypes);
    }

    public async Task<VaccinationCommandResult> CreateAsync(VaccinationRecord record, CancellationToken cancellationToken = default)
    {
        if (record.AdministeredDate == default || record.AdministeredDate.Date > DateTime.UtcNow.Date)
            return new VaccinationCommandResult(false, Error: "Aşı tarihi boş veya gelecekte olamaz.");
        var validation = await ValidateReferencesAsync(record.PatientId, record.VaccineTypeId, cancellationToken);
        if (validation.Error != null) return new VaccinationCommandResult(false, Error: validation.Error);
        record.ClinicID = ClinicId;
        record.NextDueDate = record.AdministeredDate.AddDays(validation.VaccineType!.ValidityDays);
        _context.VaccinationRecords.Add(record);
        await _context.SaveChangesAsync(cancellationToken);
        return new VaccinationCommandResult(true, record);
    }

    public async Task<VaccinationCommandResult> UpdateAsync(VaccinationRecord record, CancellationToken cancellationToken = default)
    {
        if (record.AdministeredDate == default || record.AdministeredDate.Date > DateTime.UtcNow.Date)
            return new VaccinationCommandResult(false, Error: "Aşı tarihi boş veya gelecekte olamaz.");
        var existing = await _context.VaccinationRecords.FirstOrDefaultAsync(v => v.ID == record.ID && v.IsActive, cancellationToken);
        if (existing == null) return new VaccinationCommandResult(false, NotFound: true);
        var validation = await ValidateReferencesAsync(record.PatientId, record.VaccineTypeId, cancellationToken);
        if (validation.Error != null) return new VaccinationCommandResult(false, Error: validation.Error);
        existing.PatientId = record.PatientId;
        existing.VaccineTypeId = record.VaccineTypeId;
        existing.AdministeredDate = record.AdministeredDate;
        existing.NextDueDate = record.AdministeredDate.AddDays(validation.VaccineType!.ValidityDays);
        await _context.SaveChangesAsync(cancellationToken);
        return new VaccinationCommandResult(true, existing);
    }

    public Task<VaccinationCommandResult> ArchiveAsync(Guid id, Guid actorUserId, CancellationToken cancellationToken = default) =>
        SetArchiveStateAsync(id, false, actorUserId, cancellationToken);

    public Task<VaccinationCommandResult> RestoreAsync(Guid id, Guid actorUserId, CancellationToken cancellationToken = default) =>
        SetArchiveStateAsync(id, true, actorUserId, cancellationToken);

    private async Task<(VaccineType? VaccineType, string? Error)> ValidateReferencesAsync(Guid patientId, Guid vaccineTypeId, CancellationToken cancellationToken)
    {
        var patientExists = await _context.Patients.AnyAsync(p => p.ID == patientId && p.IsActive, cancellationToken);
        var vaccineType = await _context.VaccineTypes.FirstOrDefaultAsync(v => v.ID == vaccineTypeId && v.IsActive, cancellationToken);
        return !patientExists || vaccineType == null
            ? (null, "Aynı kliniğe ait aktif bir hasta ve aşı tipi seçin.")
            : (vaccineType, null);
    }

    private async Task<VaccinationCommandResult> SetArchiveStateAsync(Guid id, bool isActive, Guid actorUserId, CancellationToken cancellationToken)
    {
        var record = await _context.VaccinationRecords.IgnoreQueryFilters()
            .FirstOrDefaultAsync(v => v.ID == id && v.ClinicID == ClinicId, cancellationToken);
        if (record == null) return new VaccinationCommandResult(false, NotFound: true);
        record.IsActive = isActive;
        record.ArchivedAt = isActive ? null : DateTime.UtcNow;
        record.ArchivedByUserId = isActive ? null : actorUserId;
        await _context.SaveChangesAsync(cancellationToken);
        var action = isActive ? "Vaccination.Restore" : "Vaccination.Archive";
        await _audit.LogAsync(new AuditLogEntry
        {
            Source = AuditLogSources.Web,
            Action = action,
            Message = $"{action} completed.",
            EntityType = nameof(VaccinationRecord),
            EntityId = record.ID.ToString(),
            ClinicId = ClinicId,
            ActorUserId = actorUserId
        }, cancellationToken);
        return new VaccinationCommandResult(true, record);
    }

    private IQueryable<VaccinationRecord> TenantRecords(bool includeArchived)
    {
        var query = _context.VaccinationRecords.IgnoreQueryFilters().Where(v => v.ClinicID == ClinicId);
        return includeArchived ? query : query.Where(v => v.IsActive);
    }

    private Guid ClinicId => _tenant.GetClinicId();
}

public sealed class VaccineTypeService : IVaccineTypeService
{
    private readonly VoxCrmDbContext _context;
    private readonly ITenantService _tenant;
    private readonly IAuditLogger _audit;

    public VaccineTypeService(VoxCrmDbContext context, ITenantService tenant, IAuditLogger audit)
    {
        _context = context;
        _tenant = tenant;
        _audit = audit;
    }

    public async Task<IReadOnlyList<VaccineType>> ListAsync(bool includeArchived = false, CancellationToken cancellationToken = default) =>
        await TenantTypes(includeArchived).OrderBy(v => v.Name).AsNoTracking().ToListAsync(cancellationToken);

    public Task<VaccineType?> GetAsync(Guid id, bool includeArchived = false, CancellationToken cancellationToken = default) =>
        TenantTypes(includeArchived).AsNoTracking().FirstOrDefaultAsync(v => v.ID == id, cancellationToken);

    public async Task<VaccineTypeCommandResult> CreateAsync(VaccineType vaccineType, CancellationToken cancellationToken = default)
    {
        var error = Validate(vaccineType);
        if (error != null) return new VaccineTypeCommandResult(false, Error: error);
        vaccineType.ClinicID = ClinicId;
        _context.VaccineTypes.Add(vaccineType);
        await _context.SaveChangesAsync(cancellationToken);
        return new VaccineTypeCommandResult(true, vaccineType);
    }

    public async Task<VaccineTypeCommandResult> UpdateAsync(VaccineType vaccineType, CancellationToken cancellationToken = default)
    {
        var error = Validate(vaccineType);
        if (error != null) return new VaccineTypeCommandResult(false, Error: error);
        var existing = await _context.VaccineTypes.FirstOrDefaultAsync(v => v.ID == vaccineType.ID && v.IsActive, cancellationToken);
        if (existing == null) return new VaccineTypeCommandResult(false, NotFound: true);
        existing.Name = vaccineType.Name;
        existing.ValidityDays = vaccineType.ValidityDays;
        existing.ReminderDaysBefore = vaccineType.ReminderDaysBefore;
        await _context.SaveChangesAsync(cancellationToken);
        return new VaccineTypeCommandResult(true, existing);
    }

    public Task<VaccineTypeCommandResult> ArchiveAsync(Guid id, Guid actorUserId, CancellationToken cancellationToken = default) =>
        SetArchiveStateAsync(id, false, actorUserId, cancellationToken);

    public Task<VaccineTypeCommandResult> RestoreAsync(Guid id, Guid actorUserId, CancellationToken cancellationToken = default) =>
        SetArchiveStateAsync(id, true, actorUserId, cancellationToken);

    private async Task<VaccineTypeCommandResult> SetArchiveStateAsync(Guid id, bool isActive, Guid actorUserId, CancellationToken cancellationToken)
    {
        var vaccineType = await _context.VaccineTypes.IgnoreQueryFilters()
            .FirstOrDefaultAsync(v => v.ID == id && v.ClinicID == ClinicId, cancellationToken);
        if (vaccineType == null) return new VaccineTypeCommandResult(false, NotFound: true);
        vaccineType.IsActive = isActive;
        vaccineType.ArchivedAt = isActive ? null : DateTime.UtcNow;
        vaccineType.ArchivedByUserId = isActive ? null : actorUserId;
        await _context.SaveChangesAsync(cancellationToken);
        var action = isActive ? "VaccineType.Restore" : "VaccineType.Archive";
        await _audit.LogAsync(new AuditLogEntry
        {
            Source = AuditLogSources.Web,
            Action = action,
            Message = $"{action} completed.",
            EntityType = nameof(VaccineType),
            EntityId = vaccineType.ID.ToString(),
            ClinicId = ClinicId,
            ActorUserId = actorUserId
        }, cancellationToken);
        return new VaccineTypeCommandResult(true, vaccineType);
    }

    private static string? Validate(VaccineType vaccineType)
    {
        if (vaccineType.ValidityDays is < 1 or > 3650)
            return "Geçerlilik süresi 1 ile 3650 gün arasında olmalıdır.";
        if (vaccineType.ReminderDaysBefore < 0 || vaccineType.ReminderDaysBefore > vaccineType.ValidityDays)
            return "Hatırlatma süresi 0 ile geçerlilik süresi arasında olmalıdır.";
        return null;
    }

    private IQueryable<VaccineType> TenantTypes(bool includeArchived)
    {
        var query = _context.VaccineTypes.IgnoreQueryFilters().Where(v => v.ClinicID == ClinicId);
        return includeArchived ? query : query.Where(v => v.IsActive);
    }

    private Guid ClinicId => _tenant.GetClinicId();
}
