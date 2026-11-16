using VoxCrm.Domain.Entities;

namespace VoxCrm.Application.Workday;

public sealed record WorkdayChoice(Guid Id, string Label);
public sealed record ClosingIssue(string Key, string Title, string Explanation, string Url, string? Acknowledgement = null);
public sealed record WorkdayPage(DateOnly Date, string TimeZone, IReadOnlyList<ClinicTask> Tasks, IReadOnlyList<Appointment> Appointments,
    IReadOnlyList<VaccinationRecord> Vaccinations, IReadOnlyList<ClosingIssue> Issues, IReadOnlyList<WorkdayChoice> Patients, IReadOnlyList<WorkdayChoice> Staff);
public sealed record TaskInput(Guid? Id, long Revision, Guid SubmissionId, string Title, string? Notes, DateOnly DueDate, Guid? PatientId, Guid? AssignedUserId, bool Important);
public sealed record CareInput(Guid ExaminationId, long Revision, string TodayActions, string HomeSteps, string CallInstructions, DateOnly? FollowUpDate);
public sealed record CareEditor(Muayene Examination, CareCard? Card, PetOwner? Recipient, IReadOnlyList<WorkdayChoice> FinanceAccounts, bool CanApprove);
public sealed record PublicCare(string ClinicName, string? ClinicPhone, string PatientName, string TodayActions, string HomeSteps, string CallInstructions, DateOnly? FollowUpDate, string? Response);
public sealed class WorkdayException(string message, bool notFound = false) : Exception(message) { public bool NotFound { get; } = notFound; }
public interface IWorkdayService
{
    Task<WorkdayPage> GetAsync(DateOnly? date, CancellationToken ct = default);
    Task<ClinicTask?> GetTaskAsync(Guid id, CancellationToken ct = default);
    Task SaveTaskAsync(TaskInput input, Guid actor, CancellationToken ct = default);
    Task SetTaskStatusAsync(Guid id, long revision, string status, Guid actor, CancellationToken ct = default);
    Task<CareEditor> GetCareEditorAsync(Guid examinationId, Guid actor, CancellationToken ct = default);
    Task SaveCareAsync(CareInput input, Guid actor, CancellationToken ct = default);
    Task<string> PublishCareAsync(Guid examinationId, long revision, Guid actor, bool veterinarianConfirmed, CancellationToken ct = default);
    Task RevokeCareAsync(Guid examinationId, long revision, Guid actor, CancellationToken ct = default);
    Task<PublicCare?> ReadPublicAsync(string token, CancellationToken ct = default);
    Task<bool> RespondAsync(string token, string response, CancellationToken ct = default);
    Task ReviewBillingAsync(Guid examinationId, Guid? accountId, string reason, Guid actor, CancellationToken ct = default);
    Task AcknowledgeAsync(DateOnly date, string issueKey, string note, Guid actor, CancellationToken ct = default);
}
