using Microsoft.EntityFrameworkCore;
using Npgsql;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
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
    private readonly ILogger<VaccinationService> _logger;
    private readonly IVaccinationReminderScheduler? _reminderScheduler;

    public VaccinationService(
        VoxCrmDbContext context,
        ITenantService tenant,
        IAuditLogger audit,
        ILogger<VaccinationService> logger,
        IVaccinationReminderScheduler reminderScheduler)
    {
        _context = context;
        _tenant = tenant;
        _audit = audit;
        _logger = logger;
        _reminderScheduler = reminderScheduler;
    }

    public VaccinationService(VoxCrmDbContext context, ITenantService tenant, IAuditLogger audit, ILogger<VaccinationService> logger)
    {
        _context = context;
        _tenant = tenant;
        _audit = audit;
        _logger = logger;
    }

    public VaccinationService(VoxCrmDbContext context, ITenantService tenant, IAuditLogger audit)
        : this(context, tenant, audit, NullLogger<VaccinationService>.Instance) { }

    public async Task<IReadOnlyList<VaccinationRecord>> ListAsync(bool includeArchived = false, CancellationToken cancellationToken = default)
    {
        var query = TenantRecords(includeArchived)
            .Include(v => v.Patient)
            .Include(v => v.VaccineType)
            .Include(v => v.WhatsAppNotifications)
            .AsNoTracking();
        var records = await query.OrderBy(v => v.NextDueDate).ToListAsync(cancellationToken);
        var groups = records.Where(x => x.IsActive).GroupBy(x => (x.PatientId, x.VaccineTypeId)).ToDictionary(x => x.Key, x => x.ToList());
        foreach (var record in records)
            if (groups.TryGetValue((record.PatientId, record.VaccineTypeId), out var applications))
                record.RenewedOn = applications.Where(x => x.AdministeredDate.Date > record.AdministeredDate.Date).Select(x => (DateTime?)x.AdministeredDate.Date).Min();
        return records;
    }

    public Task<VaccinationRecord?> GetAsync(Guid id, bool includeArchived = false, CancellationToken cancellationToken = default) =>
        TenantRecords(includeArchived).AsNoTracking().FirstOrDefaultAsync(v => v.ID == id, cancellationToken);

    public async Task<VaccinationChoices> GetChoicesAsync(CancellationToken cancellationToken = default)
    {
        var patients = await _context.Patients.Where(p => p.IsActive).Include(p => p.Owners.Where(o => o.IsActive)).ThenInclude(o => o.PetOwner).OrderBy(p => p.Name).AsNoTracking().ToListAsync(cancellationToken);
        var vaccineTypes = await _context.VaccineTypes.Where(v => v.IsActive).OrderBy(v => v.Name).AsNoTracking().ToListAsync(cancellationToken);
        var zone = await _context.Clinics.Where(x => x.ID == ClinicId).Select(x => x.TimeZoneId).SingleAsync(cancellationToken);
        return new VaccinationChoices(patients, vaccineTypes, ClinicTime.Today(zone).ToDateTime(TimeOnly.MinValue));
    }

    public async Task<VaccinationBatchResult> CreateBatchAsync(VaccinationBatchInput input, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        VaccinationBatchResult Fail(string error) => new(false, Array.Empty<VaccinationRecord>(), error);
        if (ClinicId == Guid.Empty || input.PatientId == Guid.Empty || input.SubmissionId == Guid.Empty) return Fail("Hasta ve form kimliği gerekli. Sayfayı yenileyin.");
        var clinic = await _context.Clinics.AsNoTracking().SingleAsync(x => x.ID == ClinicId, cancellationToken);
        var today = ClinicTime.Today(clinic.TimeZoneId).ToDateTime(TimeOnly.MinValue);
        if (input.AdministeredDate == default || input.AdministeredDate.Date > today || input.AdministeredDate.Year < 2000) return Fail("Geçerli bir yapılma tarihi seçin; gelecek tarih girilemez.");
        if (input.Items == null || input.Items.Count is < 1 or > 30) return Fail("1–30 uygulama seçin.");
        if (input.Items.Select(x => x.VaccineTypeId).Distinct().Count() != input.Items.Count) return Fail("Aynı uygulama türü bir formda yalnız bir kez seçilebilir.");
        if (input.Items.Any(x => x.VaccineCode?.Trim().Length > 200)) return Fail("Her satırın aşı kodu en fazla 200 karakter olabilir.");
        var typeIds = input.Items.Select(x => x.VaccineTypeId).ToList();
        var fingerprint = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(new
        { ClinicId, input.PatientId, Date = input.AdministeredDate.Date.ToString("yyyy-MM-dd"), Items = input.Items.OrderBy(x => x.VaccineTypeId).Select(x => new { x.VaccineTypeId, Code = x.VaccineCode?.Trim() ?? "" }) })));
        if (!await _context.Users.AnyAsync(x => x.Id == actorUserId && x.ClinicId == ClinicId && !x.LoginDisabled, cancellationToken)) return Fail("Klinik kullanıcı hesabı doğrulanamadı.");
        await using var transaction = _context.Database.CurrentTransaction == null ? await _context.Database.BeginTransactionAsync(cancellationToken) : null;
        await _context.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({ClinicId + ":vaccination-batch:" + input.SubmissionId}, 0))", cancellationToken);
        var existing = await _context.VaccinationRecords.IgnoreQueryFilters().Where(x => x.ClinicID == ClinicId && x.BatchSubmissionId == input.SubmissionId).ToListAsync(cancellationToken);
        if (existing.Count > 0) return existing.Count == input.Items.Count && existing.All(x => x.BatchFingerprint == fingerprint) ? new(true, existing) : Fail("Bu form daha önce farklı kayıtlarla işlendi. Yeni kayıt ekranını açın.");
        if (!await _context.Patients.AnyAsync(x => x.ID == input.PatientId && x.ClinicID == ClinicId && x.IsActive, cancellationToken)) return Fail("Aynı kliniğe ait aktif bir hasta seçin.");
        var types = await _context.VaccineTypes.Where(x => x.ClinicID == ClinicId && x.IsActive && typeIds.Contains(x.ID)).ToDictionaryAsync(x => x.ID, cancellationToken);
        if (types.Count != input.Items.Count || types.Values.Any(x => x.ValidityDays < 1)) return Fail("Seçilen uygulamalardan biri geçersiz, pasif veya başka kliniğe ait.");
        // Serialize all batches for this patient; different form tokens cannot create duplicate same-day applications.
        await _context.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({ClinicId + ":vaccination-patient:" + input.PatientId}, 0))", cancellationToken);
        if (await _context.VaccinationRecords.AnyAsync(x => x.ClinicID == ClinicId && x.PatientId == input.PatientId && x.IsActive && typeIds.Contains(x.VaccineTypeId) && x.AdministeredDate.Date == DateTime.SpecifyKind(input.AdministeredDate.Date, DateTimeKind.Utc), cancellationToken))
            return Fail("Seçilen türlerden biri bu hastaya aynı gün zaten kaydedilmiş. Listeyi kontrol edip o satırın seçimini kaldırın.");
        var records = input.Items.Select(item => new VaccinationRecord
        { ClinicID = ClinicId, PatientId = input.PatientId, VaccineTypeId = item.VaccineTypeId, AdministeredDate = DateTime.SpecifyKind(input.AdministeredDate.Date, DateTimeKind.Utc),
            NextDueDate = DateTime.SpecifyKind(input.AdministeredDate.Date.AddDays(types[item.VaccineTypeId].ValidityDays), DateTimeKind.Utc),
            SubmissionId = Guid.NewGuid(), BatchSubmissionId = input.SubmissionId, BatchFingerprint = fingerprint,
            VaccineCode = string.IsNullOrWhiteSpace(item.VaccineCode) ? null : item.VaccineCode.Trim() }).ToList();
        _context.VaccinationRecords.AddRange(records);
        foreach (var record in records) _context.SystemAuditLogs.Add(new SystemAuditLog
        { ClinicId = ClinicId, ActorUserId = actorUserId, Category = "Vaccination", Action = "Vaccination.BatchCreated", EntityType = "VaccinationRecord", EntityId = record.ID.ToString(), Message = "Çoklu uygulama kaydı oluşturuldu.", MetadataJson = System.Text.Json.JsonSerializer.Serialize(new { input.SubmissionId, Count = records.Count }) });
        try
        {
            await _context.SaveChangesAsync(cancellationToken);
            if (_reminderScheduler != null)
            {
                var related = await _context.VaccinationRecords.Where(x => x.ClinicID == ClinicId && x.PatientId == input.PatientId && typeIds.Contains(x.VaccineTypeId)).Select(x => x.ID).OrderBy(x => x).ToListAsync(cancellationToken);
                foreach (var id in related) await _reminderScheduler.ReconcileAsync(id, cancellationToken: cancellationToken);
            }
            if (transaction != null) await transaction.CommitAsync(cancellationToken);
            return new(true, records);
        }
        catch (Exception ex) when (ex is DbUpdateException or InvalidOperationException)
        {
            if (transaction != null) await transaction.RollbackAsync(cancellationToken); _context.ChangeTracker.Clear();
            _logger.LogError(ex, "Vaccination batch transaction rolled back");
            return Fail("Uygulamalar birlikte kaydedilemedi; hiçbir yeni kayıt eklenmedi. Sayfayı kontrol edip tekrar deneyin.");
        }
    }

    public async Task<VaccinationCommandResult> CreateAsync(VaccinationRecord record, CancellationToken cancellationToken = default)
    {
        if(ClinicId==Guid.Empty)return new(false,Error:"Klinik hesabı gerekli.");
        await using var transaction=_context.Database.CurrentTransaction==null?await _context.Database.BeginTransactionAsync(cancellationToken):null;
        await LockPatientsAsync([record.PatientId],cancellationToken);
        var watch = Stopwatch.StartNew();
        if (record.SubmissionId is { } submissionId)
        {
            var prior = await _context.VaccinationRecords.IgnoreQueryFilters().FirstOrDefaultAsync(x => x.ClinicID == ClinicId && x.SubmissionId == submissionId, cancellationToken);
            if (prior != null) return new VaccinationCommandResult(true, prior);
        }
        if (record.AdministeredDate == default || record.AdministeredDate.Date > (await ClinicCalendar.TodayAsync(_context,ClinicId,cancellationToken)).ToDateTime(TimeOnly.MinValue))
            return new VaccinationCommandResult(false, Error: "Aşı tarihi boş veya gelecekte olamaz.");
        var codeError = NormalizeVaccineCode(record);
        if (codeError != null)
            return new VaccinationCommandResult(false, Error: codeError);
        var validation = await ValidateReferencesAsync(record.PatientId, record.VaccineTypeId, cancellationToken);
        if (validation.Error != null) return new VaccinationCommandResult(false, Error: validation.Error);
        _logger.LogInformation("Vaccination.Create validated in {ElapsedMs}ms", watch.ElapsedMilliseconds);
        record.ClinicID = ClinicId;
        record.NextDueDate = record.AdministeredDate.AddDays(validation.VaccineType!.ValidityDays);
        if(await DuplicateAsync(record.PatientId,record.VaccineTypeId,record.AdministeredDate,Guid.Empty,cancellationToken))return new(false,Error:"Bu hasta ve uygulama aynı gün zaten kaydedilmiş.");
        _context.VaccinationRecords.Add(record);
        try { await _context.SaveChangesAsync(cancellationToken); _logger.LogInformation("Vaccination.Create persisted in {ElapsedMs}ms", watch.ElapsedMilliseconds); }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            _context.ChangeTracker.Clear();
            if (record.SubmissionId is { } key)
            {
                var prior = await _context.VaccinationRecords.IgnoreQueryFilters().FirstOrDefaultAsync(x => x.ClinicID == ClinicId && x.SubmissionId == key, cancellationToken);
                if (prior != null) return new VaccinationCommandResult(true, prior);
            }
            return new VaccinationCommandResult(false, Error: "Aşı kaydı zaten oluşturulmuş veya aynı anda ikinci kez gönderilmiş. Listeyi kontrol edin.");
        }
        if (_reminderScheduler != null)
            await _reminderScheduler.ReconcileAsync(record.ID, cancellationToken: cancellationToken);
        if(transaction!=null)await transaction.CommitAsync(cancellationToken);
        return new VaccinationCommandResult(true, record);
    }

    public async Task<VaccinationCommandResult> UpdateAsync(VaccinationRecord record, CancellationToken cancellationToken = default)
    {
        if(ClinicId==Guid.Empty)return new(false,NotFound:true);
        if (record.AdministeredDate == default || record.AdministeredDate.Date > (await ClinicCalendar.TodayAsync(_context,ClinicId,cancellationToken)).ToDateTime(TimeOnly.MinValue))
            return new VaccinationCommandResult(false, Error: "Aşı tarihi boş veya gelecekte olamaz.");
        var codeError = NormalizeVaccineCode(record);
        if (codeError != null)
            return new VaccinationCommandResult(false, Error: codeError);
        await using var transaction=_context.Database.CurrentTransaction==null?await _context.Database.BeginTransactionAsync(cancellationToken):null;
        var oldPatient=await _context.VaccinationRecords.AsNoTracking().Where(x=>x.ID==record.ID&&x.ClinicID==ClinicId).Select(x=>x.PatientId).FirstOrDefaultAsync(cancellationToken);
        await LockPatientsAsync([oldPatient,record.PatientId],cancellationToken);
        var existing = await _context.VaccinationRecords.FirstOrDefaultAsync(v => v.ID == record.ID && v.IsActive, cancellationToken);
        if (existing == null) return new VaccinationCommandResult(false, NotFound: true);
        var validation = await ValidateReferencesAsync(record.PatientId, record.VaccineTypeId, cancellationToken);
        if (validation.Error != null) return new VaccinationCommandResult(false, Error: validation.Error);
        if(await DuplicateAsync(record.PatientId,record.VaccineTypeId,record.AdministeredDate,record.ID,cancellationToken))return new(false,Error:"Bu hasta ve uygulama aynı gün zaten kaydedilmiş. Mevcut kaydı düzenleyin.");
        existing.PatientId = record.PatientId;
        existing.VaccineTypeId = record.VaccineTypeId;
        existing.AdministeredDate = record.AdministeredDate;
        existing.VaccineCode = record.VaccineCode;
        existing.NextDueDate = record.AdministeredDate.AddDays(validation.VaccineType!.ValidityDays);
        await _context.SaveChangesAsync(cancellationToken);
        if (_reminderScheduler != null)
            await _reminderScheduler.ReconcileAsync(existing.ID, cancellationToken: cancellationToken);
        if(transaction!=null)await transaction.CommitAsync(cancellationToken);
        return new VaccinationCommandResult(true, existing);
    }

    public Task<VaccinationCommandResult> ArchiveAsync(Guid id, Guid actorUserId, CancellationToken cancellationToken = default) =>
        SetArchiveStateAsync(id, false, actorUserId, cancellationToken);

    public Task<VaccinationCommandResult> RestoreAsync(Guid id, Guid actorUserId, CancellationToken cancellationToken = default) =>
        SetArchiveStateAsync(id, true, actorUserId, cancellationToken);

    private async Task LockPatientsAsync(IEnumerable<Guid> ids,CancellationToken ct)
    {
        foreach(var id in ids.Where(x=>x!=Guid.Empty).Distinct().OrderBy(x=>x))await _context.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({ClinicId+":vaccination-patient:"+id},0))",ct);
    }
    private Task<bool> DuplicateAsync(Guid patient,Guid type,DateTime date,Guid exclude,CancellationToken ct)=>_context.VaccinationRecords.IgnoreQueryFilters().AnyAsync(x=>x.ClinicID==ClinicId&&x.IsActive&&x.ID!=exclude&&x.PatientId==patient&&x.VaccineTypeId==type&&x.AdministeredDate.Date==DateTime.SpecifyKind(date.Date,DateTimeKind.Utc),ct);

    private async Task<(VaccineType? VaccineType, string? Error)> ValidateReferencesAsync(Guid patientId, Guid vaccineTypeId, CancellationToken cancellationToken)
    {
        var patientExists = await _context.Patients.AnyAsync(p => p.ID == patientId && p.IsActive, cancellationToken);
        var vaccineType = await _context.VaccineTypes.FirstOrDefaultAsync(v => v.ID == vaccineTypeId && v.IsActive, cancellationToken);
        return !patientExists || vaccineType == null
            ? (null, "Aynı kliniğe ait aktif bir hasta ve aşı tipi seçin.")
            : (vaccineType, null);
    }

    private static string? NormalizeVaccineCode(VaccinationRecord record)
    {
        record.VaccineCode = string.IsNullOrWhiteSpace(record.VaccineCode) ? null : record.VaccineCode.Trim();
        return record.VaccineCode?.Length > 200
            ? "Aşı kodu 200 karakteri geçemez."
            : null;
    }

    private async Task<VaccinationCommandResult> SetArchiveStateAsync(Guid id, bool isActive, Guid actorUserId, CancellationToken cancellationToken)
    {
        await using var transaction=_context.Database.CurrentTransaction==null?await _context.Database.BeginTransactionAsync(cancellationToken):null;
        var patient=await _context.VaccinationRecords.IgnoreQueryFilters().Where(x=>x.ID==id&&x.ClinicID==ClinicId).Select(x=>x.PatientId).FirstOrDefaultAsync(cancellationToken);
        await LockPatientsAsync([patient],cancellationToken);
        var record = await _context.VaccinationRecords.IgnoreQueryFilters()
            .FirstOrDefaultAsync(v => v.ID == id && v.ClinicID == ClinicId, cancellationToken);
        if (record == null) return new VaccinationCommandResult(false, NotFound: true);
        if(isActive){var validation=await ValidateReferencesAsync(record.PatientId,record.VaccineTypeId,cancellationToken);if(validation.Error!=null)return new(false,Error:validation.Error);if(await DuplicateAsync(record.PatientId,record.VaccineTypeId,record.AdministeredDate,record.ID,cancellationToken))return new(false,Error:"Aynı gün aynı uygulama zaten var; bu kayıt geri yüklenemez.");}
        record.IsActive = isActive;
        record.ArchivedAt = isActive ? null : DateTime.UtcNow;
        record.ArchivedByUserId = isActive ? null : actorUserId;
        await _context.SaveChangesAsync(cancellationToken);
        if (_reminderScheduler != null)
            await _reminderScheduler.ReconcileAsync(record.ID, cancellationToken: cancellationToken);
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
        if(transaction!=null)await transaction.CommitAsync(cancellationToken);
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
