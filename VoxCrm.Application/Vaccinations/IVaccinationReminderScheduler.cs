namespace VoxCrm.Application.Vaccinations;

public interface IVaccinationReminderScheduler
{
    Task<int> ReconcileAsync(
        Guid? vaccinationRecordId = null,
        DateTime? utcNow = null,
        CancellationToken cancellationToken = default);

    Task<VaccinationReminderActionResult> RetryFailedAsync(
        Guid vaccinationRecordId,
        DateTime? utcNow = null,
        CancellationToken cancellationToken = default);

    Task<VaccinationReminderDispatchResult> StartImmediateAsync(
        Guid vaccinationRecordId,
        DateTime? utcNow = null,
        CancellationToken cancellationToken = default);

    Task CompleteImmediateAsync(
        Guid vaccinationRecordId,
        string? gatewayMessageId,
        DateTime? utcNow = null,
        CancellationToken cancellationToken = default);

    Task FailImmediateAsync(
        Guid vaccinationRecordId,
        string error,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Read-only forecast of the automatic reminders a clinic would queue in the next days,
/// using the same rules as <see cref="IVaccinationReminderScheduler.ReconcileAsync"/> but ignoring the
/// on/off switch so the clinic can see what enabling it would do. Nothing is written.
/// </summary>
public interface IVaccinationReminderForecast
{
    Task<VaccinationReminderPreview> PreviewAsync(
        Guid clinicId,
        DateTime? utcNow = null,
        int horizonDays = 30,
        CancellationToken cancellationToken = default);
}

public sealed record VaccinationReminderPreviewItem(
    Guid RecordId,
    string OwnerName,
    string PatientName,
    string VaccineName,
    DateTime DueDate,
    DateTime PlannedDate,
    bool GoesNow,
    string Message);

public sealed record VaccinationReminderPreview(
    IReadOnlyList<VaccinationReminderPreviewItem> Items,
    int TotalEligible,
    int AlreadyQueued,
    int SkippedNoPrimaryOwner,
    int SkippedNoConsent,
    int SkippedNoPhone);

public sealed record VaccinationReminderActionResult(bool Succeeded, string? Error = null);

public sealed record VaccinationReminderDispatchResult(
    bool Succeeded,
    Guid? ClinicId = null,
    Guid? RequestId = null,
    string? PhoneNumber = null,
    string? Message = null,
    string? Error = null);
