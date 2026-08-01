using Microsoft.EntityFrameworkCore;
using VoxCrm.Application.Appointments;
using VoxCrm.Domain.Common;
using VoxCrm.Domain.Entities;
using VoxCrm.Infrastructure.Data;
using VoxCrm.Infrastructure.Security;

namespace VoxCrm.Infrastructure.Appointments;

public sealed class AppointmentService : IAppointmentService
{
    private const string DefaultTimeZoneId = "Europe/Istanbul";

    private readonly VoxCrmDbContext _context;
    private readonly ITenantService _tenant;
    private readonly IPiiProtector _protector;

    public AppointmentService(VoxCrmDbContext context, ITenantService tenant, IPiiProtector? protector = null)
    {
        _context = context;
        _tenant = tenant;
        _protector = protector ?? NoOpPiiProtector.Instance;
    }

    public async Task<IReadOnlyList<AppointmentListItem>> ListAsync(
        string? status,
        bool includeArchived = false,
        CancellationToken cancellationToken = default)
    {
        IQueryable<Appointment> query = TenantAppointments(includeArchived).Include(appointment => appointment.Patient);
        if (AppointmentRules.IsAllowedStatus(status))
            query = query.Where(appointment => appointment.Status == status);

        var appointments = await query
            .OrderByDescending(appointment => appointment.ScheduledAt)
            .AsNoTracking()
            .ToListAsync(cancellationToken);
        var timeZone = await GetClinicTimeZoneAsync(cancellationToken);
        var clinicToday = ToClinicLocal(DateTime.UtcNow, timeZone).Date;

        return appointments.Select(appointment => new AppointmentListItem(
            appointment.ID,
            appointment.PatientId,
            appointment.Patient?.Name ?? appointment.GuestName ?? "Kayıtsız müşteri",
            appointment.GuestPhone,
            ToClinicLocal(appointment.ScheduledAt, timeZone),
            appointment.DurationMinutes,
            appointment.AppointmentType,
            appointment.Status,
            appointment.Reason,
            ToClinicLocal(appointment.ScheduledAt, timeZone).Date == clinicToday,
            appointment.IsActive)).ToList();
    }

    public async Task<IReadOnlyList<AppointmentPatientOption>> GetPatientOptionsAsync(
        CancellationToken cancellationToken = default)
    {
        var patients = await _context.Patients
            .IgnoreQueryFilters()
            .Where(patient => patient.ClinicID == ClinicId && patient.IsActive)
            .Include(patient => patient.Owners.Where(owner => owner.IsActive))
            .ThenInclude(owner => owner.PetOwner)
            .OrderBy(patient => patient.Name)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        return patients.Select(patient =>
        {
            var owner = patient.Owners
                .OrderByDescending(link => link.IsPrimaryOwner)
                .Select(link => link.PetOwner)
                .FirstOrDefault(candidate => candidate.IsActive);
            var ownerName = owner == null
                ? null
                : string.Join(' ', new[] { owner.FirstName, owner.LastName }.Where(value => !string.IsNullOrWhiteSpace(value)));

            return new AppointmentPatientOption(
                patient.ID,
                patient.Name ?? string.Empty,
                patient.Species,
                ownerName,
                owner?.Phone);
        }).ToList();
    }

    public async Task<AppointmentEditModel?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var appointment = await TenantAppointments()
            .AsNoTracking()
            .FirstOrDefaultAsync(candidate => candidate.ID == id, cancellationToken);
        if (appointment == null)
            return null;

