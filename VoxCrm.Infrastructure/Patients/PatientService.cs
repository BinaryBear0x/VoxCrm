using Microsoft.EntityFrameworkCore;
using Npgsql;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
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
    private readonly ILogger<PatientService> _logger;

    public PatientService(VoxCrmDbContext context, ITenantService tenant, IAuditLogger audit, ILogger<PatientService> logger)
    {
        _context = context;
        _tenant = tenant;
        _audit = audit;
        _logger = logger;
    }

    public PatientService(VoxCrmDbContext context, ITenantService tenant, IAuditLogger audit)
        : this(context, tenant, audit, NullLogger<PatientService>.Instance) { }

    public async Task<IReadOnlyList<Patient>> ListAsync(string? search, bool includeArchived = false, CancellationToken cancellationToken = default)
    {
        var query = TenantPatients(includeArchived)
            .Include(p => p.Owners.Where(o => o.IsActive))
            .ThenInclude(po => po.PetOwner)
            .AsNoTracking();

        // Encrypted contact fields must be materialized/decrypted before matching.
        var patients = await query.OrderBy(p => p.Name).ToListAsync(cancellationToken);
        return string.IsNullOrWhiteSpace(search) ? patients : ApplyPatientSearch(patients.AsQueryable(), search).ToList();
    }

    public async Task<IReadOnlyList<Patient>> SearchAsync(string? search, CancellationToken cancellationToken = default)
    {
        var query = TenantPatients(false)
            .Include(p => p.Owners.Where(o => o.IsActive))
            .ThenInclude(po => po.PetOwner)
            .AsNoTracking();

        if (string.IsNullOrWhiteSpace(search)) return await query.OrderBy(p => p.Name).Take(20).ToListAsync(cancellationToken);
        var patients = await query.OrderBy(p => p.Name).ToListAsync(cancellationToken);
        return ApplyPatientSearch(patients.AsQueryable(), search).Take(20).ToList();
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
        var financeAccounts = await _context.FinanceAccounts
            .Where(a => a.IsActive && (a.PatientId == id || (a.PatientId == null && a.PetOwnerId.HasValue && ownerIds.Contains(a.PetOwnerId.Value))))
            .Include(a => a.Charges).Include(a => a.Receipts).Include(a => a.Installments)
            .AsSplitQuery().AsNoTracking().ToListAsync(cancellationToken);
        var clinicToday=await ClinicCalendar.TodayAsync(_context,ClinicId,cancellationToken);
        var debts = financeAccounts.Select(a=>VoxCrm.Infrastructure.Finance.FinanceService.Summarize(a, clinicToday)).ToList();
        return new PatientDetails(patient, availableOwners, examinations, vaccinations, appointments, debts);
    }

    public Task<Patient?> GetAsync(Guid id, bool includeArchived = false, CancellationToken cancellationToken = default) =>
        TenantPatients(includeArchived).AsNoTracking().FirstOrDefaultAsync(p => p.ID == id, cancellationToken);

    public async Task<IReadOnlyList<PetOwner>> GetActiveOwnersAsync(CancellationToken cancellationToken = default) =>
        await _context.PetOwners.Where(o => o.IsActive).OrderBy(o => o.FirstName).AsNoTracking().ToListAsync(cancellationToken);

    public async Task<PatientCommandResult> CreateAsync(Patient patient, Guid? ownerId, CancellationToken cancellationToken = default)
    {
        if(ClinicId==Guid.Empty)return new(false,Error:"Klinik hesabı gerekli.");
        var watch = Stopwatch.StartNew();
        if (patient.SubmissionId is { } submissionId)
        {
            var prior = await _context.Patients.IgnoreQueryFilters().FirstOrDefaultAsync(x => x.ClinicID == ClinicId && x.SubmissionId == submissionId, cancellationToken);
            if (prior != null) return new PatientCommandResult(true, prior);
        }
        var validationError = await NormalizeAndValidateAsync(patient, cancellationToken, assignUnknownName: true);
        if (validationError != null)
            return new PatientCommandResult(false, Error: validationError);

        await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
        if (ownerId is { } selectedOwnerId && selectedOwnerId != Guid.Empty)
        {
            var validOwner = await _context.PetOwners.AnyAsync(o => o.ID == selectedOwnerId && o.IsActive, cancellationToken);
            if (!validOwner)
                return new PatientCommandResult(false, Error: "Geçersiz veya arşivlenmiş sahip seçimi.");
        }
        _logger.LogInformation("Patient.Create validated in {ElapsedMs}ms", watch.ElapsedMilliseconds);

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

        try { await _context.SaveChangesAsync(cancellationToken); _logger.LogInformation("Patient.Create persisted in {ElapsedMs}ms", watch.ElapsedMilliseconds); }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            _context.ChangeTracker.Clear();
            if (patient.SubmissionId is { } key)
            {
                var prior = await _context.Patients.IgnoreQueryFilters().FirstOrDefaultAsync(x => x.ClinicID == ClinicId && x.SubmissionId == key, cancellationToken);
                if (prior != null) return new PatientCommandResult(true, prior);
            }
            return new PatientCommandResult(false, Error: "Kayıt zaten oluşturulmuş veya aynı anda ikinci kez gönderilmiş. Listeyi kontrol edin.");
        }
        await transaction.CommitAsync(cancellationToken);
        return new PatientCommandResult(true, patient);
    }

    public async Task<PatientCommandResult> UpdateAsync(Patient patient, CancellationToken cancellationToken = default)
    {
        var validationError = await NormalizeAndValidateAsync(patient, cancellationToken, assignUnknownName: false);
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

    private Task LockOwners(Guid patientId, CancellationToken ct) => _context.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({ClinicId+":patient-owners:"+patientId},0))", ct);
    public async Task<PatientCommandResult> AddOwnerAsync(Guid patientId, Guid ownerId, CancellationToken cancellationToken = default)
    {
        await using var tx=await _context.Database.BeginTransactionAsync(cancellationToken); await LockOwners(patientId,cancellationToken);
        if (!await _context.Patients.AnyAsync(p=>p.ID==patientId&&p.ClinicID==ClinicId&&p.IsActive,cancellationToken)||!await _context.PetOwners.AnyAsync(o=>o.ID==ownerId&&o.ClinicID==ClinicId&&o.IsActive,cancellationToken))return new(false,NotFound:true);
        var inactivePrimary=await _context.PatientOwners.Where(x=>x.ClinicID==ClinicId&&x.PatientId==patientId&&x.IsActive&&x.IsPrimaryOwner&&!x.PetOwner.IsActive).ToListAsync(cancellationToken);
        foreach(var stale in inactivePrimary)stale.IsPrimaryOwner=false;
        if(inactivePrimary.Count>0)await _context.SaveChangesAsync(cancellationToken);
        var primaryExists=await _context.PatientOwners.AnyAsync(x=>x.ClinicID==ClinicId&&x.PatientId==patientId&&x.IsActive&&x.IsPrimaryOwner&&x.PetOwner.IsActive,cancellationToken);
        var link=await _context.PatientOwners.IgnoreQueryFilters().SingleOrDefaultAsync(x=>x.ClinicID==ClinicId&&x.PatientId==patientId&&x.PetOwnerId==ownerId,cancellationToken);
        if(link==null){link=new PatientOwner{ClinicID=ClinicId,PatientId=patientId,PetOwnerId=ownerId,IsPrimaryOwner=!primaryExists};_context.PatientOwners.Add(link);}
        else if(!link.IsActive){link.IsActive=true;link.IsPrimaryOwner=!primaryExists;link.ArchivedAt=null;link.ArchivedByUserId=null;}
        if(!primaryExists)link.IsPrimaryOwner=true;
        await _context.SaveChangesAsync(cancellationToken);await tx.CommitAsync(cancellationToken);return new(true);
    }
    public async Task<PatientCommandResult> SetPrimaryOwnerAsync(Guid patientId,Guid ownerId,Guid actorUserId,CancellationToken cancellationToken=default)
    {
        await using var tx=await _context.Database.BeginTransactionAsync(cancellationToken);await LockOwners(patientId,cancellationToken);
        var links=await _context.PatientOwners.Where(x=>x.ClinicID==ClinicId&&x.PatientId==patientId&&x.IsActive).Include(x=>x.PetOwner).ToListAsync(cancellationToken);
        var target=links.SingleOrDefault(x=>x.PetOwnerId==ownerId&&x.PetOwner.IsActive);if(target==null)return new(false,NotFound:true);
        // Flush demotion first: PostgreSQL enforces the partial unique index immediately.
        foreach(var link in links.Where(x=>x.IsPrimaryOwner&&x.ID!=target.ID))link.IsPrimaryOwner=false;
        await _context.SaveChangesAsync(cancellationToken);target.IsPrimaryOwner=true;await _context.SaveChangesAsync(cancellationToken);
        _context.SystemAuditLogs.Add(new SystemAuditLog{ClinicId=ClinicId,ActorUserId=actorUserId,Category="Patient",Action="Patient.PrimaryOwnerChanged",EntityType="Patient",EntityId=patientId.ToString(),Message="Hastanın birincil sahibi değiştirildi.",MetadataJson=System.Text.Json.JsonSerializer.Serialize(new{OwnerId=ownerId})});
        await _context.SaveChangesAsync(cancellationToken);await tx.CommitAsync(cancellationToken);return new(true);
    }
    public async Task<PatientCommandResult> RemoveOwnerAsync(Guid patientId, Guid ownerId, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        await using var tx=await _context.Database.BeginTransactionAsync(cancellationToken);await LockOwners(patientId,cancellationToken);
        var link=await _context.PatientOwners.SingleOrDefaultAsync(x=>x.ClinicID==ClinicId&&x.PatientId==patientId&&x.PetOwnerId==ownerId&&x.IsActive,cancellationToken);if(link==null)return new(false,NotFound:true);
        if(link.IsPrimaryOwner&&await _context.PatientOwners.AnyAsync(x=>x.ClinicID==ClinicId&&x.PatientId==patientId&&x.ID!=link.ID&&x.IsActive&&x.PetOwner.IsActive,cancellationToken))return new(false,Error:"Önce diğer sahiplerden birini birincil yapın; ardından bu bağlantıyı kaldırın.");
        link.IsActive=false;link.IsPrimaryOwner=false;link.ArchivedAt=DateTime.UtcNow;link.ArchivedByUserId=actorUserId;await _context.SaveChangesAsync(cancellationToken);await tx.CommitAsync(cancellationToken);return new(true);
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
        var term = search.Trim().ToLowerInvariant();
        var digits = new string(term.Where(char.IsDigit).ToArray());
        var phoneSearch = digits.Length >= 3 && term.All(c => char.IsDigit(c) || char.IsWhiteSpace(c) || c is '+' or '-' or '(' or ')');

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
                (((o.PetOwner.FirstName + " " + o.PetOwner.LastName).ToLowerInvariant().Contains(term)) ||
                 (o.PetOwner.FirstName != null && o.PetOwner.FirstName.ToLower().Contains(term)) ||
                 (o.PetOwner.LastName != null && o.PetOwner.LastName.ToLower().Contains(term)) ||
                 (o.PetOwner.Phone != null && (o.PetOwner.Phone.ToLower().Contains(term) || (phoneSearch && new string(o.PetOwner.Phone.Where(char.IsDigit).ToArray()).Contains(digits)))) ||
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

    private async Task<string?> NormalizeAndValidateAsync(Patient patient, CancellationToken ct, bool assignUnknownName)
    {
        patient.Name = Normalize(patient.Name);
        patient.Species = Normalize(patient.Species);
        patient.Breed = Normalize(patient.Breed);
        patient.MicrochipNumber = Normalize(patient.MicrochipNumber);
        patient.pasaportNumarasi = Normalize(patient.pasaportNumarasi);
        patient.Notes = Normalize(patient.Notes);

        if (patient.DateOfBirth.HasValue && patient.DateOfBirth.Value.Date > (await ClinicCalendar.TodayAsync(_context,ClinicId,ct)).ToDateTime(TimeOnly.MinValue))
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
