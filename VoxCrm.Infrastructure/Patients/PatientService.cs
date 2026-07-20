using Microsoft.EntityFrameworkCore;
using VoxCrm.Application.Audit;
using VoxCrm.Application.Patients;
using VoxCrm.Domain.Common;
using VoxCrm.Domain.Entities;
using VoxCrm.Infrastructure.Data;

namespace VoxCrm.Infrastructure.Patients;

public sealed class PatientService : IPatientService
{
    private readonly VoxCrmDbContext _context;
    private readonly ITenantService _tenant;
    private readonly IAuditLogger _audit;

    public PatientService(VoxCrmDbContext context, ITenantService tenant, IAuditLogger audit)
    {
        _context = context;
        _tenant = tenant;
        _audit = audit;
    }

    public async Task<IReadOnlyList<Patient>> ListAsync(string? search, bool includeArchived = false, CancellationToken cancellationToken = default)
    {
        var query = TenantPatients(includeArchived)
            .Include(p => p.Owners.Where(o => o.IsActive))
            .ThenInclude(po => po.PetOwner)
            .AsNoTracking();

        if (!string.IsNullOrWhiteSpace(search))
            query = ApplyPatientSearch(query, search);

        return await query.OrderBy(p => p.Name).ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Patient>> SearchAsync(string? search, CancellationToken cancellationToken = default)
    {
        var query = TenantPatients(false)
            .Include(p => p.Owners.Where(o => o.IsActive))
            .ThenInclude(po => po.PetOwner)
            .AsNoTracking();

        if (!string.IsNullOrWhiteSpace(search))
            query = ApplyPatientSearch(query, search);

        return await query.OrderBy(p => p.Name).Take(20).ToListAsync(cancellationToken);
    }

    public async Task<PatientDetails?> GetDetailsAsync(Guid id, bool includeArchived = false, CancellationToken cancellationToken = default)
    {
        var patient = await TenantPatients(includeArchived)
            .Include(p => p.Owners.Where(o => o.IsActive))
            .ThenInclude(o => o.PetOwner)
            .FirstOrDefaultAsync(p => p.ID == id, cancellationToken);
        if (patient == null) return null;

        var ownerIds = patient.Owners.Select(o => o.PetOwnerId).ToList();
        var availableOwners = await _context.PetOwners
            .Where(o => o.IsActive && !ownerIds.Contains(o.ID))
            .OrderBy(o => o.FirstName)
            .AsNoTracking()
            .ToListAsync(cancellationToken);
        var examinations = await _context.Muayeneler.Where(m => m.PatientId == id).OrderByDescending(m => m.CreatedAt).AsNoTracking().ToListAsync(cancellationToken);
        var vaccinations = await _context.VaccinationRecords.IgnoreQueryFilters()
            .Where(v => v.ClinicID == ClinicId && v.PatientId == id)
            .Include(v => v.VaccineType).OrderByDescending(v => v.AdministeredDate).AsNoTracking().ToListAsync(cancellationToken);
        var appointments = await _context.Appointments.Where(a => a.PatientId == id).OrderByDescending(a => a.ScheduledAt).AsNoTracking().ToListAsync(cancellationToken);
        var debts = await _context.Borçlar.Include(d => d.PetOwner).Where(d => ownerIds.Contains(d.PetOwnerId)).OrderByDescending(d => d.DueDate).AsNoTracking().ToListAsync(cancellationToken);
        return new PatientDetails(patient, availableOwners, examinations, vaccinations, appointments, debts);
    }

    public Task<Patient?> GetAsync(Guid id, bool includeArchived = false, CancellationToken cancellationToken = default) =>
        TenantPatients(includeArchived).AsNoTracking().FirstOrDefaultAsync(p => p.ID == id, cancellationToken);

    public async Task<IReadOnlyList<PetOwner>> GetActiveOwnersAsync(CancellationToken cancellationToken = default) =>
        await _context.PetOwners.Where(o => o.IsActive).OrderBy(o => o.FirstName).AsNoTracking().ToListAsync(cancellationToken);

    public async Task<PatientCommandResult> CreateAsync(Patient patient, Guid? ownerId, CancellationToken cancellationToken = default)
    {
        var validationError = NormalizeAndValidate(patient, assignUnknownName: true);
        if (validationError != null)
            return new PatientCommandResult(false, Error: validationError);

        await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
        if (ownerId is { } selectedOwnerId && selectedOwnerId != Guid.Empty)
        {
            var validOwner = await _context.PetOwners.AnyAsync(o => o.ID == selectedOwnerId && o.IsActive, cancellationToken);
            if (!validOwner)
                return new PatientCommandResult(false, Error: "Geçersiz veya arşivlenmiş sahip seçimi.");
        }

        patient.ClinicID = ClinicId;
        _context.Patients.Add(patient);
        if (ownerId is { } validOwnerId && validOwnerId != Guid.Empty)
        {
            _context.PatientOwners.Add(new PatientOwner
            {
                ClinicID = ClinicId,
                PatientId = patient.ID,
                PetOwnerId = validOwnerId,
                IsPrimaryOwner = true
            });
        }

        await _context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new PatientCommandResult(true, patient);
    }

    public async Task<PatientCommandResult> UpdateAsync(Patient patient, CancellationToken cancellationToken = default)
    {
        var validationError = NormalizeAndValidate(patient, assignUnknownName: false);
        if (validationError != null)
            return new PatientCommandResult(false, Error: validationError);

        var existing = await _context.Patients.FirstOrDefaultAsync(p => p.ID == patient.ID && p.IsActive, cancellationToken);
        if (existing == null) return new PatientCommandResult(false, NotFound: true);
        existing.Name = patient.Name;
        existing.Species = patient.Species;
        existing.Breed = patient.Breed;
        existing.cinsiyet = patient.cinsiyet;
        existing.DateOfBirth = patient.DateOfBirth;
        existing.MicrochipNumber = patient.MicrochipNumber;
        existing.pasaportNumarasi = patient.pasaportNumarasi;
        existing.Notes = patient.Notes;
        await _context.SaveChangesAsync(cancellationToken);
        return new PatientCommandResult(true, existing);
    }

    public Task<PatientCommandResult> ArchiveAsync(Guid id, Guid actorUserId, CancellationToken cancellationToken = default) =>
        SetArchiveStateAsync(id, false, actorUserId, cancellationToken);

    public Task<PatientCommandResult> RestoreAsync(Guid id, Guid actorUserId, CancellationToken cancellationToken = default) =>
        SetArchiveStateAsync(id, true, actorUserId, cancellationToken);

    public async Task<PatientCommandResult> AddOwnerAsync(Guid patientId, Guid ownerId, CancellationToken cancellationToken = default)
    {
        if (!await _context.Patients.AnyAsync(p => p.ID == patientId && p.IsActive, cancellationToken) ||
            !await _context.PetOwners.AnyAsync(o => o.ID == ownerId && o.IsActive, cancellationToken))
            return new PatientCommandResult(false, NotFound: true);

        var link = await _context.PatientOwners.IgnoreQueryFilters()
            .FirstOrDefaultAsync(x => x.ClinicID == ClinicId && x.PatientId == patientId && x.PetOwnerId == ownerId, cancellationToken);
        if (link == null)
            _context.PatientOwners.Add(new PatientOwner { ClinicID = ClinicId, PatientId = patientId, PetOwnerId = ownerId });
        else if (!link.IsActive)
        {
            link.IsActive = true;
            link.ArchivedAt = null;
            link.ArchivedByUserId = null;
        }

        await _context.SaveChangesAsync(cancellationToken);
        return new PatientCommandResult(true);
    }

    public async Task<PatientCommandResult> RemoveOwnerAsync(Guid patientId, Guid ownerId, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        var link = await _context.PatientOwners.FirstOrDefaultAsync(x => x.PatientId == patientId && x.PetOwnerId == ownerId && x.IsActive, cancellationToken);
        if (link == null) return new PatientCommandResult(false, NotFound: true);
        link.IsActive = false;
        link.ArchivedAt = DateTime.UtcNow;
        link.ArchivedByUserId = actorUserId;
        await _context.SaveChangesAsync(cancellationToken);
        return new PatientCommandResult(true);
    }

    private async Task<PatientCommandResult> SetArchiveStateAsync(Guid id, bool isActive, Guid actorUserId, CancellationToken cancellationToken)
    {
        var patient = await _context.Patients.IgnoreQueryFilters()
            .FirstOrDefaultAsync(p => p.ID == id && p.ClinicID == ClinicId, cancellationToken);
        if (patient == null) return new PatientCommandResult(false, NotFound: true);
        patient.IsActive = isActive;
        patient.ArchivedAt = isActive ? null : DateTime.UtcNow;
        patient.ArchivedByUserId = isActive ? null : actorUserId;
        await _context.SaveChangesAsync(cancellationToken);
        await WriteAuditAsync(patient, isActive ? "Patient.Restore" : "Patient.Archive", actorUserId, cancellationToken);
        return new PatientCommandResult(true, patient);
    }

    private Task WriteAuditAsync(Patient patient, string action, Guid actorUserId, CancellationToken cancellationToken) =>
        _audit.LogAsync(new AuditLogEntry
        {
            Source = AuditLogSources.Web,
            Action = action,
            Message = $"{action} completed.",
            EntityType = nameof(Patient),
            EntityId = patient.ID.ToString(),
            ClinicId = ClinicId,
            ActorUserId = actorUserId
        }, cancellationToken);

    private static IQueryable<Patient> ApplyPatientSearch(IQueryable<Patient> query, string search)
    {
        var term = search.Trim().ToLower();

        // Cinsiyet (gender) keyword mapping
        char? genderFilter = null;
        if (term == "erkek" || term == "male") genderFilter = 'E';
        else if (term == "dişi" || term == "dis" || term == "disi" || term == "female") genderFilter = 'D';

        return query.Where(p =>
            (p.Name != null && p.Name.ToLower().Contains(term)) ||
            (p.Species != null && p.Species.ToLower().Contains(term)) ||
            (p.Breed != null && p.Breed.ToLower().Contains(term)) ||
            (p.MicrochipNumber != null && p.MicrochipNumber.ToLower().Contains(term)) ||
            (p.pasaportNumarasi != null && p.pasaportNumarasi.ToLower().Contains(term)) ||
            (p.Notes != null && p.Notes.ToLower().Contains(term)) ||
            (genderFilter.HasValue && p.cinsiyet == genderFilter.Value) ||
            p.Owners.Any(o => o.IsActive &&
                ((o.PetOwner.FirstName != null && o.PetOwner.FirstName.ToLower().Contains(term)) ||
                 (o.PetOwner.LastName != null && o.PetOwner.LastName.ToLower().Contains(term)) ||
                 (o.PetOwner.Phone != null && o.PetOwner.Phone.ToLower().Contains(term)) ||
                 (o.PetOwner.Email != null && o.PetOwner.Email.ToLower().Contains(term)) ||
                 (o.PetOwner.Address != null && o.PetOwner.Address.ToLower().Contains(term)) ||
                 (o.PetOwner.Notes != null && o.PetOwner.Notes.ToLower().Contains(term)))));
    }

    private IQueryable<Patient> TenantPatients(bool includeArchived)
    {
        var query = _context.Patients.IgnoreQueryFilters().Where(p => p.ClinicID == ClinicId);
        return includeArchived ? query : query.Where(p => p.IsActive);
    }

    private Guid ClinicId => _tenant.GetClinicId();

    private static string? NormalizeAndValidate(Patient patient, bool assignUnknownName)
    {
        patient.Name = Normalize(patient.Name);
        patient.Species = Normalize(patient.Species);
        patient.Breed = Normalize(patient.Breed);
        patient.MicrochipNumber = Normalize(patient.MicrochipNumber);
        patient.pasaportNumarasi = Normalize(patient.pasaportNumarasi);
        patient.Notes = Normalize(patient.Notes);

        if (patient.DateOfBirth?.Date > DateTime.UtcNow.Date)
            return "Doğum tarihi gelecekte olamaz.";
        if (patient.cinsiyet is not null and not ('E' or 'D'))
            return "Cinsiyet yalnızca Erkek veya Dişi olabilir.";
        if (TooLong(patient.Name, 120) || TooLong(patient.Species, 80) || TooLong(patient.Breed, 120) ||
            TooLong(patient.MicrochipNumber, 64) || TooLong(patient.pasaportNumarasi, 64) || TooLong(patient.Notes, 2000))
            return "Girilen bilgiler izin verilen uzunluğu aşıyor.";

        if (assignUnknownName && string.IsNullOrWhiteSpace(patient.Name))
            patient.Name = $"Kimliği belirsiz hayvan {patient.ID.ToString("N")[..8]}";
        return null;
    }

    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static bool TooLong(string? value, int maxLength) => value?.Length > maxLength;
}