        var timeZone = await GetClinicTimeZoneAsync(cancellationToken);
        return new AppointmentEditModel(
            appointment.ID,
            appointment.PatientId,
            ToClinicLocal(appointment.ScheduledAt, timeZone),
            appointment.DurationMinutes,
            appointment.AppointmentType,
            appointment.Status,
            appointment.Reason,
            appointment.GuestName,
            appointment.GuestPhone,
            appointment.GuestNotes);
    }

    public async Task<AppointmentCommandResult> CreateAsync(
        AppointmentCommand command,
        bool confirmConflict,
        CancellationToken cancellationToken = default)
    {
        var validation = await ValidateAndConvertAsync(command, cancellationToken);
        if (validation.Error != null)
            return validation.Error;

        await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
        await LockPatientScheduleAsync(command.PatientId, command.GuestPhone, cancellationToken);

        if (!confirmConflict &&
            await HasConflictAsync(
                NormalizePatientId(command.PatientId),
                GuestPhoneHash(command.GuestPhone),
                validation.ScheduledAtUtc,
                command.DurationMinutes,
                null,
                cancellationToken))
        {
            return ConflictWarning();
        }

        var appointment = new Appointment
        {
            ClinicID = ClinicId,
            PatientId = NormalizePatientId(command.PatientId),
            GuestName = NormalizeGuestField(command.GuestName),
            GuestPhone = NormalizeGuestField(command.GuestPhone),
            GuestNotes = NormalizeGuestField(command.GuestNotes),
            GuestPhoneLookupHash = GuestPhoneHash(command.GuestPhone),
            ScheduledAt = validation.ScheduledAtUtc,
            AppointmentType = command.AppointmentType,
            DurationMinutes = command.DurationMinutes,
            Reason = NormalizeReason(command.Reason),
            Status = AppointmentRules.DefaultStatus
        };
        _context.Appointments.Add(appointment);
        await _context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new AppointmentCommandResult(AppointmentCommandOutcome.Saved, appointment.ID);
    }

    public async Task<AppointmentCommandResult> RestoreAsync(
        Guid id,
        Guid actorUserId,
        CancellationToken cancellationToken = default)
    {
        var appointment = await TenantAppointments(includeArchived: true)
            .FirstOrDefaultAsync(candidate => candidate.ID == id && !candidate.IsActive, cancellationToken);
        if (appointment == null)
            return new AppointmentCommandResult(AppointmentCommandOutcome.NotFound);

        if (await HasConflictAsync(
                appointment.PatientId,
                appointment.PatientId.HasValue ? null : appointment.GuestPhoneLookupHash,
                appointment.ScheduledAt,
                appointment.DurationMinutes,
                appointment.ID,
                cancellationToken))
        {
            return ConflictWarning();
        }

        appointment.IsActive = true;
        appointment.ArchivedAt = null;
        appointment.ArchivedByUserId = null;
        await _context.SaveChangesAsync(cancellationToken);
        return new AppointmentCommandResult(AppointmentCommandOutcome.Saved, appointment.ID);
    }

    public async Task<AppointmentCommandResult> UpdateAsync(
        Guid id,
        AppointmentCommand command,
        bool confirmConflict,
        CancellationToken cancellationToken = default)
    {
        var validation = await ValidateAndConvertAsync(command, cancellationToken);
        if (validation.Error != null)
            return validation.Error;

        await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
        await LockPatientScheduleAsync(command.PatientId, command.GuestPhone, cancellationToken);

        var appointment = await TenantAppointments()
            .FirstOrDefaultAsync(candidate => candidate.ID == id, cancellationToken);
        if (appointment == null)
            return new AppointmentCommandResult(AppointmentCommandOutcome.NotFound);

        if (!confirmConflict &&
            await HasConflictAsync(
                NormalizePatientId(command.PatientId),
                GuestPhoneHash(command.GuestPhone),
                validation.ScheduledAtUtc,
                command.DurationMinutes,
                id,
                cancellationToken))
        {
            return ConflictWarning();
        }

        appointment.PatientId = NormalizePatientId(command.PatientId);
        appointment.GuestName = NormalizeGuestField(command.GuestName);
        appointment.GuestPhone = NormalizeGuestField(command.GuestPhone);
        appointment.GuestNotes = NormalizeGuestField(command.GuestNotes);
        appointment.GuestPhoneLookupHash = GuestPhoneHash(command.GuestPhone);
        appointment.ScheduledAt = validation.ScheduledAtUtc;
        appointment.AppointmentType = command.AppointmentType;
        appointment.DurationMinutes = command.DurationMinutes;
        appointment.Reason = NormalizeReason(command.Reason);
        await _context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new AppointmentCommandResult(AppointmentCommandOutcome.Saved, appointment.ID);
    }

    public async Task<AppointmentCommandResult> UpdateStatusAsync(
        Guid id,
        string status,
        CancellationToken cancellationToken = default)
    {
        if (!AppointmentRules.IsAllowedStatus(status))
            return ValidationFailed("Geçersiz randevu durumu.");

        var appointment = await TenantAppointments()
            .FirstOrDefaultAsync(candidate => candidate.ID == id, cancellationToken);
        if (appointment == null)
            return new AppointmentCommandResult(AppointmentCommandOutcome.NotFound);

        appointment.Status = status;
        await _context.SaveChangesAsync(cancellationToken);
        return new AppointmentCommandResult(AppointmentCommandOutcome.Saved, appointment.ID);
    }

    public async Task<AppointmentCommandResult> ArchiveAsync(
        Guid id,
        Guid actorUserId,
        CancellationToken cancellationToken = default)
    {
        var appointment = await TenantAppointments()
            .FirstOrDefaultAsync(candidate => candidate.ID == id, cancellationToken);
        if (appointment == null)
            return new AppointmentCommandResult(AppointmentCommandOutcome.NotFound);

        appointment.IsActive = false;
        appointment.ArchivedAt = DateTime.UtcNow;
        appointment.ArchivedByUserId = actorUserId;
        await _context.SaveChangesAsync(cancellationToken);
        return new AppointmentCommandResult(AppointmentCommandOutcome.Saved, appointment.ID);
    }

    private async Task<(DateTime ScheduledAtUtc, AppointmentCommandResult? Error)> ValidateAndConvertAsync(
        AppointmentCommand command,
        CancellationToken cancellationToken)
    {
        var patientId = NormalizePatientId(command.PatientId);
        if (!AppointmentRules.IsAllowedType(command.AppointmentType))
            return (default, ValidationFailed("Geçersiz randevu türü."));
        if (command.DurationMinutes is < AppointmentRules.MinimumDurationMinutes or > AppointmentRules.MaximumDurationMinutes)
            return (default, ValidationFailed($"Randevu süresi {AppointmentRules.MinimumDurationMinutes} ile {AppointmentRules.MaximumDurationMinutes} dakika arasında olmalıdır."));

        if (patientId.HasValue)
        {
            var patientExists = await _context.Patients
                .IgnoreQueryFilters()
                .AnyAsync(
                    patient => patient.ID == patientId.Value &&
                               patient.ClinicID == ClinicId &&
                               patient.IsActive,
                    cancellationToken);
            if (!patientExists)
                return (default, ValidationFailed("Geçerli ve aktif bir hasta seçin veya kayıtsız randevu seçeneğini kullanın."));
        }

        if (command.GuestName?.Trim().Length > 200)
            return (default, ValidationFailed("Kayıtsız müşteri adı 200 karakteri geçemez."));
        if (command.GuestPhone?.Trim().Length > 64)
            return (default, ValidationFailed("Kayıtsız müşteri telefonu 64 karakteri geçemez."));
        if (command.GuestNotes?.Trim().Length > 1000)
            return (default, ValidationFailed("Kayıtsız randevu notu 1000 karakteri geçemez."));

        var timeZone = await GetClinicTimeZoneAsync(cancellationToken);
        var local = DateTime.SpecifyKind(command.ScheduledAtLocal, DateTimeKind.Unspecified);
        if (timeZone.IsInvalidTime(local))
            return (default, ValidationFailed("Seçilen yerel saat, yaz saati geçişi nedeniyle geçerli değil."));

        return (TimeZoneInfo.ConvertTimeToUtc(local, timeZone), null);
    }

    private async Task<bool> HasConflictAsync(
        Guid? patientId,
        string? guestPhoneHash,
        DateTime scheduledAtUtc,
        int durationMinutes,
        Guid? excludedAppointmentId,
        CancellationToken cancellationToken)
    {
        if (!patientId.HasValue && guestPhoneHash == null)
            return false;

        var proposedEndUtc = scheduledAtUtc.AddMinutes(durationMinutes);
        var candidates = await TenantAppointments()
            .Where(appointment =>
                appointment.PatientId == patientId &&
                (patientId.HasValue || appointment.GuestPhoneLookupHash == guestPhoneHash) &&
                appointment.Status != AppointmentRules.CancelledStatus &&
                appointment.ScheduledAt < proposedEndUtc &&
                (!excludedAppointmentId.HasValue || appointment.ID != excludedAppointmentId.Value))
            .Select(appointment => new { appointment.ScheduledAt, appointment.DurationMinutes })
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        return candidates.Any(existing =>
            existing.ScheduledAt.AddMinutes(existing.DurationMinutes) > scheduledAtUtc);
    }

    private Task<int> LockPatientScheduleAsync(Guid? patientId, string? guestPhone, CancellationToken cancellationToken)
    {
        if (patientId.HasValue)
        {
            return _context.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT pg_advisory_xact_lock({ClinicId.GetHashCode()}, {patientId.Value.GetHashCode()})",
                cancellationToken);
        }

        var lockKey = $"{ClinicId:N}:{GuestPhoneHash(guestPhone) ?? "guest"}";
        return _context.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock(hashtextextended({lockKey}, 0))",
            cancellationToken);
    }

    private async Task<TimeZoneInfo> GetClinicTimeZoneAsync(CancellationToken cancellationToken)
    {
        var timeZoneId = await _context.Clinics
            .Where(clinic => clinic.ID == ClinicId && clinic.IsActive)
            .Select(clinic => clinic.TimeZoneId)
            .SingleOrDefaultAsync(cancellationToken);

        if (string.IsNullOrWhiteSpace(timeZoneId))
            timeZoneId = DefaultTimeZoneId;

        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.FindSystemTimeZoneById(DefaultTimeZoneId);
        }
        catch (InvalidTimeZoneException)
        {
            return TimeZoneInfo.FindSystemTimeZoneById(DefaultTimeZoneId);
        }
    }

    private IQueryable<Appointment> TenantAppointments(bool includeArchived = false)
    {
        var query = _context.Appointments
            .IgnoreQueryFilters()
            .Where(appointment => appointment.ClinicID == ClinicId);
        return includeArchived ? query : query.Where(appointment => appointment.IsActive);
    }

    private static DateTime ToClinicLocal(DateTime utc, TimeZoneInfo timeZone)
    {
        var normalizedUtc = utc.Kind switch
        {
            DateTimeKind.Utc => utc,
            DateTimeKind.Local => utc.ToUniversalTime(),
            _ => DateTime.SpecifyKind(utc, DateTimeKind.Utc)
        };
        return TimeZoneInfo.ConvertTimeFromUtc(normalizedUtc, timeZone);
    }

    private static AppointmentCommandResult ConflictWarning() =>
        new(
            AppointmentCommandOutcome.ConflictWarning,
            Error: "Aynı kişi/hasta için seçilen zaman aralığıyla çakışan başka bir randevu var.");

    private static AppointmentCommandResult ValidationFailed(string error) =>
        new(AppointmentCommandOutcome.ValidationFailed, Error: error);

    private static Guid? NormalizePatientId(Guid? patientId) =>
        patientId is { } id && id != Guid.Empty ? id : null;

    private static string? NormalizeGuestField(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        return value.Trim();
    }

    private string? GuestPhoneHash(string? phone) =>
        _protector.BlindIndex(ClinicId, NormalizePhone(phone));

    private static string? NormalizePhone(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : new string(value.Where(char.IsDigit).ToArray());

    private static string? NormalizeReason(string? reason) =>
        string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();

    private Guid ClinicId => _tenant.GetClinicId();
}
