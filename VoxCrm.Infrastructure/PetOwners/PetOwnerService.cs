using Microsoft.EntityFrameworkCore;
using Npgsql;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using VoxCrm.Application.Audit;
using VoxCrm.Application.PetOwners;
using VoxCrm.Domain.Common;
using VoxCrm.Domain.Entities;
using VoxCrm.Infrastructure.Data;
using VoxCrm.Infrastructure.Security;

namespace VoxCrm.Infrastructure.PetOwners;

public sealed class PetOwnerService : IPetOwnerService
{
    private readonly VoxCrmDbContext _context;
    private readonly ITenantService _tenant;
    private readonly IAuditLogger _audit;
    private readonly IPiiProtector _protector;
    private readonly ILogger<PetOwnerService> _logger;

    public PetOwnerService(VoxCrmDbContext context, ITenantService tenant, IAuditLogger audit, ILogger<PetOwnerService> logger, IPiiProtector? protector = null)
    {
        _context = context;
        _tenant = tenant;
        _audit = audit;
        _protector = protector ?? NoOpPiiProtector.Instance;
        _logger = logger;
    }

    public PetOwnerService(VoxCrmDbContext context, ITenantService tenant, IAuditLogger audit, IPiiProtector? protector = null)
        : this(context, tenant, audit, NullLogger<PetOwnerService>.Instance, protector) { }

    public async Task<IReadOnlyList<PetOwner>> ListAsync(string? search, bool includeArchived = false, CancellationToken cancellationToken = default)
    {
        var query = TenantOwners(includeArchived)
            .Include(o => o.OwnedPatients.Where(link => link.IsActive))
            .ThenInclude(link => link.Patient)
            .AsNoTracking();
        query = ApplySearch(query, search);
        return await query.OrderBy(o => o.FirstName).ThenBy(o => o.LastName).ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<PetOwner>> SearchAsync(string? search, CancellationToken cancellationToken = default)
    {
        var query = ApplySearch(
            TenantOwners(false).Include(o => o.OwnedPatients.Where(link => link.IsActive)).ThenInclude(link => link.Patient).AsNoTracking(),
            search);
        return await query.OrderBy(o => o.FirstName).ThenBy(o => o.LastName).Take(20).ToListAsync(cancellationToken);
    }

    public async Task<PetOwnerDetails?> GetDetailsAsync(Guid id, bool includeArchived = false, CancellationToken cancellationToken = default)
    {
        var owner = await TenantOwners(includeArchived)
            .Include(o => o.OwnedPatients.Where(link => link.IsActive))
            .ThenInclude(link => link.Patient)
            .FirstOrDefaultAsync(o => o.ID == id, cancellationToken);
        if (owner == null) return null;
        var existingIds = owner.OwnedPatients.Select(link => link.PatientId).ToList();
        var available = await _context.Patients.Where(p => p.IsActive && !existingIds.Contains(p.ID))
            .OrderBy(p => p.Name).AsNoTracking().ToListAsync(cancellationToken);
        return new PetOwnerDetails(owner, available);
    }

    public Task<PetOwner?> GetAsync(Guid id, bool includeArchived = false, CancellationToken cancellationToken = default) =>
        TenantOwners(includeArchived).AsNoTracking().FirstOrDefaultAsync(o => o.ID == id, cancellationToken);

    public async Task<PetOwnerCommandResult> CreateAsync(PetOwner owner, CancellationToken cancellationToken = default)
    {
        var watch = Stopwatch.StartNew();
        if (owner.SubmissionId is { } submissionId)
        {
            var prior = await _context.PetOwners.IgnoreQueryFilters().FirstOrDefaultAsync(x => x.ClinicID == ClinicId && x.SubmissionId == submissionId, cancellationToken);
            if (prior != null) return new PetOwnerCommandResult(true, prior);
        }
        var validationError = NormalizeAndValidate(owner, assignUnknownName: true);
        if (validationError != null)
            return new PetOwnerCommandResult(false, Error: validationError);
        owner.NormalizedPhone = PhoneLookup(owner.Phone);
        owner.EmailLookupHash = EmailLookup(owner.Email);
        owner.NationalIdentityLookupHash = IdentityLookup(owner.NationalIdentityNumber);
        if (await HasDuplicatePhoneAsync(owner.Phone, null, cancellationToken))
            return new PetOwnerCommandResult(false, Error: $"Bu telefon numarası ({owner.Phone}) zaten kayıtlı.");
        if (await HasDuplicateIdentityAsync(owner.NationalIdentityLookupHash, null, cancellationToken))
            return new PetOwnerCommandResult(false, Error: "Bu T.C. kimlik numarası zaten kayıtlı.");
        _logger.LogInformation("PetOwner.Create validated in {ElapsedMs}ms", watch.ElapsedMilliseconds);
        owner.ClinicID = ClinicId;
        _context.PetOwners.Add(owner);
        try { await _context.SaveChangesAsync(cancellationToken); _logger.LogInformation("PetOwner.Create persisted in {ElapsedMs}ms", watch.ElapsedMilliseconds); }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            _context.ChangeTracker.Clear();
            if (owner.SubmissionId is { } key)
            {
                var prior = await _context.PetOwners.IgnoreQueryFilters().FirstOrDefaultAsync(x => x.ClinicID == ClinicId && x.SubmissionId == key, cancellationToken);
                if (prior != null) return new PetOwnerCommandResult(true, prior);
            }
            return new PetOwnerCommandResult(false, Error: "Kayıt zaten oluşturulmuş veya aynı anda ikinci kez gönderilmiş. Listeyi kontrol edin.");
        }
        return new PetOwnerCommandResult(true, owner);
    }

