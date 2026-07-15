using Microsoft.EntityFrameworkCore;
using VoxCrm.Application.Appointments;
using VoxCrm.Domain.Common;
using VoxCrm.Domain.Entities;
using VoxCrm.Infrastructure.Data;

namespace VoxCrm.Infrastructure.Appointments;

public sealed class AppointmentService : IAppointmentService
{
    private const string DefaultTimeZoneId = "Europe/Istanbul";

    private readonly VoxCrmDbContext _context;
    private readonly ITenantService _tenant;

    public AppointmentService(VoxCrmDbContext context, ITenantService tenant)
    {
        _context = context;
        _tenant = tenant;
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
            appointment.Patient.Name ?? string.Empty,
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
            appointment.Reason);
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
        await LockPatientScheduleAsync(command.PatientId, cancellationToken);

        if (!confirmConflict &&
            await HasConflictAsync(command.PatientId, validation.ScheduledAtUtc, command.DurationMinutes, null, cancellationToken))
        {
            return ConflictWarning();
        }

        var appointment = new Appointment
        {
            ClinicID = ClinicId,
            PatientId = command.PatientId,
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
        await LockPatientScheduleAsync(command.PatientId, cancellationToken);

        var appointment = await TenantAppointments()
            .FirstOrDefaultAsync(candidate => candidate.ID == id, cancellationToken);
        if (appointment == null)
            return new AppointmentCommandResult(AppointmentCommandOutcome.NotFound);

        if (!confirmConflict &&
            await HasConflictAsync(command.PatientId, validation.ScheduledAtUtc, command.DurationMinutes, id, cancellationToken))
        {
            return ConflictWarning();
        }

        appointment.PatientId = command.PatientId;
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
        if (command.PatientId == Guid.Empty)
            return (default, ValidationFailed("Geçerli bir hasta seçin."));
        if (!AppointmentRules.IsAllowedType(command.AppointmentType))
            return (default, ValidationFailed("Geçersiz randevu türü."));
        if (command.DurationMinutes is < AppointmentRules.MinimumDurationMinutes or > AppointmentRules.MaximumDurationMinutes)
            return (default, ValidationFailed($"Randevu süresi {AppointmentRules.MinimumDurationMinutes} ile {AppointmentRules.MaximumDurationMinutes} dakika arasında olmalıdır."));

        var patientExists = await _context.Patients
            .IgnoreQueryFilters()
            .AnyAsync(
                patient => patient.ID == command.PatientId &&
                           patient.ClinicID == ClinicId &&
                           patient.IsActive,
                cancellationToken);
        if (!patientExists)
            return (default, ValidationFailed("Geçerli ve aktif bir hasta seçin."));

        var timeZone = await GetClinicTimeZoneAsync(cancellationToken);
        var local = DateTime.SpecifyKind(command.ScheduledAtLocal, DateTimeKind.Unspecified);
        if (timeZone.IsInvalidTime(local))
            return (default, ValidationFailed("Seçilen yerel saat, yaz saati geçişi nedeniyle geçerli değil."));

        return (TimeZoneInfo.ConvertTimeToUtc(local, timeZone), null);
    }

    private async Task<bool> HasConflictAsync(
        Guid patientId,
        DateTime scheduledAtUtc,
        int durationMinutes,
        Guid? excludedAppointmentId,
        CancellationToken cancellationToken)
    {
        var proposedEndUtc = scheduledAtUtc.AddMinutes(durationMinutes);
        var candidates = await TenantAppointments()
            .Where(appointment =>
                appointment.PatientId == patientId &&
                appointment.Status != AppointmentRules.CancelledStatus &&
                appointment.ScheduledAt < proposedEndUtc &&
                (!excludedAppointmentId.HasValue || appointment.ID != excludedAppointmentId.Value))
            .Select(appointment => new { appointment.ScheduledAt, appointment.DurationMinutes })
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        return candidates.Any(existing =>
            existing.ScheduledAt.AddMinutes(existing.DurationMinutes) > scheduledAtUtc);
    }

    private Task<int> LockPatientScheduleAsync(Guid patientId, CancellationToken cancellationToken) =>
        _context.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock({ClinicId.GetHashCode()}, {patientId.GetHashCode()})",
            cancellationToken);

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

    private static DateTime ToClinicLocal(DateTime utc, TimeZoneInfo timeZone) =>
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), timeZone);

    private static AppointmentCommandResult ConflictWarning() =>
        new(
            AppointmentCommandOutcome.ConflictWarning,
            Error: "Bu hastanın seçilen zaman aralığıyla çakışan başka bir randevusu var.");

    private static AppointmentCommandResult ValidationFailed(string error) =>
        new(AppointmentCommandOutcome.ValidationFailed, Error: error);

    private static string? NormalizeReason(string? reason) =>
        string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();

    private Guid ClinicId => _tenant.GetClinicId();
}
