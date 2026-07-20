namespace VoxCrm.Application.Appointments;

public static class AppointmentRules
{
    public const int MinimumDurationMinutes = 10;
    public const int MaximumDurationMinutes = 240;
    public const string DefaultStatus = "Planlandı";
    public const string CancelledStatus = "İptal";

    public static readonly IReadOnlyList<string> AllowedStatuses =
        ["Planlandı", "Tamamlandı", CancelledStatus, "Gelmedi"];

    public static readonly IReadOnlyList<string> AllowedTypes =
        ["Muayene", "Aşı", "Ameliyat", "Tıraş", "Kontrol"];

    public static bool IsAllowedStatus(string? status) =>
        status != null && AllowedStatuses.Contains(status, StringComparer.Ordinal);

    public static bool IsAllowedType(string? appointmentType) =>
        appointmentType != null && AllowedTypes.Contains(appointmentType, StringComparer.Ordinal);
}

public sealed record AppointmentListItem(
    Guid Id,
    Guid PatientId,
    string PatientName,
    DateTime ScheduledAtLocal,
    int DurationMinutes,
    string AppointmentType,
    string Status,
    string? Reason,
    bool IsToday,
    bool IsActive);

public sealed record AppointmentEditModel(
    Guid Id,
    Guid PatientId,
    DateTime ScheduledAtLocal,
    int DurationMinutes,
    string AppointmentType,
    string Status,
    string? Reason);

public sealed record AppointmentPatientOption(
    Guid Id,
    string Name,
    string? Species,
    string? OwnerName,
    string? OwnerPhone);

public sealed record AppointmentCommand(
    Guid PatientId,
    DateTime ScheduledAtLocal,
    string AppointmentType,
    int DurationMinutes,
    string? Reason);

public enum AppointmentCommandOutcome
{
    Saved,
    ConflictWarning,
    ValidationFailed,
    NotFound
}

public sealed record AppointmentCommandResult(
    AppointmentCommandOutcome Outcome,
    Guid? AppointmentId = null,
    string? Error = null)
{
    public bool Succeeded => Outcome == AppointmentCommandOutcome.Saved;
}

public interface IAppointmentService
{
    Task<IReadOnlyList<AppointmentListItem>> ListAsync(
        string? status,
        bool includeArchived = false,
        CancellationToken cancellationToken = default);
    Task<IReadOnlyList<AppointmentPatientOption>> GetPatientOptionsAsync(CancellationToken cancellationToken = default);
    Task<AppointmentEditModel?> GetAsync(Guid id, CancellationToken cancellationToken = default);
    Task<AppointmentCommandResult> CreateAsync(
        AppointmentCommand command,
        bool confirmConflict,
        CancellationToken cancellationToken = default);
    Task<AppointmentCommandResult> UpdateAsync(
        Guid id,
        AppointmentCommand command,
        bool confirmConflict,
        CancellationToken cancellationToken = default);
    Task<AppointmentCommandResult> UpdateStatusAsync(
        Guid id,
        string status,
        CancellationToken cancellationToken = default);
    Task<AppointmentCommandResult> ArchiveAsync(
        Guid id,
        Guid actorUserId,
        CancellationToken cancellationToken = default);
    Task<AppointmentCommandResult> RestoreAsync(
        Guid id,
        Guid actorUserId,
        CancellationToken cancellationToken = default);
}