    public async Task<PetOwnerCommandResult> UpdateAsync(PetOwner owner, CancellationToken cancellationToken = default)
    {
        var validationError = NormalizeAndValidate(owner, assignUnknownName: false);
        if (validationError != null)
            return new PetOwnerCommandResult(false, Error: validationError);
        var existing = await _context.PetOwners.FirstOrDefaultAsync(o => o.ID == owner.ID && o.IsActive, cancellationToken);
        if (existing == null) return new PetOwnerCommandResult(false, NotFound: true);
        var normalizedPhone = PhoneLookup(owner.Phone);
        if (await HasDuplicatePhoneAsync(owner.Phone, owner.ID, cancellationToken))
            return new PetOwnerCommandResult(false, Error: $"Bu telefon numarası ({owner.Phone}) başka bir müşteride kayıtlı.");
        var identityLookup = IdentityLookup(owner.NationalIdentityNumber);
        if (await HasDuplicateIdentityAsync(identityLookup, owner.ID, cancellationToken))
            return new PetOwnerCommandResult(false, Error: "Bu T.C. kimlik numarası başka bir müşteride kayıtlı.");
        var consentWithdrawn = existing.WhatsAppConsent && !owner.WhatsAppConsent;
        existing.FirstName = owner.FirstName;
        existing.LastName = owner.LastName;
        existing.Phone = owner.Phone;
        existing.NormalizedPhone = normalizedPhone;
        existing.Email = owner.Email;
        existing.EmailLookupHash = EmailLookup(owner.Email);
        existing.NationalIdentityNumber = owner.NationalIdentityNumber;
        existing.NationalIdentityLookupHash = identityLookup;
        existing.Address = owner.Address;
        existing.WhatsAppConsent = owner.WhatsAppConsent;
        existing.Notes = owner.Notes;
        await _context.SaveChangesAsync(cancellationToken);

        // A withdrawn consent must stop the customer's queued messages now, not at the next daily run
        // or when the gateway happens to pick them up.
        var cancelled = 0;
        if (consentWithdrawn)
        {
            cancelled = await _context.WhatsAppNotifications.IgnoreQueryFilters()
                .Where(n => n.ClinicID == existing.ClinicID && n.PetOwnerId == existing.ID
                            && (n.Status == WhatsAppNotificationStatuses.Pending || n.Status == WhatsAppNotificationStatuses.RetryScheduled))
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(n => n.Status, WhatsAppNotificationStatuses.Cancelled)
                    .SetProperty(n => n.NextAttemptAt, (DateTime?)null)
                    .SetProperty(n => n.LastError, "Müşteri WhatsApp iznini geri çekti."), cancellationToken);
        }
        return new PetOwnerCommandResult(true, existing, CancelledMessages: cancelled);
    }

    public Task<PetOwnerCommandResult> ArchiveAsync(Guid id, Guid actorUserId, CancellationToken cancellationToken = default) =>
        SetArchiveStateAsync(id, false, actorUserId, cancellationToken);

    public Task<PetOwnerCommandResult> RestoreAsync(Guid id, Guid actorUserId, CancellationToken cancellationToken = default) =>
        SetArchiveStateAsync(id, true, actorUserId, cancellationToken);

    public async Task<PetOwnerCommandResult> AddPatientAsync(Guid ownerId, Guid patientId, CancellationToken cancellationToken = default)
    {
        if (!await _context.PetOwners.AnyAsync(o => o.ID == ownerId && o.IsActive, cancellationToken) ||
            !await _context.Patients.AnyAsync(p => p.ID == patientId && p.IsActive, cancellationToken))
            return new PetOwnerCommandResult(false, NotFound: true);
        var link = await _context.PatientOwners.IgnoreQueryFilters()
            .FirstOrDefaultAsync(x => x.ClinicID == ClinicId && x.PetOwnerId == ownerId && x.PatientId == patientId, cancellationToken);
        var hasPrimaryOwner = await _context.PatientOwners.AnyAsync(
            x => x.PatientId == patientId && x.IsActive && x.IsPrimaryOwner,
            cancellationToken);
        if (link == null)
        {
            _context.PatientOwners.Add(new PatientOwner
            {
                ClinicID = ClinicId,
                PetOwnerId = ownerId,
                PatientId = patientId,
                IsPrimaryOwner = !hasPrimaryOwner
            });
        }
        else if (!link.IsActive)
        {
            link.IsActive = true;
            link.IsPrimaryOwner = link.IsPrimaryOwner && !hasPrimaryOwner;
            link.ArchivedAt = null;
            link.ArchivedByUserId = null;
        }
        await _context.SaveChangesAsync(cancellationToken);
        return new PetOwnerCommandResult(true);
    }

    public async Task<PetOwnerCommandResult> RemovePatientAsync(Guid ownerId, Guid patientId, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        var link = await _context.PatientOwners.FirstOrDefaultAsync(x => x.PetOwnerId == ownerId && x.PatientId == patientId && x.IsActive, cancellationToken);
        if (link == null) return new PetOwnerCommandResult(false, NotFound: true);
        link.IsActive = false;
        link.ArchivedAt = DateTime.UtcNow;
        link.ArchivedByUserId = actorUserId;
        await _context.SaveChangesAsync(cancellationToken);
        return new PetOwnerCommandResult(true);
    }

    private async Task<PetOwnerCommandResult> SetArchiveStateAsync(Guid id, bool isActive, Guid actorUserId, CancellationToken cancellationToken)
    {
        var owner = await _context.PetOwners.IgnoreQueryFilters()
            .FirstOrDefaultAsync(o => o.ID == id && o.ClinicID == ClinicId, cancellationToken);
        if (owner == null) return new PetOwnerCommandResult(false, NotFound: true);
        if (isActive && await HasDuplicatePhoneAsync(owner.Phone, owner.ID, cancellationToken))
            return new PetOwnerCommandResult(false, Error: "Aynı telefon numarasına sahip aktif bir müşteri zaten var.");
        if (isActive && await HasDuplicateIdentityAsync(owner.NationalIdentityLookupHash, owner.ID, cancellationToken))
            return new PetOwnerCommandResult(false, Error: "Aynı T.C. kimlik numarasına sahip aktif bir müşteri zaten var.");
        owner.IsActive = isActive;
        owner.ArchivedAt = isActive ? null : DateTime.UtcNow;
        owner.ArchivedByUserId = isActive ? null : actorUserId;
        await _context.SaveChangesAsync(cancellationToken);
        var action = isActive ? "PetOwner.Restore" : "PetOwner.Archive";
        await _audit.LogAsync(new AuditLogEntry
        {
            Source = AuditLogSources.Web,
            Action = action,
            Message = $"{action} completed.",
            EntityType = nameof(PetOwner),
            EntityId = owner.ID.ToString(),
            ClinicId = ClinicId,
            ActorUserId = actorUserId
        }, cancellationToken);
        return new PetOwnerCommandResult(true, owner);
    }

    // The stored lookup key is the hash of the digits as typed, so "0500 000 00 01" and "+90 500 000 00 01"
    // are different keys although they are the same number. The stored key stays as it is (changing it would
    // orphan every existing row); the duplicate check and the search try every spelling of the number instead.
    private string[] PhoneLookups(string? phone)
    {
        var digits = NormalizePhone(phone);
        if (digits == null) return Array.Empty<string>();
        return VoxCrm.Application.WhatsApp.WhatsAppOptOut.PhoneLookupCandidates(digits)
            .Select(candidate => _protector.BlindIndex(ClinicId, candidate))
            .Where(key => !string.IsNullOrEmpty(key))
            .Select(key => key!)
            .Distinct()
            .ToArray();
    }

    private Task<bool> HasDuplicatePhoneAsync(string? phone, Guid? excludedId, CancellationToken cancellationToken)
    {
        var keys = PhoneLookups(phone);
        if (keys.Length == 0) return Task.FromResult(false);
        return TenantOwners(false).AnyAsync(
            owner => owner.NormalizedPhone != null && keys.Contains(owner.NormalizedPhone)
                     && (!excludedId.HasValue || owner.ID != excludedId.Value),
            cancellationToken);
    }

    private Task<bool> HasDuplicateIdentityAsync(string? lookupHash, Guid? excludedId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(lookupHash)) return Task.FromResult(false);
        return TenantOwners(false).AnyAsync(
            owner => owner.NationalIdentityLookupHash == lookupHash
                     && (!excludedId.HasValue || owner.ID != excludedId.Value),
            cancellationToken);
    }

    private static string? NormalizePhone(string? phone)
    {
        if (string.IsNullOrWhiteSpace(phone)) return null;
        var digits = new string(phone.Where(char.IsDigit).ToArray());
        return digits.Length == 0 ? null : digits;
    }

    private IQueryable<PetOwner> TenantOwners(bool includeArchived)
    {
        var query = _context.PetOwners.IgnoreQueryFilters().Where(o => o.ClinicID == ClinicId);
        return includeArchived ? query : query.Where(o => o.IsActive);
    }

    private IQueryable<PetOwner> ApplySearch(IQueryable<PetOwner> query, string? search)
    {
        if (string.IsNullOrWhiteSpace(search)) return query;
        var term = search.Trim().ToLower();
        var phoneHashes = PhoneLookups(term);
        var emailHash = term.Contains('@') ? EmailLookup(term) : null;
        var identityHash = term.Length == 11 && term.All(char.IsDigit) ? IdentityLookup(term) : null;
        return query.Where(o =>
            (o.FirstName != null && o.FirstName.ToLower().Contains(term)) ||
            (o.LastName != null && o.LastName.ToLower().Contains(term)) ||
            ((o.FirstName + " " + o.LastName).ToLower().Contains(term)) ||
            (phoneHashes.Length > 0 && o.NormalizedPhone != null && phoneHashes.Contains(o.NormalizedPhone)) ||
            (emailHash != null && o.EmailLookupHash == emailHash) ||
            (identityHash != null && o.NationalIdentityLookupHash == identityHash) ||
            o.OwnedPatients.Any(link => link.IsActive &&
                ((link.Patient.Name != null && link.Patient.Name.ToLower().Contains(term)) ||
                 (link.Patient.Species != null && link.Patient.Species.ToLower().Contains(term)) ||
                 (link.Patient.Breed != null && link.Patient.Breed.ToLower().Contains(term)))));
    }

    private Guid ClinicId => _tenant.GetClinicId();
    private string? PhoneLookup(string? value) => _protector.BlindIndex(ClinicId, NormalizePhone(value));
    private string? EmailLookup(string? value) => _protector.BlindIndex(ClinicId, value?.Trim().ToLowerInvariant());
    private string? IdentityLookup(string? value) => _protector.BlindIndex(ClinicId, value);

    private static string? NormalizeAndValidate(PetOwner owner, bool assignUnknownName)
    {
        owner.FirstName = Normalize(owner.FirstName);
        owner.LastName = Normalize(owner.LastName);
        owner.Phone = Normalize(owner.Phone);
        owner.Email = Normalize(owner.Email);
        owner.NationalIdentityNumber = NormalizeIdentity(owner.NationalIdentityNumber);
        owner.Address = Normalize(owner.Address);
        owner.Notes = Normalize(owner.Notes);

        if (TooLong(owner.FirstName, 120) || TooLong(owner.LastName, 120) || TooLong(owner.Phone, 32) ||
            TooLong(owner.Email, 254) || TooLong(owner.NationalIdentityNumber, 11) ||
            TooLong(owner.Address, 500) || TooLong(owner.Notes, 2000))
            return "Girilen bilgiler izin verilen uzunluğu aşıyor.";

        if (owner.Email != null && !System.Net.Mail.MailAddress.TryCreate(owner.Email, out _))
            return "Geçerli bir e-posta adresi girin veya alanı boş bırakın.";

        if (owner.NationalIdentityNumber != null && !IsValidTurkishIdentityNumber(owner.NationalIdentityNumber))
            return "T.C. kimlik numarası 11 rakamdan oluşmalı ve geçerli olmalıdır; bilmiyorsanız alanı boş bırakın.";

        var digits = NormalizePhone(owner.Phone);
        if (digits != null && digits.Length is < 7 or > 15)
            return "Telefon numarası 7 ile 15 rakam arasında olmalıdır.";

        if (assignUnknownName && string.IsNullOrWhiteSpace(owner.FirstName))
            owner.FirstName = $"Kimliği belirsiz kişi {owner.ID.ToString("N")[..8]}";
        return null;
    }

    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static string? NormalizeIdentity(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static bool IsValidTurkishIdentityNumber(string value)
    {
        if (value.Length != 11 || value[0] == '0' || !value.All(char.IsDigit)) return false;
        var digits = value.Select(character => character - '0').ToArray();
        var tenth = ((digits[0] + digits[2] + digits[4] + digits[6] + digits[8]) * 7
                     - (digits[1] + digits[3] + digits[5] + digits[7])) % 10;
        if (tenth < 0) tenth += 10;
        return digits[9] == tenth && digits[10] == digits.Take(10).Sum() % 10;
    }
    private static bool TooLong(string? value, int maxLength) => value?.Length > maxLength;
}
